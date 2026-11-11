using System;
using System.Drawing;

namespace Kiven.WeeklyQuota
{
    internal static class PopupPlacementTests
    {
        private static void Check(Rectangle anchor, Rectangle work, Point expected)
        {
            Point actual = PanelDrawing.PinnedLocation(anchor, work,
                new Size(PanelDrawing.Width, PanelDrawing.Height), 10);
            if (expected != actual) throw new Exception("Popup placement: expected " + expected + " got " + actual);
        }

        public static int Main()
        {
            try { Run(); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
        }

        private static void Run()
        {
            Check(new Rectangle(368, 893, 32, 32), new Rectangle(0, 0, 1920, 1080), new Point(410, 861));
            Check(new Rectangle(690, 120, 32, 32), new Rectangle(0, 0, 768, 600), new Point(360, 88));
            Check(new Rectangle(-1880, 100, 32, 32), new Rectangle(-1920, 0, 1920, 1080), new Point(-1838, 68));
            Check(new Rectangle(10, 46, 32, 32), new Rectangle(0, 40, 500, 300), new Point(52, 40));
            Check(new Rectangle(10, 326, 32, 32), new Rectangle(0, 40, 500, 300), new Point(52, 244));
            WeeklyState data = WeeklyState.FromJson("{\"available\":true,\"remainingPercent\":50,\"resetsAt\":1794623400}",
                DateTimeOffset.Parse("2026-11-11T10:00:00Z"));
            using (Bitmap bitmap = new Bitmap(PanelDrawing.Width, PanelDrawing.Height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Black);
                PanelDrawing.Draw(graphics, data, 1);
                foreach (Point point in new[] { new Point(0, 0), new Point(bitmap.Width - 1, 0),
                    new Point(0, bitmap.Height - 1), new Point(bitmap.Width - 1, bitmap.Height - 1), new Point(20, 36) })
                    if (bitmap.GetPixel(point.X, point.Y).ToArgb() != Color.White.ToArgb())
                        throw new Exception("Non-white background at " + point);
                if (bitmap.GetPixel(50, 46).ToArgb() != Color.FromArgb(240, 119, 175).ToArgb())
                    throw new Exception("Progress bar differs from the sampled pink");
                if (bitmap.GetPixel(270, 46).ToArgb() == PanelDrawing.Accent.ToArgb())
                    throw new Exception("50% progress filled the far end of the track");
            }
            Console.WriteLine("Popup tests: 5 anchor placements passed; opaque white edges and exact pink half-bar passed.");
        }
    }
}
