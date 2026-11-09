using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace CodexQuota {
  // The chart or hint presenter owns this passive window's hover and scroll lifetime.
  internal sealed class UsageDetailPopup : Form {
    const int BaseWidth=252, BaseHeight=126, PaddingX=14, ColumnGap=12, NoteHeight=18;
    const int HintPaddingX=11, HintPaddingY=7, MaxHintTextWidth=296;
    readonly float scale;
    readonly Font dateFont,smallFont,labelFont,valueFont;
    readonly List<string> notes=new List<string>();
    string dateText="—",weekdayText="",headerHint="",tokenText="—",workText="—",tasksText="—";
    string hintText;
    bool english,ready;
    Control chartSource;
    int logicalWidth=BaseWidth,logicalHeight=BaseHeight;
    Size PreferredCardSize { get {return new Size(Px(logicalWidth),Px(logicalHeight));} }

    internal UsageDetailPopup(float scale) {
      this.scale=Single.IsNaN(scale)||Single.IsInfinity(scale)||scale<=0?1:scale;
      AutoScaleMode=AutoScaleMode.None;FormBorderStyle=FormBorderStyle.None;
      // An owned window stays above its chart. Form.TopMost would activate it on Show.
      StartPosition=FormStartPosition.Manual;ShowInTaskbar=false;
      DoubleBuffered=true;TabStop=false;
      SetStyle(ControlStyles.Selectable,false);
      BackColor=MinimalMaterial.Surface;
      AccessibleRole=AccessibleRole.ToolTip;
      dateFont=new Font("Microsoft YaHei UI",14,FontStyle.Bold,GraphicsUnit.Pixel);
      smallFont=new Font("Microsoft YaHei UI",11,FontStyle.Regular,GraphicsUnit.Pixel);
      labelFont=new Font("Microsoft YaHei UI",12,FontStyle.Regular,GraphicsUnit.Pixel);
      valueFont=new Font("Microsoft YaHei UI",13,FontStyle.Regular,GraphicsUnit.Pixel);
      ready=true;SetData(null,Color.Empty,false);
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
      hintText=null;
      this.english=english;
      dateText="—";weekdayText="";headerHint="";
      if(bucket!=null&&bucket.Start!=DateTime.MinValue) {
        DateTime start=bucket.Start.Date,end=bucket.End.Date>=start?bucket.End.Date:start;
        if(start==end) {
          dateText=english?start.ToString("MMMM d, yyyy",CultureInfo.InvariantCulture):start.ToString("yyyy 年 M 月 d 日",CultureInfo.InvariantCulture);
          weekdayText=start.ToString("dddd",english?CultureInfo.InvariantCulture:CultureInfo.GetCultureInfo("zh-CN"));
          headerHint=weekdayText;
        }else {
          dateText=start.ToString("yyyy/MM/dd",CultureInfo.InvariantCulture)+" — "+end.ToString("yyyy/MM/dd",CultureInfo.InvariantCulture);
          weekdayText=english?"Period total · UTC+8":"时段合计 · 北京时间";
          headerHint=english?"UTC+8":"北京时间";
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
      logicalHeight=BaseHeight+(notes.Count>0?3+notes.Count*NoteHeight:0);
      logicalWidth=MeasureCardWidth();
      ClientSize=PreferredCardSize;
      Text=L("用量详情","Usage details");AccessibleName=Text+" · "+dateText;
      AccessibleDescription=dateText+"\n"+weekdayText+"\n"+L("Token 消耗：","Tokens: ")+tokenText+"\n"+L("AI 工作时长：","AI work time: ")+workText+"\n"+L("完成任务：","Completed tasks: ")+tasksText
        +"\n"+L("Tokens：账户 · 时长与任务：本机","Tokens: account · work and tasks: this PC");
      if(notes.Count>0)AccessibleDescription+="\n"+String.Join("\n",notes.ToArray());
      Invalidate();
    }

    internal void SetHint(string text) {
      if(IsDisposed)return;
      if(InvokeRequired){BeginInvoke(new Action<string>(SetHint),text);return;}
      hintText=text??String.Empty;
      using(var bitmap=new Bitmap(1,1))
      using(var g=Graphics.FromImage(bitmap))
      using(var format=StringFormat.GenericTypographic) {
        g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        float naturalWidth=g.MeasureString(hintText,valueFont,Int32.MaxValue,format).Width;
        int textWidth=Math.Min(MaxHintTextWidth,(int)Math.Ceiling(naturalWidth)+2);
        SizeF wrapped=g.MeasureString(hintText,valueFont,new SizeF(textWidth,Int32.MaxValue),format);
        logicalWidth=2*HintPaddingX+textWidth;
        logicalHeight=2*HintPaddingY+(int)Math.Ceiling(wrapped.Height)+2;
      }
      ClientSize=PreferredCardSize;
      Text=hintText;AccessibleName=hintText;AccessibleDescription=hintText;
      Invalidate();
    }

    string TokenLabel { get { return L("账户 Tokens","Account tokens"); } }
    string WorkLabel { get { return L("工作时长","Work time"); } }
    string TaskLabel { get { return L("完成任务","Tasks done"); } }
    static float Measure(Graphics g,string text,Font font) {
      using(var format=StringFormat.GenericTypographic)return g.MeasureString(text,font,Int32.MaxValue,format).Width;
    }
    int MeasureCardWidth() {
      using(var bitmap=new Bitmap(1,1))
      using(var g=Graphics.FromImage(bitmap)) {
        g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        float labelWidth=Math.Max(Measure(g,TokenLabel,labelFont),Math.Max(Measure(g,WorkLabel,labelFont),Measure(g,TaskLabel,labelFont)));
        float valueWidth=Math.Max(Measure(g,tokenText,valueFont),Math.Max(Measure(g,workText,valueFont),Measure(g,tasksText,valueFont)));
        float contentWidth=Math.Max(labelWidth+ColumnGap+valueWidth,Measure(g,dateText,dateFont)+ColumnGap+Measure(g,headerHint,smallFont));
        foreach(string note in notes)contentWidth=Math.Max(contentWidth,Measure(g,note,smallFont));
        return Math.Max(BaseWidth,2*PaddingX+(int)Math.Ceiling(contentWidth)+2);
      }
    }

    internal static Rectangle CalculatePlacement(Rectangle anchorScreen,Size popupSize,Rectangle workArea) {
      int width=Math.Min(Math.Max(1,popupSize.Width),Math.Max(1,workArea.Width));
      int height=Math.Min(Math.Max(1,popupSize.Height),Math.Max(1,workArea.Height));
      const int gap=10;
      long left=(long)anchorScreen.Right+gap,top=anchorScreen.Top;
      if(left+width>workArea.Right) {
        left=(long)anchorScreen.Left-gap-width;
        if(left<workArea.Left) {
          // No room beside this cell. Try vertically before overlapping the anchor.
          left=anchorScreen.Left;
          long below=(long)anchorScreen.Bottom+gap,above=(long)anchorScreen.Top-gap-height;
          if(below+height<=workArea.Bottom)top=below;
          else if(above>=workArea.Top)top=above;
        }
      }
      int x=(int)Math.Max(workArea.Left,Math.Min(left,(long)workArea.Right-width));
      int y=(int)Math.Max(workArea.Top,Math.Min(top,(long)workArea.Bottom-height));
      return new Rectangle(x,y,width,height);
    }
    internal void ShowFor(Control source,Rectangle anchor) {
      if(IsDisposed)return;
      if(source==null||source.IsDisposed||!source.Visible){Hide();return;}
      chartSource=source;
      Rectangle screenAnchor=source.RectangleToScreen(anchor);
      Form owner=source.FindForm();
      if(owner==this){Hide();return;}
      Rectangle placement=CalculatePlacement(screenAnchor,PreferredCardSize,Screen.FromRectangle(screenAnchor).WorkingArea);
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
      if(message.Msg==GlassPagePanel.VerticalWheelMessage && Visible) {
        Hide();
        for(Control parent=chartSource;parent!=null;parent=parent.Parent) {
          var page=parent as GlassPagePanel;
          if(page!=null && page.ForwardWheel(ref message))return;
        }
      }
      base.WndProc(ref message);
    }
    protected override void OnResize(EventArgs e){base.OnResize(e);if(ready)UpdateShape();}
    void UpdateShape() {
      if(ClientSize.Width<=0||ClientSize.Height<=0)return;
      float visual=Math.Min(ClientSize.Width/(float)logicalWidth,ClientSize.Height/(float)logicalHeight);
      Region old=Region;using(var path=Round(new RectangleF(0,0,ClientSize.Width,ClientSize.Height),6*visual))Region=new Region(path);if(old!=null)old.Dispose();
    }
    static GraphicsPath Round(RectangleF r,float radius) {
      var path=new GraphicsPath();float d=Math.Max(.1f,Math.Min(radius*2,Math.Min(r.Width,r.Height)));
      path.AddArc(r.Left,r.Top,d,d,180,90);path.AddArc(r.Right-d,r.Top,d,d,270,90);path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.Left,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
    static void DrawText(Graphics g,string text,Font font,Color color,RectangleF bounds,bool right) {
      using(var format=StringFormat.GenericTypographic)
      using(var brush=new SolidBrush(color)) {
        format.FormatFlags|=StringFormatFlags.NoWrap;
        format.Alignment=right?StringAlignment.Far:StringAlignment.Near;
        format.LineAlignment=StringAlignment.Center;
        g.DrawString(text,font,brush,bounds,format);
      }
    }
    void DrawRow(Graphics g,int y,string label,string value,float contentWidth) {
      var bounds=new RectangleF(PaddingX,y,contentWidth,23);
      DrawText(g,label,labelFont,MinimalMaterial.Muted,bounds,false);
      DrawText(g,value,valueFont,value=="—"?MinimalMaterial.Muted:MinimalMaterial.Ink,bounds,true);
    }
    protected override void OnPaintBackground(PaintEventArgs e) {e.Graphics.Clear(BackColor);}
    protected override void OnPaint(PaintEventArgs e) {
      base.OnPaint(e);
      if(ClientSize.Width<=0||ClientSize.Height<=0)return;
      var g=e.Graphics;
      var saved=g.Save();
      try {
        g.SmoothingMode=SmoothingMode.AntiAlias;
        g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        // If a monitor is narrower than the natural card, scale all labels and values together.
        float visual=Math.Min(ClientSize.Width/(float)logicalWidth,ClientSize.Height/(float)logicalHeight);
        g.ScaleTransform(visual,visual);
        float width=ClientSize.Width/visual,height=ClientSize.Height/visual;
        float contentWidth=width-2*PaddingX;
        using(var path=Round(new RectangleF(.5f,.5f,width-1,height-1),6))
        using(var pen=new Pen(MinimalMaterial.Line)) {
          g.DrawPath(pen,path);
          if(hintText==null)g.DrawLine(pen,PaddingX,40.5f,width-PaddingX,40.5f);
        }
        if(hintText!=null) {
          var textArea=new RectangleF(HintPaddingX,HintPaddingY,width-2*HintPaddingX,height-2*HintPaddingY);
          using(var brush=new SolidBrush(MinimalMaterial.Ink))
          using(var format=StringFormat.GenericTypographic)g.DrawString(hintText,valueFont,brush,textArea,format);
          return;
        }
        var header=new RectangleF(PaddingX,9,contentWidth,23);
        DrawText(g,dateText,dateFont,MinimalMaterial.Ink,header,false);
        DrawText(g,headerHint,smallFont,MinimalMaterial.Muted,header,true);
        DrawRow(g,48,TokenLabel,tokenText,contentWidth);
        DrawRow(g,71,WorkLabel,workText,contentWidth);
        DrawRow(g,94,TaskLabel,tasksText,contentWidth);
        for(int i=0;i<notes.Count;i++) {
          var bounds=new RectangleF(PaddingX,121+i*NoteHeight,contentWidth,NoteHeight);
          DrawText(g,notes[i],smallFont,MinimalMaterial.Muted,bounds,false);
        }
      } finally {g.Restore(saved);}
    }
    protected override void Dispose(bool disposing) {
      base.Dispose(disposing);
      if(disposing){dateFont.Dispose();smallFont.Dispose();labelFont.Dispose();valueFont.Dispose();}
    }
  }
}
