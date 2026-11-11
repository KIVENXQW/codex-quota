using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Kiven.WeeklyQuota
{
    internal sealed class WeeklySidebar : IDisposable
    {
        private readonly WindowTracker tracker = new WindowTracker();
        private readonly Control dispatcher;
        private readonly Action openPanel;
        private WeeklyState state;
        private WeeklySidebarBadge badge;
        private int layoutVersion = -1;
        private bool queued, disposed;

        internal WeeklySidebar(Control dispatcher, Action openPanel, WeeklyState initial)
        {
            this.dispatcher = dispatcher;
            this.openPanel = openPanel;
            state = initial;
            tracker.Changed += QueuePosition;
        }

        private void QueuePosition(object sender, EventArgs e)
        {
            if (disposed || queued || !dispatcher.IsHandleCreated) return;
            queued = true;
            try
            {
                dispatcher.BeginInvoke((MethodInvoker)delegate {
                    queued = false;
                    if (!disposed) UpdatePosition();
                });
            }
            catch (InvalidOperationException) { queued = false; }
        }

        internal void SetState(WeeklyState value)
        {
            state = value;
            if (badge != null && !badge.IsDisposed) badge.SetState(value);
        }

        internal void UpdatePosition()
        {
            if (disposed) return;
            if (!tracker.Update())
            {
                if (badge != null && !badge.IsDisposed)
                {
                    if (!badge.IsAttachedTo(tracker.Target)) badge.Dispose();
                    else if (badge.Visible) badge.Hide();
                }
                return;
            }
            if (badge == null || badge.IsDisposed)
            {
                badge = new WeeklySidebarBadge(openPanel);
                badge.SetState(state);
                layoutVersion = -1;
            }
            if (layoutVersion != tracker.LayoutVersion || !badge.IsAttachedTo(tracker.Target))
            {
                layoutVersion = -1;
                int pixels = Math.Max(1, (int)Math.Round(24 * tracker.Scale));
                Size size = new Size(pixels, pixels);
                Point screen, client;
                if (!tracker.TryBadgeLocation(size, out screen) ||
                    !BadgeAttachment.TryScreenToClient(tracker.Target, screen, out client) ||
                    !badge.TryAttach(tracker.Target, client, size))
                {
                    if (!badge.IsDisposed) badge.Hide();
                    return;
                }
                layoutVersion = tracker.LayoutVersion;
            }
            if (!badge.Visible) badge.Show();
            BadgeAttachment.EnsureAboveSiblings(badge.Handle);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            tracker.Changed -= QueuePosition;
            tracker.Dispose();
            if (badge != null) badge.Dispose();
        }
    }

    internal sealed class WeeklySidebarBadge : Form
    {
        private readonly RingButton ring = new RingButton();
        private readonly ToolTip tip = new ToolTip();
        private IntPtr host;
        private Point attachment;

        internal WeeklySidebarBadge(Action openPanel)
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = ring.BackColor;
            ring.Dock = DockStyle.Fill;
            ring.Click += delegate { openPanel(); };
            Controls.Add(ring);
        }

        internal void SetState(WeeklyState value)
        {
            ring.Remaining = value.RemainingPercent;
            string reading = "周额度剩余 " + value.NumberText;
            ring.AccessibleName = reading + "，打开周额度面板";
            ring.AccessibleDescription = value.DetailText;
            tip.SetToolTip(ring, reading + " · " + value.DetailText);
            ring.Invalidate();
        }

        internal bool IsAttachedTo(IntPtr parent)
        {
            return parent != IntPtr.Zero && host == parent && IsHandleCreated && BadgeAttachment.IsChildOf(Handle, parent);
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams p = base.CreateParams;
                p.ExStyle |= 0x08000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                if (host != IntPtr.Zero)
                {
                    p.Parent = host;
                    p.Style = (p.Style & ~unchecked((int)0x80000000)) | 0x40000000; // WS_CHILD
                    p.ExStyle = (p.ExStyle & ~0x00040008) | 0x00080000; // layered child surface
                    p.X = attachment.X;
                    p.Y = attachment.Y;
                }
                return p;
            }
        }

        internal bool TryAttach(IntPtr parent, Point client, Size size)
        {
            BadgeAttachment.DpiScope dpi;
            if (!BadgeAttachment.TryEnterDpiContext(parent, out dpi)) return false;
            using (dpi)
            {
                try
                {
                    attachment = client;
                    if (!IsAttachedTo(parent) || !BadgeAttachment.MatchesDpi(Handle, dpi.Context))
                    {
                        Hide();
                        if (IsHandleCreated) DestroyHandle();
                        host = parent;
                        TopLevel = false;
                    }
                    if (Size != size || Region == null)
                    {
                        Size = size;
                        Region previous = Region;
                        using (GraphicsPath circle = new GraphicsPath())
                        {
                            circle.AddEllipse(0, 0, Width, Height);
                            Region = new Region(circle);
                        }
                        if (previous != null) previous.Dispose();
                    }
                    IntPtr window = Handle;
                    return BadgeAttachment.IsChildOf(window, parent) &&
                        BadgeAttachment.MatchesDpi(window, dpi.Context) &&
                        BadgeAttachment.MatchesDpi(ring.Handle, dpi.Context) &&
                        BadgeAttachment.InitializeSurface(window) &&
                        BadgeAttachment.PositionChild(window, client, size);
                }
                catch (Win32Exception) { return false; }
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0021) // WM_MOUSEACTIVATE
            {
                message.Result = new IntPtr(3); // MA_NOACTIVATE
                return;
            }
            base.WndProc(ref message);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed) tip.Dispose();
            base.Dispose(disposing);
        }

        private sealed class RingButton : Button
        {
            internal double? Remaining;

            internal RingButton()
            {
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                BackColor = Color.FromArgb(247, 251, 249);
                Cursor = Cursors.Hand;
                AccessibleRole = AccessibleRole.PushButton;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                WeeklyRing.Draw(e.Graphics, ClientRectangle, Remaining);
            }
        }
    }
}
