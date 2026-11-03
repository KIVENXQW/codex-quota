using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using CodexQuota;

class WindowTrackerNativeTests {
  [DllImport("user32.dll")] static extern void NotifyWinEvent(uint eventType,IntPtr window,int objectId,int childId);
  static void Check(bool value,string message){if(!value)throw new Exception(message);}
  static void PumpUntil(Func<bool> ready,int timeout) {
    var clock=Stopwatch.StartNew();
    while(!ready() && clock.ElapsedMilliseconds<timeout){Application.DoEvents();Thread.Sleep(1);}
  }
  [STAThread] static int Main() {
    try {
      // A hidden test-owned native window. No user window is moved or activated.
      using(var target=new Form())using(var tracker=new WindowTracker()) {
        IntPtr handle=target.Handle;
        typeof(WindowTracker).GetProperty("Target").GetSetMethod(true).Invoke(tracker,new object[]{handle});
        Check(tracker.EventsAvailable,"Expected the Windows hook registrations to succeed.");
        int callbacks=0,callbackThread=0,uiThread=Thread.CurrentThread.ManagedThreadId;
        tracker.Changed+=delegate{callbacks++;callbackThread=Thread.CurrentThread.ManagedThreadId;};
        var elapsed=Stopwatch.StartNew();
        NotifyWinEvent(0x800B,handle,0,0);
        PumpUntil(delegate{return callbacks>0;},250);
        Check(callbacks>0,"A real native location event must reach the tracker before the old 350ms poll.");
        Check(callbackThread==uiThread,"The notification must return on the UI dispatcher thread.");
        long delivered=elapsed.ElapsedMilliseconds;
        tracker.Dispose();int before=callbacks;
        NotifyWinEvent(0x800B,handle,0,0);
        PumpUntil(delegate{return callbacks!=before;},50);
        Check(callbacks==before,"Disposed trackers must no longer receive native events.");
        Console.WriteLine("Native event delivery: 4 checks PASS ("+delivered+"ms; hidden test window)");
      }
      return 0;
    }catch(Exception error){Console.Error.WriteLine("FAIL: "+error.Message);return 1;}
  }
}
