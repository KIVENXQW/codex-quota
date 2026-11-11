using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Kiven.WeeklyQuota
{
    internal static class WeeklySidebarTests
    {
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr security);
        [DllImport("user32.dll")] private static extern bool SetThreadDesktop(IntPtr desktop);
        [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);

        // All test windows belong to a private desktop that is never switched into view.
        private sealed class TestHost : NativeWindow, IDisposable
        {
            internal TestHost()
            {
                CreateHandle(new CreateParams {
                    ClassName = "STATIC", Caption = "Weekly ring attachment test",
                    Style = unchecked((int)0x80000000), ExStyle = 0x08000080,
                    X = -20000, Y = -20000, Width = 480, Height = 360
                });
            }
            public void Dispose() { if (Handle != IntPtr.Zero) DestroyHandle(); }
        }

        private static void Check(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException(message);
        }

        private static void CheckPlacement(Rectangle window, RectangleF avatar, float scale, Size badge, Point expected)
        {
            Point? offset = AvatarVisibilityState.VisibleOffset(window, avatar, false, true);
            Check(offset.HasValue, "A visible avatar should be located.");
            Point location = BadgePlacement.Calculate(window, badge, scale, offset.Value);
            Check(location == expected, "Expected badge at " + expected + ", got " + location);
        }

        private static void CheckAttachment()
        {
            int opens = 0;
            using (TestHost parent = new TestHost())
            using (TestHost panel = new TestHost())
            using (WeeklySidebarBadge badge = new WeeklySidebarBadge(delegate { opens++; }))
            {
                Point position = new Point(38, 260);
                Check(!IsWindowVisible(parent.Handle), "The test parent should start hidden.");
                Check(badge.TryAttach(parent.Handle, position, new Size(30, 30)), "Attaching to the test parent failed.");
                badge.Show();
                IntPtr child = badge.Handle;
                Check(badge.IsAttachedTo(parent.Handle) && GetParent(child) == parent.Handle, "The ring needs a real parent HWND.");
                Check(!IsWindowVisible(child), "A hidden parent must hide its ring.");
                Rectangle anchor;
                Check(!badge.TryGetAnchor(parent.Handle, out anchor) && anchor.IsEmpty, "Hidden hosts must not supply a stale anchor.");
                Check(GetForegroundWindow() != child && GetForegroundWindow() != parent.Handle, "Attaching must not activate a window.");
                Check(SendMessage(child, 0x21, parent.Handle, IntPtr.Zero) == new IntPtr(3), "A mouse hit must not activate the ring.");
                Rectangle before = BadgeAttachment.ScreenBounds(child);
                Check(before.Size == new Size(30, 30), "The white backing did not keep its requested size.");
                Point client;
                Check(BadgeAttachment.TryScreenToClient(parent.Handle, before.Location, out client) && client == position,
                    "The ring must land at the requested client coordinates.");
                Rectangle hostBounds = BadgeAttachment.ScreenBounds(parent.Handle);
                Check(SetWindowPos(parent.Handle, IntPtr.Zero, hostBounds.X + 30, hostBounds.Y - 80, 0, 0, 0x15), "Could not move the test parent.");
                Rectangle after = BadgeAttachment.ScreenBounds(child);
                Check(after.X == before.X + 30 && after.Y == before.Y - 80, "The ring must move with its native parent.");
                ShowWindow(parent.Handle, 4); // SW_SHOWNOACTIVATE on the private desktop
                Check(badge.TryGetAnchor(parent.Handle, out anchor) && anchor == after, "The panel needs the actual screen bounds after its host moves.");
                badge.Hide();
                Check(!badge.TryGetAnchor(parent.Handle, out anchor) && anchor.IsEmpty, "A hidden badge must not position the panel.");
                badge.Show();
                Check(badge.TryGetAnchor(parent.Handle, out anchor) && anchor == after, "Reopening must restore the same anchor.");

                Button button = (Button)badge.Controls[0];
                Check(button.Bounds == new Rectangle(3, 3, 24, 24), "The visible ring button should remain centered at 24px.");
                WeeklyState half = WeeklyState.FromJson("{\"available\":true,\"remainingPercent\":50,\"resetsAt\":1893456000}",
                    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
                badge.SetState(half);
                Check(button.AccessibleName.Contains("50%"), "A 50% reading must be announced as 50%.");
                using (Bitmap liveImage = new Bitmap(24, 24))
                using (Bitmap unavailableImage = new Bitmap(24, 24))
                {
                    button.DrawToBitmap(liveImage, new Rectangle(0, 0, 24, 24));
                    Check(liveImage.GetPixel(0, 0).ToArgb() == Color.White.ToArgb(), "The badge background must be pure white.");
                    badge.SetState(WeeklyState.Unavailable("reset_expired"));
                    Check(!button.AccessibleName.Contains("50%") && button.AccessibleDescription.Contains("重置中"),
                        "Expired readings must discard the previous percentage and describe the reset.");
                    button.DrawToBitmap(unavailableImage, new Rectangle(0, 0, 24, 24));
                    bool changed = false;
                    for (int y = 0; y < 24 && !changed; y++)
                        for (int x = 0; x < 24 && !changed; x++)
                            changed = liveImage.GetPixel(x, y) != unavailableImage.GetPixel(x, y);
                    Check(changed, "An expired reading must change the actual ring image.");
                }
                for (int open = 1; open <= 2; open++)
                {
                    SendMessage(button.Handle, 0x00f5, IntPtr.Zero, IntPtr.Zero); // BM_CLICK on our private test button
                    Check(opens == open, "Each click should request the details window exactly once.");
                    SendMessage(parent.Handle, 0x0006, IntPtr.Zero, panel.Handle); // WM_ACTIVATE / WA_INACTIVE
                    Check(badge.TryGetAnchor(parent.Handle, out anchor) && anchor == after, "The anchor must survive host deactivation.");
                }
                ShowWindow(parent.Handle, 7); // SW_SHOWMINNOACTIVE
                Check(!badge.TryGetAnchor(parent.Handle, out anchor) && anchor.IsEmpty, "A minimized host must not position the panel.");
                parent.Dispose();
                Check(!IsWindow(child), "Closing the host must destroy its child ring.");
                Check(!badge.TryGetAnchor(parent.Handle, out anchor) && anchor.IsEmpty, "A closed host must release its anchor.");
            }
        }

        private static void RunAttachmentOnPrivateDesktop()
        {
            IntPtr desktop = CreateDesktop("WeeklySidebar-" + Guid.NewGuid().ToString("N"),
                IntPtr.Zero, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero); // GENERIC_ALL
            Check(desktop != IntPtr.Zero, "Could not create an isolated test desktop.");
            try
            {
                Exception failure = null;
                Thread worker = new Thread((ThreadStart)delegate {
                    try
                    {
                        Check(SetThreadDesktop(desktop), "Could not join the isolated test desktop.");
                        CheckAttachment();
                    }
                    catch (Exception error) { failure = error; }
                });
                worker.SetApartmentState(ApartmentState.STA);
                worker.Start();
                worker.Join();
                if (failure != null) throw failure;
            }
            finally { CloseDesktop(desktop); }
        }

        [STAThread]
        public static int Main()
        {
            try
            {
                CheckPlacement(new Rectangle(350, 160, 1200, 830), new RectangleF(365, 932, 36, 36),
                    1F, new Size(24, 24), new Point(371, 894));
                CheckPlacement(new Rectangle(-1600, 100, 1200, 1000), new RectangleF(-1570, 970, 54, 54),
                    1.5F, new Size(36, 36), new Point(-1561, 913));
                RunAttachmentOnPrivateDesktop();
                Console.WriteLine("Weekly sidebar placement, attachment, white backing, deactivation, reopen, visibility and cleanup checks passed.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                return 1;
            }
        }
    }
}
