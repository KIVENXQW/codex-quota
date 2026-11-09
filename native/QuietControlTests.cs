using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using CodexQuota;

class QuietControlTests
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static object Field(object value, string name) { return value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(value); }
    static void Call(object value, string name, params object[] arguments) { value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(value, arguments); }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { internal int Left, Top, Right, Bottom; public override string ToString() { return Left + "," + Top + "," + Right + "," + Bottom; } }
    static string NativeBounds(Control control) { NativeRect rectangle; Check(GetWindowRect(control.Handle, out rectangle), "Test control must have a real native HWND"); return rectangle.ToString(); }
    static QuotaState Fixture()
    {
        var state = new QuotaState { Available = true, Activity = new WorkActivity { Status = "ready" } };
        for (int i = 0; i < 4; i++) state.Windows.Add(new QuotaWindow { Label = "Quota " + i, Remaining = 70 + i });
        state.DailyTokens.Add(new TokenDay { Date = DateTime.UtcNow.AddHours(8).ToString("yyyy-MM-dd"), Tokens = 1200 });
        return state;
    }
    [STAThread] static int Main()
    {
        try { Application.EnableVisualStyles(); foreach (string style in new[] { "minimal", "glass" }) Run(style); Console.WriteLine("Quiet controls: " + checks + " checks PASS"); return 0; }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + (error.InnerException ?? error).Message); return 1; }
    }
    static void Run(string style)
    {
        using (var host = new OffscreenHost())
        using (var popup = new QuotaPopup())
        {
            popup.TopLevel = false; host.Controls.Add(popup); popup.ApplyPreferences(new UserPreferences { PanelStyle = style });
            QuotaState state = Fixture(); popup.SetState(state); host.Show(); popup.Show();
            Call(popup, "Navigate", "settings"); Thread.Sleep(140); Application.DoEvents();
            var settings = (Panel)Field(popup, "settingsPage"); var corner = (Control)Field(popup, "cornerRail");
            settings.AutoScrollPosition = new Point(0, 80); Application.DoEvents();
            Point scroll = settings.AutoScrollPosition, location = popup.Location; string initial = NativeBounds(corner);
            int moves = 0; corner.LocationChanged += delegate { moves++; };
            foreach (string mode in new[] { "cornerCapsule", "cornerTray", "cornerCard" })
            {
                ((Button)Field(popup, mode)).PerformClick();
                popup.SetState(state); // A normal same-shape refresh must also leave the controls still.
                Check(moves == 0, style + ": changing a corner choice must not move its native rail away and back (moves=" + moves + ")");
                Check(NativeBounds(corner) == initial && settings.AutoScrollPosition == scroll && popup.Location == location, style + ": corner controls retain native bounds, scroll and window location");
                Check((RectangleF)Field(corner, "lens") == (RectangleF)((Control)Field(corner, "selected")).Bounds, style + ": corner selection is immediately at its final highlight position");
            }
            Check(popup.Preferences.CornerStyle == "card", style + ": stationary choices still update the preference");
            Call(popup, "Navigate", "home");
            Check(((Panel)Field(popup, "homePage")).Left == (int)Math.Round(20 * (float)Field(popup, "scale")), style + ": page navigation must not start content displacement");
            var home = (Panel)Field(popup, "homePage"); var periods = (Control)Field(popup, "homePeriodRail");
            home.AutoScrollPosition = new Point(0, 15); Application.DoEvents();
            scroll = home.AutoScrollPosition; location = popup.Location; initial = NativeBounds(periods); moves = 0;
            periods.LocationChanged += delegate { moves++; };
            foreach (string name in new[] { "homeWeek", "homeMonth", "homeAll", "homeDay" })
            {
                ((Button)Field(popup, name)).PerformClick();
                Check(moves == 0, style + ": choosing a Home period must not reset scroll and move its native controls");
                Check(NativeBounds(periods) == initial && home.AutoScrollPosition == scroll && popup.Location == location, style + ": Home periods remain at the same native position");
                Check((RectangleF)Field(periods, "lens") == (RectangleF)((Control)Field(periods, "selected")).Bounds, style + ": Home period selection is immediately at its final highlight position");
            }
            Check((string)Field(popup, "homePeriod") == "day", style + ": stationary period controls still change the selected range");
            popup.Hide(); host.Hide();
        }
    }
    sealed class OffscreenHost : Form
    {
        internal OffscreenHost() { StartPosition = FormStartPosition.Manual; Location = new Point(-30000, -30000); Size = new Size(1400, 1200); ShowInTaskbar = false; FormBorderStyle = FormBorderStyle.None; }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { CreateParams p = base.CreateParams; p.ExStyle |= 0x08000000; return p; } }
    }
}
