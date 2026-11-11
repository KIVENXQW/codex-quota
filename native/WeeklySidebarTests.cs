using System;
using System.Drawing;
using System.Runtime.InteropServices;
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

        // Never show this native parent. Only handles created by this test receive messages.
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
            using (WeeklySidebarBadge badge = new WeeklySidebarBadge(delegate { opens++; }))
            {
                Point position = new Point(38, 260);
                Check(!IsWindowVisible(parent.Handle), "The test parent must remain hidden.");
                Check(badge.TryAttach(parent.Handle, position, new Size(24, 24)), "Attaching to the test parent failed.");
                badge.Show();
                IntPtr child = badge.Handle;
                Check(badge.IsAttachedTo(parent.Handle) && GetParent(child) == parent.Handle, "The ring needs a real parent HWND.");
                Check(!IsWindowVisible(child), "A hidden parent must hide its ring.");
                Check(GetForegroundWindow() != child && GetForegroundWindow() != parent.Handle, "Attaching must not activate a window.");
                Check(SendMessage(child, 0x21, parent.Handle, IntPtr.Zero) == new IntPtr(3), "A mouse hit must not activate the ring.");
                Rectangle before = BadgeAttachment.ScreenBounds(child);
                Check(before.Size == new Size(24, 24), "The ring did not keep its requested size.");
                Point client;
                Check(BadgeAttachment.TryScreenToClient(parent.Handle, before.Location, out client) && client == position,
                    "The ring must land at the requested client coordinates.");
                Rectangle hostBounds = BadgeAttachment.ScreenBounds(parent.Handle);
                Check(SetWindowPos(parent.Handle, IntPtr.Zero, hostBounds.X + 30, hostBounds.Y - 80, 0, 0, 0x15), "Could not move the test parent.");
                Rectangle after = BadgeAttachment.ScreenBounds(child);
                Check(after.X == before.X + 30 && after.Y == before.Y - 80, "The ring must move with its native parent.");

                Button button = (Button)badge.Controls[0];
                WeeklyState half = WeeklyState.FromJson("{\"available\":true,\"remainingPercent\":50,\"resetsAt\":1893456000}",
                    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
                badge.SetState(half);
                Check(button.AccessibleName.Contains("50%"), "A 50% reading must be announced as 50%.");
                using (Bitmap liveImage = new Bitmap(24, 24))
                using (Bitmap unavailableImage = new Bitmap(24, 24))
                {
                    button.DrawToBitmap(liveImage, new Rectangle(0, 0, 24, 24));
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
                SendMessage(button.Handle, 0x00f5, IntPtr.Zero, IntPtr.Zero); // BM_CLICK on our own native button
                Check(opens == 1, "One click must open the quota panel exactly once.");
                parent.Dispose();
                Application.DoEvents();
                Check(!IsWindow(child), "Closing the host must destroy its child ring.");
            }
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
                CheckAttachment();
                Console.WriteLine("Weekly sidebar placement, attachment, state, click and cleanup checks passed.");
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
