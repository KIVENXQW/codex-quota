using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Kiven.WeeklyQuota
{
    internal static class WeeklyRingTests
    {
        // Pairs straddle 12, 3, 6 and 9 o'clock on a 240 px rendering.
        private static readonly Point[] Samples = {
            new Point(117, 16), new Point(123, 16), new Point(224, 117), new Point(224, 123),
            new Point(123, 224), new Point(117, 224), new Point(16, 123), new Point(16, 117)
        };
        private static readonly Color Active = Color.FromArgb(54, 142, 121);
        private static readonly Color Track = Color.FromArgb(219, 231, 225);
        private static readonly Color UnknownTrack = Color.FromArgb(215, 221, 218);

        public static int Main(string[] args)
        {
            try { Run(args); }
            catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
            return 0;
        }

        private static void Run(string[] args)
        {
            Check(25, "01100000");
            Check(50, "01111000");
            Check(75, "01111110");
            Check(100, "11111111");
            Check(0, "00000000");
            Check(null, "00000000");
            CheckLabelCenters();
            if (args.Length == 2 && args[0] == "--preview-dir") WritePreview(args[1]);
            Console.WriteLine("WeeklyRingTests: 6 rendered states passed");
        }

        private static void Check(double? percent, string expected)
        {
            using (Bitmap bitmap = new Bitmap(240, 240))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                WeeklyRing.Draw(graphics, new RectangleF(0, 0, 240, 240), percent);
                for (int i = 0; i < Samples.Length; i++)
                {
                    Point sample = Samples[i];
                    Color want = expected[i] == '1' ? Active : (percent.HasValue ? Track : UnknownTrack);
                    Color actual = bitmap.GetPixel(sample.X, sample.Y);
                    if (actual.ToArgb() != want.ToArgb())
                        throw new Exception(String.Format("{0} at {1}: expected {2}, got {3}", percent, sample, want, actual));
                }
                if (!percent.HasValue && bitmap.GetPixel(120, 120).GetBrightness() > 0.65f)
                    throw new Exception("Missing the unavailable dash at the center");
                if (percent == 50 && bitmap.GetPixel(121, 9).ToArgb() == Active.ToArgb())
                    throw new Exception("The outer corner at the start of the half ring should be rounded.");
            }
        }

        private static void CheckLabelCenters()
        {
            int[] sizes = { 24, 36, 48 };
            double?[] values = { 0, 1, 25, 50, 75, 100, null };
            float maxX = 0, maxY = 0;
            foreach (int size in sizes)
            {
                PointF trackCenter;
                using (Bitmap track = Render(size, 0))
                    trackCenter = MeasureCenter(track, (x, y, pixel) => pixel.A >= 128);

                foreach (double? value in values)
                using (Bitmap bitmap = Render(size, value))
                {
                    // Restrict the ink measurement to the open area inside the rim.
                    // Use 50% coverage between the face and the label's green channel.
                    float innerRadius = 9f * size / 24f;
                    int inkGreen = value.HasValue ? 78 : 126;
                    PointF center = MeasureCenter(bitmap, (x, y, pixel) => {
                        float dx = x - trackCenter.X, dy = y - trackCenter.Y;
                        return dx * dx + dy * dy <= innerRadius * innerRadius &&
                            pixel.A >= 128 && 2 * pixel.G <= 251 + inkGreen;
                    });
                    float offsetX = Math.Abs(center.X - trackCenter.X);
                    float offsetY = Math.Abs(center.Y - trackCenter.Y);
                    maxX = Math.Max(maxX, offsetX);
                    maxY = Math.Max(maxY, offsetY);
                    if (offsetX > 0.5f || offsetY > 0.5f)
                        throw new Exception(String.Format("{0}px label {1}: ink center {2}, track center {3}",
                            size, value.HasValue ? value.Value.ToString() : "dash", center, trackCenter));
                }
            }
            Console.WriteLine("Label centers: {0} renders, maximum offset X={1:0.0}px, Y={2:0.0}px",
                sizes.Length * values.Length, maxX, maxY);
        }

        private static Bitmap Render(int size, double? value)
        {
            Bitmap bitmap = new Bitmap(size, size);
            using (Graphics graphics = Graphics.FromImage(bitmap))
                WeeklyRing.Draw(graphics, new RectangleF(0, 0, size, size), value);
            return bitmap;
        }

        private static PointF MeasureCenter(Bitmap bitmap, Func<int, int, Color, bool> include)
        {
            int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1;
            for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (!include(x, y, bitmap.GetPixel(x, y))) continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
            if (right < left) throw new Exception("No rendered pixels found for the center measurement.");
            return new PointF((left + right) / 2f, (top + bottom) / 2f);
        }

        private static void WritePreview(string directory)
        {
            Directory.CreateDirectory(directory);
            using (Bitmap bitmap = new Bitmap(340, 140))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
                int[] values = { 25, 50, 75 };
                for (int i = 0; i < values.Length; i++)
                {
                    WeeklyRing.Draw(graphics, new RectangleF(30 + i * 100, 12, 80, 80), values[i]);
                    WeeklyRing.Draw(graphics, new RectangleF(58 + i * 100, 101, 24, 24), values[i]);
                }
                bitmap.Save(Path.Combine(directory, "A细环-原字体居中.png"), ImageFormat.Png);
            }
        }
    }
}
