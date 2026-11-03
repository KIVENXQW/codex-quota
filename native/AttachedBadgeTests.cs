using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexQuota {
  internal static class AttachedBadgeTests {
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] static extern bool AreDpiAwarenessContextsEqual(IntPtr first,IntPtr second);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
    [StructLayout(LayoutKind.Sequential)] struct NativePoint {public int X,Y;}
    [DllImport("user32.dll")] static extern IntPtr ChildWindowFromPointEx(IntPtr parent,NativePoint point,uint flags);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool GetLayeredWindowAttributes(IntPtr window,out uint colorKey,out byte alpha,out uint flags);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr window,int index);
    static int checks;

    [STAThread]
    public static int Main(string[] args) {
      if(args.Length>0 && args[0]=="--parent")return RunParent();
      HostProcess host=null;QuotaBadge badge=null;
      try {
        Type type=typeof(QuotaBadge);
        MethodInfo attach=type.GetMethod("TryAttach",new Type[]{typeof(IntPtr),typeof(Point)});
        MethodInfo detach=type.GetMethod("Detach",Type.EmptyTypes);
        PropertyInfo attachedParent=type.GetProperty("AttachedParent"),screenBounds=type.GetProperty("ScreenBounds");
        Check(SetProcessDpiAwarenessContext(new IntPtr(-2)),"The independent test process must start system-DPI-aware.");
        Application.EnableVisualStyles();
        IntPtr callerDpi=GetThreadDpiAwarenessContext();
        host=new HostProcess();
        uint pid;GetWindowThreadProcessId(host.Window,out pid);
        Check(pid==(uint)host.ProcessId,"Only the test's own helper HWND may be manipulated.");
        IntPtr parentDpi=GetWindowDpiAwarenessContext(host.Window);
        Check(!AreDpiAwarenessContextsEqual(callerDpi,parentDpi),"Parent and original caller must have different DPI modes to exercise safe handle recreation.");

        if(attach==null || detach==null || attachedParent==null || screenBounds==null) {
          // Exercise the old floating HWND, rather than failing only on a missing API.
          badge=NewBadge();
          Rectangle parentBounds=BadgeAttachment.ScreenBounds(host.Window);
          badge.Location=new Point(parentBounds.Left+6,parentBounds.Top+310);
          badge.Show();Pump();
          Rectangle before=BadgeAttachment.ScreenBounds(badge.Handle);
          host.Command("move 145 67");Pump();
          Rectangle after=BadgeAttachment.ScreenBounds(badge.Handle);
          Check(after.X-before.X==145 && after.Y-before.Y==67,"Parent moved (145,67) but the floating badge moved ("+(after.X-before.X)+","+(after.Y-before.Y)+"); a real child must follow without a badge positioning call.");
          throw new InvalidOperationException("Expected the child attachment API after the movement check.");
        }
        Check(true,"Badge supports the child attachment API.");

        badge=NewBadge();
        IntPtr previousHandle=badge.Handle;
        Check(AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(previousHandle),callerDpi),"The original unattached HWND must use the caller DPI mode.");
        Point client=new Point(6,310);
        Check((bool)attach.Invoke(badge,new object[]{host.Window,client}),"Attaching to the independent helper must succeed.");
        Check(AreDpiAwarenessContextsEqual(callerDpi,GetThreadDpiAwarenessContext()),"TryAttach must restore the caller's thread DPI context.");
        badge.Show();Pump();
        IntPtr child=badge.Handle,button=badge.Controls[0].Handle;
        Check((IntPtr)attachedParent.GetValue(badge,null)==host.Window && GetParent(child)==host.Window,"Badge HWND must be parented to the helper HWND.");
        uint style=unchecked((uint)GetWindowLong(child,-16));
        Check((style&0x40000000)!=0 && (style&0x80000000)==0,"Badge must have WS_CHILD and must not have WS_POPUP.");
        Check(GetParent(button)==child,"The native button must remain inside the badge.");
        uint colorKey,layerFlags;byte alpha;
        Check(GetLayeredWindowAttributes(child,out colorKey,out alpha,out layerFlags) && alpha==255 && (layerFlags&2)!=0 && (layerFlags&1)==0,"The attached badge must initialize its own opaque surface, not an invisible or color-keyed layer.");
        Check(AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(child),parentDpi),"Badge HWND DPI context must match its parent.");
        Check(AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(button),parentDpi),"The button HWND DPI context must also match the parent.");
        Check(GetDpiForWindow(child)==GetDpiForWindow(host.Window),"Parent and child must report the same effective DPI.");
        Rectangle original=(Rectangle)screenBounds.GetValue(badge,null);
        int expectedSize=(int)Math.Round(40*GetDpiForWindow(host.Window)/96d);
        Check(original.Width==expectedSize && original.Height==expectedSize,"The badge must retain its scaled 40px dimensions.");
        Point converted;
        Check(BadgeAttachment.TryScreenToClient(host.Window,original.Location,out converted) && converted==client,"Screen/client conversion must round-trip the child's placement.");
        Check(AreDpiAwarenessContextsEqual(callerDpi,GetThreadDpiAwarenessContext()),"Coordinate conversion must restore thread DPI context.");

        Point[] moves={new Point(145,67),new Point(-92,121),new Point(300,-84)};
        foreach(Point delta in moves) {
          Rectangle before=(Rectangle)screenBounds.GetValue(badge,null);
          // No tracker, timer, TryAttach, SetScale or badge positioning call occurs here.
          host.Command("move "+delta.X+" "+delta.Y);Pump();
          Rectangle after=(Rectangle)screenBounds.GetValue(badge,null);
          Check(after.X-before.X==delta.X && after.Y-before.Y==delta.Y && after.Size==before.Size,"Parent movement must carry the child by exactly the same delta without repositioning it.");
        }

        Rectangle beforeCover=(Rectangle)screenBounds.GetValue(badge,null);
        string coverReply=host.Command("cover");Pump();
        IntPtr cover=new IntPtr(Int64.Parse(coverReply.Substring(3)));
        Check(GetParent(cover)==host.Window,"The covering panel must be a sibling created by the independent host process.");
        Check(GetWindow(child,3)!=IntPtr.Zero && ChildAtBadgeCenter(host.Window,beforeCover)==cover,"A newly raised host panel must actually cover the badge in sibling Z order and native child hit testing.");
        MethodInfo ensureFront=type.GetMethod("EnsureFront",Type.EmptyTypes);
        if(ensureFront!=null)ensureFront.Invoke(badge,null);
        Pump();
        Check(GetWindow(child,3)==IntPtr.Zero,"EnsureFront must put the badge above the host's covering child; the old implementation leaves it covered.");
        Check(ChildAtBadgeCenter(host.Window,beforeCover)==child,"Native child hit testing must reach the raised badge instead of the host's covering panel.");
        Check((Rectangle)screenBounds.GetValue(badge,null)==beforeCover,"Raising the attached badge must preserve its screen position and size.");

        int clicks=0;badge.ActivatedByUser+=delegate {clicks++;};
        SendMessage(button,0x0201,new IntPtr(1),new IntPtr((10<<16)|10));
        SendMessage(button,0x0202,IntPtr.Zero,new IntPtr((10<<16)|10));
        Pump();
        Check(clicks==1,"A directed mouse press/release must still trigger the badge's existing click event exactly once.");
        host.Command("hide");Pump();
        Check(!IsWindowVisible(child),"Hiding the helper parent must hide the attached badge.");
        host.Command("show");Pump();
        Check(IsWindowVisible(child),"Showing the helper parent must show its badge without calling badge.Show again.");

        host.Command("destroy");
        WaitUntil(delegate{return !IsWindow(host.Window);},"The helper parent did not close.");
        Check(!IsWindow(child) || !IsWindowVisible(child),"Parent destruction must not leave a visible orphan badge.");
        host.Dispose();host=null;
        badge.Dispose();badge=null;
        host=new HostProcess();badge=NewBadge();
        Check((bool)attach.Invoke(badge,new object[]{host.Window,client}),"A replacement badge must attach to a replacement parent after destruction.");
        badge.Show();Pump();
        Check(IsWindowVisible(badge.Handle) && GetParent(badge.Handle)==host.Window,"The replacement attached badge must be visible and correctly parented.");
        detach.Invoke(badge,null);
        Check((IntPtr)attachedParent.GetValue(badge,null)==IntPtr.Zero && !badge.Visible,"Detach must clear attachment and hide the badge.");
        Check(AreDpiAwarenessContextsEqual(callerDpi,GetThreadDpiAwarenessContext()),"The complete attachment lifecycle must preserve caller DPI context.");
        Console.WriteLine("Attached badge checks passed: "+checks+"; cross-monitor DPI movement not exercised by the off-screen helper.");
        return 0;
      }catch(Exception error) {
        Exception cause=error is TargetInvocationException && error.InnerException!=null?error.InnerException:error;
        Console.Error.WriteLine("FAIL: "+cause);return 1;
      }finally {
        if(badge!=null)badge.Dispose();
        if(host!=null)host.Dispose();
      }
    }
    static QuotaBadge NewBadge() {
      var badge=new QuotaBadge();
      var state=new QuotaState {Available=true};state.Windows.Add(new QuotaWindow {Label="本周",Remaining=72,Used=28});
      badge.SetState(state);return badge;
    }
    static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);checks++;}
    static void Pump(){Application.DoEvents();Thread.Sleep(5);Application.DoEvents();}
    static IntPtr ChildAtBadgeCenter(IntPtr parent,Rectangle badgeBounds) {
      Point client;
      if(!BadgeAttachment.TryScreenToClient(parent,new Point(badgeBounds.Left+badgeBounds.Width/2,badgeBounds.Top+badgeBounds.Height/2),out client))return IntPtr.Zero;
      BadgeAttachment.DpiScope scope;
      if(!BadgeAttachment.TryEnterDpiContext(parent,out scope))return IntPtr.Zero;
      using(scope)return ChildWindowFromPointEx(parent,new NativePoint {X=client.X,Y=client.Y},7);
    }
    static void WaitUntil(Func<bool> predicate,string error) {
      Stopwatch watch=Stopwatch.StartNew();
      while(!predicate()){if(watch.ElapsedMilliseconds>5000)throw new TimeoutException(error);Pump();}
    }

    sealed class HostForm : Form {
      protected override bool ShowWithoutActivation {get{return true;}}
      protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000080;return p;}}
    }
    static int RunParent() {
      if(!SetProcessDpiAwarenessContext(new IntPtr(-4))){Console.WriteLine("ERROR parent DPI setup");return 2;}
      Application.EnableVisualStyles();
      var form=new HostForm {Text="Codex quota attachment test parent",ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000),Size=new Size(520,420)};
      form.Shown+=delegate {
        SetWindowPos(form.Handle,IntPtr.Zero,-20000,-20000,520,420,0x0014);
        Console.WriteLine("READY "+form.Handle.ToInt64());Console.Out.Flush();
        var reader=new Thread(delegate() {
          string command;
          while((command=Console.ReadLine())!=null) {
            string current=command;
            try {form.BeginInvoke(new Action(delegate {
              string[] parts=current.Split(' ');
              if(parts[0]=="move") {
                Rectangle bounds=BadgeAttachment.ScreenBounds(form.Handle);
                bool moved=SetWindowPos(form.Handle,IntPtr.Zero,bounds.Left+Int32.Parse(parts[1]),bounds.Top+Int32.Parse(parts[2]),0,0,0x0015);
                Console.WriteLine(moved?"OK":"ERROR move");
              }else if(parts[0]=="cover") {
                var cover=new Panel {Dock=DockStyle.Fill,BackColor=Color.Black};
                form.Controls.Add(cover);cover.Show();cover.BringToFront();
                bool raised=SetWindowPos(cover.Handle,IntPtr.Zero,0,0,0,0,0x0013);
                Console.WriteLine(raised?"OK "+cover.Handle.ToInt64():"ERROR cover");
              }else if(parts[0]=="hide"){form.Hide();Console.WriteLine("OK");}
              else if(parts[0]=="show"){form.Show();Console.WriteLine("OK");}
              else if(parts[0]=="destroy"){Console.WriteLine("OK");Console.Out.Flush();form.Close();return;}
              else Console.WriteLine("ERROR command");
              Console.Out.Flush();
            }));}catch(InvalidOperationException){return;}
          }
        });reader.IsBackground=true;reader.Start();
      };
      Application.Run(form);return 0;
    }
    sealed class HostProcess : IDisposable {
      readonly Process process;
      internal readonly IntPtr Window;
      internal int ProcessId {get{return process.Id;}}
      internal HostProcess() {
        var start=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--parent") {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        process=Process.Start(start);
        string ready=ReadReply();
        if(ready==null || !ready.StartsWith("READY ",StringComparison.Ordinal)){Dispose();throw new InvalidOperationException("Helper startup failed: "+ready);}
        Window=new IntPtr(Int64.Parse(ready.Substring(6)));
      }
      string ReadReply() {
        Task<string> reply=Task.Factory.StartNew(delegate{return process.StandardOutput.ReadLine();});
        WaitUntil(delegate{return reply.IsCompleted;},"Helper response timed out.");
        return reply.Result;
      }
      internal string Command(string command) {
        process.StandardInput.WriteLine(command);process.StandardInput.Flush();
        string reply=ReadReply();if(reply!="OK" && (reply==null || !reply.StartsWith("OK ",StringComparison.Ordinal)))throw new InvalidOperationException("Helper command failed: "+reply);
        return reply;
      }
      public void Dispose() {
        if(process==null)return;
        try {
          if(!process.HasExited){process.StandardInput.WriteLine("destroy");process.StandardInput.Flush();}
          WaitUntil(delegate{return process.HasExited;},"Test helper did not exit.");
        }catch{try{if(!process.HasExited)process.Kill();}catch{}}
        process.Dispose();
      }
    }
  }
}
