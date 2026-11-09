using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using CodexQuota;

class ChartDwellTests
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, int flags, uint access, IntPtr security);
    [DllImport("user32.dll")] static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] static extern IntPtr SetActiveWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    static int checks;
    static void Check(bool result, string reason) { if (!result) throw new Exception(reason); checks++; }

    static int Main()
    {
        // The test windows never appear on the user's desktop or move its cursor.
        IntPtr desktop = CreateDesktop("QuotaDwell-" + Guid.NewGuid().ToString("N"), IntPtr.Zero, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero);
        Exception failure = null;
        Thread worker = new Thread(delegate() {
            try {
                if (desktop == IntPtr.Zero || !SetThreadDesktop(desktop)) throw new Exception("Could not attach the private test desktop");
                TestChart(false);
                TestChart(true);
            } catch (Exception error) { failure = error; }
        });
        worker.Start();
        worker.Join();
        if (desktop != IntPtr.Zero) CloseDesktop(desktop);
        if (failure != null) { Console.Error.WriteLine(failure); return 1; }
        Console.WriteLine("Chart dwell: " + checks + " checks PASS");
        return 0;
    }

    static void Pump(int milliseconds)
    {
        Stopwatch clock = Stopwatch.StartNew();
        do { Application.DoEvents(); Thread.Sleep(4); } while (clock.ElapsedMilliseconds < milliseconds);
        Application.DoEvents();
    }

    static void Raise(Control control, string method, EventArgs args)
    {
        typeof(Control).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { args });
    }

    static void Move(Control chart, Point point)
    {
        // TrackMouseEvent synthesizes an immediate leave on an inactive desktop.
        // Deliver the managed move event to exercise the same chart handler while timing a stationary pointer.
        Raise(chart, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, 0));
    }

    static bool Visible(Form form)
    {
        foreach (Form owned in form.OwnedForms) if (owned is UsageDetailPopup && owned.Visible) return true;
        return false;
    }

    static void TestChart(bool heatmap)
    {
        List<ChartBucket> data = new List<ChartBucket>();
        for (int i = 0; i < 14; i++) {
            DateTime date = new DateTime(2026, 11, 2).AddDays(i);
            data.Add(new ChartBucket { Start = date, End = date, Tokens = 3000 + i, WorkPartial = true });
        }
        Point cell = heatmap ? new Point(6, 25) : new Point(72, 42);
        using (Form form = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Size = new Size(450, 250), ShowInTaskbar = false })
        using (Panel page = new Panel { AutoScroll = true, Size = new Size(440, 130), AutoScrollMinSize = new Size(420, 720) })
        using (Control chart = heatmap ? (Control)new TokenHeatmap(1) : new UsageBarChart(1)) {
            Action assignData = delegate {
                if (heatmap) ((TokenHeatmap)chart).SetData(data, Color.RosyBrown, true);
                else ((UsageBarChart)chart).SetData(data, false, Color.RosyBrown, true);
            };
            chart.Size = new Size(420, 210);
            page.Controls.Add(chart);
            form.Controls.Add(page);
            assignData();
            form.Show();
            Point cursor = Cursor.Position;
            Point offset = chart.PointToScreen(cell);
            form.Location = new Point(form.Left + cursor.X - offset.X, form.Top + cursor.Y - offset.Y);
            Pump(40);
            using (Bitmap bitmap = new Bitmap(chart.Width, chart.Height)) chart.DrawToBitmap(bitmap, new Rectangle(Point.Empty, chart.Size));

            int invalidations = 0;
            chart.Invalidated += delegate { invalidations++; };
            SendMessage(chart.Handle, 0x0200, IntPtr.Zero, new IntPtr((cell.Y << 16) | cell.X));
            Check(!Visible(form) && String.IsNullOrEmpty(chart.AccessibleDescription), chart.GetType().Name + " populated details on first mouse move");
            Pump(30);
            Move(chart, cell);
            Pump(520);
            Move(chart, new Point(cell.X + 1, cell.Y));
            Stopwatch finalMove = Stopwatch.StartNew();
            Move(chart, cell);
            Pump(610);
            Move(chart, cell);
            Check(!Visible(form) && invalidations == 0, "Moving within one bucket painted or showed the pending detail");
            while (finalMove.ElapsedMilliseconds < 1500 && !Visible(form)) Pump(6);
            Check(Visible(form) && finalMove.ElapsedMilliseconds >= 1000, "One full second from the final movement should reveal the detail; duplicate moves must not postpone it");
            Check(chart.AccessibleDescription.Contains("3,000"), "Wrong first cell");
            Form card = Array.Find(form.OwnedForms, delegate(Form item) { return item is UsageDetailPopup && item.Visible; });
            Check(chart.AccessibleDescription == card.AccessibleDescription && chart.AccessibleDescription.Contains("Local work records: unavailable"), "Assistive details should match the visible card, including missing coverage");

            // A wheel notch at the top limit still starts a fresh wait.
            SendMessage(page.Handle, 0x020A, new IntPtr(120 << 16), IntPtr.Zero);
            Check(!Visible(form), "The wheel at a scroll limit did not dismiss the detail");
            Pump(450);
            page.AutoScrollPosition = new Point(0, 26);
            typeof(ScrollableControl).GetMethod("OnScroll", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(page, new object[] { new ScrollEventArgs(ScrollEventType.EndScroll, 0, 26) });
            Pump(650);
            Check(!Visible(form), "The old wheel deadline survived a later scroll");
            Pump(460);
            Check(Visible(form), "An unmoving cursor did not regain detail after scrolling");
            Check(chart.AccessibleDescription.Contains(heatmap ? "3,001" : "3,000"), "Re-hit used an outdated bucket or anchor after scroll");

            page.AutoScrollPosition = Point.Empty;
            Check(!Visible(form), "Programmatic scroll did not dismiss shown detail");
            Pump(1100);
            Check(Visible(form) && chart.AccessibleDescription.Contains("3,000"), "Programmatic scroll did not re-hit the stationary point");
            Move(chart, new Point(cell.X + 1, cell.Y));
            Check(!Visible(form), "Moving after reveal did not dismiss the card");
            Point current = chart.PointToClient(cursor);
            Move(chart, current);
            Raise(chart, "OnMouseLeave", EventArgs.Empty);
            Pump(1060);
            Check(!Visible(form), "A pending callback survived MouseLeave");

            Move(chart, current);
            page.Hide();
            page.Show();
            current = chart.PointToClient(cursor);
            Pump(1060);
            Check(!Visible(form), "The old page's dwell survived hiding its ancestor");
            Move(chart, current);
            assignData();
            Pump(1060);
            Check(!Visible(form), "Resetting data retained the old candidate");
            chart.Refresh();
            Move(chart, current);
            using (Control cover = new Panel { Bounds = page.Bounds }) {
                form.Controls.Add(cover);
                cover.BringToFront();
                Move(chart, current);
                Pump(1060);
                Check(!Visible(form), "A chart behind another control showed a detail");
            }
            // On the private desktop activate a second window away from the
            // pointer. The old intent must die; a fresh dwell on the still-exposed
            // chart must succeed without transferring activation back to it.
            using (Form other = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(700, 700), Size = new Size(90, 90) }) {
                SetActiveWindow(form.Handle);
                chart.Focus();
                Move(chart, current);
                other.Show();
                SetActiveWindow(other.Handle);
                Pump(1060);
                Check(!Visible(form), "Owner deactivation retained its old pending callback");
                Move(chart, current);
                Pump(1080);
                Check(Visible(form), "A fresh dwell over an exposed inactive chart required a click");
                Raise(chart, "OnMouseLeave", EventArgs.Empty);
                Raise(chart, "OnKeyDown", new KeyEventArgs(Keys.Right));
                Check(!Visible(form), "An unfocused chart responded to keyboard selection");
            }
            page.Height = chart.Height;
            page.AutoScrollPosition = Point.Empty;
            SetActiveWindow(form.Handle);
            chart.Focus();
            Raise(chart, "OnKeyDown", new KeyEventArgs(Keys.Right));
            Check(Visible(form), "Deliberate keyboard selection should be immediate");
            page.AutoScrollPosition = new Point(0, 490);
            Check(chart.Focused && !page.RectangleToScreen(page.ClientRectangle).IntersectsWith(chart.RectangleToScreen(chart.ClientRectangle)), "Expected a focused chart outside the viewport");
            Raise(chart, "OnKeyDown", new KeyEventArgs(Keys.Right));
            Check(!Visible(form), "A keyboard selection outside the visible viewport opened a floating card");
            page.AutoScrollPosition = Point.Empty;
            Move(chart, chart.PointToClient(cursor));
            chart.Dispose();
            Pump(1060);
            Check(!Visible(form), "A pending callback survived chart disposal");
        }
    }
}
