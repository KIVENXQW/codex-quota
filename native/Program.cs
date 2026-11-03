using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace CodexQuota {
  static class Program {
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [STAThread] static void Main(string[] args) {
      if(args.Length>0 && args[0]=="--quit") {try{using(var signal=EventWaitHandle.OpenExisting("Local\\KivenCodexQuotaExit"))signal.Set();}catch{}return;}
      bool created;
      using(var mutex=new Mutex(true,"Local\\KivenCodexQuotaNative",out created)) {
        if(!created) {try{using(var signal=EventWaitHandle.OpenExisting("Local\\KivenCodexQuotaShow"))signal.Set();}catch{}return;}
        SetProcessDPIAware();
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new QuotaApplication());
      }
    }
  }
  sealed class QuotaApplication : ApplicationContext {
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    QuotaBadge badge;
    readonly Control positionDispatcher=new Control();
    readonly QuotaPopup popup=new QuotaPopup();
    readonly NotifyIcon tray=new NotifyIcon();
    readonly GlassUpdateToast updateToast=new GlassUpdateToast();
    readonly WindowTracker tracker=new WindowTracker();
    readonly System.Windows.Forms.Timer positionTimer=new System.Windows.Forms.Timer();
    readonly System.Windows.Forms.Timer refreshTimer=new System.Windows.Forms.Timer();
    readonly HttpClient http=new HttpClient(new HttpClientHandler {UseProxy=false});
    readonly EventWaitHandle showEvent=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\KivenCodexQuotaShow");
    readonly EventWaitHandle exitEvent=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\KivenCodexQuotaExit");
    readonly string root=AppDomain.CurrentDomain.BaseDirectory;
    QuotaState state=new QuotaState {Error="正在读取账户额度…"};
    UserPreferences preferences=UserPreferences.Load();
    ToolStripMenuItem detailsItem,refreshItem,toggleItem,updateItem,exitItem;
    UpdateController updater;
    bool refreshing,stopping,badgeEnabled=true,positionQueued;
    string lastStatus;
    DateTime lastStatusCapture=DateTime.MinValue;
    int attachedLayoutVersion=-1;
    readonly object statusWriteLock=new object();
    string pendingStatus;
    bool statusWriting;
    public QuotaApplication() {
      EnsureBadge();
      http.Timeout=TimeSpan.FromSeconds(45);
      tray.Text="Codex 额度 · 正在读取";SetTrayIcon("—",false);tray.Visible=true;
      var menu=new ContextMenuStrip();
      detailsItem=new ToolStripMenuItem("查看额度详情",null,delegate{ShowDetails(false);});menu.Items.Add(detailsItem);
      refreshItem=new ToolStripMenuItem("立即刷新",null,delegate{Refresh();});menu.Items.Add(refreshItem);
      toggleItem=new ToolStripMenuItem("头像上方显示额度");toggleItem.Checked=true;toggleItem.CheckOnClick=true;toggleItem.CheckedChanged+=delegate{badgeEnabled=toggleItem.Checked;Position();};menu.Items.Add(toggleItem);
      updateItem=new ToolStripMenuItem("检查更新",null,delegate{if(updater!=null)updater.Check(true);});menu.Items.Add(updateItem);
      menu.Items.Add(new ToolStripSeparator());exitItem=new ToolStripMenuItem("退出额度工具",null,delegate{ExitThread();});menu.Items.Add(exitItem);tray.ContextMenuStrip=menu;
      tray.MouseClick+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left) {if(popup.Visible)popup.Hide();else ShowDetails(false);}};
      popup.RefreshRequested+=delegate{Refresh();};
      popup.OpenUpdateRequested+=delegate{ShowDetails(false);};
      updateToast.OpenRequested+=delegate{popup.ShowUpdates();};
      popup.PreferencesChanged+=delegate {
        preferences=popup.Preferences;
        try{preferences.Save();}catch{MessageBox.Show(T("偏好未能保存，下次打开将使用上次设置。","Could not save preferences; previous settings will be used next time."),"Codex");}
        ApplyPreferences();Render();
      };
      popup.ApplyPreferences(preferences);ApplyPreferences();
      updater=new UpdateController(root,preferences,tray,delegate{ExitThread();},popup.UpdateView);
      popup.UpdateView.StateChanged+=delegate{if(!stopping){if(!popup.UpdateView.UpdateAvailable)updateToast.Hide();RenderTray();}};
      updater.UpdateDetected+=delegate {
        if(!stopping&&updater.Available!=null)updateToast.NotifyAvailable(updater.Available.Version,preferences,Screen.FromPoint(Cursor.Position).WorkingArea);
      };
      badge.SetState(state);popup.SetState(state);
      // This independent dispatcher survives destruction of the external parent and its child.
      IntPtr dispatcherHandle=positionDispatcher.Handle;
      tracker.Changed+=delegate{QueuePosition();};
      // Keep the timer for IPC, discovery, and hook-failure recovery. Movement uses Changed.
      positionTimer.Interval=350;positionTimer.Tick+=delegate{if(exitEvent.WaitOne(0)){ExitThread();return;}if(showEvent.WaitOne(0))ShowDetails(false);Position();SaveStatus();};positionTimer.Start();
      refreshTimer.Interval=60000;refreshTimer.Tick+=delegate{Refresh();};refreshTimer.Start();
      EventHandler first=null;first=async delegate{Application.Idle-=first;await Task.Run((Action)EnsureService);if(!stopping){Refresh();updater.Start();}};Application.Idle+=first;
    }
    string T(string zh,string en){return preferences.Language=="en"?en:zh;}
    void EnsureBadge() {
      if(badge!=null && !badge.IsDisposed)return;
      badge=new QuotaBadge();attachedLayoutVersion=-1;
      badge.ActivatedByUser+=delegate{if(popup.Visible)popup.Hide();else ShowDetails(true);};
      badge.SetAppearance(preferences);badge.SetState(state);
    }
    void ApplyPreferences() {
      EnsureBadge();
      badge.SetAppearance(preferences);
      detailsItem.Text=T("查看额度详情","Usage details");refreshItem.Text=T("立即刷新","Refresh now");
      toggleItem.Text=T("头像上方显示额度","Show badge above avatar");exitItem.Text=T("退出额度工具","Quit quota companion");
      updateItem.Text=T("检查更新","Check for updates");if(updater!=null)updater.SetPreferences(preferences);
    }
    void EnsureService() {
      try {
        var start=new ProcessStartInfo("powershell.exe","-NoProfile -ExecutionPolicy Bypass -File \""+Path.Combine(root,"Start-Panel.ps1")+"\"");
        start.UseShellExecute=false;start.CreateNoWindow=true;start.WindowStyle=ProcessWindowStyle.Hidden;start.WorkingDirectory=root;
        using(var process=Process.Start(start))process.WaitForExit(10000);
      }catch{}
    }
    async void Refresh() {
      if(refreshing || stopping)return;refreshing=true;
      try {
        string json=await http.GetStringAsync("http://127.0.0.1:43761/api/usage");
        if(stopping)return;
        state=QuotaState.Parse(json);
      }catch {
        state.Stale=state.Available;
        state.Error="连接暂时中断，请点击刷新重试。";
      }finally {
        refreshing=false;
        if(!stopping)Render();
      }
    }
    void Render() {
      EnsureBadge();
      badge.SetState(state);popup.SetState(state);
      RenderTray();Position();SaveStatus(true);
    }
    void RenderTray() {
      var primary=state.Primary;
      string value=primary!=null && primary.Remaining.HasValue?primary.Remaining.Value.ToString("0",CultureInfo.InvariantCulture):"—";
      string tip="Codex · "+T("剩余 ","Remaining ")+value+"%";
      if(state.Stale)tip+=T("（旧数据）"," (stale)");
      if(primary!=null && primary.ResetAt.HasValue)tip+="\n"+T("重置 ","Resets ")+ResetDate(primary.ResetAt.Value)+T(" 北京时间"," UTC+8");
      if(!state.Available)tip="Codex · "+T(state.Error??"额度暂不可用","Usage temporarily unavailable");
      if(popup.UpdateView.UpdateAvailable)tip+=T(" · 有更新"," · Update available");
      tray.Text=tip.Length>63?tip.Substring(0,63):tip;
      SetTrayIcon(state.Stale?"!":value,state.Stale || !state.Available);
    }
    static string ResetDate(long seconds) {
      return new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero).AddSeconds(seconds).ToOffset(TimeSpan.FromHours(8)).ToString("MM/dd HH:mm");
    }
    void SetTrayIcon(string text,bool muted) {
      using(var bitmap=new Bitmap(32,32))using(var g=Graphics.FromImage(bitmap)) {
        g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.Transparent);
        using(var brush=new SolidBrush(muted?Color.FromArgb(135,140,145):preferences.AccentColor))
        using(var font=new Font("Segoe UI",text.Length>=3?15:20,FontStyle.Bold,GraphicsUnit.Pixel)) {
          var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};
          g.DrawString(text,font,brush,new RectangleF(0,0,32,29),format);g.FillRectangle(brush,6,29,20,2);format.Dispose();
        }
        if(popup.UpdateView.UpdateAvailable) {
          using(var border=new SolidBrush(Color.White))g.FillEllipse(border,23,0,9,9);
          using(var dot=new SolidBrush(QuotaAppearance.Darken(preferences.AccentColor,.25f)))g.FillEllipse(dot,25,2,5,5);
        }
        IntPtr handle=bitmap.GetHicon();
        Icon icon;using(var original=Icon.FromHandle(handle))icon=(Icon)original.Clone();DestroyIcon(handle);
        var previous=tray.Icon;tray.Icon=icon;if(previous!=null)previous.Dispose();
      }
    }
    void ShowDetails(bool nearBadge) {
      popup.SetState(state);
      Rectangle bounds=badge!=null&&!badge.IsDisposed?badge.ScreenBounds:Rectangle.Empty;
      nearBadge=nearBadge&&!bounds.IsEmpty;
      var area=Screen.FromPoint(nearBadge?bounds.Location:Cursor.Position).WorkingArea;
      Point desired=nearBadge?new Point(bounds.Right+10,bounds.Bottom-popup.Height):new Point(area.Right-popup.Width-12,area.Bottom-popup.Height-12);
      popup.ShowAt(new Point(Math.Max(area.Left,Math.Min(desired.X,area.Right-popup.Width)),Math.Max(area.Top,Math.Min(desired.Y,area.Bottom-popup.Height))));
      SaveStatus(true);
    }
    void QueuePosition() {
      if(stopping || positionQueued || positionDispatcher.IsDisposed)return;
      positionQueued=true;
      // A burst of native events schedules one move, using the latest window bounds.
      // Never do UI Automation, process discovery, or disk I/O inside a WinEvent callback.
      try {
        positionDispatcher.BeginInvoke((MethodInvoker)delegate {
          positionQueued=false;
          if(!stopping)Position();
        });
      }catch(InvalidOperationException){positionQueued=false;}
    }
    void Position() {
      bool visible=badgeEnabled && tracker.Update();
      if(visible) {
        EnsureBadge();
        // Moving the parent already moves its WS_CHILD. Reposition only for layout changes.
        if(badge.AttachedParent!=tracker.Target || attachedLayoutVersion!=tracker.LayoutVersion) {
          BadgeAttachment.DpiScope dpiScope;
          if(!BadgeAttachment.TryEnterDpiContext(tracker.Target,out dpiScope)){if(badge.Visible)badge.Hide();return;}
          using(dpiScope) {
            badge.SetScale(tracker.Scale);
            Point client;
            if(!BadgeAttachment.TryScreenToClient(tracker.Target,tracker.BadgeLocation(badge.Size),out client) || !badge.TryAttach(tracker.Target,client)) {
              if(badge.Visible)badge.Hide();
              return;
            }
          }
          attachedLayoutVersion=tracker.LayoutVersion;
        }
        if(!badge.Visible)badge.Show();
        badge.EnsureFront();
      }else if(badge!=null && !badge.IsDisposed && badge.Visible)badge.Hide();
    }
    void SaveStatus(bool force=false) {
      DateTime now=DateTime.UtcNow;
      if(!force && now-lastStatusCapture<TimeSpan.FromSeconds(1))return;
      lastStatusCapture=now;
      try {
        bool hasBadge=badge!=null&&!badge.IsDisposed&&badge.IsHandleCreated;
        Rectangle bounds=hasBadge?badge.ScreenBounds:Rectangle.Empty;
        var payload=new {version=AppVersion.Current,pid=Process.GetCurrentProcess().Id,trayVisible=tray.Visible,badgeVisible=hasBadge&&badge.Visible,badgeHandle=hasBadge?badge.Handle.ToInt64():0L,badgeBounds=new {x=bounds.Left,y=bounds.Top,width=bounds.Width,height=bounds.Height},popupVisible=popup.Visible,popupHandle=popup.Handle.ToInt64(),remaining=state.Primary==null?null:state.Primary.Remaining,stale=state.Stale,available=state.Available,target=tracker.Target.ToInt64(),tokenDays=state.DailyTokens.Count,activityStatus=state.Activity.Status,workSeconds=state.Activity.TotalWorkSeconds,completedTasks=state.Activity.CompletedTasks,language=preferences.Language,accent=preferences.AccentHtml,updaterBuiltIn=true,eventDrivenTracking=tracker.EventsAvailable,avatarVisible=tracker.AvatarVisible,attachedToWindow=hasBadge&&badge.AttachedParent!=IntPtr.Zero,badgeParent=hasBadge?badge.AttachedParent.ToInt64():0L};
        string json=new JavaScriptSerializer().Serialize(payload);
        if(json==lastStatus)return;lastStatus=json;
        lock(statusWriteLock){pendingStatus=json;if(statusWriting)return;statusWriting=true;}
        ThreadPool.QueueUserWorkItem(delegate {
          while(true) {
            string next;lock(statusWriteLock){next=pendingStatus;pendingStatus=null;if(next==null){statusWriting=false;return;}}
            try{string dir=Path.Combine(root,".run");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"native-status.json"),next);}catch{}
          }
        });
      }catch{}
    }
    protected override void ExitThreadCore() {
      stopping=true;tracker.Dispose();if(updater!=null)updater.Dispose();positionTimer.Stop();refreshTimer.Stop();tray.Visible=false;
      if(badge!=null)badge.Dispose();updateToast.Dispose();popup.Close();tray.Dispose();positionTimer.Dispose();refreshTimer.Dispose();positionDispatcher.Dispose();http.Dispose();showEvent.Dispose();exitEvent.Dispose();base.ExitThreadCore();
    }
  }
}
