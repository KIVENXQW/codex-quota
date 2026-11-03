using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace CodexQuota {
  public static class BadgeAttachment {
    const int WindowStyle=-16;
    const long ChildStyle=0x40000000L,PopupStyle=0x80000000L;
    const uint NoSize=0x0001,NoMove=0x0002,NoZOrder=0x0004,NoActivate=0x0010,NoOwnerZOrder=0x0200;
    [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
    [DllImport("user32.dll")] static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool AreDpiAwarenessContextsEqual(IntPtr first,IntPtr second);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool ScreenToClient(IntPtr window,ref NativePoint point);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] static extern bool SetLayeredWindowAttributes(IntPtr window,uint colorKey,byte alpha,uint flags);

    internal sealed class DpiScope : IDisposable {
      IntPtr previous;
      internal readonly IntPtr Context;
      internal DpiScope(IntPtr old,IntPtr context){previous=old;Context=context;}
      public void Dispose(){if(previous!=IntPtr.Zero){SetThreadDpiAwarenessContext(previous);previous=IntPtr.Zero;}}
    }
    internal static bool TryEnterDpiContext(IntPtr window,out DpiScope scope) {
      scope=null;
      if(window==IntPtr.Zero || !IsWindow(window))return false;
      try {
        IntPtr context=GetWindowDpiAwarenessContext(window);
        if(context==IntPtr.Zero)return false;
        IntPtr previous=SetThreadDpiAwarenessContext(context);
        if(previous==IntPtr.Zero)return false;
        scope=new DpiScope(previous,context);return true;
      }catch(EntryPointNotFoundException){return false;}
      catch(DllNotFoundException){return false;}
    }
    public static bool TryScreenToClient(IntPtr parent,Point screen,out Point client) {
      client=Point.Empty;DpiScope scope;
      if(!TryEnterDpiContext(parent,out scope))return false;
      using(scope) {
        NativePoint point=new NativePoint {X=screen.X,Y=screen.Y};
        if(!ScreenToClient(parent,ref point))return false;
        client=new Point(point.X,point.Y);return true;
      }
    }
    internal static Rectangle ScreenBounds(IntPtr window) {
      DpiScope scope;
      if(!TryEnterDpiContext(window,out scope))return Rectangle.Empty;
      using(scope) {
        NativeRect rect;
        return GetWindowRect(window,out rect)?Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom):Rectangle.Empty;
      }
    }
    internal static float ScaleForWindow(IntPtr window) {
      uint dpi=GetDpiForWindow(window);
      return dpi==0?1:dpi/96f;
    }
    internal static bool MatchesDpi(IntPtr window,IntPtr context) {
      return IsWindow(window) && AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(window),context);
    }
    internal static bool IsChildOf(IntPtr window,IntPtr parent) {
      if(!IsWindow(window) || GetParent(window)!=parent)return false;
      long style=IntPtr.Size==8?GetWindowLongPtr(window,WindowStyle).ToInt64():unchecked((uint)GetWindowLong(window,WindowStyle));
      return (style&ChildStyle)!=0 && (style&PopupStyle)==0;
    }
    internal static bool PositionChild(IntPtr window,Point client,Size size) {
      return SetWindowPos(window,IntPtr.Zero,client.X,client.Y,size.Width,size.Height,NoZOrder|NoActivate|NoOwnerZOrder);
    }
    internal static bool InitializeSurface(IntPtr window) {
      // An opaque layered child owns a redirected surface even when the host opts out.
      // No color key or WS_EX_TRANSPARENT: its pixels and mouse target remain present.
      return SetLayeredWindowAttributes(window,0,255,2); // LWA_ALPHA
    }
    internal static bool EnsureAboveSiblings(IntPtr window) {
      if(!IsWindow(window))return false;
      // Chromium inserts full-client renderer/GPU siblings above foreign children.
      // Change our own Z order only; never move the badge or modify the host styles.
      if(GetWindow(window,3)==IntPtr.Zero)return true; // GW_HWNDPREV: already first
      return SetWindowPos(window,IntPtr.Zero,0,0,0,0,NoMove|NoSize|NoActivate|NoOwnerZOrder); // HWND_TOP
    }
  }
}
