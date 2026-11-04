using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexQuota
{
    internal static class GlassMaterial
    {
        internal static void Surface(Graphics graphics, RectangleF bounds, float radius, Color bottom, float scale, bool shadow)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            if (shadow)
            {
                // A small falloff below the lens gives depth without a hard outline.
                for (int i = 5; i > 0; i--)
                {
                    RectangleF shade = bounds;
                    shade.Inflate(i * .5f * scale, i * .4f * scale);
                    shade.Offset(0, 2.2f * scale);
                    using (GraphicsPath path = QuotaAppearance.RoundRectangle(shade, radius + i * .4f * scale))
                    using (Brush fill = new SolidBrush(Color.FromArgb(4, 71, 50, 80))) graphics.FillPath(fill, path);
                }
            }
            using (GraphicsPath path = QuotaAppearance.RoundRectangle(bounds, radius))
            using (Brush fill = new LinearGradientBrush(bounds, Color.FromArgb(253, 252, 255), bottom, 125f))
            using (Pen edge = new Pen(Color.FromArgb(210, Color.White), Math.Max(1, scale)))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(edge, path);
            }
            RectangleF inner = bounds; inner.Inflate(-1.1f * scale, -1.1f * scale);
            using (GraphicsPath path = QuotaAppearance.RoundRectangle(inner, Math.Max(1, radius - scale)))
            using (Pen highlight = new Pen(Color.FromArgb(75, Color.White), scale)) graphics.DrawPath(highlight, path);
        }
    }

    internal sealed class GlassPagePanel : Panel
    {
        internal GlassPagePanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }
    }

    internal sealed class GlassSegmentRail : Panel
    {
        private readonly float scale;
        private readonly Timer motion = new Timer();
        private readonly Stopwatch clock = new Stopwatch();
        private RoundedButton selected;
        private RectangleF lens, from, target;
        private bool arranging;
        internal Color Accent = QuotaAppearance.Green;
        private bool minimal;
        internal bool Minimal { get { return minimal; } set { if(minimal==value)return;minimal=value;motion.Stop();PerformLayout();Invalidate(true); } }

        internal GlassSegmentRail(float factor, string name, params RoundedButton[] buttons)
        {
            scale = factor; Name = name;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            TabStop = false;
            foreach (RoundedButton button in buttons)
            {
                Controls.Add(button);
                button.BackColor = Color.Transparent;
                button.AccessibleRole = AccessibleRole.RadioButton;
                if (button.Selected) selected = button;
            }
            motion.Interval = 15;
            motion.Tick += delegate
            {
                double progress = Math.Min(1, clock.Elapsed.TotalMilliseconds / 240);
                float eased = (float)(1 - Math.Pow(1 - progress, 4));
                lens = new RectangleF(from.X + (target.X - from.X) * eased, target.Y,
                    from.Width + (target.Width - from.Width) * eased, target.Height);
                if (progress >= 1) { lens = target; motion.Stop(); clock.Reset(); }
                Invalidate(true);
            };
        }

        internal void SelectButton(RoundedButton button)
        {
            if (selected == button && target == (RectangleF)button.Bounds) return;
            selected = button; from = lens; target = button.Bounds;
            Form owner = FindForm();
            if (Minimal || lens.Width <= 0 || owner == null || !owner.Visible || !SystemInformation.IsMenuAnimationEnabled)
            {
                motion.Stop(); lens = target;
            }
            else { clock.Restart(); motion.Start(); }
            Invalidate(true);
        }

        internal RoundedButton AdjacentButton(RoundedButton current, int direction)
        {
            int index = Controls.IndexOf(current), count = Controls.Count;
            if (index < 0 || count == 0) return null;
            return (RoundedButton)Controls[(index + direction + count) % count];
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (arranging || Controls.Count == 0) return;
            arranging = true;
            int inset = Minimal ? 0 : Math.Max(2, (int)Math.Round(3 * scale));
            int available = Math.Max(0, Width - inset * 2);
            int compactLeft = 0;
            for (int i = 0; i < Controls.Count; i++)
            {
                int left = inset + (int)Math.Round(available * i / (double)Controls.Count);
                int right = inset + (int)Math.Round(available * (i + 1) / (double)Controls.Count);
                if(Minimal && Name=="mainNavigation") {left=compactLeft;right=left+(int)Math.Round((i==0?48:76)*scale);compactLeft=right+(int)Math.Round(8*scale);}
                Controls[i].SetBounds(left, inset, Math.Max(0, right - left), Math.Max(0, Height - inset * 2));
            }
            if (selected != null)
            {
                RectangleF next = selected.Bounds;
                if (next != target) { motion.Stop(); lens = target = next; }
            }
            arranging = false; Invalidate(true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 4 || Height < 4) return;
            Graphics graphics = e.Graphics; graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if(Minimal) {
                if(Name=="mainNavigation")using(var line=new Pen(MinimalMaterial.Line,scale))graphics.DrawLine(line,0,Height-scale,Width,Height-scale);
                if(selected!=null) {
                    float left=selected.Left+4*scale,width=Math.Min(selected.Width-8*scale,TextRenderer.MeasureText(selected.Text,selected.Font,Size.Empty,TextFormatFlags.NoPadding).Width);
                    if(Name=="mainNavigation")using(var line=new Pen(QuotaAppearance.Darken(Accent,.12f),2*scale))graphics.DrawLine(line,left,Height-scale,left+width,Height-scale);
                }
                return;
            }
            RectangleF track = new RectangleF(.5f * scale, .5f * scale, Width - scale, Height - scale);
            float radius = Math.Min(19 * scale, Height * .43f);
            Color upper = QuotaAppearance.Blend(Accent, Color.FromArgb(220, 220, 232), .9f);
            Color lower = QuotaAppearance.Blend(Accent, Color.FromArgb(233, 231, 241), .92f);
            using (GraphicsPath path = QuotaAppearance.RoundRectangle(track, radius))
            using (Brush fill = new LinearGradientBrush(track, upper, lower, 90f))
            using (Pen rim = new Pen(Color.FromArgb(145, Color.White), scale))
            {
                graphics.FillPath(fill, path); graphics.DrawPath(rim, path);
            }
            using (GraphicsPath path = QuotaAppearance.RoundRectangle(new RectangleF(scale, scale, Width - 2 * scale, Height - 2 * scale), radius))
            using (Pen inset = new Pen(Color.FromArgb(15, 72, 54, 88), scale)) graphics.DrawPath(inset, path);
            if (lens.Width > 1 && lens.Height > 1)
            {
                RectangleF glass = lens; glass.Inflate(-.5f * scale, -.5f * scale); glass.Height -= scale;
                using (GraphicsPath shade = QuotaAppearance.RoundRectangle(new RectangleF(glass.X - .5f * scale, glass.Y + 2.2f * scale, glass.Width + scale, glass.Height), Math.Max(4, radius - scale)))
                using (Pen shadow = new Pen(Color.FromArgb(18, 78, 61, 91), 2.5f * scale)) graphics.DrawPath(shadow, shade);
                GlassMaterial.Surface(graphics, glass, Math.Max(4, radius - 2 * scale),
                    QuotaAppearance.Blend(Accent, Color.FromArgb(245, 242, 250), .95f), scale, true);
                using (Pen reflection = new Pen(Color.FromArgb(235, Color.White), 1.4f * scale))
                {
                    reflection.StartCap = reflection.EndCap = LineCap.Round;
                    e.Graphics.DrawLine(reflection, glass.Left + radius, glass.Top + 1.2f * scale, glass.Right - radius, glass.Top + 1.2f * scale);
                }
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) motion.Dispose(); base.Dispose(disposing); }
    }

    internal sealed class GlassTransparencySlider : Control
    {
        private int value = 45;
        private readonly float scale;
        internal Color Accent = QuotaAppearance.Green;
        public int Minimum { get { return 10; } }
        public int Maximum { get { return 80; } }
        public int Value
        {
            get { return value; }
            set
            {
                int next = Math.Max(Minimum, Math.Min(Maximum, value));
                if (this.value == next) return;
                this.value = next; Invalidate();
                EventHandler handler = ValueChanged;
                if (handler != null) handler(this, EventArgs.Empty);
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            }
        }
        public event EventHandler ValueChanged;
        internal GlassTransparencySlider(float factor)
        {
            scale = factor;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            BackColor = Color.Transparent; Cursor = Cursors.Hand; TabStop = true; AccessibleRole = AccessibleRole.Slider;
        }
        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Enabled) return;
            switch (e.KeyCode)
            {
                case Keys.Left: case Keys.Down: Value--; break;
                case Keys.Right: case Keys.Up: Value++; break;
                case Keys.PageDown: Value -= 10; break;
                case Keys.PageUp: Value += 10; break;
                case Keys.Home: Value = Minimum; break;
                case Keys.End: Value = Maximum; break;
                default: return;
            }
            e.Handled = true; e.SuppressKeyPress = true;
        }
        private void SetFromPointer(int x)
        {
            float padding = 12 * scale;
            Value = Minimum + (int)Math.Round((Maximum - Minimum) * (x - padding) / Math.Max(1, Width - padding * 2));
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (!Enabled || e.Button != MouseButtons.Left) return;
            Focus(); Capture = true; SetFromPointer(e.X);
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Enabled && Capture) SetFromPointer(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left) Capture = false; }
        protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) Capture = false; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float left = 12 * scale, right = Width - left, y = Height / 2f;
            float thumb = left + (right - left) * (Value - Minimum) / (Maximum - Minimum);
            using (Pen track = new Pen(Color.FromArgb(216, 211, 222), 5 * scale))
            using (Pen fill = new Pen(Enabled ? QuotaAppearance.Darken(Accent, .1f) : Color.FromArgb(185, 182, 192), 5 * scale))
            {
                track.StartCap = track.EndCap = fill.StartCap = fill.EndCap = LineCap.Round;
                g.DrawLine(track, left, y, right, y); if (thumb > left) g.DrawLine(fill, left, y, thumb, y);
            }
            GlassMaterial.Surface(g, new RectangleF(thumb - 10 * scale, y - 10 * scale, 20 * scale, 20 * scale), 10 * scale, Color.FromArgb(243, 238, 247), scale, true);
            if (Focused && ShowFocusCues)
                using (Pen focus = new Pen(QuotaAppearance.Darken(Accent, .3f), 1.5f * scale)) g.DrawEllipse(focus, thumb - 13 * scale, y - 13 * scale, 26 * scale, 26 * scale);
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new SliderAccessibility(this); }
        private sealed class SliderAccessibility : ControlAccessibleObject
        {
            private readonly GlassTransparencySlider slider;
            internal SliderAccessibility(GlassTransparencySlider owner) : base(owner) { slider = owner; }
            public override string Value
            {
                get { return slider.Value.ToString() + "%"; }
                set { int number; if (slider.Enabled && Int32.TryParse((value ?? "").TrimEnd('%'), out number)) slider.Value = number; }
            }
        }
    }
}
