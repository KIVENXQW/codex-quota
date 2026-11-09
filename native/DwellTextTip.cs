using System;
using System.Drawing;
using System.Windows.Forms;

namespace CodexQuota {
    internal sealed class DwellTextTip : IDisposable {
        private readonly Control source;
        private readonly Func<int, string> describe;
        private readonly float scale;
        private UsageDetailPopup tip;
        private readonly StationaryHover dwell;

        internal DwellTextTip(Control source, float scale, Func<Point, int> hitTest, Func<int, string> describe) {
            this.source = source;
            this.scale = scale;
            this.describe = describe;
            dwell = new StationaryHover(source, hitTest, Show, Hide);
            source.MouseMove += MouseMoved;
        }

        private void MouseMoved(object sender, MouseEventArgs e) { dwell.Move(e.Location); }
        internal void Cancel() { dwell.Cancel(); }

        private void Show(int index) {
            string text = describe(index);
            if (String.IsNullOrEmpty(text)) return;
            if (tip == null) tip = new UsageDetailPopup(scale);
            Point anchor = source.PointToClient(Control.MousePosition);
            tip.SetHint(text);
            tip.ShowFor(source, new Rectangle(anchor, new Size(1, 1)));
        }

        private void Hide() {
            if (tip != null && tip.Visible) tip.Hide();
        }

        public void Dispose() {
            source.MouseMove -= MouseMoved;
            dwell.Dispose();
            if (tip != null) tip.Dispose();
        }
    }
}
