using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace CodexQuota {
  internal static class BadgePlacement {
    internal static Point Calculate(Rectangle bounds,Size badge,float scale,Point? avatarOffset) {
      if(avatarOffset.HasValue)return new Point(bounds.Left+avatarOffset.Value.X-badge.Width/2,bounds.Bottom-avatarOffset.Value.Y-badge.Height-(int)(8*scale));
      return new Point(bounds.Left+(int)(6*scale),bounds.Bottom-(int)(50*scale)-badge.Height);
    }
  }
  internal sealed class AvatarVisibilityState {
    IntPtr target;
    Rectangle bounds;
    float scale;
    internal int Revision {get;private set;}
    internal bool Visible {get;private set;}
    internal Point? Offset {get;private set;}
    internal bool ObserveWindow(IntPtr nextTarget,Rectangle nextBounds,float nextScale) {
      bool reset=target!=nextTarget || bounds.Size!=nextBounds.Size || Math.Abs(scale-nextScale)>.001f;
      // Even moving away and back invalidates an in-flight coordinate snapshot.
      if(target!=nextTarget || bounds!=nextBounds || Math.Abs(scale-nextScale)>.001f)Revision++;
      target=nextTarget;bounds=nextBounds;scale=nextScale;
      if(reset){Visible=false;Offset=null;}
      return reset;
    }
    internal void Invalidate(){Revision++;Visible=false;Offset=null;}
    internal bool Apply(int revision,Point? offset) {
      if(revision!=Revision)return false;
      Offset=offset;Visible=offset.HasValue;return true;
    }
    internal static Point? VisibleOffset(Rectangle window,RectangleF avatar,bool offscreen,bool pointHitsAvatar) {
      if(offscreen || !pointHitsAvatar || window.Width<=0 || window.Height<=0 || avatar.Width<=0 || avatar.Height<=0)return null;
      if(Single.IsNaN(avatar.X) || Single.IsNaN(avatar.Y) || Single.IsNaN(avatar.Width) || Single.IsNaN(avatar.Height) ||
         Single.IsInfinity(avatar.X) || Single.IsInfinity(avatar.Y) || Single.IsInfinity(avatar.Width) || Single.IsInfinity(avatar.Height))return null;
      if(avatar.Left<window.Left || avatar.Top<window.Top || avatar.Right>window.Right || avatar.Bottom>window.Bottom)return null;
      float centerX=avatar.Left+avatar.Width/2,centerY=avatar.Top+avatar.Height/2;
      if(centerX>window.Left+window.Width/3f || centerY<window.Top+window.Height/2f)return null;
      return new Point((int)centerX-window.Left,window.Bottom-(int)avatar.Top);
    }
  }
  internal class WindowTracker : IDisposable {
    const uint EventSystemForeground=0x0003;
    const uint EventSystemMinimizeStart=0x0016,EventSystemMinimizeEnd=0x0017;
    const uint EventObjectDestroy=0x8001,EventObjectShow=0x8002,EventObjectHide=0x8003,EventObjectReorder=0x8004,EventObjectLocationChange=0x800B;
    const int ObjectIdWindow=0,ChildIdSelf=0;
    // OUTOFCONTEXT delivers callbacks on the registering message-pump thread.
    const uint WinEventOutOfContext=0;
    delegate void WinEventCallback(IntPtr hook,uint eventType,IntPtr window,int objectId,int childId,uint eventThread,uint eventTime);
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWinEventHook(uint eventMin,uint eventMax,IntPtr module,WinEventCallback callback,uint processId,uint threadId,uint flags);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out Rect rect,int size);
    public IntPtr Target {get;private set;}
    public Rectangle Bounds {get;private set;}
    public float Scale {get;private set;}
    public int LayoutVersion {get;private set;}
    public bool AvatarVisible {get{return avatarVisibility.Visible;}}
    public bool EventsAvailable {get;private set;}
    public event EventHandler Changed;
    // Root the delegate until every native hook has been removed.
    readonly WinEventCallback winEventCallback;
    readonly List<IntPtr> hooks=new List<IntPtr>();
    readonly AvatarVisibilityState avatarVisibility=new AvatarVisibilityState();
    DateTime lastSearch=DateTime.MinValue,lastAvatarSearch=DateTime.MinValue;
    DateTime lastAvatarCheck=DateTime.MinValue;
    AutomationElement cachedAvatar;
    Point? avatarOffset;
    bool searchingAvatar,searchRequested=true,disposed;
    // Construct and dispose on the application's UI message-pump thread.
    public WindowTracker() {
      winEventCallback=OnWinEvent;
      bool foreground=InstallHook(EventSystemForeground,EventSystemForeground);
      bool location=InstallHook(EventObjectLocationChange,EventObjectLocationChange);
      bool minimize=InstallHook(EventSystemMinimizeStart,EventSystemMinimizeEnd);
      bool visibility=InstallHook(EventObjectDestroy,EventObjectReorder);
      EventsAvailable=foreground && location && minimize && visibility;
    }
    bool InstallHook(uint first,uint last) {
      try {
        IntPtr hook=SetWinEventHook(first,last,IntPtr.Zero,winEventCallback,0,0,WinEventOutOfContext);
        if(hook==IntPtr.Zero)return false;
        hooks.Add(hook);return true;
      }catch(DllNotFoundException){return false;}
      catch(EntryPointNotFoundException){return false;}
    }
    void OnWinEvent(IntPtr hook,uint eventType,IntPtr window,int objectId,int childId,uint eventThread,uint eventTime) {
      if(disposed)return;
      if(eventType==EventSystemForeground) {
        // Target discovery belongs to Update, never to this native callback.
        searchRequested=true;
        // Away/back events may be coalesced before Update sees the intermediate
        // foreground. Revoke old confirmation now; reuse the cached element later.
        InvalidateAvatar(false);
      } else {
        if(window==IntPtr.Zero || window!=Target)return;
        if(eventType==EventSystemMinimizeStart || eventType==EventSystemMinimizeEnd) {
          // System events identify the target window directly.
        } else {
          // Carets, client objects, and resized child controls are irrelevant.
          if(childId!=ChildIdSelf)return;
          if(eventType==EventObjectReorder) {
            if(objectId!=ObjectIdWindow && objectId!=-4)return; // parent client reorders its HWND children
          } else if(objectId!=ObjectIdWindow)return;
          if(eventType==EventObjectDestroy)searchRequested=true;
          else if(eventType!=EventObjectShow && eventType!=EventObjectHide && eventType!=EventObjectLocationChange && eventType!=EventObjectReorder)return;
        }
      }
      EventHandler handler=Changed;
      if(handler!=null)handler(this,EventArgs.Empty);
    }
    public void Dispose() {
      if(disposed)return;
      disposed=true;EventsAvailable=false;
      avatarVisibility.Invalidate();avatarOffset=null;cachedAvatar=null;
      foreach(IntPtr hook in hooks)UnhookWinEvent(hook);
      hooks.Clear();Changed=null;
      GC.KeepAlive(winEventCallback);
    }
    public bool Update() {
      if(disposed)return false;
      IntPtr foreground=GetForegroundWindow();
      uint foregroundPid;GetWindowThreadProcessId(foreground,out foregroundPid);
      if(searchRequested || DateTime.UtcNow-lastSearch>TimeSpan.FromSeconds(2)) {
        searchRequested=false;
        lastSearch=DateTime.UtcNow;
        IntPtr selected=IntPtr.Zero;
        long largest=0;
        foreach(var p in Process.GetProcessesByName("ChatGPT")) {
          try {
            IntPtr handle=p.MainWindowHandle;
            Rect r;
            if(handle==IntPtr.Zero || !IsWindowVisible(handle) || !GetWindowRect(handle,out r))continue;
            long size=(long)(r.Right-r.Left)*(r.Bottom-r.Top);
            if((uint)p.Id==foregroundPid){selected=handle;break;}
            if(size>largest){largest=size;selected=handle;}
          } catch{} finally{p.Dispose();}
        }
        if(selected!=Target){Target=selected;InvalidateAvatar(true);LayoutVersion++;}
      }
      if(Target==IntPtr.Zero || IsIconic(Target) || !IsWindowVisible(Target)){InvalidateAvatar(false);return false;}
      // A visible Codex window may be beside the foreground browser. Its child
      // badge follows native window occlusion; avatar probing decides visibility.
      Rect bounds;
      if(DwmGetWindowAttribute(Target,9,out bounds,Marshal.SizeOf(typeof(Rect)))!=0 && !GetWindowRect(Target,out bounds)){InvalidateAvatar(false);return false;}
      Size previousSize=Bounds.Size;float previousScale=Scale;
      Bounds=Rectangle.FromLTRB(bounds.Left,bounds.Top,bounds.Right,bounds.Bottom);
      try{Scale=GetDpiForWindow(Target)/96f;}catch{Scale=1f;}
      if(Scale<=0)Scale=1;
      // Pure translation belongs to the native parent; it must not move its child again.
      if(previousSize!=Bounds.Size || Math.Abs(previousScale-Scale)>.001f)LayoutVersion++;
      if(avatarVisibility.ObserveWindow(Target,Bounds,Scale)) {
        // Revoke confirmation immediately, but keep the element and probe cadence
        // while resizing so each size event cannot trigger a new tree traversal.
        avatarOffset=null;
      }
      if(Bounds.Width<=150 || Bounds.Height<=150){InvalidateAvatar(false);return false;}
      LocateAvatar();
      return AvatarVisible;
    }
    public Point BadgeLocation(Size badge) {
      return BadgePlacement.Calculate(Bounds,badge,Scale,avatarOffset);
    }
    void InvalidateAvatar(bool clearElement) {
      avatarVisibility.Invalidate();avatarOffset=null;lastAvatarCheck=DateTime.MinValue;
      if(clearElement){cachedAvatar=null;lastAvatarSearch=DateTime.MinValue;}
    }
    sealed class AvatarObservation {
      internal AutomationElement Element;
      internal Point? Offset;
    }
    static bool IsAvatarName(string name) {
      return name=="打开个人资料菜单" || name=="Open profile menu" || name=="Open account menu";
    }
    static AvatarObservation ReadAvatar(AutomationElement element,Rectangle bounds) {
      var observation=new AvatarObservation();
      if(element==null)return observation;
      try {
        var current=element.Current;
        if(current.ControlType!=ControlType.Button || !IsAvatarName(current.Name))return observation;
        observation.Element=element;
        var r=current.BoundingRectangle;
        var avatar=new RectangleF((float)r.Left,(float)r.Top,(float)r.Width,(float)r.Height);
        Point? offset=AvatarVisibilityState.VisibleOffset(bounds,avatar,current.IsOffscreen,true);
        if(!offset.HasValue)return observation;
        // IsOffscreen alone does not report occlusion by an overlaid web view.
        var hit=AutomationElement.FromPoint(new System.Windows.Point(r.Left+r.Width/2,r.Top+r.Height/2));
        for(int depth=0;hit!=null && depth<8;depth++) {
          if(Automation.Compare(element,hit)){observation.Offset=offset;break;}
          hit=TreeWalker.RawViewWalker.GetParent(hit);
        }
        return observation;
      }catch(ElementNotAvailableException){return new AvatarObservation();}
      catch(InvalidOperationException){return new AvatarObservation();}
      catch(ArgumentException){return new AvatarObservation();}
      catch(COMException){return new AvatarObservation();}
    }
    static AvatarObservation ProbeAvatar(IntPtr target,Rectangle bounds,AutomationElement cached,bool searchTree) {
      AvatarObservation observation=ReadAvatar(cached,bounds);
      if(observation.Offset.HasValue || !searchTree)return observation;
      try {
        var root=AutomationElement.FromHandle(target);
        var name=new OrCondition(new PropertyCondition(AutomationElement.NameProperty,"打开个人资料菜单"),new PropertyCondition(AutomationElement.NameProperty,"Open profile menu"),new PropertyCondition(AutomationElement.NameProperty,"Open account menu"));
        var matches=root.FindAll(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.IsOffscreenProperty,false),name));
        foreach(AutomationElement element in matches) {
          AvatarObservation candidate=ReadAvatar(element,bounds);
          if(candidate.Offset.HasValue)return candidate;
          if(observation.Element==null && candidate.Element!=null)observation=candidate;
        }
      }catch(ElementNotAvailableException){}
      catch(InvalidOperationException){}
      catch(ArgumentException){}
      catch(COMException){}
      return observation;
    }
    async void LocateAvatar() {
      DateTime now=DateTime.UtcNow;
      if(disposed || searchingAvatar || now-lastAvatarCheck<TimeSpan.FromMilliseconds(850))return;
      bool searchTree=now-lastAvatarSearch>=TimeSpan.FromSeconds(2);
      if(cachedAvatar==null && !searchTree)return;
      searchingAvatar=true;lastAvatarCheck=now;
      // Reserve a search interval before the async work so movement cannot cause
      // repeated full-tree searches when their results are subsequently discarded.
      if(searchTree)lastAvatarSearch=now;
      IntPtr target=Target;Rectangle bounds=Bounds;int revision=avatarVisibility.Revision;
      AutomationElement cached=cachedAvatar;
      try {
        AvatarObservation result=await Task.Run(delegate{return ProbeAvatar(target,bounds,cached,searchTree);});
        if(disposed || target!=Target || bounds!=Bounds || revision!=avatarVisibility.Revision)return;
        Rect actual;
        if(DwmGetWindowAttribute(target,9,out actual,Marshal.SizeOf(typeof(Rect)))!=0 && !GetWindowRect(target,out actual))return;
        if(Rectangle.FromLTRB(actual.Left,actual.Top,actual.Right,actual.Bottom)!=bounds)return;
        bool wasVisible=AvatarVisible;Point? oldOffset=avatarOffset;
        cachedAvatar=result.Element;
        if(!avatarVisibility.Apply(revision,result.Offset))return;
        avatarOffset=avatarVisibility.Offset;
        if(wasVisible!=AvatarVisible || oldOffset!=avatarOffset) {
          LayoutVersion++;
          EventHandler handler=Changed;if(handler!=null)handler(this,EventArgs.Empty);
        }
      }finally{searchingAvatar=false;}
    }
  }
}
