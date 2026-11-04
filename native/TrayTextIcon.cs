using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;

namespace CodexQuota {
  public static class TrayTextIcon {
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool DestroyIcon(IntPtr icon);
    internal static string CenterText(double? remaining,bool stale) {
      if(!remaining.HasValue||Double.IsNaN(remaining.Value)||Double.IsInfinity(remaining.Value))return "—";
      return stale?"!":Math.Round(Math.Max(0,Math.Min(100,remaining.Value))).ToString("0",CultureInfo.InvariantCulture);
    }
    static void CheckSize(Size size){if(size.Width<1||size.Width>256||size.Height<1||size.Height>256)throw new ArgumentOutOfRangeException("size","Tray icon dimensions must be between 1 and 256 pixels.");}
    internal static Bitmap Render(Size size,double? remaining,bool stale,Color accent,bool darkTaskbar,bool updateAvailable) {
      CheckSize(size);var bitmap=new Bitmap(size.Width,size.Height,PixelFormat.Format32bppArgb);
      try {using(var g=Graphics.FromImage(bitmap)) {
        g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        string value=CenterText(remaining,stale);
        Color ink=darkTaskbar?Color.FromArgb(251,248,252):Color.FromArgb(33,30,39);
        // Preserve cap height for 100: fit the glyph path horizontally instead of
        // shrinking all three digits into a short, faint line at 16 px.
        using(var family=new FontFamily("Segoe UI"))using(var path=new GraphicsPath())using(var cap=new GraphicsPath())using(var format=new StringFormat(StringFormat.GenericTypographic))using(var brush=new SolidBrush(ink)) {
          path.AddString(value,family,(int)FontStyle.Bold,size.Height,PointF.Empty,format);
          cap.AddString("0",family,(int)FontStyle.Bold,size.Height,PointF.Empty,format);
          RectangleF bounds=path.GetBounds(),capBounds=cap.GetBounds();float sy=size.Height*.72f/capBounds.Height;
          float sx=Math.Min(sy,(size.Width-1.5f)/bounds.Width);
          float left=(size.Width-bounds.Width*sx)/2,top=size.Height*.24f;
          using(var matrix=new Matrix(sx,0,0,sy,left-bounds.X*sx,top-capBounds.Y*sy)){path.Transform(matrix);g.FillPath(brush,path);}
        }
        if(updateAvailable) {
          float dot=Math.Max(3,Math.Min(size.Width,size.Height)*.18f);var bounds=new RectangleF(size.Width-dot-.25f,.25f,dot,dot);
          if(accent.IsEmpty)accent=Color.FromArgb(218,145,170);
          Color mark=darkTaskbar?Color.FromArgb(255,(accent.R+255)/2,(accent.G+255)/2,(accent.B+255)/2):Color.FromArgb(255,accent.R/2,accent.G/2,accent.B/2);
          using(var brush=new SolidBrush(mark))g.FillEllipse(brush,bounds);
        }
      }return bitmap;}catch{bitmap.Dispose();throw;}
    }
    public static Icon Create(double? remaining,bool stale,Color accent,bool darkTaskbar,bool updateAvailable,Size size) {
      using(var bitmap=Render(size,remaining,stale,accent,darkTaskbar,updateAvailable)) {
        IntPtr handle=bitmap.GetHicon();try{using(var icon=Icon.FromHandle(handle))return (Icon)icon.Clone();}finally{if(handle!=IntPtr.Zero)DestroyIcon(handle);}
      }
    }
  }
}
