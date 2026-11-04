using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CodexQuota;

class BadgeVisualTests {
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
  static int checks;
  static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
  static QuotaState State(double? remaining,bool stale){var state=new QuotaState {Available=true,Stale=stale};state.Windows.Add(new QuotaWindow {Remaining=remaining});return state;}
  static Bitmap Paint(Control control,Bitmap previous) {
    var bitmap=previous==null?new Bitmap(control.Width,control.Height,PixelFormat.Format32bppArgb):(Bitmap)previous.Clone();
    using(var g=Graphics.FromImage(bitmap)) {
      if(previous==null)g.Clear(Color.Magenta);
      IntPtr dc=g.GetHdc();try{SendMessage(control.Handle,0x000F,dc,IntPtr.Zero);}finally{g.ReleaseHdc(dc);}
    }return bitmap;
  }
  static Bitmap Render(double? value,bool stale,string language,float scale) {
    using(var badge=new QuotaBadge()) {
      badge.SetScale(scale);badge.SetAppearance(new UserPreferences {Language=language});badge.SetState(State(value,stale));
      return Paint(badge.Controls[0],null);
    }
  }
  static Rectangle Ink(Bitmap bitmap,int top,int bottom) {
    int left=bitmap.Width,right=-1,first=bitmap.Height,last=-1;
    for(int y=top;y<bottom;y++)for(int x=2;x<bitmap.Width-2;x++){Color c=bitmap.GetPixel(x,y);if(c.R<170&&c.G<160&&c.B<170){left=Math.Min(left,x);right=Math.Max(right,x);first=Math.Min(first,y);last=Math.Max(last,y);}}
    return right<left?Rectangle.Empty:Rectangle.FromLTRB(left,first,right+1,last+1);
  }
  static bool Equal(Bitmap left,Bitmap right){if(left.Size!=right.Size)return false;for(int y=0;y<left.Height;y++)for(int x=0;x<left.Width;x++)if(left.GetPixel(x,y)!=right.GetPixel(x,y))return false;return true;}
  static void Board(string path) {
    double?[] values={85,93,100,0,93.5,null,85,93};string[] labels={"85%","93%","100%","0%","93.5%","Unknown","Stale 85%","English"};
    using(var board=new Bitmap(1200,650))using(var g=Graphics.FromImage(board)) {
      g.Clear(Color.FromArgb(244,241,248));using(var font=new Font("Segoe UI",19,GraphicsUnit.Pixel))g.DrawString("Actual WM_PAINT · avatar badge · sample quota",font,Brushes.Black,20,16);
      for(int row=0;row<2;row++)for(int i=0;i<values.Length;i++) {
        int x=20+i*147,y=70+row*285;float scale=row+1;
        using(var small=new Font("Segoe UI",12,GraphicsUnit.Pixel))g.DrawString(labels[i]+" · "+(scale*100)+"%",small,Brushes.DimGray,x,y);
        using(var bitmap=Render(values[i],i==6,i==7?"en":"zh",scale)) {
          g.DrawImageUnscaled(bitmap,x+4,y+26);g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;
          g.DrawImage(bitmap,new Rectangle(x,y+116,120,120),new Rectangle(Point.Empty,bitmap.Size),GraphicsUnit.Pixel);
        }
      }board.Save(path,ImageFormat.Png);
    }
  }
  [STAThread] static int Main(string[] args) {
    try {
      Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
      if(args.Length==2&&args[0]=="--render")Board(args[1]);
      using(var bitmap=Render(93,false,"zh",1)) {
        Rectangle number=Ink(bitmap,17,37);
        Check(number.Height>=15,"Avatar quota digits must retain at least 15 px of dark cap height at 100% DPI; observed "+number.Height);
        int opaque=0;for(int y=17;y<37;y++)for(int x=3;x<37;x++){Color c=bitmap.GetPixel(x,y);if(c.R<100&&c.G<100&&c.B<110)opaque++;}
        Check(opaque>=105,"Two-digit quota must have substantial bold strokes, not thin text");
      }
      foreach(float scale in new[]{1f,1.5f,2f})foreach(double? value in new double?[]{0,85,100,93.5,null})foreach(string language in new[]{"zh","en"})using(var bitmap=Render(value,false,language,scale)) {
        Check(bitmap.Size==new Size((int)(40*scale),(int)(40*scale)),"Badge must preserve the attached 40 logical pixel geometry");
        Rectangle number=Ink(bitmap,(int)(17*scale),(int)(37*scale));
        Check(!number.IsEmpty&&number.Left>=2*scale&&number.Right<=38*scale,"Number/percent must fit without touching or clipping against glass edges");
        Check(!Ink(bitmap,(int)(3*scale),(int)(15*scale)).IsEmpty,"Remaining label must be visible in both languages at every DPI");
        bool cleared=true;for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)cleared&=bitmap.GetPixel(x,y).ToArgb()!=Color.Magenta.ToArgb();
        Check(cleared,"Real WM_PAINT must replace the entire previous surface");
      }
      using(var badge=new QuotaBadge()) {
        badge.SetAppearance(new UserPreferences {Language="en"});badge.SetState(State(93.5,false));
        Check(badge.Controls[0].AccessibleName.Contains("93.5%"),"Fractional quota must remain exact in the badge's accessible value");
        badge.SetState(State(null,false));Check(badge.Controls[0].AccessibleName.Contains("—"),"Missing quota must remain missing instead of becoming zero");
        badge.SetState(State(100,true));Check(badge.Controls[0].AccessibleName.Contains("100%")&&badge.Controls[0].AccessibleName.Contains("cached"),"Stale quota must preserve its value and announce cached state");
        badge.SetState(State(100,false));using(var old=Paint(badge.Controls[0],null)) {
          badge.SetState(State(0,false));using(var repainted=Paint(badge.Controls[0],old))using(var clean=Render(0,false,"en",1))Check(Equal(repainted,clean),"Native WM_PAINT from 100 to 0 must erase all previous number and percent pixels");
        }
      }
      using(var current=Render(85,false,"zh",1))using(var stale=Render(85,true,"zh",1))Check(!Equal(current,stale),"Stale quota must remain visibly distinguishable from current quota");
      Console.WriteLine("Badge visual: "+checks+" checks PASS (real WM_PAINT; no PrintWindow or desktop capture)");return 0;
    }catch(Exception error){Console.Error.WriteLine("FAIL: "+error.Message);return 1;}
  }
}
