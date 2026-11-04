using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace CodexQuota {
  // A passive hover card. Its chart owns hover/scroll/hide lifetime; this window
  // never activates or consumes the pointer that is selecting the chart bucket.
  internal sealed class UsageDetailPopup : Form {
    static readonly Color DefaultAccent=Color.FromArgb(218,145,170);
    readonly float scale;
    readonly Font dateFont,smallFont,labelFont,tokenFont,workFont,tasksFont;
    readonly List<string> notes=new List<string>();
    Color accent=DefaultAccent;
    string dateText="—",weekdayText="",tokenText="—",workText="—",tasksText="—";
    bool english,ready;
    int logicalHeight=289;

    internal UsageDetailPopup(float scale) {
      this.scale=Single.IsNaN(scale)||Single.IsInfinity(scale)||scale<=0?1:scale;
      AutoScaleMode=AutoScaleMode.None;FormBorderStyle=FormBorderStyle.None;
      StartPosition=FormStartPosition.Manual;ShowInTaskbar=false;TopMost=true;
      DoubleBuffered=true;TabStop=false;
      SetStyle(ControlStyles.Selectable,false);
      BackColor=Color.FromArgb(245,240,248);
      AccessibleRole=AccessibleRole.ToolTip;
      dateFont=new Font("Microsoft YaHei UI",14,FontStyle.Bold,GraphicsUnit.Pixel);
      smallFont=new Font("Microsoft YaHei UI",11,FontStyle.Regular,GraphicsUnit.Pixel);
      labelFont=new Font("Microsoft YaHei UI",12,FontStyle.Regular,GraphicsUnit.Pixel);
      tokenFont=new Font("Microsoft YaHei UI",23,FontStyle.Bold,GraphicsUnit.Pixel);
      workFont=new Font("Microsoft YaHei UI",15,FontStyle.Bold,GraphicsUnit.Pixel);
      tasksFont=new Font("Microsoft YaHei UI",18,FontStyle.Bold,GraphicsUnit.Pixel);
      ready=true;SetData(null,DefaultAccent,false);
    }
    int Px(int value){return Math.Max(1,(int)Math.Round(value*scale));}
    string L(string zh,string en){return english?en:zh;}
    static bool KnownWork(double? seconds){return seconds.HasValue&&!Double.IsNaN(seconds.Value)&&!Double.IsInfinity(seconds.Value)&&seconds.Value>=0&&seconds.Value<Int64.MaxValue-1d;}
    static string WorkTime(double? seconds,bool english) {
      if(!KnownWork(seconds))return "—";
      long total=(long)Math.Round(seconds.Value),hours=total/3600,minutes=(total%3600)/60,remainder=total%60;
      return english?hours+"h "+minutes+"m "+remainder+"s":hours+"时"+minutes+"分"+remainder+"秒";
    }
    internal void SetData(ChartBucket bucket,Color accent,bool english) {
      if(IsDisposed)return;
      if(InvokeRequired){BeginInvoke(new Action<ChartBucket,Color,bool>(SetData),bucket,accent,english);return;}
      this.accent=accent.IsEmpty?DefaultAccent:accent;this.english=english;
      BackColor=Tone(Color.FromArgb(245,240,248),.14f);
      dateText="—";weekdayText="";
      if(bucket!=null&&bucket.Start!=DateTime.MinValue) {
        DateTime start=bucket.Start.Date,end=bucket.End.Date>=start?bucket.End.Date:start;
        if(start==end) {
          dateText=english?start.ToString("MMMM d, yyyy",CultureInfo.InvariantCulture):start.ToString("yyyy 年 M 月 d 日",CultureInfo.InvariantCulture);
          weekdayText=start.ToString("dddd",english?CultureInfo.InvariantCulture:CultureInfo.GetCultureInfo("zh-CN"));
        }else {
          dateText=start.ToString("yyyy/MM/dd",CultureInfo.InvariantCulture)+" — "+end.ToString("yyyy/MM/dd",CultureInfo.InvariantCulture);
          weekdayText=english?"Period total · UTC+8":"时段合计 · 北京时间";
        }
      }
      bool hasTokens=bucket!=null&&bucket.Tokens.HasValue&&bucket.Tokens.Value>=0;
      bool hasWork=bucket!=null&&KnownWork(bucket.WorkSeconds),hasTasks=bucket!=null&&bucket.Tasks.HasValue&&bucket.Tasks.Value>=0;
      tokenText=hasTokens?bucket.Tokens.Value.ToString("N0",CultureInfo.InvariantCulture):"—";
      workText=WorkTime(bucket==null?null:bucket.WorkSeconds,english);
      tasksText=hasTasks?bucket.Tasks.Value.ToString("N0",CultureInfo.InvariantCulture):"—";
      notes.Clear();
      if(!hasTokens)notes.Add(L("Token 记录：暂无记录","Token records: unavailable"));
      else if(bucket.TokenPartial)notes.Add(L("Token 记录：部分覆盖","Token records: partial"));
      if(!hasWork&&!hasTasks)notes.Add(L("本机工作记录：暂无记录","Local work records: unavailable"));
      else if(bucket.WorkPartial||!hasWork||!hasTasks)notes.Add(L("本机工作记录：部分覆盖","Local work records: partial"));
      if(notes.Count==0)notes.Add(L("Tokens：账户 · 时长与任务：本机","Tokens: account · work and tasks: this PC"));
      logicalHeight=289+(notes.Count-1)*22;
      ClientSize=new Size(Px(312),Px(logicalHeight));
      Text=L("用量详情","Usage details");AccessibleName=Text+" · "+dateText;
      AccessibleDescription=dateText+"\n"+weekdayText+"\n"+L("Token 消耗：","Tokens: ")+tokenText+"\n"+L("AI 工作时长：","AI work time: ")+workText+"\n"+L("完成任务：","Completed tasks: ")+tasksText+"\n"+String.Join("\n",notes.ToArray());
      UpdateShape();Invalidate();
    }

    internal static Rectangle CalculatePlacement(Rectangle anchorScreen,Size popupSize,Rectangle workArea) {
      int width=Math.Min(Math.Max(1,popupSize.Width),Math.Max(1,workArea.Width));
      int height=Math.Min(Math.Max(1,popupSize.Height),Math.Max(1,workArea.Height));
      long candidate=(long)anchorScreen.Right+10;
      if(candidate+width>workArea.Right)candidate=(long)anchorScreen.Left-10-width;
      int x=(int)Math.Max(workArea.Left,Math.Min(candidate,(long)workArea.Right-width));
      int y=(int)Math.Max(workArea.Top,Math.Min((long)anchorScreen.Top,(long)workArea.Bottom-height));
      return new Rectangle(x,y,width,height);
    }
    internal void ShowFor(Control source,Rectangle anchor) {
      if(IsDisposed)return;
      if(source==null||source.IsDisposed||!source.Visible){Hide();return;}
      Rectangle screenAnchor=source.RectangleToScreen(anchor);
      Form owner=source.FindForm();
      if(owner==this){Hide();return;}
      Rectangle placement=CalculatePlacement(screenAnchor,new Size(Px(312),Px(logicalHeight)),Screen.FromRectangle(screenAnchor).WorkingArea);
      Bounds=placement;
      if(Owner!=owner)Owner=owner;
      if(!Visible){if(owner==null)Show();else Show(owner);}
    }
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {
      get {var p=base.CreateParams;p.ExStyle|=0x08000080|0x00000020;p.ClassStyle|=0x00020000;return p;}
    }
    protected override void WndProc(ref Message message) {
      if(message.Msg==0x0021){message.Result=new IntPtr(3);return;} // MA_NOACTIVATE
      if(message.Msg==0x0084){message.Result=new IntPtr(-1);return;} // HTTRANSPARENT
      base.WndProc(ref message);
    }
    protected override void OnResize(EventArgs e){base.OnResize(e);if(ready)UpdateShape();}
    void UpdateShape() {
      if(ClientSize.Width<=0||ClientSize.Height<=0)return;
      float visual=Math.Min(ClientSize.Width/312f,ClientSize.Height/(float)logicalHeight);
      Region old=Region;using(var path=Round(new RectangleF(0,0,ClientSize.Width,ClientSize.Height),18*visual))Region=new Region(path);if(old!=null)old.Dispose();
    }
    static GraphicsPath Round(RectangleF r,float radius) {
      var path=new GraphicsPath();float d=Math.Max(.1f,Math.Min(radius*2,Math.Min(r.Width,r.Height)));
      path.AddArc(r.Left,r.Top,d,d,180,90);path.AddArc(r.Right-d,r.Top,d,d,270,90);path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.Left,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
    Color Tone(Color reference,float amount) {
      return Color.FromArgb(reference.A,Channel(reference.R+(accent.R-DefaultAccent.R)*amount),Channel(reference.G+(accent.G-DefaultAccent.G)*amount),Channel(reference.B+(accent.B-DefaultAccent.B)*amount));
    }
    static int Channel(double value){return (int)Math.Max(0,Math.Min(255,Math.Round(value)));}
    void DrawText(Graphics graphics,string text,Font font,Color color,RectangleF bounds) {
      using(var format=new StringFormat{FormatFlags=StringFormatFlags.NoWrap})using(var brush=new SolidBrush(color)) {
        Font fitted=null;
        try {
          float size=font.Size;
          while(size>10&&graphics.MeasureString(text,fitted??font,Int32.MaxValue,format).Width>bounds.Width) {
            if(fitted!=null)fitted.Dispose();size=Math.Max(10,size-.5f);fitted=new Font(font.FontFamily,size,font.Style,GraphicsUnit.Pixel);
          }
          graphics.DrawString(text,fitted??font,brush,bounds,format);
        }finally{if(fitted!=null)fitted.Dispose();}
      }
    }
    protected override void OnPaintBackground(PaintEventArgs e) {e.Graphics.Clear(BackColor);}
    protected override void OnPaint(PaintEventArgs e) {
      base.OnPaint(e);
      var g=e.Graphics;var saved=g.Save();
      try {
        g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        float visual=Math.Min(ClientSize.Width/312f,ClientSize.Height/(float)logicalHeight);g.ScaleTransform(visual,visual);
        var card=new RectangleF(0,0,312,logicalHeight);
        using(var path=Round(card,18))using(var fill=new LinearGradientBrush(card,Color.FromArgb(255,253,255),Tone(Color.FromArgb(245,240,248),.14f),48f))g.FillPath(fill,path);
        using(var path=Round(new RectangleF(.5f,.5f,311,logicalHeight-1),18))using(var line=new Pen(Color.FromArgb(240,255,255,255)))g.DrawPath(line,path);
        Color ink=Tone(Color.FromArgb(51,43,56),.08f),muted=Tone(Color.FromArgb(117,105,124),.08f),divider=Tone(Color.FromArgb(31,159,116,151),.20f);
        DrawText(g,dateText,dateFont,ink,new RectangleF(18,16,276,24));
        DrawText(g,weekdayText,smallFont,muted,new RectangleF(18,42,276,20));
        DrawText(g,L("Token 消耗","Tokens used"),labelFont,muted,new RectangleF(18,80,276,22));
        DrawText(g,tokenText,tokenFont,ink,new RectangleF(17,105,277,37));
        using(var line=new Pen(divider))g.DrawLine(line,18,154,294,154);
        DrawText(g,L("AI 工作时长","AI work time"),smallFont,muted,new RectangleF(18,172,162,22));
        DrawText(g,L("完成任务","Tasks done"),smallFont,muted,new RectangleF(202,172,92,22));
        DrawText(g,workText,workFont,ink,new RectangleF(18,199,180,28));
        DrawText(g,tasksText,tasksFont,ink,new RectangleF(202,196,92,31));
        using(var line=new Pen(divider))g.DrawLine(line,18,244,294,244);
        for(int i=0;i<notes.Count;i++) {
          using(var path=Round(new RectangleF(18,261+i*22,5,5),2.5f))using(var brush=new SolidBrush(Tone(Color.FromArgb(184,104,149),.65f)))g.FillPath(brush,path);
          DrawText(g,notes[i],smallFont,muted,new RectangleF(30,254+i*22,264,22));
        }
      }finally{g.Restore(saved);}
    }
    protected override void Dispose(bool disposing) {
      base.Dispose(disposing);
      if(disposing){dateFont.Dispose();smallFont.Dispose();labelFont.Dispose();tokenFont.Dispose();workFont.Dispose();tasksFont.Dispose();}
    }
  }
}
