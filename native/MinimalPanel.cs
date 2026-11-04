using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace CodexQuota {
  internal static class MinimalMaterial {
    internal static readonly Color Surface=Color.FromArgb(253,252,254),Ink=Color.FromArgb(52,50,56),Muted=Color.FromArgb(109,102,114),Line=Color.FromArgb(233,230,235);
    internal static void Icon(Graphics g,string kind,Rectangle bounds,Color color) {
      float s=Math.Min(bounds.Width,bounds.Height)/24f,cx=bounds.X+bounds.Width/2f,cy=bounds.Y+bounds.Height/2f;
      using(var pen=new Pen(color,Math.Max(1,1.35f*s))) {
        pen.StartCap=pen.EndCap=LineCap.Round;
        if(kind=="close"){g.DrawLine(pen,cx-3.5f*s,cy-3.5f*s,cx+3.5f*s,cy+3.5f*s);g.DrawLine(pen,cx+3.5f*s,cy-3.5f*s,cx-3.5f*s,cy+3.5f*s);}
        else if(kind=="refresh"){g.DrawArc(pen,cx-5*s,cy-5*s,10*s,10*s,35,300);g.DrawLines(pen,new[]{new PointF(cx+4.8f*s,cy-6*s),new PointF(cx+4.8f*s,cy-2*s),new PointF(cx+.8f*s,cy-2*s)});}
        else if(kind=="appearance") {g.DrawEllipse(pen,cx-2.7f*s,cy-2.7f*s,5.4f*s,5.4f*s);for(int i=0;i<8;i++){double angle=i*Math.PI/4;g.DrawLine(pen,cx+(float)Math.Cos(angle)*5*s,cy+(float)Math.Sin(angle)*5*s,cx+(float)Math.Cos(angle)*6.5f*s,cy+(float)Math.Sin(angle)*6.5f*s);}}
      }
    }
  }

  // The same account and local-activity data, presented without unsupported rate tiles.
  internal sealed class MinimalOverview : Control {
    readonly float scale;
    readonly Font normal,small,valueFont,workFont,secondaryFont;
    readonly ToolTip tooltip=new ToolTip();
    QuotaState state;
    List<QuotaWindow> secondary=new List<QuotaWindow>();
    string tokenValue="—",workValue="—",taskValue="—",tokenHint="",workHint="",scopeHint="";
    bool english,partial;
    Color accent=QuotaAppearance.Green;
    internal int DesiredHeight {get;private set;}
    internal MinimalOverview(float factor) {
      scale=factor;normal=new Font("Microsoft YaHei UI",13*scale,FontStyle.Regular,GraphicsUnit.Pixel);small=new Font("Microsoft YaHei UI",11*scale,FontStyle.Regular,GraphicsUnit.Pixel);
      valueFont=new Font("Segoe UI",26*scale,FontStyle.Regular,GraphicsUnit.Pixel);workFont=new Font("Microsoft YaHei UI",24*scale,FontStyle.Regular,GraphicsUnit.Pixel);secondaryFont=new Font("Microsoft YaHei UI",19*scale,FontStyle.Regular,GraphicsUnit.Pixel);
      SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;
      DesiredHeight=444;
    }
    string L(string zh,string en){return english?en:zh;}
    internal void SetData(QuotaState next,long? tokens,double? work,int? turns,string tokenScope,string workScope,string periodScope,bool workPartial,UserPreferences prefs) {
      state=next;english=prefs.Language=="en";accent=QuotaAppearance.Darken(prefs.AccentColor,.12f);partial=workPartial;
      tokenValue=UsageText.Number(tokens,true);workValue=UsageText.Duration(work,english,false);if(!english)workValue=workValue.Replace("时","小时");taskValue=turns.HasValue?turns.Value.ToString("N0",CultureInfo.InvariantCulture):"—";
      tokenHint=tokenScope;workHint=workScope;scopeHint=periodScope;
      secondary.Clear();QuotaWindow primary=state==null?null:state.Primary;
      if(state!=null&&state.Windows!=null)foreach(var window in state.Windows)if(window!=null&&!Object.ReferenceEquals(window,primary))secondary.Add(window);
      DesiredHeight=444+secondary.Count*56+(HasCredits?26:0);
      var description=new List<string>{L("账户剩余额度 ","Quota remaining ")+(primary==null?"—":QuotaAppearance.Percentage(primary.Remaining)),L("Token 用量 ","Token usage ")+tokenValue,L("AI 工作时长 ","AI work time ")+workValue,taskValue+L(" 个已完成回合"," completed turns")};
      foreach(var window in secondary)description.Add(WindowName(window)+" "+QuotaAppearance.Percentage(window.Remaining)+" "+Reset(window));
      if(HasCredits)description.Add(CreditsText());
      if(state==null||state.Stale||!state.Available)description.Add(L("缓存或暂不可用","Cached or unavailable"));
      AccessibleName=String.Join("; ",description.ToArray());tooltip.SetToolTip(this,AccessibleName+"\n"+(primary==null?"":WindowName(primary)+" · "+Reset(primary))+"\n"+tokenHint+"\n"+workHint);Invalidate();
    }
    bool HasCredits {get{return state!=null&&(!String.IsNullOrWhiteSpace(state.Credits)||state.ResetCredits.HasValue);}}
    string CreditsText(){return (!String.IsNullOrWhiteSpace(state.Credits)?L("积分 ","Credits ")+state.Credits:"")+(state.ResetCredits.HasValue?(String.IsNullOrWhiteSpace(state.Credits)?"":" · ")+L("重置次数 ","Resets ")+state.ResetCredits.Value:"");}
    string WindowName(QuotaWindow window){string name=window==null?"":window.Label??"";if(english){if(name.Contains("周"))return "Weekly";name=name.Replace("小时","hours");}return String.IsNullOrWhiteSpace(name)?L("账户额度","Account quota"):name;}
    string Reset(QuotaWindow window){DateTime? reset=window==null?null:QuotaAppearance.ResetTime(window.ResetAt);return reset.HasValue?L("重置 ","Resets ")+reset.Value.ToString("MM/dd HH:mm"):L("重置时间暂不可用","Reset time unavailable");}
    int Px(float value){return (int)Math.Round(value*scale);}
    void DrawText(Graphics g,string text,Font font,Rectangle rect,Color color,bool right){TextRenderer.DrawText(g,text,font,rect,color,TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding|TextFormatFlags.VerticalCenter|(right?TextFormatFlags.Right:TextFormatFlags.Left));}
    void Rule(Graphics g,int y){using(var pen=new Pen(MinimalMaterial.Line,scale))g.DrawLine(pen,0,Px(y),Width,Px(y));}
    void Subline(Graphics g,string left,string right,int y){int split=right.Length==0?Width:Width/2;DrawText(g,left,small,new Rectangle(0,Px(y),split,Px(18)),MinimalMaterial.Muted,false);if(right.Length>0)DrawText(g,right,small,new Rectangle(split,Px(y),Width-split,Px(18)),MinimalMaterial.Muted,true);}
    void Metric(Graphics g,string label,string value,int y,string hint,string status,bool work) {
      DrawText(g,label,normal,new Rectangle(0,Px(y+15),Width/2,Px(33)),MinimalMaterial.Ink,false);
      DrawText(g,value,work?workFont:valueFont,new Rectangle(Width/2,Px(y+9),Width/2,Px(40)),value=="—"?MinimalMaterial.Muted:MinimalMaterial.Ink,true);
      Subline(g,hint,status,y+53);Rule(g,y+107);
    }
    protected override void OnPaint(PaintEventArgs e) {
      base.OnPaint(e);if(Width<10)return;Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
      QuotaWindow primary=state==null?null:state.Primary;bool stale=state==null||state.Stale||!state.Available;
      DrawText(g,L("账户剩余额度","Quota remaining"),normal,new Rectangle(0,Px(13),Width/2,Px(33)),MinimalMaterial.Ink,false);
      string quotaText=primary==null?"—":QuotaAppearance.Percentage(primary.Remaining);
      if(quotaText.EndsWith("%",StringComparison.Ordinal)){DrawText(g,quotaText.Substring(0,quotaText.Length-1),valueFont,new Rectangle(Width/2,Px(7),Width/2-Px(17),Px(40)),stale?MinimalMaterial.Muted:MinimalMaterial.Ink,true);DrawText(g,"%",normal,new Rectangle(Width-Px(14),Px(19),Px(14),Px(22)),MinimalMaterial.Muted,true);}
      else DrawText(g,quotaText,valueFont,new Rectangle(Width/2,Px(7),Width/2,Px(40)),MinimalMaterial.Muted,true);
      using(var pen=new Pen(Color.FromArgb(236,229,233),3*scale)){pen.StartCap=pen.EndCap=LineCap.Round;g.DrawLine(pen,Px(1),Px(59),Width-Px(1),Px(59));}
      double? remaining=primary==null?null:primary.Remaining;
      if(remaining.HasValue&&!Double.IsNaN(remaining.Value)&&!Double.IsInfinity(remaining.Value)&&remaining.Value>0)using(var pen=new Pen(stale?Color.FromArgb(170,160,173):accent,3*scale)){pen.StartCap=pen.EndCap=LineCap.Round;g.DrawLine(pen,Px(1),Px(59),Px(1)+(Width-Px(2))*(float)Math.Min(100,remaining.Value)/100,Px(59));}
      Subline(g,stale?L("缓存 / 暂不可用","Cached / unavailable"):WindowName(primary),Reset(primary),73);Rule(g,118);
      int y=118;
      foreach(var window in secondary){DrawText(g,WindowName(window)+L("剩余"," remaining"),normal,new Rectangle(0,Px(y+6),Width*2/3,Px(24)),MinimalMaterial.Muted,false);DrawText(g,QuotaAppearance.Percentage(window.Remaining),secondaryFont,new Rectangle(Width*2/3,Px(y+3),Width/3,Px(28)),stale?MinimalMaterial.Muted:MinimalMaterial.Ink,true);DrawText(g,Reset(window),small,new Rectangle(0,Px(y+31),Width,Px(18)),MinimalMaterial.Muted,false);y+=56;Rule(g,y);}
      if(HasCredits){DrawText(g,CreditsText(),small,new Rectangle(0,Px(y+4),Width,Px(20)),MinimalMaterial.Muted,false);y+=26;Rule(g,y);}
      Metric(g,L("Token 用量","Token usage"),tokenValue,y,tokenHint,tokenValue=="—"?L("暂无记录","No record"):"",false);y+=107;
      Metric(g,L("AI 工作时长","AI work time"),workValue,y,L("本机已完成回合","Completed turns on this PC"),L("并行时段已去重","Overlaps merged"),true);y+=107;
      DrawText(g,taskValue+L(" 个已完成回合"," completed turns"),normal,new Rectangle(0,Px(y+17),Width*2/3,Px(24)),MinimalMaterial.Ink,false);
      DrawText(g,partial?L("部分记录","Partial records"):scopeHint,small,new Rectangle(Width*2/3,Px(y+17),Width/3,Px(24)),MinimalMaterial.Muted,true);
      string note=L("Token 来自账户统计；工作时长来自本机。\n缺失数据保留为 —，不换算为每小时消耗。","Tokens cover the account; work time covers this PC.\nMissing data stays —; no per-hour rate is inferred.");
      TextRenderer.DrawText(g,note,small,new Rectangle(0,Px(y+61),Width,Px(49)),MinimalMaterial.Muted,TextFormatFlags.WordBreak|TextFormatFlags.NoPadding|TextFormatFlags.EndEllipsis);
    }
    protected override void Dispose(bool disposing){if(disposing){normal.Dispose();small.Dispose();valueFont.Dispose();workFont.Dispose();secondaryFont.Dispose();tooltip.Dispose();}base.Dispose(disposing);}
  }
}
