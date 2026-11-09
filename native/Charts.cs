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

    }

    internal sealed class UsageBarChart : Control
    {
        private List<ChartBucket> buckets = new List<ChartBucket>();
        private readonly List<RectangleF> hitAreas = new List<RectangleF>();
        private readonly ChartDetailPresenter details;
        private readonly Font small;
        private Color accent = Color.FromArgb(218, 145, 170);
        private bool english;
        private bool timeMetric;
        private readonly float scale;

        internal UsageBarChart(float factor)
        {
            scale = factor;
            small = new Font("Microsoft YaHei UI", 11 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            AccessibleRole = AccessibleRole.Chart;
            details = new ChartDetailPresenter(this, scale, BucketAt, ShowDetails);
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
            details.Hover.Cancel();
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
                    Color fillColor = index == details.Selected ? QuotaAppearance.Darken(accent, .16f) : accent;
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

        private int BucketAt(Point point)
        {
            for (int i = 0; i < hitAreas.Count && i < buckets.Count; i++)
                if (hitAreas[i].Contains(point)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            details.Hover.Move(e.Location);
        }

        private void ShowDetails(int index)
        {
            if (index < hitAreas.Count && index < buckets.Count)
                details.Show(index, buckets[index], Rectangle.Round(hitAreas[index]), accent, english);
        }

        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if ((e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) && buckets.Count > 0)
            {
                details.Hover.Select(Math.Max(0, Math.Min(buckets.Count - 1, details.Selected + (e.KeyCode == Keys.Right ? 1 : -1))));
                e.Handled = true;
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) { details.Dispose(); small.Dispose(); } base.Dispose(disposing); }
    }

    internal sealed class TokenHeatmap : Control
    {
        internal bool Compact;
        private List<ChartBucket> buckets = new List<ChartBucket>();
        private readonly List<RectangleF> cells = new List<RectangleF>();
        private readonly ChartDetailPresenter details;
        private readonly Font small;
        private readonly float scale;
        private Color accent = Color.FromArgb(218, 145, 170);
        private bool english;
        private HeatmapScale colors = HeatmapScale.Create(new List<ChartBucket>());

        internal TokenHeatmap(float factor)
        {
            scale = factor;
            small = new Font("Microsoft YaHei UI", 11 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            details = new ChartDetailPresenter(this, scale, BucketAt, ShowDetails);
            AccessibleRole = AccessibleRole.Chart;
            TabStop = true;
        }

        internal void SetData(List<ChartBucket> value, Color color, bool useEnglish, HeatmapScale annualScale = null)
        {
            buckets = value ?? new List<ChartBucket>();
            cells.Clear();
            accent = color;
            english = useEnglish;
            colors = annualScale ?? HeatmapScale.Create(buckets);
            details.Hover.Cancel();
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
            float pitch = Math.Min(26 * scale, (Width - 1) / (float)Math.Max(1, columns));
            if(Compact)pitch=Math.Min(pitch,Math.Max(4*scale,(Height-66*scale)/7f));
            float gap = Math.Max(1, Math.Min(3 * scale,pitch * .12f));
            float size = Math.Max(2, pitch - gap);
            float top = 19 * scale;
            float origin=Compact?Math.Max(0,(Width-columns*pitch)/2):0;
            int previousMonth = -1;
            for (int i = 0; i < buckets.Count; i++)
            {
                ChartBucket bucket = buckets[i];
                int position = i + offset;
                int column = position / 7;
                int row = position % 7;
                float x = origin + column * pitch;
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
                Color color = colors.ColorFor(bucket.Tokens,accent);
                using (Brush brush = new SolidBrush(color))
                using (GraphicsPath path = QuotaAppearance.RoundRectangle(cell, Math.Min(3f * scale, size / 3)))
                {
                    g.FillPath(brush, path);
                    if (colors.Level(bucket.Tokens)<0 || details.Selected == i)
                        using (Pen pen = new Pen(details.Selected == i ? QuotaAppearance.Darken(accent, .3f) : Color.FromArgb(232, 234, 237), Math.Max(1, scale))) g.DrawPath(pen, path);
                }
            }
            float legendY = top + 7 * pitch + 9 * scale;
            float xLegend=origin;
            if(colors.BandCount>0) {
                xLegend=LegendText(g,english?"Less":"少",xLegend,legendY)+5*scale;
                for(int level=1;level<=colors.BandCount;level++){LegendCell(g,colors.ColorForBand(level,accent),xLegend,legendY,false);xLegend+=15*scale;}
                xLegend=LegendText(g,english?"More":"多",xLegend,legendY)+14*scale;
            }
            LegendCell(g,colors.ColorFor(0,accent),xLegend,legendY,false);xLegend+=15*scale;
            xLegend=LegendText(g,"0",xLegend,legendY)+14*scale;
            LegendCell(g,colors.ColorFor(null,accent),xLegend,legendY,true);xLegend+=15*scale;
            LegendText(g,english?"No record":"无记录",xLegend,legendY);
            LegendText(g,english?"Colors use the same annual distribution":"色阶按全年分布统一分档",Compact?Math.Min(origin,Math.Max(0,Width-250*scale)):0,legendY+20*scale);
        }
        private float LegendText(Graphics g,string text,float x,float y) {
            using(var brush=new SolidBrush(QuotaAppearance.Muted))g.DrawString(text,small,brush,x,y-2*scale);
            return x+g.MeasureString(text,small).Width;
        }
        private void LegendCell(Graphics g,Color color,float x,float y,bool outline) {
            using(var path=QuotaAppearance.RoundRectangle(new RectangleF(x,y,10*scale,10*scale),2*scale)) {
                using(var brush=new SolidBrush(color))g.FillPath(brush,path);
                if(outline)using(var pen=new Pen(Color.FromArgb(224,218,230),scale))g.DrawPath(pen,path);
            }
        }

        internal int BucketAt(Point location) {for(int i=0;i<cells.Count&&i<buckets.Count;i++)if(cells[i].Contains(location))return i;return -1;}
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            details.Hover.Move(e.Location);
        }

        private void ShowDetails(int index)
        {
            if (index < cells.Count && index < buckets.Count)
                details.Show(index, buckets[index], Rectangle.Round(cells[index]), accent, english);
        }

        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if ((e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) && buckets.Count > 0)
            {
                int current = details.Selected;
                details.Hover.Select(current < 0 ? buckets.Count - 1 : Math.Max(0, Math.Min(buckets.Count - 1, current + (e.KeyCode == Keys.Right ? 1 : -1))));
                e.Handled = true;
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) { details.Dispose(); small.Dispose(); } base.Dispose(disposing); }
    }
}
