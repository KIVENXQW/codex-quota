using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Drawing;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using CodexQuota;

class IntegratedUpdateTests {
  static int checks;
  static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
  static object Get(object value,string name){return value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);}
  static void Set(object value,string name,object field){value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(value,field);}
  static void Call(object value,string name,params object[] args){value.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,args);}
  [STAThread] static int Main(){try {
    var ctor=typeof(UpdateController).GetConstructor(new[]{typeof(string),typeof(UserPreferences),typeof(NotifyIcon),typeof(Action),typeof(IUpdateView)});
    Check(ctor!=null,"The controller must accept an integrated update surface");
    string root=Path.Combine(Path.GetTempPath(),"CodexQuotaIntegrated-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
    var view=new FakeView();
    using(var tray=new NotifyIcon())using(var controller=(UpdateController)ctor.Invoke(new object[]{root,new UserPreferences(),tray,(Action)delegate{},view})) {
      controller.Check(true);
      Check(view.OpenCount==1&&view.InfoCount==1,"Manual checks with no source must show the supplied surface");
      Check(Get(controller,"dialog")==null,"The integrated path must not allocate a separate dialog");
      view.RaiseInstall();Check(!(bool)Get(controller,"downloading"),"No release means no installation can start");
      using(var cancellation=new CancellationTokenSource()) {
        Set(controller,"cancellation",cancellation);Set(controller,"downloading",true);
        view.RaiseCancel();Check(cancellation.IsCancellationRequested,"Integrated cancel must cancel the real active operation token");
      }
      using(var cancellation=new CancellationTokenSource()) {
        Set(controller,"cancellation",cancellation);Set(controller,"handingOff",true);
        view.RaiseCancel();Check(!cancellation.IsCancellationRequested,"Cancellation must not interrupt the replacement handoff");
      }
      Set(controller,"cancellation",null);Set(controller,"downloading",false);Set(controller,"handingOff",false);
      var release=new UpdateRelease{Version="9.0.0",Notes="First\nSecond"};
      Call(controller,"PublishAvailable",release);
      Check(view.Release==release,"Background release metadata must reach the persistent surface");
      Check(view.OpenCount==1,"Publishing background update metadata must not open the panel");
      var showResult=typeof(UpdateController).GetMethod("ShowPreviousResult",BindingFlags.Instance|BindingFlags.NonPublic);
      Check(!(bool)showResult.Invoke(controller,null),"A delayed helper receipt must be retried without inventing completion");
      Directory.CreateDirectory(Path.Combine(root,".data"));string result=Path.Combine(root,".data","update-result.json");
      File.WriteAllText(result,"{");Check(!(bool)showResult.Invoke(controller,null),"An incomplete receipt must be retried");
      File.WriteAllText(result,new JavaScriptSerializer().Serialize(new{success=true,fromVersion="0.0.1",toVersion="0.0.2",completedAt=DateTime.UtcNow.ToString("o")}));
      Check(!(bool)showResult.Invoke(controller,null)&&view.OpenCount==1,"A different version receipt must not show completion");
      File.WriteAllText(result,new JavaScriptSerializer().Serialize(new{success=true,fromVersion="0.0.1",toVersion=AppVersion.Current,completedAt=DateTime.UtcNow.ToString("o")}));
      Check((bool)showResult.Invoke(controller,null)&&view.OpenCount==2&&view.Release==null,"A matching successful helper receipt must show completion and clear the update badge");
      Check((bool)showResult.Invoke(controller,null)&&view.OpenCount==2,"A consumed receipt must not reopen the page");
    }
    using(var host=new Form())using(var glass=new GlassUpdateView(new UserPreferences())) {
      host.Controls.Add(glass);
      int install=0,cancel=0,back=0,open=0;glass.InstallRequested+=delegate{install++;};glass.CancelRequested+=delegate{cancel++;};glass.BackRequested+=delegate{back++;};glass.OpenRequested+=delegate{open++;};
      glass.Size=new Size(390,480);glass.ShowRelease(new UpdateRelease{Version="2.2.0",Notes="第一项修复\n第二项修复"});
      Check(!((Button)Get(glass,"primary")).Bounds.IntersectsWith(((Button)Get(glass,"secondary")).Bounds),"A hidden popup must lay out both install and Later buttons without overlap before its first opening");
      Check(glass.UpdateAvailable&&!glass.Busy,"Real release metadata must enable the persistent update indicator");
      Check(((TextBox)Get(glass,"notes")).Text=="第一项修复\r\n第二项修复","Release notes must retain line breaks");
      glass.Open();Check(open==1,"Opening the update view must request its owning popup");
      Call(glass,"RequestInstall");Call(glass,"RequestInstall");Check(install==1&&glass.Busy,"Double clicking install must start only one operation");
      var backButton=(Button)Get(glass,"back");typeof(Button).GetMethod("OnClick",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(backButton,new object[]{EventArgs.Empty});
      Check(back==1&&cancel==0&&glass.Busy,"Returning to usage must preserve the active download");
      glass.ShowProgress("正在校验",80);Call(glass,"RequestCancel");Call(glass,"RequestCancel");Check(cancel==1,"An explicit cancel during verification must be delivered once");
      glass.ShowInfo("下载已取消","当前版本未更改",false);Check(glass.UpdateAvailable&&!glass.Busy,"Canceling must preserve the available release for retry");
      glass.ShowProgress("Restart",100);glass.SetHandoff();Call(glass,"RequestCancel");Check(cancel==1&&!((Button)Get(glass,"secondary")).Enabled,"Replacement handoff must disable cancellation");
      glass.ApplyPreferences(new UserPreferences{Language="en",AccentHtml="#789AC0"});Check(glass.StatusText=="Restarting","Language changes must preserve the current operation state");
      glass.SetAvailable(null);glass.ShowInfo("Update complete","Your records are preserved",false);Check(!glass.UpdateAvailable&&!glass.Busy,"Completion must clear the update indicator");
      glass.ShowRelease(new UpdateRelease{Version="2.2.0",Notes="Fixes badge visibility when Codex remains visible beside another browser.\nIncludes optional login startup.\nPreserves your language, color and local usage records."});
      using(var bitmap=new Bitmap(390,480)){glass.DrawToBitmap(bitmap,new Rectangle(0,0,390,480));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"glass-update-native.png"));}
    }
    Console.WriteLine("Integrated updater: "+checks+" checks PASS");return 0;
  }catch(Exception e){Console.Error.WriteLine("FAIL "+(e.InnerException??e).Message);return 1;}}
  sealed class FakeView : IUpdateStatusView {
    public event EventHandler InstallRequested;
    public event EventHandler CancelRequested;
    public bool IsDisposed {get{return false;}}
    public int OpenCount,InfoCount;
    public UpdateRelease Release;
    public void ShowChecking(){}
    public void ShowRelease(UpdateRelease release){Release=release;}
    public void ShowInfo(string headline,string detail,bool error){InfoCount++;}
    public void ShowProgress(string detail,int percent){}
    public void Open(){OpenCount++;}
    public void SetAvailable(UpdateRelease release){Release=release;}
    public void SetHandoff(){}
    public void RaiseCancel(){if(CancelRequested!=null)CancelRequested(this,EventArgs.Empty);}
    public void RaiseInstall(){if(InstallRequested!=null)InstallRequested(this,EventArgs.Empty);}
  }
}
