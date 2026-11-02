using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace CodexQuota
{
    internal sealed class AccentPalette : Control
    {
        private readonly Color[] presets = { Color.FromArgb(218, 145, 170), Color.FromArgb(103, 163, 139), Color.FromArgb(111, 151, 198), Color.FromArgb(184, 158, 99), Color.FromArgb(156, 139, 193), Color.FromArgb(130, 145, 156) };
        private readonly SaturationSurface saturation;
        private readonly HueSurface hue;
        private readonly TextBox hex;
        private readonly Button apply;
        private readonly ToolTip tooltip = new ToolTip();
        private readonly float scale;
        private Color selected;
        private bool updating;
        private bool english;
        private Rectangle hexBorder;
        internal event EventHandler SelectedColorChanged;
        internal Color SelectedColor { get { return selected; } }

        internal AccentPalette(float factor, Font font)
        {
            scale = factor;
            Font = font;
            BackColor = Color.White;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            for (int i = 0; i < presets.Length; i++)
            {
                Swatch swatch = new Swatch(presets[i]);
                swatch.Tag = presets[i];
                swatch.SetBounds(Px(i * 48), Px(2), Px(34), Px(34));
                swatch.AccessibleName = ColorTranslator.ToHtml(presets[i]);
                tooltip.SetToolTip(swatch, ColorTranslator.ToHtml(presets[i]));
                swatch.Click += delegate(object sender, EventArgs e)
                {
                    SetColor((Color)((Control)sender).Tag);
                    Commit();
                };
                Controls.Add(swatch);
            }
            saturation = new SaturationSurface();
            saturation.SetBounds(0, Px(58), Px(330), Px(146));
            saturation.PreviewChanged += delegate { UpdatePreview(); };
            saturation.SelectionCommitted += delegate { Commit(); };
            Controls.Add(saturation);
            hue = new HueSurface();
            hue.SetBounds(0, Px(219), Px(330), Px(18));
            hue.PreviewChanged += delegate
            {
                saturation.Hue = hue.Hue;
                saturation.Invalidate();
                UpdatePreview();
            };
            hue.SelectionCommitted += delegate { Commit(); };
            Controls.Add(hue);
            hex = new TextBox();
            hex.BorderStyle = BorderStyle.None;
            hex.Font = font;
            hex.MaxLength = 7;
            hex.AccessibleName = "Hex color";
            hex.SetBounds(Px(12), Px(261), Px(128), Px(24));
            hex.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { ApplyHex(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            Controls.Add(hex);
            apply = new RoundedButton();
            apply.Font = font;
            apply.SetBounds(Px(170), Px(250), Px(90), Px(37));
            apply.Click += delegate { ApplyHex(); };
            Controls.Add(apply);
            hexBorder = new Rectangle(0, Px(250), Px(152), Px(37));
            SetLanguage(false);
            SetColor(presets[0]);
            Height = Px(296);
        }

        private int Px(int value) { return (int)Math.Round(value * scale); }
        internal void SetLanguage(bool useEnglish)
        {
            english = useEnglish;
            apply.Text = english ? "Apply" : "应用";
            saturation.AccessibleName = english ? "Saturation and brightness. Arrow keys adjust." : "饱和度和亮度，可使用方向键调整";
            hue.AccessibleName = english ? "Hue. Arrow keys adjust." : "色相，可使用方向键调整";
            hex.AccessibleName = english ? "Custom color hex value" : "自定义颜色十六进制值";
        }

        internal void SetColor(Color color)
        {
            updating = true;
            selected = Color.FromArgb(color.R, color.G, color.B);
            double h, s, v;
            ColorMath.ToHsv(selected, out h, out s, out v);
            hue.Hue = h;
            saturation.Hue = h;
            saturation.Saturation = s;
            saturation.Value = v;
            hex.Text = "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
            hex.ForeColor = QuotaAppearance.Text;
            foreach (Control control in Controls)
            {
                Swatch swatch = control as Swatch;
                if (swatch != null) { swatch.Selected = swatch.SwatchColor.ToArgb() == selected.ToArgb(); swatch.Invalidate(); }
            }
            hue.Invalidate();
            saturation.Invalidate();
            Invalidate();
            updating = false;
        }

        private void UpdatePreview()
        {
            if (updating) return;
            selected = ColorMath.FromHsv(hue.Hue, saturation.Saturation, saturation.Value);
            hex.Text = "#" + selected.R.ToString("X2") + selected.G.ToString("X2") + selected.B.ToString("X2");
            hex.ForeColor = QuotaAppearance.Text;
            Invalidate();
        }

        private void ApplyHex()
        {
            string text = hex.Text.Trim().TrimStart('#');
            int value;
            if (text.Length != 6 || !Int32.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
            {
                hex.ForeColor = Color.FromArgb(174, 60, 74);
                tooltip.Show(english ? "Enter six hex digits, for example #DA91AA" : "请输入六位颜色值，例如 #DA91AA", hex, 0, hex.Height + 3, 3500);
                return;
            }
            SetColor(Color.FromArgb((value >> 16) & 255, (value >> 8) & 255, value & 255));
            Commit();
        }

        private void Commit()
        {
            EventHandler handler = SelectedColorChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (saturation != null) { saturation.Width = Math.Max(Px(160), Width); hue.Width = saturation.Width; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = QuotaAppearance.RoundRectangle(hexBorder, Px(8)))
            using (Pen pen = new Pen(QuotaAppearance.Line)) e.Graphics.DrawPath(pen, path);
            using (Brush brush = new SolidBrush(selected))
            using (GraphicsPath path = QuotaAppearance.RoundRectangle(new RectangleF(Width - Px(36), Px(252), Px(34), Px(34)), Px(8)))
                e.Graphics.FillPath(brush, path);
        }

        protected override void Dispose(bool disposing) { if (disposing) tooltip.Dispose(); base.Dispose(disposing); }

        private sealed class Swatch : Button
        {
            internal Color SwatchColor { get; private set; }
            internal bool Selected;
            internal Swatch(Color color)
            {
                SwatchColor = color;
                Cursor = Cursors.Hand;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(Color.White);
                using (Brush brush = new SolidBrush(SwatchColor))
                using (GraphicsPath path = QuotaAppearance.RoundRectangle(new RectangleF(3, 3, Width - 6, Height - 6), 8)) e.Graphics.FillPath(brush, path);
                if (Selected || Focused)
                    using (Pen pen = new Pen(QuotaAppearance.Darken(SwatchColor, .3f), 1.5f))
                    using (GraphicsPath path = QuotaAppearance.RoundRectangle(new RectangleF(1, 1, Width - 2, Height - 2), 10)) e.Graphics.DrawPath(pen, path);
            }
        }
    }

    internal sealed class SaturationSurface : Control
    {
        internal double Hue;
        internal double Saturation = .4;
        internal double Value = .85;
        internal event EventHandler PreviewChanged;
        internal event EventHandler SelectionCommitted;
        private bool dragging;

        internal SaturationSurface()
        {
            Cursor = Cursors.Cross;
            TabStop = true;
            AccessibleRole = AccessibleRole.Slider;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (GraphicsPath path = QuotaAppearance.RoundRectangle(bounds, 10))
            {
                GraphicsState saved = e.Graphics.Save();
                e.Graphics.SetClip(path);
                using (LinearGradientBrush horizontal = new LinearGradientBrush(bounds, Color.White, ColorMath.FromHsv(Hue, 1, 1), 0f)) e.Graphics.FillRectangle(horizontal, bounds);
                using (LinearGradientBrush vertical = new LinearGradientBrush(bounds, Color.FromArgb(0, Color.Black), Color.Black, 90f)) e.Graphics.FillRectangle(vertical, bounds);
                e.Graphics.Restore(saved);
                using (Pen border = new Pen(QuotaAppearance.Line)) e.Graphics.DrawPath(border, path);
            }
            float x = (float)(Saturation * (Width - 1));
            float y = (float)((1 - Value) * (Height - 1));
            x = Math.Max(6, Math.Min(Width - 7, x));
            y = Math.Max(6, Math.Min(Height - 7, y));
            using (Pen shadow = new Pen(Color.FromArgb(100, Color.Black), 3)) e.Graphics.DrawEllipse(shadow, x - 5, y - 5, 10, 10);
            using (Pen white = new Pen(Color.White, 2)) e.Graphics.DrawEllipse(white, x - 5, y - 5, 10, 10);
        }

        private void Adjust(Point point)
        {
            Saturation = Math.Max(0, Math.Min(1, point.X / (double)Math.Max(1, Width - 1)));
            Value = Math.Max(0, Math.Min(1, 1 - point.Y / (double)Math.Max(1, Height - 1)));
            Invalidate();
            EventHandler handler = PreviewChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Focus(); dragging = true; Capture = true; Adjust(e.Location); } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) Adjust(e.Location); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false; Capture = false; Adjust(e.Location);
            EventHandler handler = SelectionCommitted; if (handler != null) handler(this, EventArgs.Empty);
        }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode != Keys.Left && e.KeyCode != Keys.Right && e.KeyCode != Keys.Up && e.KeyCode != Keys.Down) return;
            Saturation = Math.Max(0, Math.Min(1, Saturation + (e.KeyCode == Keys.Right ? .01 : e.KeyCode == Keys.Left ? -.01 : 0)));
            Value = Math.Max(0, Math.Min(1, Value + (e.KeyCode == Keys.Up ? .01 : e.KeyCode == Keys.Down ? -.01 : 0)));
            Invalidate();
            EventHandler preview = PreviewChanged; if (preview != null) preview(this, EventArgs.Empty);
            EventHandler commit = SelectionCommitted; if (commit != null) commit(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    internal sealed class HueSurface : Control
    {
        internal double Hue;
        internal event EventHandler PreviewChanged;
        internal event EventHandler SelectionCommitted;
        private bool dragging;
        internal HueSurface()
        {
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.Slider;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 2, Math.Max(1, Width - 1), Math.Max(1, Height - 5));
            using (GraphicsPath path = QuotaAppearance.RoundRectangle(bounds, bounds.Height / 2f))
            using (LinearGradientBrush brush = new LinearGradientBrush(bounds, Color.Red, Color.Red, 0f))
            {
                ColorBlend blend = new ColorBlend();
                blend.Colors = new Color[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red };
                blend.Positions = new float[] { 0, 1 / 6f, 2 / 6f, 3 / 6f, 4 / 6f, 5 / 6f, 1 };
                brush.InterpolationColors = blend;
                e.Graphics.FillPath(brush, path);
            }
            float x = Math.Max(6, Math.Min(Width - 7, (float)(Hue / 360 * (Width - 1))));
            using (Brush fill = new SolidBrush(ColorMath.FromHsv(Hue, 1, 1))) e.Graphics.FillEllipse(fill, x - 6, 1, 12, Height - 3);
            using (Pen pen = new Pen(Color.White, 2)) e.Graphics.DrawEllipse(pen, x - 6, 1, 12, Height - 3);
        }
        private void Adjust(int x)
        {
            Hue = Math.Max(0, Math.Min(359.999, x / (double)Math.Max(1, Width - 1) * 360));
            Invalidate();
            EventHandler handler = PreviewChanged; if (handler != null) handler(this, EventArgs.Empty);
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Focus(); dragging = true; Capture = true; Adjust(e.X); } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) Adjust(e.X); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); if (!dragging) return;
            dragging = false; Capture = false; Adjust(e.X);
            EventHandler handler = SelectionCommitted; if (handler != null) handler(this, EventArgs.Empty);
        }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode != Keys.Left && e.KeyCode != Keys.Right) return;
            Hue = (Hue + (e.KeyCode == Keys.Right ? 2 : 358)) % 360;
            Invalidate();
            EventHandler preview = PreviewChanged; if (preview != null) preview(this, EventArgs.Empty);
            EventHandler commit = SelectionCommitted; if (commit != null) commit(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    internal static class ColorMath
    {
        internal static Color FromHsv(double hue, double saturation, double value)
        {
            double chroma = value * saturation;
            double x = chroma * (1 - Math.Abs((hue / 60) % 2 - 1));
            double m = value - chroma;
            double r = 0, g = 0, b = 0;
            if (hue < 60) { r = chroma; g = x; }
            else if (hue < 120) { r = x; g = chroma; }
            else if (hue < 180) { g = chroma; b = x; }
            else if (hue < 240) { g = x; b = chroma; }
            else if (hue < 300) { r = x; b = chroma; }
            else { r = chroma; b = x; }
            return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
        }
        internal static void ToHsv(Color color, out double hue, out double saturation, out double value)
        {
            double max = Math.Max(color.R, Math.Max(color.G, color.B));
            double min = Math.Min(color.R, Math.Min(color.G, color.B));
            hue = color.GetHue();
            saturation = max <= 0 ? 0 : (max - min) / max;
            value = max / 255;
        }
    }
}
