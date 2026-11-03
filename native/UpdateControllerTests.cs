using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using CodexQuota;

class UpdateControllerTests {
  static int passed,failed;
  static void Check(bool value,string message){if(!value)throw new Exception(message);}
  static void Test(string name,Action test){try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
  static bool Due(UpdateController controller,UpdateConfiguration config){return (bool)typeof(UpdateController).GetMethod("Due",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,new object[]{config});}
  static string Temp(){string root=Path.Combine(Path.GetTempPath(),"CodexQuotaControllerTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,".data"));return root;}
  static void LastCheck(string root,DateTimeOffset when){File.WriteAllText(Path.Combine(root,".data","update-check.json"),new JavaScriptSerializer().Serialize(new Dictionary<string,string>{{"lastCheckUtc",when.ToString("o")}}));}
  static void Set(UpdateController controller,string field,object value){typeof(UpdateController).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(controller,value);}
  static UpdateDialog Dialog(UpdateController controller){typeof(UpdateController).GetMethod("EnsureDialog",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,null);return (UpdateDialog)typeof(UpdateController).GetField("dialog",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);}
  static void BalloonClick(NotifyIcon tray){typeof(NotifyIcon).GetMethod("OnBalloonTipClicked",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(tray,null);}
  static void UserClose(UpdateDialog dialog){typeof(UpdateDialog).GetMethod("OnFormClosing",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dialog,new object[]{new FormClosingEventArgs(CloseReason.UserClosing,false)});}
  static void BusyNotification(string phase){
    int presentations=0;
    using(var tray=new NotifyIcon())using(var cancellation=new CancellationTokenSource())using(var controller=new UpdateController(Temp(),new UserPreferences(),tray,delegate{},delegate{presentations++;})) {
      var dialog=Dialog(controller);
      Set(controller,"available",new UpdateRelease {Version="2.2.0",Notes="Release notes"});
      Set(controller,"cancellation",cancellation);
      Set(controller,phase=="check"?"checking":"downloading",true);
      if(phase=="check")dialog.ShowChecking();else dialog.ShowProgress(phase,phase=="download"?43:80);
      BalloonClick(tray);
      Check(presentations==1,"Click did not open the ongoing operation");
      UserClose(dialog);
      Check(cancellation.IsCancellationRequested,"Notification replaced the ongoing operation; closing no longer canceled it");
    }
  }
  [STAThread] static int Main(){
    string root=Temp();
    using(var tray=new NotifyIcon())using(var controller=new UpdateController(root,new UserPreferences(),tray,delegate{})) {
      var config=new UpdateConfiguration {CheckOnStartup=false,CheckIntervalHours=24};
      Test("disabling startup check still permits a due periodic check",delegate{LastCheck(root,DateTimeOffset.UtcNow.AddHours(-25));Check(Due(controller,config),"Periodic checks were disabled with the startup check");});
      Test("periodic checks honor interval with startup checking disabled",delegate{LastCheck(root,DateTimeOffset.UtcNow.AddHours(-1));Check(!Due(controller,config),"Periodic check ignored the configured interval");});
      Test("first periodic check is due when no check has been recorded",delegate{File.Delete(Path.Combine(root,".data","update-check.json"));Check(Due(controller,config),"First periodic check was suppressed");});
    }
    Test("old balloon during download keeps close-to-cancel behavior",delegate{BusyNotification("download");});
    Test("old balloon during verification keeps close-to-cancel behavior",delegate{BusyNotification("verify");});
    Test("old balloon during a check keeps close-to-cancel behavior",delegate{BusyNotification("check");});
    Test("idle balloon displays release notes and Later does not cancel",delegate{
      int presentations=0;
      using(var tray=new NotifyIcon())using(var cancellation=new CancellationTokenSource())using(var controller=new UpdateController(Temp(),new UserPreferences(),tray,delegate{},delegate{presentations++;})) {
        Set(controller,"available",new UpdateRelease {Version="2.2.0",Notes="First fix\nSecond fix"});Set(controller,"cancellation",cancellation);
        BalloonClick(tray);var dialog=Dialog(controller);
        var notes=(TextBox)typeof(UpdateDialog).GetField("notes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialog);
        Check(presentations==1&&notes.Text=="First fix\r\nSecond fix","Release notes were not presented intact");
        UserClose(dialog);Check(!cancellation.IsCancellationRequested,"Later incorrectly canceled a nonexistent operation");
      }
    });
    Console.WriteLine("Update controller: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
  }
}
