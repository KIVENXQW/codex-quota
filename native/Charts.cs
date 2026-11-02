using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace CodexQuota
{
    internal static class UsageText
    {
        internal static string Number(long? value, bool compact)
        {
            if (!value.HasValue) return "—";
            if (!compact) return value.Value.ToString("N0", CultureInfo.InvariantCulture);
            if (value.Value >= 1000000000) return (value.Value / 1000000000d).ToString("0.#", CultureInfo.InvariantCulture) + "B";
            if (value.Value >= 1000000) return (value.Value / 1000000d).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (value.Value >= 1000) return (value.Value / 1000d).ToString("0.#", CultureInfo.InvariantCulture) + "K";
            return value.Value.ToString(CultureInfo.InvariantCulture);
        }

        internal static string Duration(double? seconds, bool english, bool precise)
        {
            if (!seconds.HasValue || Double.IsNaN(seconds.Value) || Double.IsInfinity(seconds.Value)) return "—";
            long total = Math.Max(0, (long)Math.Round(seconds.Value));
            long hours = total / 3600;
            long minutes = (total % 3600) / 60;
            long remain = total % 60;
            if (precise)
                return english ? hours + "h " + minutes + "m " + remain + "s" : hours + " 小时 " + minutes + " 分 " + remain + " 秒";
            if (hours >= 24) return english ? (hours / 24) + "d " + (hours % 24) + "h" : (hours / 24) + "天 " + (hours % 24) + "时";
            if (hours > 0) return english ? hours + "h " + minutes + "m" : hours + "时 " + minutes + "分";
            if (minutes > 0) return english ? minutes + "m" : minutes + "分钟";
            return english ? remain + "s" : remain + "秒";
        }

        internal static string BucketTooltip(ChartBucket bucket, bool english)
        {
            string dates = bucket.Start.ToString("yyyy-MM-dd") + (bucket.End.Date > bucket.Start.Date ? " – " + bucket.End.ToString("yyyy-MM-dd") : "");
            string text = dates + "\nTokens  " + Number(bucket.Tokens, false) + "\n" +
                (english ? "AI work time  " : "AI 工作时长  ") + Duration(bucket.WorkSeconds, english, true) + "\n" +
                (english ? "Completed tasks  " : "完成任务  ") + (bucket.Tasks.HasValue ? bucket.Tasks.Value.ToString("N0") : "—");
            if (bucket.TokenPartial) text += english ? "\nToken coverage: partial" : "\nToken 记录：部分覆盖";
            if (bucket.WorkPartial) text += english ? "\nLocal activity coverage: partial" : "\n本机工作记录：部分覆盖";
            if (!bucket.Tokens.HasValue) text += english ? "\nNo Token record" : "\n暂无 Token 记录";
            if (!bucket.WorkSeconds.HasValue) text += english ? "\nNo local activity record" : "\n暂无本机工作记录";
            return text;
        }
    }

    internal sealed class UsageBarChart : Control
    {
        private List<ChartBucket> buckets = new List<ChartBucket>();
        private readonly List<RectangleF> hitAreas = new List<RectangleF>();
        private readonly ToolTip tooltip = new ToolTip();
        private readonly Font small;
        private Color accent = Color.FromArgb(218, 145, 170);
        private bool english;
        private bool timeMetric;
        private int hovered = -1;
        private readonly float scale;

        internal UsageBarChart(float factor)
        {
            scale = factor;
            small = new Font("Microsoft YaHei UI", 11 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            AccessibleRole = AccessibleRole.Chart;
            tooltip.InitialDelay = 100;
            tooltip.ReshowDelay = 50;
            tooltip.AutoPopDelay = 18000;
            TabStop = true;
        }

        internal void SetData(List<ChartBucket> value, bool useTime, Color color, bool useEnglish)
        {
            buckets = value ?? new List<ChartBucket>();
            hitAreas.Clear();
            timeMetric = useTime;
            accent = color;
            english = useEnglish;
            AccessibleName = english ? "Usage chart. Arrow keys select a period." : "用量柱状图，方向键可选择日期查看明细";
            hovered = -1;
            tooltip.SetToolTip(this, "");
            Invalidate();
        }

        private double? Value(ChartBucket item)
        {
            return timeMetric ? item.WorkSeconds : (item.Tokens.HasValue ? (double?)item.Tokens.Value : null);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float left = 60 * scale, right = 9 * scale, top = 13 * scale, bottom = 28 * scale;
            float chartWidth = Math.Max(10, Width - left - right), chartHeight = Math.Max(12, Height - top - bottom);
            double maximum = 0;
            bool any = false;
            foreach (ChartBucket bucket in buckets)
            {
                double? value = Value(bucket);
                if (value.HasValue) { maximum = Math.Max(maximum, value.Value); any = true; }
            }
            if (maximum <= 0) maximum = 1;
            for (int line = 0; line <= 2; line++)
            {
                float y = top + chartHeight * line / 2;
                using (Pen pen = new Pen(Color.FromArgb(236, 238, 240))) g.DrawLine(pen, left, y, Width - right, y);
                double tick = maximum * (2 - line) / 2;
                string text = timeMetric ? (tick / 3600).ToString("0.#", CultureInfo.InvariantCulture) + "h" : UsageText.Number((long)tick, true);
                DrawText(g, text, new RectangleF(0, y - 8 * scale, left - 7 * scale, 16 * scale), QuotaAppearance.Muted, StringAlignment.Far);
            }
            hitAreas.Clear();
            int count = Math.Max(1, buckets.Count);
            float slot = chartWidth / count;
            float barWidth = Math.Max(3 * scale, Math.Min(36 * scale, slot * .55f));
            for (int index = 0; index < buckets.Count; index++)
            {
                ChartBucket item = buckets[index];
                float x = left + slot * index + (slot - barWidth) / 2;
                hitAreas.Add(new RectangleF(left + slot * index, top, slot, chartHeight + bottom));
                double? value = Value(item);
                if (value.HasValue && value.Value > 0)
                {
                    float height = Math.Max(3 * scale, (float)(value.Value / maximum * chartHeight));
                    RectangleF bar = new RectangleF(x, top + chartHeight - height, barWidth, height);
                    Color fillColor = index == hovered ? QuotaAppearance.Darken(accent, .16f) : accent;
                    bool partial = timeMetric ? item.WorkPartial : item.TokenPartial;
                    using (GraphicsPath path = QuotaAppearance.RoundRectangle(bar, Math.Min(4 * scale, barWidth / 3)))
                    using (Brush brush = new SolidBrush(partial ? QuotaAppearance.Blend(fillColor, Color.White, .25f) : fillColor))
                        g.FillPath(brush, path);
                    if (partial)
                    {
                        using (Pen pen = new Pen(QuotaAppearance.Darken(accent, .16f), scale))
                        {
                            pen.DashStyle = DashStyle.Dot;
                            g.DrawLine(pen, x + 2 * scale, top + chartHeight - height + 2 * scale, x + barWidth - 2 * scale, top + chartHeight - height + 2 * scale);
                        }
                    }
                }
                else if (value.HasValue)
                {
                    using (Pen pen = new Pen(accent, 2 * scale)) g.DrawLine(pen, x, top + chartHeight - scale, x + barWidth, top + chartHeight - scale);
                }
                else DrawText(g, "—", new RectangleF(x, top + chartHeight - 17 * scale, barWidth, 16 * scale), QuotaAppearance.Muted, StringAlignment.Center);
                string label = item.Label;
                if (String.IsNullOrWhiteSpace(label)) label = item.Start.ToString("M/d");
                DrawText(g, label, new RectangleF(left + slot * index - 3 * scale, top + chartHeight + 7 * scale, slot + 6 * scale, 17 * scale), QuotaAppearance.Muted, StringAlignment.Center);
            }
            if (!any)
            {
                using (Brush wash = new SolidBrush(Color.FromArgb(225, Color.White))) g.FillRectangle(wash, left, top, chartWidth, chartHeight - 20 * scale);
                DrawText(g, english ? "No records for this range" : "该时段暂无记录", new RectangleF(left, top + chartHeight / 2 - 10 * scale, chartWidth, 20 * scale), QuotaAppearance.Muted, StringAlignment.Center);
            }
        }

        private void DrawText(Graphics g, string text, RectangleF area, Color color, StringAlignment align)
        {
            using (Brush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = align;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(text, small, brush, area, format);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int next = -1;
            for (int i = 0; i < hitAreas.Count; i++) if (hitAreas[i].Contains(e.Location)) { next = i; break; }
            if (next >= buckets.Count) next = -1;
            if (next != hovered)
            {
                hovered = next;
                tooltip.SetToolTip(this, hovered >= 0 ? UsageText.BucketTooltip(buckets[hovered], english) : "");
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = -1; tooltip.Hide(this); Invalidate(); }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if ((e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) && buckets.Count > 0)
            {
                hovered = Math.Max(0, Math.Min(buckets.Count - 1, hovered + (e.KeyCode == Keys.Right ? 1 : -1)));
                tooltip.Show(UsageText.BucketTooltip(buckets[hovered], english), this, Width / 2, Height / 2, 15000);
                Invalidate();
                e.Handled = true;
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) { tooltip.Dispose(); small.Dispose(); } base.Dispose(disposing); }
    }

    internal sealed class TokenHeatmap : Control
    {
        private List<ChartBucket> buckets = new List<ChartBucket>();
        private readonly List<RectangleF> cells = new List<RectangleF>();
        private readonly ToolTip tooltip = new ToolTip();
        private readonly Font small;
        private readonly float scale;
        private Color accent = Color.FromArgb(218, 145, 170);
        private bool english;
        private int hovered = -1;

        internal TokenHeatmap(float factor)
        {
            scale = factor;
            small = new Font("Microsoft YaHei UI", 11 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            tooltip.InitialDelay = 100;
            tooltip.ReshowDelay = 50;
            tooltip.AutoPopDelay = 15000;
            AccessibleRole = AccessibleRole.Chart;
            TabStop = true;
        }

        internal void SetData(List<ChartBucket> value, Color color, bool useEnglish)
        {
            buckets = value ?? new List<ChartBucket>();
            cells.Clear();
            accent = color;
            english = useEnglish;
            hovered = -1;
            tooltip.SetToolTip(this, "");
            AccessibleName = english ? "Daily Token heatmap, previous 365 days" : "最近 365 天 Token 用量热力图";
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            cells.Clear();
            if (buckets.Count == 0) return;
            int offset = ((int)buckets[0].Start.DayOfWeek + 6) % 7;
            int columns = (int)Math.Ceiling((buckets.Count + offset) / 7d);
            float gap = Math.Max(1, scale);
            float pitch = Math.Min(9 * scale, (Width - 1) / (float)Math.Max(1, columns));
            float size = Math.Max(2, pitch - gap);
            float top = 19 * scale;
            long max = 0;
            foreach (ChartBucket bucket in buckets) if (bucket.Tokens.HasValue) max = Math.Max(max, bucket.Tokens.Value);
            int previousMonth = -1;
            for (int i = 0; i < buckets.Count; i++)
            {
                ChartBucket bucket = buckets[i];
                int position = i + offset;
                int column = position / 7;
                int row = position % 7;
                float x = column * pitch;
                if (bucket.Start.Month != previousMonth && (i == 0 || bucket.Start.Day == 1))
                {
                    if (column < columns - 2)
                    {
                        string month = english ? bucket.Start.ToString("MMM", CultureInfo.InvariantCulture) : bucket.Start.Month.ToString() + "月";
                        using (Brush brush = new SolidBrush(QuotaAppearance.Muted)) g.DrawString(month, small, brush, x, 0);
                    }
                    previousMonth = bucket.Start.Month;
                }
                RectangleF cell = new RectangleF(x, top + row * pitch, size, size);
                cells.Add(cell);
                Color color = Color.FromArgb(241, 242, 244);
                if (bucket.Tokens.HasValue && bucket.Tokens.Value > 0 && max > 0)
                {
                    double normalized = Math.Log(1 + bucket.Tokens.Value) / Math.Log(1 + max);
                    float blend = normalized > .85 ? 0 : normalized > .65 ? .25f : normalized > .4 ? .5f : .72f;
                    color = QuotaAppearance.Blend(accent, Color.White, blend);
                }
                else if (!bucket.Tokens.HasValue) color = Color.White;
                using (Brush brush = new SolidBrush(color))
                using (GraphicsPath path = QuotaAppearance.RoundRectangle(cell, Math.Min(1.5f * scale, size / 3)))
                {
                    g.FillPath(brush, path);
                    if (!bucket.Tokens.HasValue || hovered == i)
                        using (Pen pen = new Pen(hovered == i ? QuotaAppearance.Darken(accent, .3f) : Color.FromArgb(232, 234, 237), Math.Max(1, scale))) g.DrawPath(pen, path);
                }
            }
            float legendY = top + 7 * pitch + 9 * scale;
            string legend = english ? "Less" : "少";
            using (Brush brush = new SolidBrush(QuotaAppearance.Muted)) g.DrawString(legend, small, brush, 0, legendY - 2 * scale);
            for (int level = 0; level < 5; level++)
            {
                Color color = level == 0 ? Color.FromArgb(241, 242, 244) : QuotaAppearance.Blend(accent, Color.White, (4 - level) * .24f);
                using (Brush brush = new SolidBrush(color)) g.FillRectangle(brush, 29 * scale + level * 11 * scale, legendY, 8 * scale, 8 * scale);
            }
            using (Brush brush = new SolidBrush(QuotaAppearance.Muted))
            {
                g.DrawString(english ? "More" : "多", small, brush, 88 * scale, legendY - 2 * scale);
                g.DrawString(english ? "Outline: no record" : "空心：暂无记录", small, brush, Width - 106 * scale, legendY - 2 * scale);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int next = -1;
            for (int i = 0; i < cells.Count; i++) if (cells[i].Contains(e.Location)) { next = i; break; }
            if (next >= buckets.Count) next = -1;
            if (next != hovered)
            {
                hovered = next;
                tooltip.SetToolTip(this, hovered >= 0 ? UsageText.BucketTooltip(buckets[hovered], english) : "");
                Invalidate();
            }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = -1; tooltip.Hide(this); Invalidate(); }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if ((e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) && buckets.Count > 0)
            {
                hovered = hovered < 0 ? buckets.Count - 1 : Math.Max(0, Math.Min(buckets.Count - 1, hovered + (e.KeyCode == Keys.Right ? 1 : -1)));
                tooltip.Show(UsageText.BucketTooltip(buckets[hovered], english), this, Width / 2, Height / 2, 15000);
                Invalidate();
                e.Handled = true;
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) { tooltip.Dispose(); small.Dispose(); } base.Dispose(disposing); }
    }
}
