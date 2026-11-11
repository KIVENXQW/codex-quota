using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace Kiven.WeeklyQuota
{
    internal static class WeeklyRing
    {
        private static readonly Color Face = Color.FromArgb(248, 251, 249);
        private static readonly Color Track = Color.FromArgb(219, 231, 225);
        private static readonly Color Active = Color.FromArgb(54, 142, 121);
        private static readonly Color Ink = Color.FromArgb(33, 78, 67);
        private static readonly Color UnknownTrack = Color.FromArgb(215, 221, 218);
        private static readonly Color UnknownInk = Color.FromArgb(112, 126, 119);

        public static void Draw(Graphics g, RectangleF bounds, double? remainingPercent)
        {
            float unit = Math.Min(bounds.Width, bounds.Height) / 24f;
            if (unit <= 0) return;
            float cx = bounds.X + bounds.Width / 2f;
            float cy = bounds.Y + bounds.Height / 2f;
            float radius = 10.4f * unit;
            RectangleF circle = new RectangleF(cx - radius, cy - radius, 2 * radius, 2 * radius);
            double value = remainingPercent.GetValueOrDefault(double.NaN);
            bool known = value >= 0 && value <= 100;

            GraphicsState state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush fill = new SolidBrush(Face))
                using (Pen rim = new Pen(known ? Track : UnknownTrack, 1.55f * unit))
                {
                    g.FillEllipse(fill, circle);
                    g.DrawEllipse(rim, circle);
                    if (known && value > 0)
                    {
                        rim.Color = Active;
                        rim.Width = 2.12f * unit;
                        if (value == 100) g.DrawEllipse(rim, circle);
                        else DrawRoundedArc(g, rim, circle, radius, value);
                    }
                }

                string label = "—";
                if (known)
                    label = value > 0 && value < 0.5 ? "<1" :
                        Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
                DrawLabel(g, label, cx, cy, unit, known ? Ink : UnknownInk);
            }
            finally { g.Restore(state); }
        }

        private static void DrawLabel(Graphics g, string label, float cx, float cy, float unit, Color color)
        {
            using (FontFamily family = new FontFamily("Segoe UI"))
            using (GraphicsPath glyphs = new GraphicsPath())
            using (StringFormat format = StringFormat.GenericTypographic)
            using (SolidBrush brush = new SolidBrush(color))
            {
                glyphs.AddString(label, family, (int)FontStyle.Bold, 10.9f, PointF.Empty, format);
                // Measure the ink, including curved extrema, rather than the line box or Bezier handles.
                RectangleF inkBounds;
                using (GraphicsPath outline = (GraphicsPath)glyphs.Clone())
                {
                    outline.Flatten(null, 0.01f);
                    inkBounds = outline.GetBounds();
                }
                float scale = unit * Math.Min(1f, 16f / inkBounds.Width);
                float left = cx - (inkBounds.Left + inkBounds.Width / 2f) * scale;
                float top = cy - (inkBounds.Top + inkBounds.Height / 2f) * scale;
                using (Matrix placement = new Matrix(scale, 0, 0, scale, left, top))
                    glyphs.Transform(placement);
                g.FillPath(brush, glyphs);
            }
        }

        private static void DrawRoundedArc(Graphics g, Pen pen, RectangleF circle, float radius, double percent)
        {
            double sweep = percent * 3.6;
            // Round caps project past their centerline ends. Inset both ends so
            // their outer tangents still land on the true percentage boundaries.
            double capDegrees = Math.Asin(pen.Width / (2 * radius)) * 180 / Math.PI;
            if (sweep > 2 * capDegrees)
            {
                pen.SetLineCap(LineCap.Round, LineCap.Round, DashCap.Round);
                g.DrawArc(pen, circle, (float)(-90 + capDegrees), (float)(sweep - 2 * capDegrees));
                return;
            }
            double half = sweep * Math.PI / 360;
            float dotRadius = (float)(radius * Math.Sin(half));
            float x = circle.Left + radius + (float)(radius * Math.Sin(half));
            float y = circle.Top + radius - (float)(radius * Math.Cos(half));
            using (SolidBrush color = new SolidBrush(pen.Color))
                if (dotRadius > 0) g.FillEllipse(color, x - dotRadius, y - dotRadius, 2 * dotRadius, 2 * dotRadius);
        }
    }
}
