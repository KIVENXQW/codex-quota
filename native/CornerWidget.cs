using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CodexQuota {
  // A real alpha surface: only the glass is translucent; glyph interiors stay opaque.
  // The reflections are drawn locally. No desktop capture or synthetic quota data.
  public class CornerWidget : Form {
    UserPreferences preferences=new UserPreferences();
    QuotaState state=new QuotaState();
    Rectangle workingArea;
    float scale=1;
    bool positioned,mouseDown,dragged;
    Point dragStart,windowStart;
    Point? sessionLocation;
    public event EventHandler ActivatedByUser;

    public CornerWidget() {
      AutoScaleMode=AutoScaleMode.None;FormBorderStyle=FormBorderStyle.None;
      ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;
      Text="Codex 额度";AccessibleName="Codex 额度";Cursor=Cursors.Hand;
      SetStyle(ControlStyles.Selectable,false);
    }
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {
      get {var p=base.CreateParams;p.ExStyle|=0x00080000|0x08000000|0x00000080;return p;}
    }
    protected override void WndProc(ref Message m) {
      if(m.Msg==0x0021){m.Result=new IntPtr(3);return;} // MA_NOACTIVATE
      base.WndProc(ref m);
      if(m.Msg==0x031A||m.Msg==0x001A)RefreshSurface(); // theme / system preferences
    }
    public void ApplyPreferences(UserPreferences value) {
      preferences=value??new UserPreferences();
      if(preferences.CornerStyle=="tray"){Hide();return;}
      if(positioned)UpdatePosition();
    }
    public void SetState(QuotaState value) {
      state=value??new QuotaState();
      AccessibleName=(preferences.Language=="en"?"Codex remaining ":"Codex 剩余额度 ")+ValueText(state);
      RefreshSurface();
    }
    public void PositionAt(Rectangle area,float dpiScale) {
      workingArea=area;scale=NormalizeScale(dpiScale);positioned=area.Width>0&&area.Height>0;
      UpdatePosition();
    }
    void UpdatePosition() {
      if(!positioned||preferences.CornerStyle=="tray"){Hide();return;}
      Rectangle bounds=CalculateBounds(workingArea,scale,preferences.CornerStyle,sessionLocation);
      Bounds=bounds;if(sessionLocation.HasValue)sessionLocation=bounds.Location;
      RefreshSurface();if(!Visible)Show();
    }
    void RefreshSurface() {
      if(!positioned||preferences.CornerStyle=="tray"||IsDisposed)return;
      // Creating the HWND does not show it. Upload the complete alpha surface before Show.
      using(var bitmap=Render(preferences,state,scale,DarkDesktop()))SetLayeredBitmap(bitmap);
    }
    protected override void OnMouseDown(MouseEventArgs e) {
      base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;
      mouseDown=true;dragged=false;dragStart=PointToScreen(e.Location);windowStart=Location;Capture=true;
    }
    protected override void OnMouseMove(MouseEventArgs e) {
      base.OnMouseMove(e);if(!mouseDown)return;
      Point current=PointToScreen(e.Location);int dx=current.X-dragStart.X,dy=current.Y-dragStart.Y;
      if(!dragged&&Math.Abs(dx)<SystemInformation.DragSize.Width/2&&Math.Abs(dy)<SystemInformation.DragSize.Height/2)return;
      dragged=true; // Crossing the threshold is a drag even when an edge prevents movement.
      Rectangle next=CalculateBounds(workingArea,scale,preferences.CornerStyle,new Point(windowStart.X+dx,windowStart.Y+dy));
      if(next.Location!=Location){Location=next.Location;sessionLocation=next.Location;}
    }
    protected override void OnMouseUp(MouseEventArgs e) {
      base.OnMouseUp(e);if(e.Button!=MouseButtons.Left||!mouseDown)return;
      bool activate=!dragged&&ClientRectangle.Contains(e.Location);mouseDown=false;Capture=false;
      if(activate){var handler=ActivatedByUser;if(handler!=null)handler(this,EventArgs.Empty);}
    }
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture)mouseDown=false;}

    static bool DarkDesktop() {
      try {using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) {
        object value=key==null?null:key.GetValue("SystemUsesLightTheme");if(value is int)return (int)value==0;
      }}catch{}return SystemColors.Control.GetBrightness()<.5f;
    }
    static float NormalizeScale(float value){return Single.IsNaN(value)||Single.IsInfinity(value)||value<=0?1:Math.Max(.5f,Math.Min(4,value));}
    internal static Size LogicalSize(string style){return style=="capsule"?new Size(132,44):new Size(184,88);}
    internal static Rectangle CalculateBounds(Rectangle area,float dpiScale,string style,Point? desired) {
      float factor=NormalizeScale(dpiScale);Size logical=LogicalSize(style);
      var size=new Size((int)Math.Round(logical.Width*factor),(int)Math.Round(logical.Height*factor));
      int margin=(int)Math.Round(16*factor);
      Point point=desired??new Point(area.Right-size.Width-margin,area.Bottom-size.Height-margin);
      point.X=Math.Max(area.Left,Math.Min(point.X,area.Right-size.Width));point.Y=Math.Max(area.Top,Math.Min(point.Y,area.Bottom-size.Height));
      return new Rectangle(point,size);
    }
    internal static double? Remaining(QuotaState value) {
      if(value==null||(!value.Available&&!value.Stale)||value.Primary==null)return null;
      double? number=value.Primary.Remaining;
      if(!number.HasValue||Double.IsNaN(number.Value)||Double.IsInfinity(number.Value))return null;
      return Math.Max(0,Math.Min(100,number.Value));
    }
    internal static string ValueText(QuotaState value) {double? number=Remaining(value);return number.HasValue?Math.Round(number.Value).ToString("0",CultureInfo.InvariantCulture):"—";}
    internal static string ResetText(QuotaState value,bool english) {
      if(value!=null&&value.Stale)return english?"Cached quota · refresh":"旧数据 · 请刷新";
      if(value!=null&&(value.Available||value.Stale)&&value.Primary!=null&&value.Primary.ResetAt.HasValue) {
        try {
          string timestamp=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(value.Primary.ResetAt.Value).AddHours(8).ToString("MM/dd HH:mm",CultureInfo.InvariantCulture);
          return english?"Resets "+timestamp:timestamp+" 重置";
        }catch(ArgumentOutOfRangeException){}
      }
      return english?"Reset time unavailable":"暂无重置时间";
    }
    static Color Mix(Color first,Color last,float amount){return Color.FromArgb((int)(first.R+(last.R-first.R)*amount),(int)(first.G+(last.G-first.G)*amount),(int)(first.B+(last.B-first.B)*amount));}
    static GraphicsPath Rounded(RectangleF r,float radius) {
      float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));var path=new GraphicsPath();
      path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d,r.Y,d,d,270,90);
      path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.X,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
    static void Label(Graphics g,string text,float pixels,FontStyle weight,Color color,RectangleF box,StringAlignment alignment,string family) {
      using(var font=new Font(family,pixels,weight,GraphicsUnit.Pixel))using(var brush=new SolidBrush(color))
      using(var format=new StringFormat(StringFormat.GenericTypographic)){format.Alignment=alignment;format.LineAlignment=StringAlignment.Center;format.FormatFlags|=StringFormatFlags.NoWrap;format.Trimming=StringTrimming.EllipsisCharacter;g.DrawString(text,font,brush,box,format);}
    }
    static float Measure(Graphics g,string text,float pixels,FontStyle weight) {
      using(var font=new Font("Segoe UI",pixels,weight,GraphicsUnit.Pixel))using(var format=new StringFormat(StringFormat.GenericTypographic))return g.MeasureString(text,font,1000,format).Width;
    }
    internal static Bitmap Render(UserPreferences options,QuotaState quota,float dpiScale,bool dark) {
      options=options??new UserPreferences();quota=quota??new QuotaState();
      Size logical=LogicalSize(options.CornerStyle);float factor=NormalizeScale(dpiScale);int width=(int)Math.Round(logical.Width*factor),height=(int)Math.Round(logical.Height*factor);
      var result=new Bitmap(width,height,PixelFormat.Format32bppPArgb);
      try {using(var g=Graphics.FromImage(result)) {
        g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;g.ScaleTransform(factor,factor);
        bool capsule=options.CornerStyle=="capsule",english=options.Language=="en";
        Color ink=dark?Color.FromArgb(251,246,252):Color.FromArgb(48,44,58),muted=dark?Color.FromArgb(225,216,228):Color.FromArgb(101,93,107);
        Color accent=Mix(options.AccentColor,dark?Color.White:Color.FromArgb(93,14,48),dark?.35f:.48f);
        if(quota.Stale)accent=dark?Color.FromArgb(194,183,199):Color.FromArgb(131,121,140);
        int alpha=(int)Math.Round(255*(1-Math.Max(10,Math.Min(80,options.GlassTransparency))/100.0));
        Color body=dark?Color.FromArgb(49,38,59):Color.FromArgb(255,248,253);
        var surface=new RectangleF(.5f,.5f,logical.Width-1,logical.Height-1);float radius=capsule?22:16;
        using(var path=Rounded(surface,radius)) {
          using(var fill=new SolidBrush(Color.FromArgb(alpha,body)))g.FillPath(fill,path);
          // Restrained reflective wash leaves even the most transparent setting genuinely clear.
          using(var wash=new LinearGradientBrush(surface,Color.FromArgb(dark?14:24,255,255,255),Color.FromArgb(0,255,255,255),65f))g.FillPath(wash,path);
          using(var edge=new LinearGradientBrush(surface,Color.FromArgb(dark?150:235,255,255,255),Color.FromArgb(dark?95:130,183,144,173),78f))using(var pen=new Pen(edge,1))g.DrawPath(pen,path);
          var inner=new RectangleF(2.5f,2.5f,logical.Width-5,logical.Height-5);
          using(var reflection=Rounded(inner,radius-2))using(var pen=new Pen(Color.FromArgb(dark?25:75,255,255,255),.8f))g.DrawPath(pen,reflection);
        }
        string value=ValueText(quota);double? remaining=Remaining(quota);bool known=remaining.HasValue;
        if(capsule) {
          string label=quota.Stale?(english?"stale":"旧值"):(english?"left":"剩余");
          float numberWidth=Measure(g,value,26,FontStyle.Bold),percentWidth=known?Measure(g,"%",14,FontStyle.Regular)+2:0;
          float labelWidth=english&&quota.Stale?31:24;
          float groupWidth=5+7+numberWidth+percentWidth+7+labelWidth,left=(132-groupWidth)/2;
          using(var brush=new SolidBrush(accent))g.FillEllipse(brush,left,19.5f,5,5);
          Label(g,value,26,FontStyle.Bold,ink,new RectangleF(left+12,5,numberWidth+1,32),StringAlignment.Near,"Segoe UI");
          if(known)Label(g,"%",14,FontStyle.Regular,ink,new RectangleF(left+12+numberWidth+2,14,percentWidth,21),StringAlignment.Near,"Segoe UI");
          Label(g,label,11,FontStyle.Regular,muted,new RectangleF(left+12+numberWidth+percentWidth+7,7,labelWidth+2,30),StringAlignment.Near,"Microsoft YaHei UI");
        }else {
          float numberWidth=Measure(g,value,36,FontStyle.Bold),percentWidth=known?Measure(g,"%",16,FontStyle.Regular)+2:0;
          float numberLeft=170-numberWidth-percentWidth;
          Label(g,english?"Left":"剩余额度",14,FontStyle.Bold,muted,new RectangleF(14,10,numberLeft-20,38),StringAlignment.Near,"Microsoft YaHei UI");
          Label(g,value,36,FontStyle.Bold,ink,new RectangleF(numberLeft,6,numberWidth+1,42),StringAlignment.Near,"Segoe UI");
          if(known)Label(g,"%",16,FontStyle.Regular,ink,new RectangleF(numberLeft+numberWidth+2,19,percentWidth,25),StringAlignment.Near,"Segoe UI");
          var track=new RectangleF(14,51,156,3);
          using(var path=Rounded(track,1.5f))using(var brush=new SolidBrush(Color.FromArgb(dark?48:42,accent)))g.FillPath(brush,path);
          if(remaining.HasValue&&remaining.Value>0)using(var path=Rounded(new RectangleF(track.X,track.Y,(float)(track.Width*remaining.Value/100),track.Height),1.5f))using(var brush=new SolidBrush(accent))g.FillPath(brush,path);
          Label(g,ResetText(quota,english),13,FontStyle.Bold,muted,new RectangleF(14,59,156,20),StringAlignment.Near,"Microsoft YaHei UI");
        }
      }return result;}catch{result.Dispose();throw;}
    }

    [StructLayout(LayoutKind.Sequential)] struct NativePoint {public int X,Y;public NativePoint(int x,int y){X=x;Y=y;}}
    [StructLayout(LayoutKind.Sequential)] struct NativeSize {public int Width,Height;public NativeSize(int width,int height){Width=width;Height=height;}}
    [StructLayout(LayoutKind.Sequential,Pack=1)] struct Blend {public byte Operation,Flags,Alpha,Format;}
    [StructLayout(LayoutKind.Sequential)] struct BitmapInfo {public uint Size;public int Width,Height;public ushort Planes,BitCount;public uint Compression,SizeImage;public int XPelsPerMeter,YPelsPerMeter;public uint ColorsUsed,ColorsImportant;}
    [DllImport("user32.dll",SetLastError=true)] static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr destinationDc,ref NativePoint destination,ref NativeSize size,IntPtr sourceDc,ref NativePoint source,uint key,ref Blend blend,uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc,ref BitmapInfo info,uint usage,out IntPtr bits,IntPtr section,uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    void SetLayeredBitmap(Bitmap bitmap) {
      IntPtr screenDc=GetDC(IntPtr.Zero),memoryDc=IntPtr.Zero,nativeBitmap=IntPtr.Zero,previous=IntPtr.Zero;
      try {
        memoryDc=CreateCompatibleDC(screenDc);
        var info=new BitmapInfo {Size=(uint)Marshal.SizeOf(typeof(BitmapInfo)),Width=bitmap.Width,Height=-bitmap.Height,Planes=1,BitCount=32};IntPtr bits;
        nativeBitmap=CreateDIBSection(screenDc,ref info,0,out bits,IntPtr.Zero,0);
        if(memoryDc==IntPtr.Zero||nativeBitmap==IntPtr.Zero)throw new Win32Exception();
        BitmapData data=bitmap.LockBits(new Rectangle(Point.Empty,bitmap.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
        try {int bytes=bitmap.Width*4;var row=new byte[bytes];for(int y=0;y<bitmap.Height;y++){Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),row,0,bytes);Marshal.Copy(row,0,IntPtr.Add(bits,y*bytes),bytes);}}
        finally {bitmap.UnlockBits(data);}
        previous=SelectObject(memoryDc,nativeBitmap);var destination=new NativePoint(Left,Top);var size=new NativeSize(bitmap.Width,bitmap.Height);var source=new NativePoint(0,0);var blend=new Blend {Alpha=255,Format=1};
        if(!UpdateLayeredWindow(Handle,screenDc,ref destination,ref size,memoryDc,ref source,0,ref blend,2))throw new Win32Exception(Marshal.GetLastWin32Error());
      }finally {
        if(previous!=IntPtr.Zero)SelectObject(memoryDc,previous);if(nativeBitmap!=IntPtr.Zero)DeleteObject(nativeBitmap);if(memoryDc!=IntPtr.Zero)DeleteDC(memoryDc);if(screenDc!=IntPtr.Zero)ReleaseDC(IntPtr.Zero,screenDc);
      }
    }
  }
}
