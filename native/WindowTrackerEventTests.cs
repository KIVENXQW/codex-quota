using System;
using System.Reflection;

namespace CodexQuota {
  internal static class WindowTrackerEventTests {
    static int checks;
    static void Check(bool condition,string description) {
      if(!condition)throw new InvalidOperationException(description);
      checks++;
    }
    [STAThread]
    public static int Main() {
      WindowTracker tracker=null;
      try {
        // The same source compiles against the pre-hook tracker for a red baseline.
        Type type=typeof(WindowTracker);
        EventInfo changed=type.GetEvent("Changed");
        Check(changed!=null,"WindowTracker must notify location changes without waiting for its polling timer.");
        Check(typeof(IDisposable).IsAssignableFrom(type),"Tracker must release native hooks on shutdown.");
        MethodInfo callback=type.GetMethod("OnWinEvent",BindingFlags.Instance|BindingFlags.NonPublic);
        FieldInfo search=type.GetField("searchRequested",BindingFlags.Instance|BindingFlags.NonPublic);
        Check(callback!=null && search!=null,"Event callback and deferred target discovery must exist.");
        tracker=new WindowTracker();
        int notifications=0;
        changed.AddEventHandler(tracker,new EventHandler(delegate { notifications++; }));
        IntPtr target=new IntPtr(0x1234),other=new IntPtr(0x2345);
        MethodInfo setTarget=type.GetProperty("Target").GetSetMethod(true);
        setTarget.Invoke(tracker,new object[]{target});
        search.SetValue(tracker,false);
        Action<uint,IntPtr,int,int> send=delegate(uint eventType,IntPtr window,int objectId,int childId) {
          callback.Invoke(tracker,new object[]{IntPtr.Zero,eventType,window,objectId,childId,(uint)0,(uint)0});
        };

        send(0x800B,target,0,0);
        Check(notifications==1,"A target window move must notify immediately.");
        Check(!(bool)search.GetValue(tracker),"A move must not force process rediscovery.");
        send(0x800B,other,0,0);
        send(0x800B,target,-4,0);
        send(0x800B,target,0,5);
        send(0x800B,IntPtr.Zero,0,0);
        Check(notifications==1,"Unrelated windows, clients, child objects, and null windows must be ignored.");

        send(0x0003,other,0,0);
        Check(notifications==2,"Foreground changes must notify even for another process.");
        Check((bool)search.GetValue(tracker),"Foreground changes must invalidate target discovery immediately.");
        search.SetValue(tracker,false);
        send(0x8001,target,-4,0);
        send(0x8001,other,0,0);
        Check(notifications==2 && !(bool)search.GetValue(tracker),"Unrelated or child destruction must not invalidate the target.");
        send(0x8001,target,0,0);
        Check(notifications==3 && (bool)search.GetValue(tracker),"Target destruction must notify and request rediscovery.");

        send(0x0016,target,0,0);
        send(0x0017,target,0,0);
        Check(notifications==5,"Minimize and restore must both notify.");
        send(0x0016,other,0,0);
        send(0x0017,other,0,0);
        Check(notifications==5,"Other windows' minimize events must be ignored.");
        send(0x8002,target,0,0);
        send(0x8003,target,0,0);
        Check(notifications==7,"Target show and hide must both notify.");
        send(0x800A,target,0,0);
        Check(notifications==7,"Unsubscribed object-state events must be ignored.");

        setTarget.Invoke(tracker,new object[]{other});
        send(0x800B,target,0,0);
        send(0x800B,other,0,0);
        Check(notifications==8,"Only the current target must drive location notifications after a target switch.");
        send(0x8004,other,-4,0);
        Check(notifications==9,"Target client child reordering must notify so renderer siblings cannot hide the badge.");
        send(0x8004,target,-4,0);
        send(0x8004,other,-4,5);
        Check(notifications==9,"Unrelated containers and individual reordered descendants must be ignored.");
        ((IDisposable)tracker).Dispose();
        Check(!(bool)type.GetProperty("EventsAvailable").GetValue(tracker,null),"Disposal must clear event availability.");
        send(0x800B,other,0,0);
        send(0x0003,target,0,0);
        Check(notifications==9,"Queued callbacks after disposal must be ignored.");
        Check(!tracker.Update(),"Disposed tracker updates must return before touching windows or UIA.");
        ((IDisposable)tracker).Dispose();
        Check(true,"Repeated disposal must be safe.");
        Console.WriteLine("WindowTracker event checks passed: "+checks);
        return 0;
      } catch(Exception error) {
        Exception cause=error is TargetInvocationException && error.InnerException!=null?error.InnerException:error;
        Console.Error.WriteLine("FAIL: "+cause.Message);
        return 1;
      } finally {
        IDisposable disposable=tracker as IDisposable;
        if(disposable!=null)disposable.Dispose();
      }
    }
  }
}
