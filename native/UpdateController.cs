using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace CodexQuota {
  public sealed class UpdateController : IDisposable {
    readonly string root;
    readonly NotifyIcon tray;
    readonly Action quit;
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    UserPreferences preferences;
    UpdateDialog dialog;
    UpdateRelease available;
    CancellationTokenSource cancellation;
    bool checking,downloading,showResult,disposed,handingOff;
    string StatePath {get{return Path.Combine(root,".data","update-check.json");}}
    string L(string zh,string en){return preferences.Language=="en"?en:zh;}
    public UpdateController(string directory,UserPreferences prefs,NotifyIcon icon,Action exit) {
      root=directory;preferences=prefs;tray=icon;quit=exit;
      timer.Interval=3600000;timer.Tick+=delegate{Check(false);};
      tray.BalloonTipClicked+=delegate{if(available!=null){showResult=true;EnsureDialog();dialog.ShowRelease(available);OpenDialog();}};
    }
    public void SetPreferences(UserPreferences value){preferences=value;}
    public void Start(){timer.Start();Check(false);}
    Dictionary<string,string> ReadState(){try{return new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(StatePath));}catch{return new Dictionary<string,string>();}}
    bool Due(UpdateConfiguration config) {
      if(!config.CheckOnStartup)return false;
      string text;DateTimeOffset time;var saved=ReadState();
      return !saved.TryGetValue("lastCheckUtc",out text)||!DateTimeOffset.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out time)||DateTimeOffset.UtcNow-time>=TimeSpan.FromHours(config.CheckIntervalHours);
    }
    bool Record(string version,bool notify) {
      var saved=ReadState();string previous;bool first=!saved.TryGetValue("notifiedVersion",out previous)||previous!=version;
      saved["lastCheckUtc"]=DateTimeOffset.UtcNow.ToString("o");if(notify&&version!=null)saved["notifiedVersion"]=version;
      try{Directory.CreateDirectory(Path.GetDirectoryName(StatePath));File.WriteAllText(StatePath,new JavaScriptSerializer().Serialize(saved));}catch{}
      return first;
    }
    void EnsureDialog() {
      if(dialog!=null&&!dialog.IsDisposed)return;
      dialog=new UpdateDialog(preferences,AppVersion.Current);
      dialog.InstallRequested+=delegate{Install();};
      dialog.CancelRequested+=delegate{if(!handingOff&&cancellation!=null)cancellation.Cancel();};
    }
    void OpenDialog(){if(disposed||dialog==null||dialog.IsDisposed)return;if(!dialog.Visible)dialog.Show();dialog.Activate();}
    public async void Check(bool interactive) {
      if(disposed)return;
      if(downloading){if(interactive)OpenDialog();return;}
      if(interactive){showResult=true;EnsureDialog();if(downloading){OpenDialog();return;}}
      if(checking){if(interactive){dialog.ShowChecking();OpenDialog();}return;}
      UpdateConfiguration config;
      try{config=UpdateConfiguration.Load(root);}catch{if(interactive){dialog.ShowInfo(L("更新来源配置有误","Invalid update source"),L("请检查 update-source.json 中的仓库名称。","Check the repository name in update-source.json."),true);OpenDialog();}return;}
      if(!config.IsConfigured){if(interactive){dialog.ShowInfo(L("更新器已就绪","Updater is ready"),L("此版本尚未关联 GitHub 发布仓库。配置发布仓库后，即可检查并安装新版。","This build is not connected to a GitHub release repository yet. Once configured, it can check for and install new versions."),false);OpenDialog();}return;}
      if(!interactive&&!Due(config))return;
      showResult=interactive;checking=true;var operation=new CancellationTokenSource();cancellation=operation;operation.CancelAfter(TimeSpan.FromSeconds(40));
      if(interactive){dialog.ShowChecking();OpenDialog();}
      try {
        using(var client=new UpdateClient(root,config))available=await client.CheckAsync(operation.Token);
        operation.Token.ThrowIfCancellationRequested();
        if(disposed)return;
        bool first=Record(available==null?null:available.Version,available!=null);
        if(showResult){EnsureDialog();if(available==null)dialog.ShowInfo(L("已是最新版本","You're up to date"),"CodexQuota "+AppVersion.Current,false);else dialog.ShowRelease(available);OpenDialog();}
        else if(available!=null&&first) {
          tray.ShowBalloonTip(7000,L("Codex 额度有新版本","Codex Quota update available"),L("发现 ","Version ")+available.Version+L("。点击查看更新说明。"," is available. Click to view the release notes."),ToolTipIcon.Info);
        }
      }catch(OperationCanceledException){if(showResult&&dialog!=null&&!dialog.IsDisposed)dialog.ShowInfo(L("检查已取消或超时","Check canceled or timed out"),L("稍后可从托盘再次检查更新。","You can check again from the tray menu."),false);}
      catch(Exception error) {if(showResult&&dialog!=null&&!dialog.IsDisposed)dialog.ShowInfo(L("检查更新失败","Update check failed"),error is InvalidDataException?L("发布信息或校验文件不完整，请稍后重试。","The release or checksum information is incomplete. Try again later."):L("无法读取 GitHub 发布信息，请检查网络和更新来源。","Could not read GitHub releases. Check your network and update source."),true);}
      finally{checking=false;operation.Dispose();if(Object.ReferenceEquals(cancellation,operation))cancellation=null;}
    }
    async void Install() {
      if(disposed||downloading||available==null)return;
      if(checking){showResult=true;EnsureDialog();dialog.ShowChecking();return;}
      downloading=true;var operation=new CancellationTokenSource();cancellation=operation;
      try {
        var config=UpdateConfiguration.Load(root);EnsureDialog();dialog.ShowProgress(L("正在下载更新包…","Downloading update…"),0);
        var progress=new Progress<UpdateProgress>(delegate(UpdateProgress value){if(dialog==null||dialog.IsDisposed||disposed)return;dialog.ShowProgress(value.Phase=="download"?L("正在下载更新包…","Downloading update…"):value.Phase=="verify"?L("正在校验文件…","Verifying files…"):L("即将重启并更新…","Restarting to update…"),value.Percent);});
        PreparedUpdate prepared;
        using(var client=new UpdateClient(root,config))prepared=await client.PrepareAsync(available,progress,operation.Token);
        operation.Token.ThrowIfCancellationRequested();
        var start=new ProcessStartInfo(prepared.HelperPath,"\""+prepared.PlanPath+"\"") {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(prepared.HelperPath)};
        using(var helper=Process.Start(start)){if(helper==null)throw new IOException("Updater helper could not start.");}
        handingOff=true;quit();
      }catch(OperationCanceledException){if(dialog!=null&&!dialog.IsDisposed)dialog.ShowInfo(L("下载已取消","Download canceled"),L("当前版本未作更改。","Your installed version has not changed."),false);}
      catch(Exception error){if(dialog!=null&&!dialog.IsDisposed)dialog.ShowInfo(L("未能安装更新","Could not install update"),error is InvalidDataException?L("更新包未通过校验，当前版本未作更改。","The package did not pass verification. Your installed version has not changed."):L("下载或准备更新失败，请稍后重试。","The download or preparation failed. Try again later."),true);}
      finally{downloading=false;operation.Dispose();if(Object.ReferenceEquals(cancellation,operation))cancellation=null;}
    }
    public void Dispose(){disposed=true;timer.Stop();timer.Dispose();if(cancellation!=null)cancellation.Cancel();if(dialog!=null&&!dialog.IsDisposed)dialog.Close();}
  }
}
