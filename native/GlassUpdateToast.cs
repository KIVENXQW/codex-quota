using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexQuota {
  // A passive reminder above the tray. Persistent state lives in the panel.
  internal sealed class GlassUpdateToast : Form {
    readonly Timer expiry=new Timer();
    readonly Font headingFont,bodyFont;
    readonly float scale;
    Color accent=Color.FromArgb(218,145,170);
    string headline,description;
    Rectangle dismissArea;
    public event EventHandler OpenRequested;
    public GlassUpdateToast() {
      using(var g=Graphics.FromHwnd(IntPtr.Zero))scale=Math.Max(1,g.DpiX/96f);
      AutoScaleMode=AutoScaleMode.None;FormBorderStyle=FormBorderStyle.None;
      ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;TopMost=true;DoubleBuffered=true;
      BackColor=Color.FromArgb(247,241,248);Size=new Size(Px(294),Px(88));
      headingFont=new Font("Microsoft YaHei UI",13*scale,FontStyle.Regular,GraphicsUnit.Pixel);
      bodyFont=new Font("Microsoft YaHei UI",11*scale,FontStyle.Regular,GraphicsUnit.Pixel);
      AccessibleRole=AccessibleRole.Alert;Cursor=Cursors.Hand;
      expiry.Interval=15000;expiry.Tick+=delegate{Hide();};
      VisibleChanged+=delegate{if(!Visible)expiry.Stop();};
    }
    int Px(int value){return (int)Math.Round(value*scale);}
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {
      get{var p=base.CreateParams;p.ExStyle|=0x08000080;p.ClassStyle|=0x00020000;return p;}
    }
    public void NotifyAvailable(string version,UserPreferences preferences,Rectangle workArea) {
      bool english=preferences.Language=="en";accent=preferences.AccentColor;
      headline=english?"An update is available":"有更新";
      description=(String.IsNullOrWhiteSpace(version)?"":"v"+version+" · ")+(english?"View what's new":"查看更新内容");
      AccessibleName=headline+" "+description;
      Location=new Point(Math.Max(workArea.Left,workArea.Right-Width-Px(14)),Math.Max(workArea.Top,workArea.Bottom-Height-Px(12)));
      Invalidate();Show();expiry.Stop();expiry.Start();
    }
    protected override void OnResize(EventArgs e) {
      base.OnResize(e);
      if(Width<=0||Height<=0)return;
      var old=Region;
      using(var path=QuotaAppearance.RoundRectangle(new RectangleF(0,0,Width,Height),Px(19)))Region=new Region(path);
      if(old!=null)old.Dispose();
      dismissArea=new Rectangle(Width-Px(40),Px(7),Px(32),Px(32));
    }
    protected override void OnPaint(PaintEventArgs e) {
      var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
      using(var path=QuotaAppearance.RoundRectangle(new RectangleF(.5f,.5f,Width-1,Height-1),Px(19))) {
        using(var fill=new LinearGradientBrush(ClientRectangle,Color.FromArgb(255,253,255),QuotaAppearance.Blend(Color.FromArgb(241,240,249),accent,.10f),50f))g.FillPath(fill,path);
        using(var edge=new Pen(Color.FromArgb(250,255,255,255),1))g.DrawPath(edge,path);
      }
      using(var dot=new SolidBrush(accent))g.FillEllipse(dot,Px(18),Px(24),Px(7),Px(7));
      TextRenderer.DrawText(g,headline??"",headingFont,new Rectangle(Px(36),Px(16),Width-Px(82),Px(24)),QuotaAppearance.Text,TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
      TextRenderer.DrawText(g,description??"",bodyFont,new Rectangle(Px(36),Px(44),Width-Px(54),Px(24)),Color.FromArgb(105,91,108),TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
      using(var pen=new Pen(Color.FromArgb(114,105,119),1.3f*scale)) {
        int centerX=dismissArea.Left+dismissArea.Width/2,centerY=dismissArea.Top+dismissArea.Height/2,r=Px(4);
        g.DrawLine(pen,centerX-r,centerY-r,centerX+r,centerY+r);g.DrawLine(pen,centerX-r,centerY+r,centerX+r,centerY-r);
      }
    }
    protected override void OnMouseUp(MouseEventArgs e) {
      base.OnMouseUp(e);if(e.Button!=MouseButtons.Left)return;Hide();
      if(dismissArea.Contains(e.Location))return;
      var handler=OpenRequested;if(handler!=null)handler(this,EventArgs.Empty);
    }
    protected override void Dispose(bool disposing) {
      if(disposing){expiry.Dispose();headingFont.Dispose();bodyFont.Dispose();}base.Dispose(disposing);
    }
  }
}
