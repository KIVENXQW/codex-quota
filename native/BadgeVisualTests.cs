using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CodexQuota;

class BadgeVisualTests {
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
  [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr window,IntPtr region);
  [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
  [DllImport("gdi32.dll")] static extern bool PtInRegion(IntPtr region,int x,int y);
  [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr region);
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
      Bitmap image=Paint(badge.Controls[0],null);
      for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)if(!badge.Region.IsVisible(x+.5f,y+.5f))image.SetPixel(x,y,Color.Transparent);
      return image;
    }
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
      foreach(float scale in new[]{1f,1.5f,2f})using(var badge=new QuotaBadge()) {
        badge.SetScale(scale);IntPtr region=CreateRectRgn(0,0,0,0);
        try {
          Check(GetWindowRgn(badge.Handle,region)>1,"Avatar must have a real native shape region");
          Check(!PtInRegion(region,(int)(4*scale),(int)(4*scale))&&!PtInRegion(region,(int)(35*scale),(int)(4*scale)),"Avatar native silhouette must be circular, with no rounded-square corner patches");
          Check(PtInRegion(region,(int)(20*scale),(int)(20*scale))&&PtInRegion(region,(int)(2*scale),(int)(20*scale)),"Circular badge must retain its center and full circular click area");
        }finally{DeleteObject(region);}
      }
      using(var bitmap=Render(85,false,"zh",1)) {
        Rectangle number=CenterInk(bitmap);
        Check(number.Height>=8&&number.Height<=12&&number.Width<=20,"Center quota must be modest 13–14 px typography inside the glass ring, not large card text");
        Check(RoseArc(bitmap)>25,"Avatar must show the approved colored circular quota arc");
      }
      foreach(float scale in new[]{1f,1.5f,2f})foreach(double? value in new double?[]{0,85,100,93.5,null})foreach(string language in new[]{"zh","en"})using(var bitmap=Render(value,false,language,scale)) {
        Check(bitmap.Size==new Size((int)(40*scale),(int)(40*scale)),"Badge must preserve the attached 40 logical pixel geometry");
        Check(!CenterInk(bitmap).IsEmpty,"Known and missing quota must have a readable center glyph at every DPI");
        Check(bitmap.GetPixel(0,0).A==0&&bitmap.GetPixel(bitmap.Width-1,bitmap.Height-1).A==0,"Actual native shape must remove square glass corners");
      }
      using(var badge=new QuotaBadge()) {
        badge.SetAppearance(new UserPreferences {Language="en"});badge.SetState(State(93.5,false));
        Check(badge.Controls[0].AccessibleName.Contains("93.5%"),"Fractional quota must remain exact in the badge's accessible value");
        badge.SetState(State(null,false));Check(badge.Controls[0].AccessibleName.Contains("—"),"Missing quota must remain missing instead of becoming zero");
        badge.SetState(State(100,true));Check(badge.Controls[0].AccessibleName.Contains("100%")&&badge.Controls[0].AccessibleName.Contains("cached"),"Stale quota must preserve its value and announce cached state");
        badge.SetState(State(100,false));using(var old=Paint(badge.Controls[0],null)) {
          badge.SetState(State(0,false));using(var repainted=Paint(badge.Controls[0],old))using(var clean=Paint(badge.Controls[0],null))Check(Equal(repainted,clean),"Native WM_PAINT from 100 to 0 must erase the previous number and colored arc");
        }
        using(var normal=Paint(badge.Controls[0],null)) {
          badge.Controls[0].GetType().GetMethod("OnMouseEnter",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(badge.Controls[0],new object[]{EventArgs.Empty});
          using(var hovered=Paint(badge.Controls[0],null))Check(!Equal(normal,hovered),"Circular glass badge must preserve a visible hover response");
        }
      }
      using(var current=Render(85,false,"zh",1))using(var stale=Render(85,true,"zh",1))Check(!Equal(current,stale),"Stale quota must remain visibly distinguishable from current quota");
      using(var zero=Render(0,false,"zh",1))using(var full=Render(100,false,"zh",1))using(var unknown=Render(null,false,"zh",1))using(var stale=Render(85,true,"zh",1))Check(RoseArc(full)>RoseArc(zero)+30&&RoseArc(unknown)==0&&RoseArc(stale)==0,"Only current known remaining quota must fill the colored glass ring");
      Console.WriteLine("Badge visual: "+checks+" checks PASS (real WM_PAINT; no PrintWindow or desktop capture)");return 0;
    }catch(Exception error){Console.Error.WriteLine("FAIL: "+error.Message);return 1;}
  }
  static Rectangle CenterInk(Bitmap bitmap) {
    int side=bitmap.Width;int first=side,last=-1,left=side,right=-1;
    for(int y=(int)(side*.32);y<(int)(side*.67);y++)for(int x=(int)(side*.25);x<(int)(side*.75);x++){Color c=bitmap.GetPixel(x,y);if(c.A>180&&c.R<170&&c.G<135&&c.B<165){first=Math.Min(first,y);last=Math.Max(last,y);left=Math.Min(left,x);right=Math.Max(right,x);}}
    return right<left?Rectangle.Empty:Rectangle.FromLTRB(left,first,right+1,last+1);
  }
  static int RoseArc(Bitmap bitmap) {
    int n=0;double side=bitmap.Width;
    for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++){double dx=(x+.5)/side-.5,dy=(y+.5)/side-.48;double r=Math.Sqrt(dx*dx+dy*dy);Color c=bitmap.GetPixel(x,y);if(r>.32&&r<.40&&c.A>100&&c.R-c.G>50&&c.B-c.G>20)n++;}return n;
  }
}
