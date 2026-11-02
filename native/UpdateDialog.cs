using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexQuota
{
    public sealed class UpdateDialog : Form
    {
        private readonly bool english;
        private readonly float scale;
        private readonly Font regularFont, smallFont, headingFont;
        private readonly Label heading, version, detailLabel, progressText;
        private readonly Panel notesFrame;
        private readonly TextBox notes;
        private readonly RoundedButton install, dismiss;
        private readonly UpdateProgress progress;
        private bool operationPending;
        private bool cancelRaised;
        private bool releaseReady;

        public event EventHandler InstallRequested;
        public event EventHandler CancelRequested;

        public UpdateDialog(UserPreferences prefs, string currentVersion)
        {
            prefs = prefs ?? new UserPreferences();
            english = prefs.Language == "en";
            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
                scale = Math.Max(1, graphics.DpiX / 96f);

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            ForeColor = QuotaAppearance.Text;
            Text = L("Codex 用量面板更新", "Codex usage companion update");
            AccessibleName = Text;
            ClientSize = new Size(Px(420), Px(330));

            regularFont = new Font("Microsoft YaHei UI", 13 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            smallFont = new Font("Microsoft YaHei UI", 11 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            headingFont = new Font("Microsoft YaHei UI", 15 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            Font = regularFont;

            heading = MakeLabel(headingFont, QuotaAppearance.Text);
            heading.SetBounds(Px(22), Px(19), Px(376), Px(28));
            Controls.Add(heading);
            version = MakeLabel(smallFont, QuotaAppearance.Muted);
            version.Text = L("当前版本 ", "Current version ") + (String.IsNullOrWhiteSpace(currentVersion) ? "—" : currentVersion);
            version.SetBounds(Px(22), Px(51), Px(376), Px(20));
            Controls.Add(version);
            detailLabel = MakeLabel(regularFont, QuotaAppearance.Muted);
            detailLabel.TextAlign = ContentAlignment.TopLeft;
            detailLabel.SetBounds(Px(22), Px(85), Px(376), Px(40));
            Controls.Add(detailLabel);

            notesFrame = new Panel();
            notesFrame.SetBounds(Px(22), Px(133), Px(376), Px(111));
            notesFrame.BackColor = Color.FromArgb(249, 249, 251);
            notesFrame.Paint += PaintNotesFrame;
            Controls.Add(notesFrame);
            notes = new TextBox();
            notes.Multiline = true;
            notes.ReadOnly = true;
            notes.WordWrap = true;
            notes.ScrollBars = ScrollBars.Vertical;
            notes.BorderStyle = BorderStyle.None;
            notes.BackColor = notesFrame.BackColor;
            notes.ForeColor = QuotaAppearance.Text;
            notes.Font = regularFont;
            notes.AccessibleName = L("更新说明", "Release notes");
            notes.SetBounds(Px(10), Px(10), Px(356), Px(91));
            notesFrame.Controls.Add(notes);

            progress = new UpdateProgress(prefs.AccentColor);
            progress.SetBounds(Px(22), Px(253), Px(376), Px(6));
            Controls.Add(progress);
            progressText = MakeLabel(smallFont, QuotaAppearance.Muted);
            progressText.TextAlign = ContentAlignment.MiddleRight;
            progressText.SetBounds(Px(22), Px(262), Px(376), Px(17));
            Controls.Add(progressText);

            install = new RoundedButton();
            install.Font = regularFont;
            install.Accent = prefs.AccentColor;
            install.Selected = true;
            install.Text = L("下载并安装", "Download & install");
            install.AccessibleName = install.Text;
            install.SetBounds(Px(145), Px(285), Px(159), Px(32));
            install.Click += InstallClicked;
            Controls.Add(install);

            dismiss = new RoundedButton();
            dismiss.Font = regularFont;
            dismiss.Accent = prefs.AccentColor;
            dismiss.SetBounds(Px(314), Px(285), Px(84), Px(32));
            dismiss.Click += delegate { Close(); };
            Controls.Add(dismiss);
            CancelButton = dismiss;
            ShowChecking();
        }

        private int Px(int value) { return (int)Math.Round(value * scale); }
        private string L(string zh, string en) { return english ? en : zh; }

        private Label MakeLabel(Font font, Color color)
        {
            Label label = new Label();
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.AutoEllipsis = true;
            label.UseMnemonic = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        public void ShowChecking()
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { BeginInvoke(new Action(ShowChecking)); return; }
            operationPending = true;
            cancelRaised = false;
            releaseReady = false;
            heading.ForeColor = QuotaAppearance.Text;
            heading.Text = L("正在检查更新", "Checking for updates");
            detailLabel.Text = L("正在获取最新版本信息…", "Looking for the latest release…");
            notesFrame.Visible = false;
            progress.Visible = false;
            progressText.Visible = false;
            install.Visible = false;
            dismiss.Text = L("取消", "Cancel");
        }

        public void ShowRelease(UpdateRelease release)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { BeginInvoke(new Action<UpdateRelease>(ShowRelease), release); return; }
            if (release == null)
            {
                ShowInfo(L("暂时无法获取更新", "Update unavailable"), L("请稍后重新检查。", "Please check again later."), true);
                return;
            }
            operationPending = false;
            cancelRaised = false;
            releaseReady = true;
            heading.ForeColor = QuotaAppearance.Text;
            heading.Text = L("有新版本可用", "An update is available");
            detailLabel.Text = L("新版本 ", "New version ") + (String.IsNullOrWhiteSpace(release.Version) ? "—" : release.Version);
            notes.Text = String.IsNullOrWhiteSpace(release.Notes) ? L("此版本未提供更新说明。", "No release notes were provided.") : release.Notes;
            notes.SelectionStart = 0;
            notes.SelectionLength = 0;
            notesFrame.Visible = true;
            progress.Visible = false;
            progressText.Visible = false;
            install.Enabled = true;
            install.Visible = true;
            dismiss.Text = L("稍后", "Later");
        }

        public void ShowInfo(string headline, string detail, bool error)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { BeginInvoke(new Action<string, string, bool>(ShowInfo), headline, detail, error); return; }
            operationPending = false;
            releaseReady = false;
            heading.ForeColor = error ? Color.FromArgb(156, 67, 80) : QuotaAppearance.Text;
            heading.Text = headline ?? "";
            bool longDetail = detail != null && (detail.Length > 110 || detail.IndexOf('\n') >= 0);
            detailLabel.Text = longDetail ? "" : detail ?? "";
            notes.Text = longDetail ? detail : "";
            notes.SelectionStart = 0;
            notes.SelectionLength = 0;
            notesFrame.Visible = longDetail;
            progress.Visible = false;
            progressText.Visible = false;
            install.Visible = false;
            dismiss.Text = L("关闭", "Close");
        }

        public void ShowProgress(string detail, int percent)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { BeginInvoke(new Action<string, int>(ShowProgress), detail, percent); return; }
            operationPending = true;
            releaseReady = false;
            heading.ForeColor = QuotaAppearance.Text;
            heading.Text = L("正在准备更新", "Preparing the update");
            detailLabel.Text = detail ?? "";
            progress.Percent = Math.Max(0, Math.Min(100, percent));
            progress.Visible = percent >= 0;
            progress.Invalidate();
            progressText.Text = percent < 0 ? "" : progress.Percent.ToString() + "%";
            progressText.Visible = percent >= 0;
            install.Visible = false;
            dismiss.Text = L("取消", "Cancel");
        }

        private void InstallClicked(object sender, EventArgs e)
        {
            if (!releaseReady || IsDisposed) return;
            releaseReady = false;
            operationPending = true;
            cancelRaised = false;
            install.Enabled = false;
            ShowProgress(L("正在准备下载…", "Preparing the download…"), -1);
            EventHandler handler = InstallRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (operationPending && !cancelRaised)
            {
                cancelRaised = true;
                operationPending = false;
                EventHandler handler = CancelRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            }
            base.OnFormClosing(e);
        }

        private void PaintNotesFrame(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(QuotaAppearance.Line))
            using (GraphicsPath path = QuotaAppearance.RoundRectangle(new RectangleF(.5f, .5f, notesFrame.Width - 1, notesFrame.Height - 1), Px(8)))
                e.Graphics.DrawPath(pen, path);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                regularFont.Dispose();
                smallFont.Dispose();
                headingFont.Dispose();
            }
        }

        private sealed class UpdateProgress : Control
        {
            private readonly Color accent;
            internal int Percent { get; set; }
            internal UpdateProgress(Color color)
            {
                accent = color;
                BackColor = Color.White;
                AccessibleRole = AccessibleRole.ProgressBar;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = QuotaAppearance.RoundRectangle(new RectangleF(0, 0, Width, Height), Height / 2f))
                using (Brush background = new SolidBrush(QuotaAppearance.Line))
                {
                    e.Graphics.FillPath(background, path);
                    GraphicsState state = e.Graphics.Save();
                    e.Graphics.SetClip(path);
                    using (Brush fill = new SolidBrush(accent)) e.Graphics.FillRectangle(fill, 0, 0, Width * Percent / 100f, Height);
                    e.Graphics.Restore(state);
                }
                AccessibleName = Percent.ToString() + "%";
            }
        }
    }
}
