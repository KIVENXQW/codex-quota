using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexQuota
{
    internal sealed class StationaryHover : IDisposable
    {
        const int DwellMilliseconds = 1000;
        readonly Control source;
        readonly Func<Point, int> hitTest;
        readonly Action<int> show;
        readonly Action hide;
        readonly Timer timer = new Timer();
        readonly Stopwatch quiet = new Stopwatch();
        readonly List<Control> ancestors = new List<Control>();
        Form owner;
        ScrollableControl viewport;
        Point viewportOffset, screenPoint;
        int candidate;
        bool tracking, afterScroll, shown, disposed;

        [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);

        internal StationaryHover(Control source, Func<Point, int> hitTest, Action<int> show, Action hide)
        {
            this.source = source;
            this.hitTest = hitTest;
            this.show = show;
            this.hide = hide;
            timer.Interval = DwellMilliseconds;
            timer.Tick += Tick;
            source.ParentChanged += ParentChanged;
            source.VisibleChanged += CancelOnChange;
            source.SizeChanged += CancelOnChange;
            source.LocationChanged += LocationChanged;
            source.HandleDestroyed += CancelOnChange;
            source.Leave += CancelOnChange;
            source.MouseLeave += CancelOnChange;
            source.MouseWheel += Wheel;
            BindAncestors();
        }

        void BindAncestors()
        {
            for (Control parent = source.Parent; parent != null; parent = parent.Parent) {
                ancestors.Add(parent);
                parent.ParentChanged += ParentChanged;
                parent.VisibleChanged += CancelOnChange;
                parent.SizeChanged += CancelOnChange;
                parent.LocationChanged += LocationChanged;
                var scroll = parent as ScrollableControl;
                if (scroll != null) {
                    if (viewport == null && scroll.AutoScroll) {
                        viewport = scroll;
                        viewportOffset = scroll.AutoScrollPosition;
                    }
                    scroll.Scroll += Scrolled;
                    scroll.MouseWheel += Wheel;
                }
            }
            owner = source.FindForm();
            if (owner != null) owner.Deactivate += CancelOnChange;
        }

        void UnbindAncestors()
        {
            if (owner != null) owner.Deactivate -= CancelOnChange;
            owner = null;
            foreach (Control parent in ancestors) {
                parent.ParentChanged -= ParentChanged;
                parent.VisibleChanged -= CancelOnChange;
                parent.SizeChanged -= CancelOnChange;
                parent.LocationChanged -= LocationChanged;
                var scroll = parent as ScrollableControl;
                if (scroll != null) {
                    scroll.Scroll -= Scrolled;
                    scroll.MouseWheel -= Wheel;
                }
            }
            ancestors.Clear();
            viewport = null;
        }

        void ParentChanged(object sender, EventArgs e)
        {
            Cancel();
            UnbindAncestors();
            BindAncestors();
        }

        bool CanTrack
        {
            get { return !disposed && source.IsHandleCreated && source.Visible && source.Enabled && owner != null && owner.Visible; }
        }

        internal void Move(Point clientPoint)
        {
            if (!CanTrack) return;
            Point point = source.PointToScreen(clientPoint);
            int index = hitTest(clientPoint);
            if (tracking && point == screenPoint && (afterScroll || candidate == index)) return;
            if (index < 0) { Cancel(); return; }
            Arm(point, index, false);
        }

        void Arm(Point point, int index, bool scrolling)
        {
            HideShown();
            screenPoint = point;
            candidate = index;
            tracking = true;
            afterScroll = scrolling;
            quiet.Restart();
            // Keep one Windows timer alive throughout movement. On its next tick,
            // only the time still owed since the last distinct point needs scheduling.
            if (!timer.Enabled) {
                timer.Interval = DwellMilliseconds;
                timer.Start();
            }
        }

        void AfterScroll()
        {
            if (viewport != null) viewportOffset = viewport.AutoScrollPosition;
            if (!CanTrack) { Cancel(); return; }
            Point cursor = Cursor.Position;
            if (source.ClientRectangle.Contains(source.PointToClient(cursor))) Arm(cursor, -1, true);
            else Cancel();
        }

        void Scrolled(object sender, ScrollEventArgs e) { AfterScroll(); }
        void Wheel(object sender, MouseEventArgs e) { AfterScroll(); }
        void LocationChanged(object sender, EventArgs e)
        {
            // Assigning AutoScrollPosition moves the child without raising Scroll.
            if (afterScroll || (viewport != null && viewport.AutoScrollPosition != viewportOffset)) AfterScroll();
            else Cancel();
        }
        void CancelOnChange(object sender, EventArgs e) { Cancel(); }

        void Tick(object sender, EventArgs e)
        {
            if (!timer.Enabled) return;
            if (!tracking || !CanTrack) { Cancel(); return; }
            int remaining = (int)Math.Max(0, DwellMilliseconds - quiet.ElapsedMilliseconds);
            if (remaining > 0) { timer.Interval = remaining; return; }
            timer.Stop();

            // A mouse message can be stale by the time its timer fires. Hit-test
            // the live HWND (including viewport clipping and covering windows)
            // and the chart's current cell at the unchanged screen point.
            if (Cursor.Position != screenPoint || WindowFromPoint(screenPoint) != source.Handle) { Cancel(); return; }
            int current = hitTest(source.PointToClient(screenPoint));
            if (current < 0 || (!afterScroll && current != candidate)) { Cancel(); return; }
            candidate = current;
            afterScroll = false;
            shown = true;
            show(current);
        }

        internal void Select(int index)
        {
            if (!CanTrack || index < 0 || !source.Focused || GetActiveWindow() != owner.Handle) { Cancel(); return; }
            timer.Stop();
            tracking = afterScroll = false;
            shown = true;
            show(index);
        }

        void HideShown()
        {
            if (!shown) return;
            shown = false;
            hide();
        }

        internal void Cancel()
        {
            if (disposed) return;
            tracking = afterScroll = false;
            timer.Stop();
            HideShown();
        }

        public void Dispose()
        {
            if (disposed) return;
            Cancel();
            disposed = true;
            UnbindAncestors();
            source.ParentChanged -= ParentChanged;
            source.VisibleChanged -= CancelOnChange;
            source.SizeChanged -= CancelOnChange;
            source.LocationChanged -= LocationChanged;
            source.HandleDestroyed -= CancelOnChange;
            source.Leave -= CancelOnChange;
            source.MouseLeave -= CancelOnChange;
            source.MouseWheel -= Wheel;
            timer.Dispose();
        }
    }

    internal sealed class ChartDetailPresenter : IDisposable
    {
        readonly Control source;
        readonly float scale;
        UsageDetailPopup popup;
        internal readonly StationaryHover Hover;
        internal int Selected { get; private set; }

        internal ChartDetailPresenter(Control source, float scale, Func<Point, int> hitTest, Action<int> show)
        {
            this.source = source;
            this.scale = scale;
            Selected = -1;
            Hover = new StationaryHover(source, hitTest, show, Hide);
        }

        Rectangle VisibleAnchor(Rectangle anchor)
        {
            Rectangle visible = source.RectangleToScreen(Rectangle.Intersect(source.ClientRectangle, anchor));
            for (Control parent = source.Parent; parent != null; parent = parent.Parent)
                visible.Intersect(parent.RectangleToScreen(parent.ClientRectangle));
            return visible.Width > 0 && visible.Height > 0 ? source.RectangleToClient(visible) : Rectangle.Empty;
        }

        internal void Show(int index, ChartBucket bucket, Rectangle anchor, Color accent, bool english)
        {
            if (Selected == index) return;
            if (popup == null) popup = new UsageDetailPopup(scale);
            Selected = index;
            popup.SetData(bucket, accent, english);
            source.AccessibleDescription = popup.AccessibleDescription;
            Rectangle visible = VisibleAnchor(anchor);
            if (visible.IsEmpty) popup.Hide();
            else popup.ShowFor(source, visible);
            // Show can dispatch window messages synchronously, including dismissal.
            if (Selected != index) { if (popup.Visible) popup.Hide(); return; }
            source.Invalidate();
        }

        void Hide()
        {
            if (Selected < 0) return;
            Selected = -1;
            if (popup != null && popup.Visible) popup.Hide();
            source.AccessibleDescription = null;
            source.Invalidate();
        }

        public void Dispose()
        {
            Hover.Dispose();
            if (popup != null) popup.Dispose();
        }
    }
}
