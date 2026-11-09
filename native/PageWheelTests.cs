using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CodexQuota;

class PageWheelTests
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, int flags, uint access, IntPtr security);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] static extern IntPtr GetFocus();

    static int checks, failures;
    static void Check(bool condition, string description)
    {
        checks++;
        if (!condition) { failures++; Console.Error.WriteLine("FAIL: " + description); }
    }

    static void Wheel(Control receiver, Point screenPoint, int delta)
    {
        int packed = (ushort)screenPoint.X | ((ushort)screenPoint.Y << 16);
        SendMessage(receiver.Handle, 0x020A, new IntPtr(delta << 16), new IntPtr(packed));
        Application.DoEvents();
    }

    static int Offset(Panel page) { return -page.AutoScrollPosition.Y; }

    static void Run(string style)
    {
        using (var popup = new QuotaPopup())
        using (var detail = new UsageDetailPopup(1))
        {
            popup.ApplyPreferences(new UserPreferences { PanelStyle = style });
            popup.Location = new Point(40, 40);
            popup.Show();
            var tab = (Button)popup.Controls.Find("historyTab", true)[0];
            tab.PerformClick();
            Application.DoEvents();
            var page = (Panel)popup.Controls["historyPage"];
            var bars = page.Controls.OfType<UsageBarChart>().Single();
            var heatmap = page.Controls.OfType<TokenHeatmap>().Single();

            tab.Focus();
            Point chartPoint = bars.PointToScreen(new Point(80, 25));
            Wheel(page, chartPoint, -120);
            int step = Offset(page);
            Check(step > 0, style + " bare page scrolls");
            page.AutoScrollPosition = Point.Empty;
            Wheel(bars, chartPoint, -120);
            Check(Offset(page) == step && tab.Focused, style + " unfocused chart scrolls once without changing focus");
            page.AutoScrollPosition = Point.Empty;
            bars.Focus();
            for (int i = 0; i < 4; i++) Wheel(bars, chartPoint, -30);
            Check(Offset(page) == step && bars.Focused, style + " four high-resolution wheel deltas equal one notch");
            page.AutoScrollPosition = Point.Empty;
            Wheel(heatmap, heatmap.PointToScreen(new Point(10, 25)), -120);
            Check(Offset(page) == step, style + " heatmap scrolls its page once");

            page.AutoScrollPosition = Point.Empty;
            tab.Focus();
            Wheel(tab, chartPoint, -120);
            Check(Offset(page) == step && tab.Focused, style + " focused tab passes a wheel over the chart to its page");
            int beforeHeaderWheel = Offset(page);
            Wheel(tab, tab.PointToScreen(Point.Empty), -120);
            Check(Offset(page) == beforeHeaderWheel, style + " wheel over the header itself leaves the history page still");
            page.AutoScrollPosition = Point.Empty;
            bars.Focus();
            detail.ShowFor(bars, new Rectangle(64, 10, 20, 25));
            Check(detail.Visible && GetActiveWindow() == popup.Handle && GetFocus() == bars.Handle, style + " showing detail preserves chart focus and owner activation");
            Point cardPoint = detail.PointToScreen(new Point(10, 10));
            Wheel(detail, cardPoint, -120);
            Check(Offset(page) == step && !detail.Visible, style + " wheel delivered directly to detail dismisses it and scrolls once");

            int bottom = page.DisplayRectangle.Height - page.ClientSize.Height;
            for (int i = 0; step > 0 && i < bottom / step + 2; i++) Wheel(page, chartPoint, -120);
            Check(Offset(page) == bottom, style + " repeated wheel reaches the bottom");
            int edgeEvents = 0;
            page.MouseWheel += delegate { edgeEvents++; };
            Wheel(tab, page.PointToScreen(new Point(10, 10)), -120);
            Check(edgeEvents == 1 && Offset(page) == bottom, style + " wheel at bottom still notifies hover exactly once");
            for (int i = 0; step > 0 && i < bottom / step + 2; i++) Wheel(page, chartPoint, 120);
            Check(Offset(page) == 0, style + " reverse wheel reaches the top");
        }
    }

    static int Main()
    {
        // Native focus and activation tests run on a private, never-selected desktop.
        // Opening or closing the real topmost forms cannot affect the user's desktop.
        IntPtr desktop = CreateDesktop("QuotaWheelTests-" + Guid.NewGuid().ToString("N"), IntPtr.Zero, IntPtr.Zero, 0, 0x1ff, IntPtr.Zero);
        if (desktop == IntPtr.Zero || !SetThreadDesktop(desktop)) {
            Console.Error.WriteLine("Cannot attach wheel tests to a private desktop: " + Marshal.GetLastWin32Error());
            return 1;
        }
        Application.EnableVisualStyles();
        foreach (string style in new[] { "minimal", "glass" }) Run(style);
        Console.WriteLine("Native page wheel: " + (checks - failures) + "/" + checks + " checks PASS");
        return failures == 0 ? 0 : 1;
    }
}
