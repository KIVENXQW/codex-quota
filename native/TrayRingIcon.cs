using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;

namespace CodexQuota {
  // The approved B (thick refracted rim) study, rendered at four times the
  // destination size. All glass effects are local pixels; no screen capture.
  public static class TrayRingIcon {
    static readonly Color DefaultAccent=Color.FromArgb(218,145,170);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyIcon(IntPtr icon);

    internal static double? Normalize(double? remaining) {
      if(!remaining.HasValue||Double.IsNaN(remaining.Value)||Double.IsInfinity(remaining.Value))return null;
      return Math.Max(0,Math.Min(100,remaining.Value));
    }
    internal static string CenterText(double? remaining,bool stale) {
      double? value=Normalize(remaining);
      if(!value.HasValue)return "—";
      return stale?"!":Math.Round(value.Value).ToString("0",CultureInfo.InvariantCulture);
    }
    static void CheckSize(int size,string parameter) {
      if(size<1||size>256)throw new ArgumentOutOfRangeException(parameter,"Tray icon dimensions must be between 1 and 256 pixels.");
    }
    static int Channel(double value){return (int)Math.Max(0,Math.Min(255,Math.Round(value)));}
    static Color Shift(Color reference,Color accent,float amount) {
      return Color.FromArgb(reference.A,Channel(reference.R+(accent.R-DefaultAccent.R)*amount),Channel(reference.G+(accent.G-DefaultAccent.G)*amount),Channel(reference.B+(accent.B-DefaultAccent.B)*amount));
    }
    static Color Desaturate(Color value) {
      int grey=Channel(value.R*.2126+value.G*.7152+value.B*.0722);
      return Color.FromArgb(value.A,grey,grey,grey);
    }
    static Color Tone(Color reference,Color accent,float amount,bool stale) {
      Color value=Shift(reference,accent,amount);return stale?Desaturate(value):value;
    }
    static GraphicsPath Circle(RectangleF rectangle){var path=new GraphicsPath();path.AddEllipse(rectangle);return path;}
    static RectangleF Inset(RectangleF rectangle,float amount){return new RectangleF(rectangle.X+amount,rectangle.Y+amount,rectangle.Width-2*amount,rectangle.Height-2*amount);}
    static void Arc(Graphics graphics,RectangleF rectangle,Color first,Color last,float width,float start,float sweep) {
      if(sweep<=0)return;
      using(var brush=new LinearGradientBrush(rectangle,first,last,75f))using(var pen=new Pen(brush,width)) {
        pen.StartCap=pen.EndCap=LineCap.Round;
        if(sweep>=360)graphics.DrawEllipse(pen,rectangle);else graphics.DrawArc(pen,rectangle,start,sweep);
      }
    }

    public static Bitmap Render(int size,double? remaining,bool stale,Color accent,bool dark,bool updateAvailable) {
      CheckSize(size,"size");
      double? value=Normalize(remaining);
      string number=CenterText(value,stale);
      if(accent.IsEmpty)accent=DefaultAccent;
      int canvas=checked(size*4);
      using(var hi=new Bitmap(canvas,canvas,PixelFormat.Format32bppArgb)) {
        using(var g=Graphics.FromImage(hi)) {
          g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
          g.ScaleTransform(4,4);float s=size;
          var r=new RectangleF(s*.09f,s*.07f,s*.82f,s*.82f);
          var shadow=new RectangleF(r.X,r.Y+s*.045f,r.Width,r.Height);
          using(var path=Circle(shadow))using(var brush=new PathGradientBrush(path)) {
            brush.CenterColor=Tone(Color.FromArgb(dark?30:38,76,46,78),accent,.18f,stale);
            brush.SurroundColors=new[]{Color.Transparent};g.FillPath(brush,path);
          }
          using(var path=Circle(r))
          using(var fill=new LinearGradientBrush(r,Color.FromArgb(dark?77:132,255,255,255),Tone(Color.FromArgb(dark?55:56,211,188,223),accent,.18f,stale),70f))g.FillPath(fill,path);

          using(var rim=new LinearGradientBrush(r,Color.FromArgb(238,255,255,255),Tone(Color.FromArgb(150,168,140,172),accent,.18f,stale),75f))
          using(var pen=new Pen(rim,s*.13f))g.DrawEllipse(pen,Inset(r,s*.05f));
          Arc(g,Inset(r,s*.012f),Color.FromArgb(220,255,255,255),Tone(Color.FromArgb(100,213,193,224),accent,.18f,stale),s*.014f,185,155);
          Arc(g,Inset(r,s*.115f),Tone(Color.FromArgb(135,125,94,137),accent,.18f,stale),Tone(Color.FromArgb(40,240,226,242),accent,.18f,stale),s*.022f,15,165);

          var data=Inset(r,s*.053f);float thickness=s*.052f;
          Arc(g,data,Tone(Color.FromArgb(dark?84:92,189,159,187),accent,.18f,stale),Tone(Color.FromArgb(dark?64:88,211,189,211),accent,.18f,stale),thickness,-90,359);
          if(value.HasValue&&value.Value>0) {
            Color rose=Tone(dark?Color.FromArgb(237,171,198):Color.FromArgb(181,96,137),accent,1f,stale);
            Color deep=Tone(dark?Color.FromArgb(204,121,165):Color.FromArgb(151,67,110),accent,1f,stale);
            Arc(g,data,rose,deep,thickness,-90,value.Value>=100?360f:(float)(value.Value*3.6));
          }
          Color text=Tone(dark?Color.FromArgb(255,244,250):Color.FromArgb(91,53,76),accent,.25f,stale);
          using(var font=new Font("Segoe UI",s*(number.Length>=3?.29f:.35f),FontStyle.Bold,GraphicsUnit.Pixel))
          using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap})
          using(var brush=new SolidBrush(text))g.DrawString(number,font,brush,new RectangleF(-.2f,-s*.025f,s,s),format);

          if(updateAvailable) {
            var dot=new RectangleF(s*.7325f,s*.0425f,s*.215f,s*.215f);
            using(var brush=new SolidBrush(Shift(dark?Color.FromArgb(237,171,198):Color.FromArgb(151,67,110),accent,1f)))g.FillEllipse(brush,dot);
            using(var border=new Pen(dark?Color.FromArgb(245,52,47,60):Color.FromArgb(245,255,255,255),s*.035f))g.DrawEllipse(border,dot);
          }
        }
        var final=new Bitmap(size,size,PixelFormat.Format32bppArgb);
        try {
          using(var g=Graphics.FromImage(final)) {
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
            g.DrawImage(hi,new Rectangle(0,0,size,size),new Rectangle(0,0,canvas,canvas),GraphicsUnit.Pixel);
          }
          return final;
        }catch{final.Dispose();throw;}
      }
    }

    public static Icon Create(double? remaining,bool stale,Color accent,bool dark,bool updateAvailable,Size iconSize) {
      CheckSize(iconSize.Width,"iconSize");CheckSize(iconSize.Height,"iconSize");
      int side=Math.Min(iconSize.Width,iconSize.Height);
      using(var ring=Render(side,remaining,stale,accent,dark,updateAvailable))
      using(var canvas=new Bitmap(iconSize.Width,iconSize.Height,PixelFormat.Format32bppArgb)) {
        using(var graphics=Graphics.FromImage(canvas)) {
          graphics.Clear(Color.Transparent);graphics.DrawImageUnscaled(ring,(canvas.Width-side)/2,(canvas.Height-side)/2);
        }
        IntPtr handle=canvas.GetHicon();
        try { using(var icon=Icon.FromHandle(handle))return (Icon)icon.Clone(); }
        finally { if(handle!=IntPtr.Zero)DestroyIcon(handle); }
      }
    }
  }
}
