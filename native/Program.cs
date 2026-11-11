using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Kiven.WeeklyQuota
{
    internal static class Program
    {
        private const string InstanceName = "Local\\KivenMinimalWeeklyQuota_20261111";
        private const string OpenName = InstanceName + "_Open";
        private const string QuitName = InstanceName + "_Quit";

        [STAThread]
        public static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && (args[0] == "--preview" || args[0] == "--preview-unavailable" || args[0] == "--preview-zero"))
                return SavePreview(args[0], args[1]);
            if (args.Length == 1 && args[0] == "--quit") { Signal(QuitName); return 0; }
            bool owns = false;
            using (Mutex mutex = new Mutex(false, InstanceName))
            {
                try { owns = mutex.WaitOne(0, false); }
                catch (AbandonedMutexException) { owns = true; }
                if (!owns) { Signal(OpenName); return 0; }
                try
                {
                    using (EventWaitHandle open = new EventWaitHandle(false, EventResetMode.AutoReset, OpenName))
                    using (EventWaitHandle quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitName))
                    using (WeeklyPanel panel = new WeeklyPanel(open, quit))
                        Application.Run(panel);
                }
                finally { mutex.ReleaseMutex(); }
            }
            return 0;
        }

        private static void Signal(string name)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try { using (EventWaitHandle signal = EventWaitHandle.OpenExisting(name)) { signal.Set(); return; } }
                catch (WaitHandleCannotBeOpenedException) { Thread.Sleep(75); }
            }
        }

        private static int SavePreview(string mode, string path)
        {
            if (!Path.IsPathRooted(path)) return 2;
            WeeklyState state = mode == "--preview-unavailable" ? WeeklyState.Unavailable("no_weekly") :
                WeeklyState.FromJson("{\"available\":true,\"remainingPercent\":" + (mode == "--preview-zero" ? "0" : "68") + ",\"resetsAt\":1794623400}", DateTimeOffset.Parse("2026-11-11T10:00:00Z"));
            using (Bitmap bitmap = new Bitmap(320, 184, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                PanelDrawing.Draw(graphics, state, 1F);
                bitmap.Save(path, ImageFormat.Png);
            }
            return 0;
        }
    }

    internal sealed class WeeklyPanel : Form
    {
        private readonly object workerGate = new object();
        private readonly string root = AppDomain.CurrentDomain.BaseDirectory;
        private readonly EventWaitHandle openSignal;
        private readonly EventWaitHandle quitSignal;
        private readonly ManualResetEvent stopSignals = new ManualResetEvent(false);
        private readonly System.Windows.Forms.Timer refreshTimer = new System.Windows.Forms.Timer();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly ContextMenuStrip trayMenu = new ContextMenuStrip();
        private readonly Button updateButton = new Button();
        private readonly ToolStripMenuItem releaseMenu = new ToolStripMenuItem();
        private readonly ToolStripMenuItem checkMenu = new ToolStripMenuItem("检查更新");
        private WeeklySidebar sidebar;
        private Thread signalThread;
        private Process worker;
        private Process updateWorker;
        private UpdateResult release;
        private bool checkingUpdate;
        private bool reportUpdate;
        private WeeklyState state = WeeklyState.Loading;
        private bool refreshing;
        private volatile bool exiting;
        private float scale;
        private DateTimeOffset nextRefresh = DateTimeOffset.MinValue;
        private DateTimeOffset nextUpdateCheck = DateTimeOffset.MinValue;

        public WeeklyPanel(EventWaitHandle open, EventWaitHandle quit)
        {
            openSignal = open; quitSignal = quit;
            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero)) scale = Math.Max(1F, graphics.DpiX / 96F);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            Text = "周额度";
            BackColor = PanelDrawing.Background;
            DoubleBuffered = true;
            ClientSize = new Size((int)(320 * scale), (int)(184 * scale));
            using (GraphicsPath path = PanelDrawing.RoundedRectangle(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), 14F * scale)) Region = new Region(path);
            string iconPath = Path.Combine(root, "assets", "launcher.ico");
            Icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;
            tray.Icon = Icon;
            ApplyState(state);
            updateButton.SetBounds((int)(133 * scale), (int)(17 * scale), (int)(138 * scale), (int)(30 * scale));
            updateButton.FlatStyle = FlatStyle.Flat;
            updateButton.FlatAppearance.BorderSize = 0;
            updateButton.BackColor = PanelDrawing.Background;
            updateButton.ForeColor = Color.FromArgb(139, 66, 88);
            updateButton.Font = new Font("Microsoft YaHei UI", 12F * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            updateButton.Cursor = Cursors.Hand;
            updateButton.AutoEllipsis = true;
            updateButton.TextAlign = ContentAlignment.MiddleRight;
            updateButton.Visible = false;
            updateButton.Click += delegate { OpenRelease(); };
            Controls.Add(updateButton);
            trayMenu.Items.Add("打开", null, delegate { OpenPanel(); });
            trayMenu.Items.Add("刷新", null, delegate { RefreshReading(); });
            releaseMenu.Visible = false;
            releaseMenu.Click += delegate { OpenRelease(); };
            checkMenu.Click += delegate { CheckUpdate(true); };
            trayMenu.Items.Add(releaseMenu);
            trayMenu.Items.Add(checkMenu);
            trayMenu.Items.Add("退出", null, delegate { ExitPanel(); });
            tray.ContextMenuStrip = trayMenu;
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) OpenPanel(); };
            refreshTimer.Interval = 1000;
            refreshTimer.Tick += delegate {
                if (sidebar != null) sidebar.UpdatePosition();
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (state.Available && state.ResetsAt.HasValue && state.ResetsAt.Value <= now)
                { ApplyState(WeeklyState.Unavailable("reset_expired")); nextRefresh = DateTimeOffset.MinValue; }
                if (now >= nextRefresh) RefreshReading();
                if (now >= nextUpdateCheck) CheckUpdate(false);
            };
            Shown += delegate {
                Rectangle area = Screen.FromControl(this).WorkingArea;
                Location = new Point(area.Right - Width - (int)(24 * scale), area.Bottom - Height - (int)(24 * scale));
                sidebar = new WeeklySidebar(this, OpenPanel, state);
                sidebar.UpdatePosition();
                signalThread = new Thread(WatchSignals) { IsBackground = true, Name = "WeeklyQuota signals" };
                tray.Visible = true;
                signalThread.Start(); refreshTimer.Start(); RefreshReading(); CheckUpdate(false);
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PanelDrawing.Draw(e.Graphics, state, scale);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Escape) { Hide(); return true; }
            if (keyData == Keys.F5) { RefreshReading(); return true; }
            return base.ProcessCmdKey(ref message, keyData);
        }

        private void ApplyState(WeeklyState value)
        {
            state = value;
            if (sidebar != null) sidebar.SetState(value);
            string label = "周额度剩余 " + state.NumberText;
            string summary = label + "；" + state.DetailText;
            tray.Text = summary.Length > 63 ? summary.Substring(0, 63) : summary;
            AccessibleName = label;
            AccessibleDescription = state.DetailText;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (new RectangleF(274 * scale, 10 * scale, 36 * scale, 36 * scale).Contains(e.Location)) { Hide(); return; }
            ReleaseCapture();
            SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason != CloseReason.WindowsShutDown && e.CloseReason != CloseReason.TaskManagerClosing)
            { e.Cancel = true; Hide(); return; }
            base.OnFormClosing(e);
        }

        private void OpenPanel()
        {
            if (exiting) return;
            Show(); WindowState = FormWindowState.Normal; Activate(); BringToFront();
        }

        private void WatchSignals()
        {
            WaitHandle[] handles = new WaitHandle[] { stopSignals, openSignal, quitSignal };
            while (true)
            {
                int selected = WaitHandle.WaitAny(handles);
                if (selected == 0) return;
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke((Action)(selected == 1 ? (Action)OpenPanel : ExitPanel)); }
                catch (InvalidOperationException) { return; }
            }
        }

        private async void RefreshReading()
        {
            if (refreshing || exiting) return;
            refreshing = true; nextRefresh = DateTimeOffset.UtcNow.AddSeconds(60);
            try
            {
                WeeklyState result = await Task.Run((Func<WeeklyState>)ReadWorker);
                if (!exiting && !IsDisposed) ApplyState(result);
            }
            catch (Exception) { if (!exiting && !IsDisposed) ApplyState(WeeklyState.Unavailable("read_failed")); }
            finally { refreshing = false; }
        }

        private WeeklyState ReadWorker()
        {
            string error;
            string json = ReadNode("weekly-reader.mjs", " --once --managed", 25000, false, out error);
            return json == null ? WeeklyState.Unavailable(error) : WeeklyState.FromJson(json, DateTimeOffset.UtcNow);
        }

        private async void CheckUpdate(bool manual)
        {
            if (exiting) return;
            if (manual) reportUpdate = true;
            if (checkingUpdate) return;
            checkingUpdate = true;
            nextUpdateCheck = DateTimeOffset.UtcNow.AddDays(1);
            checkMenu.Text = "正在检查更新…";
            UpdateResult result = UpdateResult.Unavailable;
            try
            {
                result = await Task.Run(delegate {
                    string error;
                    return UpdateResult.Parse(ReadNode("weekly-update.mjs", "", 10000, true, out error));
                });
            }
            catch (Exception) { }
            if (exiting || IsDisposed) return;
            checkingUpdate = false;
            checkMenu.Text = "检查更新";
            if (result.Status != "unavailable")
            {
                release = result.Status == "available" ? result : null;
                if (release != null)
                {
                    updateButton.Text = "新版 " + release.Version + " ↗";
                    updateButton.AccessibleName = "打开极简周额度版 " + release.Version + " 发布页";
                    releaseMenu.Text = "打开新版 " + release.Version + " 发布页";
                }
                updateButton.Visible = releaseMenu.Visible = release != null;
            }
            if (reportUpdate)
            {
                reportUpdate = false;
                MessageBox.Show(Visible ? this : null, result.Message, "周额度 · 检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void OpenRelease()
        {
            if (release == null || exiting) return;
            try { Process.Start(new ProcessStartInfo(release.Url) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(Visible ? this : null, "无法打开默认浏览器。发布页地址为\n" + release.Url, "周额度", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private string ReadNode(string script, string arguments, int timeout, bool isUpdate, out string error)
        {
            error = "read_failed";
            string node = FindNode(root);
            if (node == null) { error = "node_missing"; return null; }
            string reader = Path.Combine(root, script);
            if (!File.Exists(reader)) { error = "reader_missing"; return null; }
            Process process = new Process();
            process.StartInfo = new ProcessStartInfo {
                FileName = node, Arguments = "\"" + reader + "\"" + arguments, WorkingDirectory = root,
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            try
            {
                lock (workerGate)
                {
                    if (exiting) return null;
                    process.Start();
                    if (isUpdate) updateWorker = process; else worker = process;
                }
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeout))
                { StopWorker(process); error = "timeout"; return null; }
                string json = stdout.GetAwaiter().GetResult().Trim();
                stderr.GetAwaiter().GetResult();
                return isUpdate && process.ExitCode != 0 ? null : json;
            }
            catch (Exception) { return null; }
            finally
            {
                lock (workerGate)
                {
                    if (ReferenceEquals(worker, process)) worker = null;
                    if (ReferenceEquals(updateWorker, process)) updateWorker = null;
                }
                process.Dispose();
            }
        }

        private static string FindNode(string directory)
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_WEEKLY_NODE");
            if (!String.IsNullOrWhiteSpace(configured))
            {
                string expanded = Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"'));
                if (Path.IsPathRooted(expanded) && File.Exists(expanded)) return expanded;
            }
            string local = Path.Combine(directory, "node.exe");
            if (File.Exists(local)) return local;
            foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try
                {
                    if (String.IsNullOrWhiteSpace(entry)) continue;
                    string candidate = Path.Combine(Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"')), "node.exe");
                    if (Path.IsPathRooted(candidate) && File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { }
            }
            string bundled = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "codex-runtimes", "codex-primary-runtime", "dependencies", "node", "bin", "node.exe");
            return File.Exists(bundled) ? bundled : null;
        }

        private static void StopWorker(Process process)
        {
            try
            {
                if (process.HasExited) return;
                process.StandardInput.Close();
                if (!process.WaitForExit(1500)) { process.Kill(); process.WaitForExit(1000); }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            catch (IOException) { }
        }

        private async void ExitPanel()
        {
            if (exiting) return;
            exiting = true; refreshTimer.Stop(); Hide(); tray.Visible = false; stopSignals.Set();
            DisposeSidebar();
            Process current, update;
            lock (workerGate) { current = worker; update = updateWorker; }
            await Task.Run(delegate {
                if (current != null) StopWorker(current);
                if (update != null) StopWorker(update);
            });
            Close();
        }

        private void DisposeSidebar()
        {
            if (sidebar == null) return;
            sidebar.Dispose();
            sidebar = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                DisposeSidebar();
                stopSignals.Set();
                if (signalThread != null && signalThread != Thread.CurrentThread) signalThread.Join(250);
                refreshTimer.Dispose(); tray.Dispose(); trayMenu.Dispose(); stopSignals.Dispose();
                if (Icon != null && !Object.ReferenceEquals(Icon, SystemIcons.Application)) Icon.Dispose();
            }
            base.Dispose(disposing);
        }

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    }

    internal sealed class UpdateResult
    {
        internal string Status { get; private set; }
        internal string Version { get; private set; }
        internal string Url { get; private set; }
        internal static readonly UpdateResult Unavailable = new UpdateResult { Status = "unavailable" };

        internal string Message
        {
            get
            {
                switch (Status)
                {
                    case "available": return "发现新版 " + Version + "。点击面板或托盘菜单中的新版入口即可打开发布页。";
                    case "current": return "当前已是最新的极简周额度版。";
                    case "unpublished": return "暂时没有已发布的极简周额度正式版。";
                    default: return "暂时无法检查更新，请稍后重试。";
                }
            }
        }

        internal static UpdateResult Parse(string json)
        {
            if (String.IsNullOrWhiteSpace(json) || json.Length > 4096) return Unavailable;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 4096, RecursionLimit = 8 };
                Dictionary<string, object> data = serializer.DeserializeObject(json) as Dictionary<string, object>;
                object rawStatus, rawVersion, rawUrl;
                if (data == null || !data.TryGetValue("status", out rawStatus)) return Unavailable;
                string status = rawStatus as string;
                if (status == "current" || status == "unpublished") return new UpdateResult { Status = status };
                if (status != "available" || !data.TryGetValue("version", out rawVersion) || !data.TryGetValue("url", out rawUrl)) return Unavailable;
                string version = rawVersion as string;
                if (version == null || !Regex.IsMatch(version, @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z")) return Unavailable;
                string expected = "https://github.com/KIVENXQW/codex-quota/releases/tag/weekly-v" + version;
                if (!String.Equals(expected, rawUrl as string, StringComparison.Ordinal)) return Unavailable;
                return new UpdateResult { Status = status, Version = version, Url = expected };
            }
            catch (ArgumentException) { return Unavailable; }
            catch (InvalidOperationException) { return Unavailable; }
        }
    }

    internal static class PanelDrawing
    {
        internal static readonly Color Background = Color.FromArgb(251, 250, 248);

        public static void Draw(Graphics graphics, WeeklyState state, float scale)
        {
            GraphicsState saved = graphics.Save();
            graphics.ScaleTransform(scale, scale);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);
            using (GraphicsPath card = RoundedRectangle(new RectangleF(0.5F, 0.5F, 319F, 183F), 14F))
            using (SolidBrush background = new SolidBrush(Background))
            using (Pen border = new Pen(Color.FromArgb(232, 226, 220), 1F))
            { graphics.FillPath(background, card); graphics.DrawPath(border, card); }
            using (Font title = new Font("Microsoft YaHei UI", 13F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font number = new Font("Segoe UI Semibold", 64F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font detail = new Font("Microsoft YaHei UI", 12.5F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush labelBrush = new SolidBrush(Color.FromArgb(147, 113, 105)))
            using (SolidBrush numberBrush = new SolidBrush(Color.FromArgb(43, 39, 35)))
            using (SolidBrush detailBrush = new SolidBrush(Color.FromArgb(126, 117, 109)))
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                graphics.DrawString("周额度剩余", title, labelBrush, new PointF(28, 27), format);
                graphics.DrawString(state.NumberText, number, numberBrush, new PointF(24, 53), format);
                graphics.DrawString(state.DetailText, detail, detailBrush, new PointF(28, 145), format);
            }
            if (state.RemainingPercent.HasValue)
            {
                RectangleF track = new RectangleF(28F, 130F, 264F, 5F);
                using (GraphicsPath trackPath = RoundedRectangle(track, track.Height / 2F))
                using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(228, 225, 222)))
                    graphics.FillPath(trackBrush, trackPath);

                float filledWidth = (float)(track.Width * state.RemainingPercent.Value / 100D);
                if (filledWidth > 0F)
                {
                    RectangleF fill = new RectangleF(track.X, track.Y, filledWidth, track.Height);
                    using (GraphicsPath fillPath = RoundedRectangle(fill, Math.Min(fill.Width, fill.Height) / 2F))
                    using (SolidBrush fillBrush = new SolidBrush(Color.FromArgb(174, 104, 122)))
                        graphics.FillPath(fillBrush, fillPath);
                }
            }
            using (Pen close = new Pen(Color.FromArgb(157, 147, 138), 1.15F))
            {
                close.StartCap = LineCap.Round; close.EndCap = LineCap.Round;
                graphics.DrawLine(close, 287, 23, 294, 30); graphics.DrawLine(close, 294, 23, 287, 30);
            }
            graphics.Restore(saved);
        }

        public static GraphicsPath RoundedRectangle(RectangleF rectangle, float radius)
        {
            float diameter = radius * 2F;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure(); return path;
        }
    }
}
