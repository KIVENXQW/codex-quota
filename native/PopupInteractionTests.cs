using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using CodexQuota;

class PopupInteractionTests
{
    static int checks;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    static object Field(object target, string name) { return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
    static void Set(object target, string name, object value) { target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value); }
    static void Call(object target, string name, params object[] arguments) { target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments); }
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll")] static extern bool LogicalToPhysicalPointForPerMonitorDPI(IntPtr window, ref Point point);
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { internal int Left, Top, Right, Bottom; }
    [STAThread] static int Main()
    {
        try { Application.EnableVisualStyles(); NativeHookLifetime(); Run(); Console.WriteLine("Popup interactions: " + checks + " checks PASS"); return 0; }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + (error.InnerException ?? error).Message); return 1; }
    }
    static void NativeHookLifetime()
    {
        using (var popup = new QuotaPopup())
        using (var empty = new Region(Rectangle.Empty))
        {
            IntPtr window = popup.Handle;
            SetWindowLong(window, -20, GetWindowLong(window, -20) | 0x08000000); // WS_EX_NOACTIVATE.
            Region previousRegion = popup.Region; popup.Region = empty;
            if (previousRegion != null) previousRegion.Dispose();
            popup.Location = new Point(-30000, -30000);
            using (var guard = new OffscreenPositionGuard(window))
            {
                Check(popup.TopLevel, "Hook registration fixture must exercise a real top-level popup");
                Check((IntPtr)Field(popup, "pointerHookHandle") == IntPtr.Zero, "A hidden top-level popup has no mouse hook");
                popup.Show(); // No ShowAt/Activate and no physical input injection.
                NativeRect rectangle;
                Check(GetWindowRect(window, out rectangle) && rectangle.Right < SystemInformation.VirtualScreen.Left && rectangle.Bottom < SystemInformation.VirtualScreen.Top,
                    "The real top-level fixture remains completely outside the virtual desktop");
                MixedCoordinateHits(popup);
                IntPtr firstHook = (IntPtr)Field(popup, "pointerHookHandle");
                Check(popup.Visible && firstHook != IntPtr.Zero, "Showing the top-level popup must register the actual Windows mouse hook");
                popup.Hide();
                Check((IntPtr)Field(popup, "pointerHookHandle") == IntPtr.Zero, "Hiding clears the mouse hook handle");
                Check(!UnhookWindowsHookEx(firstHook), "The hidden popup's Windows hook must already be unregistered");
                popup.Show();
                IntPtr secondHook = (IntPtr)Field(popup, "pointerHookHandle");
                Check(secondHook != IntPtr.Zero, "Reopening registers a real mouse hook again");
                popup.Dispose();
                Check((IntPtr)Field(popup, "pointerHookHandle") == IntPtr.Zero, "Disposal clears the visible popup's mouse hook handle");
                Check(!UnhookWindowsHookEx(secondHook), "Disposal must unregister the actual Windows hook");
            }
        }
    }
    static void ObservePointer(QuotaPopup popup, int message, Point point, bool physical)
    {
        MethodInfo method = typeof(QuotaPopup).GetMethod("ObservePointerButton", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Invoke(popup, method.GetParameters().Length == 2 ? new object[] { message, point } : new object[] { message, point, physical });
    }
    static void MixedCoordinateHits(QuotaPopup popup)
    {
        Rectangle bounds = popup.RectangleToScreen(popup.ClientRectangle);
        Point nearRightEdge = new Point(bounds.Right - 1, bounds.Top + 50);
        // At 150% on a monitor to the left of the primary, a physical point may
        // lie outside the logical bounds even though it is one pixel inside them.
        Point physicalEdge = new Point((int)Math.Round(nearRightEdge.X * 1.5), (int)Math.Round(nearRightEdge.Y * 1.5));
        Check(!bounds.Contains(physicalEdge), "The coordinate-space fixture must distinguish raw physical coordinates from logical bounds");
        FieldInfo converterField = typeof(QuotaPopup).GetField("physicalToLogical", BindingFlags.Instance | BindingFlags.NonPublic);
        object savedConverter = converterField == null ? null : converterField.GetValue(popup);
        int calls = 0;
        Func<IntPtr, Point, Point?> mapped = delegate(IntPtr window, Point point) { calls++; return window == popup.Handle && point == physicalEdge ? (Point?)nearRightEdge : null; };
        try
        {
            if (converterField != null) converterField.SetValue(popup, mapped);
            ObservePointer(popup, 0x0201, physicalEdge, true); ObservePointer(popup, 0x0202, physicalEdge, true);
            Call(popup, "DismissAfterOutsideClick", null, EventArgs.Empty);
            Check(popup.Visible, "A physical hook point at the logical right edge stays inside after per-window conversion");
            Check(calls > 0, "Hook hit testing must use the window coordinate converter");
            int beforeLogical = calls;
            bool logicalInside = (bool)typeof(QuotaPopup).GetMethod("PointerInside", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, new object[] { nearRightEdge });
            Check(logicalInside && calls == beforeLogical, "Cursor.Position fallback is already logical and must not be converted again");
            using (var owned = new OffscreenHost())
            using (var empty = new Region(Rectangle.Empty))
            {
                owned.Location = new Point(-28000, -28000); owned.Size = new Size(220, 130); owned.Region = empty;
                popup.AddOwnedForm(owned); owned.Show();
                Point ownedEdge = new Point(owned.Bounds.Right - 1, owned.Bounds.Bottom - 1);
                Point ownedPhysical = new Point(ownedEdge.X * 2, ownedEdge.Y * 2);
                bool parentAttempt = false, ownerAttempt = false;
                mapped = delegate(IntPtr window, Point point) {
                    if (window == popup.Handle) { parentAttempt = true; return null; }
                    if (window == owned.Handle && point == ownedPhysical) { ownerAttempt = true; return ownedEdge; }
                    return null;
                };
                converterField.SetValue(popup, mapped);
                ObservePointer(popup, 0x0201, ownedPhysical, true); ObservePointer(popup, 0x0202, ownedPhysical, true);
                Call(popup, "DismissAfterOutsideClick", null, EventArgs.Empty);
                Check(popup.Visible && parentAttempt && ownerAttempt, "Owned-window hits must convert independently with the owned HWND after the main window rejects the point");
                owned.Hide(); popup.RemoveOwnedForm(owned);
            }
        }
        finally { if (converterField != null) converterField.SetValue(popup, savedConverter); }
        Func<IntPtr, Point, Point?> native = (Func<IntPtr, Point, Point?>)savedConverter;
        Point physical = nearRightEdge;
        Check(LogicalToPhysicalPointForPerMonitorDPI(popup.Handle, ref physical), "The real hidden HWND supports logical-to-physical conversion");
        Point? converted = native(popup.Handle, physical);
        Check(converted.HasValue && Math.Abs(converted.Value.X - nearRightEdge.X) <= 1 && Math.Abs(converted.Value.Y - nearRightEdge.Y) <= 1,
            "The production converter successfully round-trips the actual HWND's near-edge coordinates");
        Check(!native(popup.Handle, new Point(physical.X + 10000, physical.Y + 10000)).HasValue,
            "Native conversion rejects a point outside this window instead of reusing another window's coordinates");
    }
    sealed class OffscreenPositionGuard : NativeWindow, IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct WindowPosition
        {
            internal IntPtr Window, InsertAfter;
            internal int X, Y, Width, Height;
            internal uint Flags;
        }
        internal OffscreenPositionGuard(IntPtr window) { AssignHandle(window); }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0046) // WM_WINDOWPOSCHANGING: even an unexpected layout cannot expose the fixture.
            {
                WindowPosition position = (WindowPosition)Marshal.PtrToStructure(message.LParam, typeof(WindowPosition));
                position.X = -30000; position.Y = -30000; position.Flags |= 0x0010; // SWP_NOACTIVATE.
                Marshal.StructureToPtr(position, message.LParam, false);
            }
            base.WndProc(ref message);
        }
        public void Dispose() { if (Handle != IntPtr.Zero) ReleaseHandle(); }
    }
    static void Run()
    {
        using (var host = new OffscreenHost())
        using (var popup = new QuotaPopup())
        {
            // The tested Form is a child of an offscreen, non-activating host.
            // Its normal placement code cannot move any window onto the desktop.
            popup.TopLevel = false; host.Controls.Add(popup); host.Show(); popup.Show();
            Set(popup, "requestedPoint", new Point(80, 60)); Call(popup, "LayoutPage");
            int deactivations = 0; popup.Deactivate += delegate { deactivations++; };
            SendMessage(popup.Handle, 0x0006, new IntPtr(1), IntPtr.Zero); // WA_ACTIVE notification only.
            SendMessage(popup.Handle, 0x0200, IntPtr.Zero, new IntPtr(12 | (12 << 16))); // Pointer crosses panel.
            SendMessage(popup.Handle, 0x0006, IntPtr.Zero, host.Handle); // Focus handoff without a click.
            Check(deactivations == 1, "Native WM_ACTIVATE inactive notification must exercise the real deactivation path");
            Check(popup.Visible, "Focus handoff during pointer movement must not dismiss the popup");
            SendMessage(popup.Handle, 0x02A3, IntPtr.Zero, IntPtr.Zero); // WM_MOUSELEAVE.
            SendMessage(popup.Handle, 0x0200, IntPtr.Zero, new IntPtr(24 | (24 << 16)));
            Check(popup.Visible, "Leaving and reentering the panel without a click keeps it open");

            Point location = popup.Location;
            float scale = (float)Field(popup, "scale");
            for (int i = 0; i < 4; i++)
            {
                Call(popup, "Navigate", i % 2 == 0 ? "history" : "home");
                Panel content = (Panel)Field(popup, i % 2 == 0 ? "mainPage" : "homePage");
                Call(popup, "Animate", null, EventArgs.Empty);
                Check(popup.Location == location, "A page switch must not move the top-level popup");
                Check(Math.Abs(popup.Opacity - 1) < .0001, "Page switches must not flash the whole window opacity");
                Check(Math.Abs(content.Left - (int)Math.Round(20 * scale)) <= (int)Math.Round(2 * scale), "Page motion stays within two logical pixels");
                Thread.Sleep(125); Call(popup, "Animate", null, EventArgs.Empty);
                Check(!((System.Windows.Forms.Timer)Field(popup, "animation")).Enabled, "Page transition is complete within 120 ms");
                Check(popup.Location == location && content.Left == (int)Math.Round(20 * scale), "Page transition settles without a final window or content jump");
            }

            Rectangle bounds = popup.RectangleToScreen(popup.ClientRectangle);
            Point inside = new Point(bounds.Left + 30, bounds.Top + 30), outside = new Point(bounds.Right + 20, bounds.Bottom + 20);
            ObservePointer(popup, 0x0201, inside, false);
            ObservePointer(popup, 0x0202, inside, false);
            Call(popup, "DismissAfterOutsideClick", null, EventArgs.Empty);
            Check(popup.Visible, "An ordinary click inside the panel keeps it open");
            ObservePointer(popup, 0x0201, inside, false);
            ObservePointer(popup, 0x0202, outside, false); // A drag started inside.
            Call(popup, "DismissAfterOutsideClick", null, EventArgs.Empty);
            Check(popup.Visible, "Dragging a control beyond the panel does not count as an outside click");
            ObservePointer(popup, 0x0201, outside, false);
            ObservePointer(popup, 0x0202, outside, false);
            Call(popup, "DismissAfterOutsideClick", null, EventArgs.Empty);
            Check(!popup.Visible, "A deliberate completed outside click dismisses an already inactive panel");
            popup.Show();
            ObservePointer(popup, 0x0201, outside, false);
            Check(popup.Visible, "An entry press must not preempt the later avatar/corner/tray click toggle");
            ObservePointer(popup, 0x0202, outside, false);
            if (popup.Visible) popup.Hide(); else popup.Show(); // The existing entry MouseClick handler.
            Call(popup, "DismissAfterOutsideClick", null, EventArgs.Empty);
            Check(!popup.Visible, "Clicking an entry to close does not reopen the panel");
            Check(!((System.Windows.Forms.Timer)Field(popup, "outsideClickDelay")).Enabled, "Hiding the popup cancels queued outside-click dismissal");
            ObservePointer(popup, 0x0201, outside, false); // Opening press occurred while hidden.
            if (popup.Visible) popup.Hide(); else popup.Show();
            ObservePointer(popup, 0x0202, outside, false);
            Call(popup, "DismissAfterOutsideClick", null, EventArgs.Empty);
            Check(popup.Visible, "The click that opens the popup cannot immediately close it again");
            Call(popup, "OnKeyDown", new KeyEventArgs(Keys.Escape));
            Check(!popup.Visible, "Escape still closes the usage panel");
            popup.Show();
            Call((Control)Field(popup, "close"), "OnClick", EventArgs.Empty);
            Check(!popup.Visible, "The explicit close control still dismisses the panel");
            host.Hide();
        }
    }
    sealed class OffscreenHost : Form
    {
        internal OffscreenHost() { StartPosition = FormStartPosition.Manual; Location = new Point(-30000, -30000); Size = new Size(1400, 1200); ShowInTaskbar = false; FormBorderStyle = FormBorderStyle.None; }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { CreateParams p = base.CreateParams; p.ExStyle |= 0x08000000; return p; } }
    }
}
