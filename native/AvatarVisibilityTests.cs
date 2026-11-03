using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace CodexQuota {
  internal static class AvatarVisibilityTests {
    static int checks;
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks++;}
    static void Field(object target,string name,object value){target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);}
    [STAThread]
    public static int Main() {
      WindowTracker tracker=null;Form host=null;
      try {
        // An off-screen window belonging only to this test contains no avatar.
        // Keep target discovery pinned to it: do not inspect or manipulate Codex.
        host=new TestWindow {StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000),Size=new Size(800,600),ShowInTaskbar=false};
        host.Show();Application.DoEvents();
        tracker=new WindowTracker();
        typeof(WindowTracker).GetProperty("Target").GetSetMethod(true).Invoke(tracker,new object[]{host.Handle});
        Field(tracker,"searchRequested",false);Field(tracker,"lastSearch",DateTime.UtcNow.AddHours(1));
        Field(tracker,"lastAvatarSearch",DateTime.UtcNow);Field(tracker,"avatarOffset",new Point(26,30));
        Check(!tracker.Update(),"An absent avatar must hide the badge even when an old anchor is cached; the old tracker returns visible here.");
        PropertyInfo publicVisible=typeof(WindowTracker).GetProperty("AvatarVisible");
        Check(publicVisible!=null && !(bool)publicVisible.GetValue(tracker,null),"AvatarVisible must expose the unconfirmed/absent state.");

        Type stateType=typeof(WindowTracker).Assembly.GetType("CodexQuota.AvatarVisibilityState");
        Check(stateType!=null,"Visibility state must reject stale asynchronous observations.");
        object state=Activator.CreateInstance(stateType,true);
        MethodInfo observe=stateType.GetMethod("ObserveWindow",BindingFlags.Instance|BindingFlags.NonPublic);
        MethodInfo apply=stateType.GetMethod("Apply",BindingFlags.Instance|BindingFlags.NonPublic);
        MethodInfo visibleOffset=stateType.GetMethod("VisibleOffset",BindingFlags.Static|BindingFlags.NonPublic);
        PropertyInfo revision=stateType.GetProperty("Revision",BindingFlags.Instance|BindingFlags.NonPublic);
        PropertyInfo visible=stateType.GetProperty("Visible",BindingFlags.Instance|BindingFlags.NonPublic);
        PropertyInfo offset=stateType.GetProperty("Offset",BindingFlags.Instance|BindingFlags.NonPublic);
        object trackerState=typeof(WindowTracker).GetField("avatarVisibility",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(tracker);
        int confirmedRevision=(int)revision.GetValue(trackerState,null);
        apply.Invoke(trackerState,new object[]{confirmedRevision,(Point?)new Point(26,30)});
        // The test host never activates. Preserve a confirmed, unobscured avatar
        // observation while another process owns the foreground window.
        Field(tracker,"lastAvatarCheck",DateTime.UtcNow.AddHours(1));
        Check(tracker.Update(),"A confirmed visible avatar must remain eligible when another application is foreground.");
        DateTime searchStamp=(DateTime)typeof(WindowTracker).GetField("lastAvatarSearch",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(tracker);
        typeof(WindowTracker).GetMethod("OnWinEvent",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(tracker,new object[]{IntPtr.Zero,(uint)3,host.Handle,0,0,(uint)0,(uint)0});
        Check(!(bool)publicVisible.GetValue(tracker,null) && (int)revision.GetValue(trackerState,null)>confirmedRevision,"Foreground notifications must revoke confirmation before a coalesced Update can flash a stale badge.");
        Check((DateTime)typeof(WindowTracker).GetField("lastAvatarSearch",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(tracker)==searchStamp,"Foreground invalidation must preserve the full-tree search throttle.");
        Func<int> generation=delegate{return (int)revision.GetValue(state,null);};
        Func<bool> isVisible=delegate{return (bool)visible.GetValue(state,null);};
        Action<IntPtr,Rectangle> geometry=delegate(IntPtr target,Rectangle bounds){observe.Invoke(state,new object[]{target,bounds,1f});};
        Func<int,Point?,bool> accept=delegate(int token,Point? point){return (bool)apply.Invoke(state,new object[]{token,point});};
        Func<Rectangle,RectangleF,bool,bool,Point?> policy=delegate(Rectangle bounds,RectangleF avatar,bool offscreen,bool hit){object result=visibleOffset.Invoke(null,new object[]{bounds,avatar,offscreen,hit});return result==null?(Point?)null:(Point)result;};

        IntPtr first=new IntPtr(123),second=new IntPtr(456);
        Rectangle bounds1=new Rectangle(100,100,1000,800);
        RectangleF avatar1=new RectangleF(110,850,28,28);
        geometry(first,bounds1);
        Point? anchor=policy(bounds1,avatar1,false,true);
        Check(anchor.HasValue && anchor.Value==new Point(24,50),"A visible lower-left avatar must produce the correct anchor.");
        Check(accept(generation(),anchor) && isVisible(),"A current visible observation must allow the badge to appear.");
        Check(!policy(bounds1,avatar1,true,true).HasValue,"IsOffscreen must reject a hidden avatar.");
        Check(!policy(bounds1,avatar1,false,false).HasValue,"An avatar covered by the page must be rejected despite IsOffscreen=false.");
        Check(!policy(bounds1,RectangleF.Empty,false,true).HasValue,"Empty bounds must be rejected.");
        Check(!policy(bounds1,new RectangleF(900,850,28,28),false,true).HasValue,"A similarly named button outside the left rail must be rejected.");
        Check(!policy(bounds1,new RectangleF(110,150,28,28),false,true).HasValue,"A button outside the lower half must be rejected.");
        Check(!policy(bounds1,new RectangleF(90,850,28,28),false,true).HasValue,"An avatar clipped outside the window must be rejected.");
        Check(!policy(bounds1,new RectangleF(float.NaN,850,28,28),false,true).HasValue,"Invalid numeric bounds must be rejected.");

        accept(generation(),null);
        Check(!isVisible() && offset.GetValue(state,null)==null,"A missing/covered result must clear visibility and the old anchor.");
        Check(accept(generation(),anchor) && isVisible(),"A newly visible avatar must recover without restarting the tracker.");
        int beforeMove=generation();
        Rectangle moved=new Rectangle(240,210,1000,800);
        geometry(first,moved);
        Check(isVisible() && (Point)offset.GetValue(state,null)==anchor.Value,"Pure translation must retain a previously confirmed anchor without flicker.");
        Check(!accept(beforeMove,null) && isVisible(),"An out-of-date missing result during movement must not hide a confirmed avatar.");
        geometry(first,bounds1);
        Check(!accept(beforeMove,new Point(40,70)),"Moving away and back must still invalidate the earlier coordinate snapshot.");
        accept(generation(),null);
        Check(!accept(beforeMove,anchor) && !isVisible(),"An old visible result must not reshow an avatar after it was hidden.");
        accept(generation(),anchor);
        int beforeResize=generation();
        geometry(first,new Rectangle(100,100,1100,800));
        Check(!isVisible() && offset.GetValue(state,null)==null,"A size change must immediately revoke avatar confirmation.");
        Check(!accept(beforeResize,anchor) && !isVisible(),"A result from before resizing must not restore the badge.");
        accept(generation(),anchor);
        int beforeTarget=generation();
        geometry(second,bounds1);
        Check(!isVisible() && !accept(beforeTarget,anchor),"Changing target must revoke confirmation and reject the former target's result.");
        Check(accept(generation(),anchor) && isVisible(),"A fresh observation on the replacement target must recover visibility.");
        Console.WriteLine("Avatar visibility checks passed: "+checks);
        return 0;
      }catch(Exception error) {
        Exception cause=error is TargetInvocationException && error.InnerException!=null?error.InnerException:error;
        Console.Error.WriteLine("FAIL: "+cause.Message);return 1;
      }finally {if(tracker!=null)tracker.Dispose();if(host!=null)host.Dispose();}
    }
    sealed class TestWindow : Form {
      protected override bool ShowWithoutActivation {get{return true;}}
      protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000080;return p;}}
    }
  }
}
