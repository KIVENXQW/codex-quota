using System;
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
  internal class WindowTracker {
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; }
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
    DateTime lastSearch=DateTime.MinValue,lastAvatarSearch=DateTime.MinValue;
    Point? avatarOffset;
    bool searchingAvatar;
    public bool Update(bool nativePopupVisible) {
      IntPtr foreground=GetForegroundWindow();
      uint foregroundPid;GetWindowThreadProcessId(foreground,out foregroundPid);
      if(DateTime.UtcNow-lastSearch>TimeSpan.FromSeconds(2)) {
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
        if(selected!=Target){Target=selected;avatarOffset=null;lastAvatarSearch=DateTime.MinValue;}
      }
      if(Target==IntPtr.Zero || IsIconic(Target) || !IsWindowVisible(Target))return false;
      uint targetPid;GetWindowThreadProcessId(Target,out targetPid);
      if(foregroundPid!=targetPid && !(nativePopupVisible && foregroundPid==(uint)Process.GetCurrentProcess().Id))return false;
      Rect bounds;
      if(DwmGetWindowAttribute(Target,9,out bounds,Marshal.SizeOf(typeof(Rect)))!=0 && !GetWindowRect(Target,out bounds))return false;
      Bounds=Rectangle.FromLTRB(bounds.Left,bounds.Top,bounds.Right,bounds.Bottom);
      try{Scale=GetDpiForWindow(Target)/96f;}catch{Scale=1f;}
      if(Scale<=0)Scale=1;
      LocateAvatar();
      return Bounds.Width>150 && Bounds.Height>150;
    }
    public Point BadgeLocation(Size badge) {
      return BadgePlacement.Calculate(Bounds,badge,Scale,avatarOffset);
    }
    async void LocateAvatar() {
      if(searchingAvatar || DateTime.UtcNow-lastAvatarSearch<TimeSpan.FromSeconds(5))return;
      searchingAvatar=true;lastAvatarSearch=DateTime.UtcNow;
      IntPtr target=Target;Rectangle bounds=Bounds;
      try {
        Point? result=await Task.Run(delegate {
          try {
            var root=AutomationElement.FromHandle(target);
            var name=new OrCondition(new PropertyCondition(AutomationElement.NameProperty,"打开个人资料菜单"),new PropertyCondition(AutomationElement.NameProperty,"Open profile menu"),new PropertyCondition(AutomationElement.NameProperty,"Open account menu"));
            var match=root.FindFirst(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),name));
            if(match!=null) {
              var r=match.Current.BoundingRectangle;
              if(!r.IsEmpty && r.Width>0 && r.Height>0 && r.Left<bounds.Left+bounds.Width/2 && r.Top>bounds.Top+bounds.Height/2)
                return (Point?)new Point((int)(r.Left+r.Width/2)-bounds.Left,bounds.Bottom-(int)r.Top);
            }
          }catch{}
          return null;
        });
        if(target==Target && result.HasValue && bounds==Bounds)avatarOffset=result;
        else if(bounds!=Bounds)lastAvatarSearch=DateTime.MinValue;
      }finally{searchingAvatar=false;}
    }
  }
}
