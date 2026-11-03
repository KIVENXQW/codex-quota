using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexQuota {
  public sealed class GlassUpdateView : UserControl, IUpdateStatusView {
    readonly float scale;
    readonly Font regular,small,large;
    readonly Panel content;
    readonly UpdateSurface versionCard,notesCard;
    readonly Label headline,target,current,intro,notesTitle,scope,phase,percentage,steps,notice;
    readonly TextBox notes;
    readonly UpdateActionButton back,primary,secondary;
    readonly GlassProgress progress;
    UserPreferences preferences;
    UpdateRelease release;
    string mode="idle",infoTitle="",infoDetail="",progressDetail="";
    int percent;
    bool error,cancelRequested;
    public event EventHandler InstallRequested,CancelRequested,OpenRequested,BackRequested,StateChanged;
    public bool UpdateAvailable {get{return release!=null;}}
    public bool Busy {get{return mode=="checking"||mode=="progress"||mode=="handoff";}}
    public string CurrentVersion {get{return AppVersion.Current;}}
    public string TargetVersion {get{return release==null?"":release.Version;}}
    public string StatusText {get{return mode=="checking"?L("检查中","Checking"):mode=="handoff"?L("正在重启","Restarting"):mode=="progress"?L("更新 ","Updating ")+Math.Max(0,percent)+"%":UpdateAvailable?L("有更新","Update available"):"";}}
    bool English {get{return preferences!=null&&preferences.Language=="en";}}
    Color Accent {get{return preferences==null?Color.FromArgb(218,145,170):preferences.AccentColor;}}
    int Px(int value){return (int)Math.Round(value*scale);}
    string L(string zh,string en){return English?en:zh;}

    public GlassUpdateView(UserPreferences prefs) {
      using(var g=Graphics.FromHwnd(IntPtr.Zero))scale=Math.Max(1,g.DpiX/96f);
      SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);
      BackColor=Color.Transparent;AutoScaleMode=AutoScaleMode.None;
      regular=new Font("Microsoft YaHei UI",13*scale,FontStyle.Regular,GraphicsUnit.Pixel);
      small=new Font("Microsoft YaHei UI",11*scale,FontStyle.Regular,GraphicsUnit.Pixel);
      large=new Font("Microsoft YaHei UI",22*scale,FontStyle.Regular,GraphicsUnit.Pixel);Font=regular;
      back=new UpdateActionButton();back.Click+=delegate{if(mode!="handoff")Raise(BackRequested);};Controls.Add(back);
      content=new Panel{AutoScroll=true,BackColor=Color.Transparent};Controls.Add(content);
      versionCard=new UpdateSurface();content.Controls.Add(versionCard);
      headline=MakeLabel(regular);target=MakeLabel(large);current=MakeLabel(small);intro=MakeLabel(small);
      versionCard.Controls.Add(headline);versionCard.Controls.Add(target);versionCard.Controls.Add(current);versionCard.Controls.Add(intro);
      notesTitle=MakeLabel(regular);content.Controls.Add(notesTitle);
      notesCard=new UpdateSurface();content.Controls.Add(notesCard);
      notes=new TextBox{Multiline=true,ReadOnly=true,BorderStyle=BorderStyle.None,ScrollBars=ScrollBars.Vertical,WordWrap=true,Font=regular,BackColor=Color.FromArgb(251,249,252),ForeColor=Color.FromArgb(55,50,61)};
      notesCard.Controls.Add(notes);
      phase=MakeLabel(regular);percentage=MakeLabel(regular);percentage.TextAlign=ContentAlignment.MiddleRight;
      content.Controls.Add(phase);content.Controls.Add(percentage);
      progress=new GlassProgress();content.Controls.Add(progress);
      steps=MakeLabel(small);content.Controls.Add(steps);notice=MakeLabel(small);content.Controls.Add(notice);
      scope=MakeLabel(small);scope.TextAlign=ContentAlignment.TopLeft;Controls.Add(scope);
      primary=new UpdateActionButton{Primary=true};primary.Click+=delegate{RequestInstall();};Controls.Add(primary);
      secondary=new UpdateActionButton();secondary.Click+=delegate{if(Busy)RequestCancel();else Raise(BackRequested);};Controls.Add(secondary);
      AccessibleName="Codex update";Resize+=delegate{LayoutContent();};ApplyPreferences(prefs);
    }
    Label MakeLabel(Font font){return new Label{Font=font,ForeColor=Color.FromArgb(54,48,61),BackColor=Color.Transparent,UseMnemonic=false,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft};}
    void Raise(EventHandler handler){if(handler!=null)handler(this,EventArgs.Empty);}
    static string Multiline(string value){return(value??"").Replace("\r\n","\n").Replace("\r","\n").Replace("\n","\r\n");}
    public void ApplyPreferences(UserPreferences prefs) {
      if(IsDisposed)return;preferences=prefs??new UserPreferences();
      foreach(var button in new[]{back,primary,secondary}){button.Font=regular;button.Accent=Accent;button.Invalidate();}
      versionCard.Accent=Accent;notesCard.Accent=Accent;progress.Accent=Accent;Render();
    }
    public void SetAvailable(UpdateRelease value){if(IsDisposed)return;release=value;if(value==null&&mode=="release")mode="idle";Render();}
    public void ShowRelease(UpdateRelease value){if(IsDisposed)return;release=value;mode=value==null?"idle":"release";error=false;cancelRequested=false;Render();}
    public void ShowChecking(){if(IsDisposed)return;mode="checking";percent=-1;cancelRequested=false;error=false;Render();}
    public void ShowInfo(string title,string detail,bool isError){if(IsDisposed)return;mode="info";infoTitle=title??"";infoDetail=detail??"";error=isError;cancelRequested=false;Render();}
    public void ShowProgress(string detail,int value){if(IsDisposed)return;mode="progress";progressDetail=detail??"";percent=Math.Max(-1,Math.Min(100,value));error=false;Render();}
    public void SetHandoff(){if(IsDisposed)return;mode="handoff";percent=100;progressDetail=L("正在重启额度工具…","Restarting the quota companion…");Render();}
    public void Open(){if(!IsDisposed)Raise(OpenRequested);}
    void RequestInstall(){if(Busy||release==null||IsDisposed)return;cancelRequested=false;ShowProgress(L("正在准备下载…","Preparing the download…"),0);Raise(InstallRequested);}
    void RequestCancel(){if(!Busy||mode=="handoff"||cancelRequested)return;cancelRequested=true;secondary.Enabled=false;secondary.Text=L("正在取消…","Canceling…");Raise(CancelRequested);}
    void Render() {
      if(IsDisposed||versionCard==null)return;
      back.Text=L("‹ 返回面板","‹ Back to panel");back.AccessibleName=L("返回用量面板","Back to usage panel");back.Enabled=mode!="handoff";
      headline.Text=mode=="info"?infoTitle:mode=="idle"?L("检查软件更新","Software updates"):mode=="checking"?L("正在检查更新","Checking for updates"):Busy?L("正在更新额度工具","Updating Codex Quota"):L("有新版本可用","An update is available");
      headline.ForeColor=error?Color.FromArgb(143,55,72):Color.FromArgb(54,48,61);
      target.Text=release==null?CurrentVersion:TargetVersion;current.Text=L("当前 ","Current ")+CurrentVersion;
      intro.Text=L("更新额度工具，你的 Codex 聊天可以继续。","Your Codex chat can continue during the update.");
      notesTitle.Text=mode=="info"?L("更新状态","Update status"):mode=="idle"?L("当前版本","Installed version"):L("这次更新","What's new");
      notes.Text=Multiline(mode=="info"?infoDetail:mode=="checking"?L("正在连接 GitHub 获取最新版本信息。","Reading the latest release from GitHub."):release==null?L("可从托盘菜单检查最新版本。","Check for updates from the tray menu."):String.IsNullOrWhiteSpace(release.Notes)?L("此版本未提供更新说明。","No release notes were provided."):release.Notes);
      notes.AccessibleName=notesTitle.Text;notes.SelectionStart=0;notes.SelectionLength=0;
      bool running=mode=="progress"||mode=="handoff";
      notesTitle.Visible=!running;notesCard.Visible=!running;
      phase.Visible=percentage.Visible=progress.Visible=steps.Visible=notice.Visible=running;
      phase.Text=progressDetail;percentage.Text=percent<0?"":percent+"%";progress.SetValue(Math.Max(0,percent));
      steps.Text=L("下载       校验       重启工具","Download       Verify       Restart");
      notice.Text=mode=="handoff"?L("文件已校验，即将重新打开面板。","Files verified. The panel will reopen shortly."):L("可以返回面板继续查看用量。","You can return to the panel while this runs.");
      scope.Text=L("只重启额度工具，不会关闭 Codex 聊天。","Only the quota companion restarts; Codex stays open.");
      primary.Text=Busy?(mode=="checking"?L("检查中…","Checking…"):L("更新中…","Updating…")):mode=="info"&&release!=null?L("重新下载安装","Retry installation"):L("立即更新","Update now");
      primary.Enabled=!Busy&&release!=null;primary.Visible=release!=null||Busy;
      secondary.Text=Busy?(mode=="handoff"?L("正在重启","Restarting"):cancelRequested?L("正在取消…","Canceling…"):L("取消","Cancel")):L("稍后","Later");
      if(mode=="info"&&release==null)secondary.Text=L("返回面板","Back to panel");
      secondary.Enabled=mode!="handoff"&&!cancelRequested;
      AccessibleDescription=headline.Text+". "+(running?phase.Text+" "+percentage.Text:notes.Text);
      LayoutContent();Invalidate();Raise(StateChanged);
    }
    void LayoutContent() {
      if(content==null||Width<=0||Height<=0)return;
      int width=Math.Max(Px(180),ClientSize.Width),pad=Px(2),gap=Px(10),buttonH=Px(44);
      back.SetBounds(pad,0,Math.Min(Px(142),width),buttonH);
      int bottom=Math.Max(Px(160),Height-Px(96));content.SetBounds(0,Px(58),width,Math.Max(Px(80),bottom-Px(58)));
      int w=Math.Max(Px(180),content.Width-SystemInformation.VerticalScrollBarWidth-Px(4));
      versionCard.SetBounds(0,0,w,Px(134));headline.SetBounds(Px(16),Px(13),w-Px(32),Px(24));
      target.SetBounds(Px(16),Px(42),Math.Min(Px(150),w-Px(32)),Px(34));current.SetBounds(Px(170),Px(48),Math.Max(0,w-Px(186)),Px(24));
      intro.SetBounds(Px(16),Px(87),w-Px(32),Px(34));
      bool running=mode=="progress"||mode=="handoff";
      if(running){phase.SetBounds(0,Px(163),w-Px(60),Px(28));percentage.SetBounds(w-Px(60),Px(163),Px(60),Px(28));progress.SetBounds(0,Px(205),w,Px(8));steps.SetBounds(0,Px(229),w,Px(25));notice.SetBounds(0,Px(270),w,Px(44));content.AutoScrollMinSize=new Size(0,Px(320));}
      else {notesTitle.SetBounds(0,Px(156),w,Px(26));int notesH=Math.Max(Px(132),content.Height-Px(196));notesCard.SetBounds(0,Px(192),w,notesH);notes.SetBounds(Px(14),Px(12),w-Px(28),notesH-Px(24));content.AutoScrollMinSize=new Size(0,Px(192)+notesH);}
      scope.SetBounds(0,bottom+Px(5),width,Px(30));
      int side=Math.Min(Px(112),(width-gap)/2);secondary.SetBounds(width-side,Height-buttonH,side,buttonH);primary.SetBounds(0,Height-buttonH,width-side-gap,buttonH);
      // Control.Visible includes the parent's visibility. A closed popup still
      // needs the two-button geometry ready for its first opening.
      if(release==null&&!Busy)secondary.SetBounds(0,Height-buttonH,width,buttonH);
    }
    protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing){regular.Dispose();small.Dispose();large.Dispose();}}

    sealed class UpdateSurface : Panel {
      internal Color Accent=Color.FromArgb(218,145,170);
      internal UpdateSurface(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;}
      protected override void OnPaintBackground(PaintEventArgs e){base.OnPaintBackground(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;var rect=new RectangleF(1,1,Math.Max(1,Width-2),Math.Max(1,Height-2));using(var p=Round(rect,16))using(var fill=new LinearGradientBrush(ClientRectangle,Color.FromArgb(253,251,254),Blend(Color.FromArgb(247,245,251),Accent,.06f),90f)){e.Graphics.FillPath(fill,p);using(var line=new Pen(Color.FromArgb(240,255,255,255)))e.Graphics.DrawPath(line,p);}}
    }
    sealed class UpdateActionButton : Button {
      internal Color Accent=Color.FromArgb(218,145,170);internal bool Primary;bool hover;
      internal UpdateActionButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);UseVisualStyleBackColor=false;BackColor=Color.Transparent;}
      protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
      protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;Color bottom=Primary?Blend(Color.White,Accent,Enabled?.55f:.2f):Color.FromArgb(245,243,249);if(hover&&Enabled)bottom=Blend(bottom,Accent,.13f);using(var shadow=Round(new RectangleF(1,3,Width-2,Height-4),12))using(var sb=new SolidBrush(Color.FromArgb(19,68,48,74)))e.Graphics.FillPath(sb,shadow);using(var p=Round(new RectangleF(1,1,Width-2,Height-5),12))using(var b=new LinearGradientBrush(ClientRectangle,Color.FromArgb(254,252,255),bottom,90f)){e.Graphics.FillPath(b,p);using(var line=new Pen(Color.FromArgb(235,255,255,255)))e.Graphics.DrawPath(line,p);}TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(4,0,Width-8,Height-4),Enabled?Color.FromArgb(54,48,61):Color.FromArgb(128,120,134),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(6,5,Width-12,Height-14));}
    }
    sealed class GlassProgress : Control {
      internal Color Accent=Color.FromArgb(218,145,170);double displayed;int wanted;readonly Timer animation=new Timer();readonly Stopwatch watch=new Stopwatch();double from;
      internal GlassProgress(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);AccessibleRole=AccessibleRole.ProgressBar;animation.Interval=15;animation.Tick+=delegate{double t=Math.Min(1,watch.ElapsedMilliseconds/180d);displayed=from+(wanted-from)*(1-Math.Pow(1-t,3));Invalidate();if(t>=1)animation.Stop();};}
      internal void SetValue(int value){wanted=Math.Max(0,Math.Min(100,value));AccessibleName=wanted+"%";if(!Visible||!SystemInformation.IsMenuAnimationEnabled){animation.Stop();displayed=wanted;Invalidate();return;}if(Math.Abs(wanted-displayed)<.1)return;from=displayed;watch.Restart();animation.Start();}
      protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var p=Round(new RectangleF(0,0,Width,Height),Height/2f))using(var track=new SolidBrush(Color.FromArgb(231,226,235))){e.Graphics.FillPath(track,p);var save=e.Graphics.Save();e.Graphics.SetClip(p);using(var fill=new LinearGradientBrush(ClientRectangle,Blend(Accent,Color.White,.2f),Accent,0f))e.Graphics.FillRectangle(fill,0,0,(float)(Width*displayed/100d),Height);e.Graphics.Restore(save);}}
      protected override void Dispose(bool disposing){if(disposing)animation.Dispose();base.Dispose(disposing);}
    }
    static Color Blend(Color a,Color b,float n){return Color.FromArgb((int)(a.R+(b.R-a.R)*n),(int)(a.G+(b.G-a.G)*n),(int)(a.B+(b.B-a.B)*n));}
    static GraphicsPath Round(RectangleF r,float radius){var p=new GraphicsPath();float d=Math.Max(1,Math.Min(radius*2,Math.Min(r.Width,r.Height)));p.AddArc(r.Left,r.Top,d,d,180,90);p.AddArc(r.Right-d,r.Top,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.Left,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
  }
}
