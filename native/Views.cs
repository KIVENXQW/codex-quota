using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace CodexQuota
{
    internal static class QuotaAppearance
    {
        internal static readonly Color Text = Color.FromArgb(35, 38, 43);
        internal static readonly Color Muted = Color.FromArgb(114, 121, 129);
        internal static readonly Color Line = Color.FromArgb(229, 232, 236);
        internal static readonly Color Green = Color.FromArgb(218, 145, 170);
        internal static readonly Color Mint = Color.FromArgb(250, 238, 243);

        internal static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb((int)(from.R + (to.R - from.R) * amount), (int)(from.G + (to.G - from.G) * amount), (int)(from.B + (to.B - from.B) * amount));
        }
        internal static Color Darken(Color color, float amount) { return Blend(color, Color.Black, amount); }

        internal static GraphicsPath RoundRectangle(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Max(.1f, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static string Percentage(double? value)
        {
            if (!value.HasValue || Double.IsNaN(value.Value) || Double.IsInfinity(value.Value)) return "—";
            return Math.Max(0, Math.Min(100, value.Value)).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        internal static DateTime? ResetTime(long? seconds)
        {
            if (!seconds.HasValue) return null;
            try
            {
                return DateTime.SpecifyKind(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddSeconds(seconds.Value).AddHours(8), DateTimeKind.Unspecified);
            }
            catch (ArgumentOutOfRangeException) { return null; }
        }

        internal static string ResetCountdown(DateTime? reset)
        {
            if (!reset.HasValue) return "暂无时间";
            TimeSpan left = reset.Value - DateTime.UtcNow.AddHours(8);
            if (left.TotalSeconds <= 0) return "已到重置时间";
            if (left.TotalDays >= 1) return ((int)left.TotalDays).ToString() + " 天 " + left.Hours.ToString() + " 小时后";
            if (left.TotalHours >= 1) return ((int)left.TotalHours).ToString() + " 小时 " + left.Minutes.ToString() + " 分后";
            return Math.Max(1, (int)Math.Ceiling(left.TotalMinutes)).ToString() + " 分钟后";
        }
    }

    public sealed class QuotaBadge : Form
    {
        private readonly BadgeButton badge;
        private float scale = 1;
        private bool scaleInitialized;
        public event EventHandler ActivatedByUser;

        public QuotaBadge()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = QuotaAppearance.Mint;
            Text = "Codex 额度";
            AccessibleName = "Codex 额度";
            badge = new BadgeButton();
            badge.Dock = DockStyle.Fill;
            badge.Click += delegate
            {
                EventHandler handler = ActivatedByUser;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            Controls.Add(badge);
            SetScale(1);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                return parameters;
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0021) // WM_MOUSEACTIVATE: clicks work without switching away from Codex.
            {
                message.Result = new IntPtr(3); // MA_NOACTIVATE
                return;
            }
            base.WndProc(ref message);
        }

        public void SetState(QuotaState state)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<QuotaState>(SetState), state); return; }
            badge.SetState(state);
        }

        public void SetAppearance(UserPreferences prefs) { badge.SetAppearance(prefs ?? new UserPreferences()); }

        public void SetScale(float factor)
        {
            if (Single.IsNaN(factor) || Single.IsInfinity(factor)) factor = 1;
            factor = Math.Max(0.75f, Math.Min(4, factor));
            if (scaleInitialized && Math.Abs(scale - factor) < 0.001f) return;
            scale = factor;
            scaleInitialized = true;
            Size = new Size((int)Math.Round(40 * scale), (int)Math.Round(40 * scale));
            badge.SetScale(scale);
            Region previous = Region;
            using (GraphicsPath path = QuotaAppearance.RoundRectangle(new RectangleF(0, 0, Width, Height), 9 * scale))
                Region = new Region(path);
            if (previous != null) previous.Dispose();
        }

        private sealed class BadgeButton : Button
        {
            private float scale = 1;
            private bool stale;
            private bool hover;
            private string percentage = "—";
            private Color accent = QuotaAppearance.Green;
            private bool english;
            private Font labelFont;
            private Font valueFont;

            internal BadgeButton()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                Cursor = Cursors.Hand;
                AccessibleRole = AccessibleRole.PushButton;
                AccessibleName = "Codex 额度，打开详情";
                AccessibleDescription = "点击查看剩余额度和重置时间";
                TabStop = true;
                SetScale(1);
            }

            internal void SetScale(float factor)
            {
                scale = factor;
                if (labelFont != null) labelFont.Dispose();
                if (valueFont != null) valueFont.Dispose();
                labelFont = new Font("Segoe UI", 10 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
                valueFont = new Font("Segoe UI", 13 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
                Invalidate();
            }

            internal void SetAppearance(UserPreferences prefs) { accent = prefs.AccentColor; english = prefs.Language == "en"; UpdateAccessibleName(); Invalidate(); }

            internal void SetState(QuotaState state)
            {
                QuotaWindow primary = state == null ? null : state.Primary;
                percentage = primary == null ? "—" : QuotaAppearance.Percentage(primary.Remaining);
                stale = state == null || state.Stale || !state.Available;
                UpdateAccessibleName();
                Invalidate();
            }

            private void UpdateAccessibleName()
            {
                AccessibleName = english ? "Codex quota, " + percentage + " remaining" + (stale ? ", cached or unavailable" : "") + ", open details" : "Codex 额度，剩余 " + percentage + (stale ? "，缓存或暂不可用" : "") + "，打开详情";
                AccessibleDescription = english ? "Click to view quota and reset times" : "点击查看剩余额度和重置时间";
                Text = AccessibleName;
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Color background = stale ? Color.FromArgb(237, 240, 242) : QuotaAppearance.Blend(accent, Color.White, hover ? .65f : .79f);
                Color foreground = stale ? QuotaAppearance.Muted : QuotaAppearance.Darken(accent, .48f);
                e.Graphics.Clear(background);
                TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
                TextRenderer.DrawText(e.Graphics, english ? "Left" : "剩余", labelFont,
                    new Rectangle(0, (int)(4 * scale), Width, (int)(14 * scale)), foreground, flags);
                TextRenderer.DrawText(e.Graphics, percentage, valueFont,
                    new Rectangle(0, (int)(18 * scale), Width, (int)(18 * scale)), foreground, flags);
                if (stale)
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (Brush dot = new SolidBrush(QuotaAppearance.Muted))
                        e.Graphics.FillEllipse(dot, Width - 7 * scale, 4 * scale, 3 * scale, 3 * scale);
                }
                if (Focused && ShowFocusCues)
                    ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(3, 3, Width - 6, Height - 6), foreground, background);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    if (labelFont != null) labelFont.Dispose();
                    if (valueFont != null) valueFont.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }

    internal sealed class RoundedButton : Button
    {
        internal Color Accent = QuotaAppearance.Green;
        internal bool Selected;
        private bool hover;
        internal RoundedButton()
        {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand; BackColor = Color.White;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.Clear(BackColor);
            Color fill = Selected ? QuotaAppearance.Blend(Accent, Color.White, .84f) : hover ? Color.FromArgb(244, 245, 247) : Color.White;
            using (Brush brush = new SolidBrush(fill))
            using (GraphicsPath path = QuotaAppearance.RoundRectangle(new RectangleF(0, 0, Width - 1, Height - 1), 7)) e.Graphics.FillPath(brush, path);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Selected ? QuotaAppearance.Darken(Accent, .42f) : QuotaAppearance.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3));
        }
    }

    public sealed class QuotaPopup : Form
    {
        private readonly float scale;
        private readonly Font regular, small, valueFont;
        private readonly Label title, footer, rangeTitle, chartTitle, heatTitle, recordTitle, scope;
        private readonly RoundedButton appearance, refresh, close, back, day, week, month, tokens, time, chinese, englishButton;
        private readonly Panel mainPage, settingsPage;
        private readonly Label settingsLanguage, settingsAccent, settingsCustom, settingsHint;
        private readonly AccentPalette palette;
        private readonly QuotaSummaryView quota;
        private readonly MetricCards summary, records;
        private readonly UsageBarChart bars;
        private readonly TokenHeatmap heatmap;
        private readonly Timer animation;
        private readonly Stopwatch animationClock = new Stopwatch();
        private Point targetLocation, requestedPoint;
        private QuotaState state;
        private UserPreferences preferences = new UserPreferences();
        private string period = "day";
        private bool timeMetric, settings;
        public event EventHandler RefreshRequested;
        public event EventHandler PreferencesChanged;
        public UserPreferences Preferences { get { return preferences; } }
        private bool English { get { return preferences.Language == "en"; } }
        private int Px(int value) { return (int)Math.Round(value * scale); }
        private string L(string zh, string en) { return English ? en : zh; }

        public QuotaPopup()
        {
            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero)) scale = Math.Max(1, graphics.DpiX / 96f);
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false; TopMost = true; KeyPreview = true; DoubleBuffered = true;
            BackColor = Color.White; ForeColor = QuotaAppearance.Text; Text = "Codex";
            regular = new Font("Microsoft YaHei UI", 13 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            small = new Font("Microsoft YaHei UI", 11 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            valueFont = new Font("Microsoft YaHei UI", 20 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            Font = regular;
            title = LabelFor("Codex", regular, QuotaAppearance.Text); Controls.Add(title);
            appearance = ButtonFor("", delegate { ShowSettings(true); }); Controls.Add(appearance);
            refresh = ButtonFor("", delegate { EventHandler handler = RefreshRequested; if (handler != null) handler(this, EventArgs.Empty); }); Controls.Add(refresh);
            close = ButtonFor("×", delegate { Hide(); }); Controls.Add(close);
            back = ButtonFor("‹", delegate { ShowSettings(false); }); Controls.Add(back);
            mainPage = new Panel(); mainPage.AutoScroll = true; mainPage.BackColor = Color.White; Controls.Add(mainPage);
            settingsPage = new Panel(); settingsPage.AutoScroll = true; settingsPage.BackColor = Color.White; settingsPage.Visible = false; Controls.Add(settingsPage);
            footer = LabelFor("", small, QuotaAppearance.Muted); Controls.Add(footer);
            quota = new QuotaSummaryView(scale, regular, small, valueFont); mainPage.Controls.Add(quota);
            rangeTitle = LabelFor("", regular, QuotaAppearance.Text); mainPage.Controls.Add(rangeTitle);
            summary = new MetricCards(scale, small, valueFont, 3); mainPage.Controls.Add(summary);
            chartTitle = LabelFor("", regular, QuotaAppearance.Text); mainPage.Controls.Add(chartTitle);
            day = ButtonFor("", delegate { SetPeriod("day"); }); mainPage.Controls.Add(day);
            week = ButtonFor("", delegate { SetPeriod("week"); }); mainPage.Controls.Add(week);
            month = ButtonFor("", delegate { SetPeriod("month"); }); mainPage.Controls.Add(month);
            tokens = ButtonFor("Tokens", delegate { timeMetric = false; UpdateData(); }); mainPage.Controls.Add(tokens);
            time = ButtonFor("", delegate { timeMetric = true; UpdateData(); }); mainPage.Controls.Add(time);
            bars = new UsageBarChart(scale); mainPage.Controls.Add(bars);
            heatTitle = LabelFor("", regular, QuotaAppearance.Text); mainPage.Controls.Add(heatTitle);
            heatmap = new TokenHeatmap(scale); mainPage.Controls.Add(heatmap);
            recordTitle = LabelFor("", regular, QuotaAppearance.Text); mainPage.Controls.Add(recordTitle);
            records = new MetricCards(scale, small, valueFont, 2); mainPage.Controls.Add(records);
            scope = LabelFor("", small, QuotaAppearance.Muted); scope.TextAlign = ContentAlignment.TopLeft; mainPage.Controls.Add(scope);
            settingsLanguage = LabelFor("", regular, QuotaAppearance.Text); settingsPage.Controls.Add(settingsLanguage);
            chinese = ButtonFor("中文", delegate { preferences.Language = "zh"; NotifyPreferences(); }); settingsPage.Controls.Add(chinese);
            englishButton = ButtonFor("English", delegate { preferences.Language = "en"; NotifyPreferences(); }); settingsPage.Controls.Add(englishButton);
            settingsAccent = LabelFor("", regular, QuotaAppearance.Text); settingsPage.Controls.Add(settingsAccent);
            settingsCustom = LabelFor("", small, QuotaAppearance.Muted); settingsPage.Controls.Add(settingsCustom);
            palette = new AccentPalette(scale, regular);
            palette.SelectedColorChanged += delegate
            {
                Color color = palette.SelectedColor;
                preferences.AccentHtml = "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
                NotifyPreferences();
            };
            settingsPage.Controls.Add(palette);
            settingsHint = LabelFor("", small, QuotaAppearance.Muted); settingsHint.TextAlign = ContentAlignment.TopLeft; settingsPage.Controls.Add(settingsHint);
            animation = new Timer(); animation.Interval = 15; animation.Tick += Animate;
            VisibleChanged += delegate { if (!Visible) { animation.Stop(); animationClock.Reset(); Opacity = 1; } };
            Deactivate += delegate { if (Visible) Hide(); };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { if (settings) ShowSettings(false); else Hide(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            ApplyPreferences(new UserPreferences());
        }
        private Label LabelFor(string text, Font font, Color color)
        {
            Label label = new Label(); label.Text = text; label.Font = font; label.ForeColor = color; label.BackColor = Color.Transparent;
            label.AutoEllipsis = true; label.UseMnemonic = false; label.TextAlign = ContentAlignment.MiddleLeft; return label;
        }
        private RoundedButton ButtonFor(string text, EventHandler handler)
        {
            RoundedButton button = new RoundedButton(); button.Text = text; button.Font = regular; button.Click += handler; return button;
        }
        protected override CreateParams CreateParams
        {
            get { CreateParams p = base.CreateParams; p.ExStyle |= 0x00000080; p.ClassStyle |= 0x00020000; return p; }
        }
        public void ApplyPreferences(UserPreferences prefs)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<UserPreferences>(ApplyPreferences), prefs); return; }
            preferences = new UserPreferences { Language = prefs != null && prefs.Language == "en" ? "en" : "zh", AccentHtml = prefs == null ? "#DA91AA" : prefs.AccentHtml };
            palette.SetLanguage(English); palette.SetColor(preferences.AccentColor);
            foreach (RoundedButton button in new RoundedButton[] { appearance, refresh, close, back, day, week, month, tokens, time, chinese, englishButton }) { button.Accent = preferences.AccentColor; button.Invalidate(); }
            UpdateData();
        }
        private void NotifyPreferences()
        {
            ApplyPreferences(preferences);
            EventHandler handler = PreferencesChanged; if (handler != null) handler(this, EventArgs.Empty);
        }
        public void SetState(QuotaState next)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<QuotaState>(SetState), next); return; }
            state = next; UpdateData();
        }
        private void SetPeriod(string next) { period = next; UpdateData(); }
        private void UpdateData()
        {
            DateTime today = DateTime.UtcNow.AddHours(8).Date;
            List<ChartBucket> buckets = UsageAnalytics.BuildBuckets(state, period, today);
            UsageSummary totals = UsageAnalytics.Summarize(buckets);
            title.Text = settings ? L("外观", "Appearance") : L("Codex 用量", "Codex usage");
            AccessibleName = title.Text;
            appearance.Text = L("外观", "Style"); refresh.Text = L("刷新", "Refresh");
            close.AccessibleName = L("关闭详情", "Close details"); back.AccessibleName = L("返回统计", "Back to usage");
            day.Text = L("日", "Day"); week.Text = L("周", "Week"); month.Text = L("月", "Month"); time.Text = L("时长", "Time");
            day.Selected = period == "day"; week.Selected = period == "week"; month.Selected = period == "month";
            tokens.Selected = !timeMetric; time.Selected = timeMetric;
            foreach (RoundedButton button in new RoundedButton[] { day, week, month, tokens, time }) button.Invalidate();
            rangeTitle.Text = period == "day" ? L("最近 7 天", "Last 7 days") : period == "week" ? L("最近 4 周", "Last 4 weeks") : L("最近 6 个月", "Last 6 months");
            summary.SetItems(new string[] { "Tokens" + (totals.TokenPartial ? " · " + L("部分", "partial") : ""), L("AI 工作时长", "AI work time") + (totals.WorkPartial ? " *" : ""), L("完成任务", "Tasks done") + (totals.WorkPartial ? " *" : "") },
                new string[] { UsageText.Number(totals.Tokens, true), UsageText.Duration(totals.WorkSeconds, English, false), totals.Tasks.HasValue ? totals.Tasks.Value.ToString("N0") : "—" });
            quota.SetData(state, preferences.AccentColor, English);
            chartTitle.Text = L("用量趋势", "Usage trend");
            bars.SetData(buckets, timeMetric, preferences.AccentColor, English);
            heatTitle.Text = L("Token 热力图 · 最近 365 天", "Token activity · last 365 days");
            heatmap.SetData(UsageAnalytics.BuildDaily(state, today.AddDays(-364), today), preferences.AccentColor, English);
            recordTitle.Text = L("累计与记录", "Totals and records");
            bool hasActivity = state != null && state.Activity != null && (state.Activity.Status == "ready" || state.Activity.DailyBuckets.Count > 0);
            string activityMark = hasActivity && state.Activity.Status != "ready" ? " *" : "";
            records.SetItems(new string[] { L("累计 Tokens", "All-time Tokens"), L("单日峰值", "Peak daily Tokens"), L("本机工作时长", "Recorded work time") + activityMark, L("本机完成任务", "Recorded tasks") + activityMark, L("最长单次工作", "Longest AI turn"), L("连续天数 · 当前 / 最长", "Streak days · current / best") },
                new string[] { UsageText.Number(state == null ? null : state.LifetimeTokens, true), UsageText.Number(state == null ? null : state.PeakDailyTokens, true), UsageText.Duration(hasActivity ? (double?)state.Activity.TotalWorkSeconds : null, English, false), hasActivity ? state.Activity.CompletedTasks.ToString("N0") : "—", UsageText.Duration(state == null ? null : state.LongestRunningTurnSec, English, false), (state == null || !state.CurrentStreakDays.HasValue ? "—" : state.CurrentStreakDays.Value.ToString()) + " / " + (state == null || !state.LongestStreakDays.HasValue ? "—" : state.LongestStreakDays.Value.ToString()) });
            scope.Text = ScopeText(totals);
            footer.Text = FooterText();
            settingsLanguage.Text = L("语言", "Language"); settingsAccent.Text = L("主题色", "Accent color");
            settingsCustom.Text = L("预设色与自定义调色", "Presets and custom palette");
            settingsHint.Text = L("颜色同步应用于头像上方徽标、图表和系统托盘。\n设置自动保存。", "Your accent colors the avatar badge, charts and tray.\nChanges are saved automatically.");
            chinese.Selected = !English; englishButton.Selected = English; chinese.Invalidate(); englishButton.Invalidate();
            LayoutPage(); Invalidate();
        }
        private string ScopeText(UsageSummary totals)
        {
            string text = L("Tokens：账户统计。工作时长与完成任务：本机采集。", "Tokens: account usage. Work time and tasks: this PC.");
            WorkActivity activity = state == null ? null : state.Activity;
            if (activity == null || activity.Status == "unavailable") text += "\n" + L("本机工作记录暂不可用；缺失数据以 — 显示。", "Local activity unavailable; missing data is shown as —.");
            else if (!String.IsNullOrWhiteSpace(activity.CoveredFrom)) text += "\n" + L("本机记录覆盖：", "Local coverage: ") + activity.CoveredFrom + " – " + activity.CoveredTo;
            if (totals.TokenPartial || totals.WorkPartial) text += "\n" + L("* 部分时段缺少记录；悬停图表可查看精确明细。", "* Partial coverage. Hover a chart for exact details.");
            return text;
        }
        private string FooterText()
        {
            if (state == null) return L("正在更新…", "Updating…");
            DateTimeOffset updated;
            string stamp = DateTimeOffset.TryParse(state.UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out updated) ? updated.ToOffset(TimeSpan.FromHours(8)).ToString("HH:mm:ss") : "—";
            return (state.Stale || !state.Available ? L("缓存数据 · ", "Cached · ") : "") + stamp + L(" 更新 · 北京时间", " updated · UTC+8");
        }
        private void LayoutPage()
        {
            Point mainScroll = new Point(-mainPage.AutoScrollPosition.X, -mainPage.AutoScrollPosition.Y);
            Point settingsScroll = new Point(-settingsPage.AutoScrollPosition.X, -settingsPage.AutoScrollPosition.Y);
            mainPage.SuspendLayout(); settingsPage.SuspendLayout();
            mainPage.AutoScrollPosition = Point.Empty; settingsPage.AutoScrollPosition = Point.Empty;
            Rectangle work = Screen.FromPoint(Visible ? targetLocation : requestedPoint).WorkingArea;
            Size = new Size(Math.Min(Px(430), work.Width - Px(16)), Math.Min(Px(620), work.Height - Px(16)));
            title.SetBounds(Px(settings ? 48 : 18), Px(13), Px(180), Px(30));
            back.SetBounds(Px(12), Px(12), Px(28), Px(32)); back.Visible = settings;
            appearance.SetBounds(Width - Px(154), Px(12), Px(48), Px(32)); appearance.Visible = !settings;
            refresh.SetBounds(Width - Px(103), Px(12), Px(64), Px(32)); refresh.Visible = !settings;
            close.SetBounds(Width - Px(36), Px(12), Px(24), Px(32));
            mainPage.SetBounds(Px(18), Px(57), Width - Px(36), Height - Px(96)); settingsPage.Bounds = mainPage.Bounds;
            footer.SetBounds(Px(18), Height - Px(30), Width - Px(36), Px(22));
            int width = mainPage.Width - SystemInformation.VerticalScrollBarWidth - Px(2);
            int y = 0;
            quota.SetBounds(0, y, width, Px(quota.DesiredHeight)); y += quota.Height + Px(20);
            rangeTitle.SetBounds(0, y, width, Px(22)); y += Px(29);
            summary.SetBounds(0, y, width, Px(75)); y += Px(96);
            chartTitle.SetBounds(0, y, Px(136), Px(26));
            int segmentWidth = Px(42);
            day.SetBounds(width - segmentWidth * 3, y, segmentWidth, Px(27)); week.SetBounds(width - segmentWidth * 2, y, segmentWidth, Px(27)); month.SetBounds(width - segmentWidth, y, segmentWidth, Px(27));
            y += Px(36);
            tokens.SetBounds(0, y, Px(62), Px(26)); time.SetBounds(Px(68), y, Px(56), Px(26)); y += Px(33);
            bars.SetBounds(0, y, width, Px(151)); y += Px(176);
            heatTitle.SetBounds(0, y, width, Px(24)); y += Px(31);
            heatmap.SetBounds(0, y, width, Px(100)); y += Px(119);
            recordTitle.SetBounds(0, y, width, Px(24)); y += Px(31);
            records.SetBounds(0, y, width, Px(220)); y += Px(232);
            int scopeHeight = TextRenderer.MeasureText(scope.Text, small, new Size(width, 0), TextFormatFlags.WordBreak).Height + Px(9);
            scope.SetBounds(0, y, width, Math.Max(Px(68), scopeHeight)); y += scope.Height + Px(8);
            mainPage.AutoScrollMinSize = new Size(0, y);
            settingsLanguage.SetBounds(0, 0, width, Px(24));
            chinese.SetBounds(0, Px(33), Px(82), Px(34)); englishButton.SetBounds(Px(92), Px(33), Px(90), Px(34));
            settingsAccent.SetBounds(0, Px(96), width, Px(25)); settingsCustom.SetBounds(0, Px(124), width, Px(23));
            palette.SetBounds(0, Px(158), width, Px(296));
            settingsHint.SetBounds(0, Px(474), width, Px(50)); settingsPage.AutoScrollMinSize = new Size(0, Px(535));
            mainPage.ResumeLayout(); settingsPage.ResumeLayout();
            mainPage.AutoScrollPosition = mainScroll; settingsPage.AutoScrollPosition = settingsScroll;
            Region old = Region;
            using (GraphicsPath path = QuotaAppearance.RoundRectangle(new RectangleF(0, 0, Width, Height), Px(12))) Region = new Region(path);
            if (old != null) old.Dispose();
            if (Visible && !animation.Enabled) { targetLocation = ClampPoint(requestedPoint, work); Location = targetLocation; }
        }
        private Point ClampPoint(Point point, Rectangle work)
        {
            return new Point(Math.Max(work.Left + Px(8), Math.Min(point.X, work.Right - Width - Px(8))), Math.Max(work.Top + Px(8), Math.Min(point.Y, work.Bottom - Height - Px(8))));
        }
        private void ShowSettings(bool show)
        {
            settings = show; mainPage.Visible = !show; settingsPage.Visible = show;
            UpdateData(); if (show) chinese.Select(); else appearance.Select();
        }
        public void ShowAt(Point point)
        {
            requestedPoint = point; LayoutPage(); targetLocation = ClampPoint(point, Screen.FromPoint(point).WorkingArea);
            bool animate = !Visible && SystemInformation.IsMenuAnimationEnabled;
            animation.Stop(); Location = animate ? new Point(targetLocation.X, targetLocation.Y + Px(8)) : targetLocation; Opacity = animate ? 0 : 1;
            if (!Visible) Show(); Activate();
            if (animate) { animationClock.Restart(); animation.Start(); }
            if (settings) chinese.Select(); else refresh.Select();
        }
        private void Animate(object sender, EventArgs e)
        {
            double progress = Math.Min(1, animationClock.Elapsed.TotalMilliseconds / 180);
            double eased = 1 - Math.Pow(1 - progress, 3);
            Opacity = eased; Location = new Point(targetLocation.X, targetLocation.Y + (int)Math.Round(Px(8) * (1 - eased)));
            if (progress >= 1) { animation.Stop(); animationClock.Reset(); Location = targetLocation; Opacity = 1; }
        }
        protected override void OnFormClosing(FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } base.OnFormClosing(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen border = new Pen(QuotaAppearance.Line))
            using (GraphicsPath path = QuotaAppearance.RoundRectangle(new RectangleF(.5f, .5f, Width - 1, Height - 1), Px(12)))
            { e.Graphics.DrawPath(border, path); e.Graphics.DrawLine(border, Px(18), Height - Px(38), Width - Px(18), Height - Px(38)); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) animation.Dispose(); base.Dispose(disposing);
            if (disposing) { regular.Dispose(); small.Dispose(); valueFont.Dispose(); }
        }
    }

    internal sealed class MetricCards : Control
    {
        private readonly float scale;
        private readonly Font labelFont, valueFont;
        private readonly int columns;
        private string[] names = new string[0], values = new string[0];
        internal MetricCards(float factor, Font small, Font large, int count)
        {
            scale = factor; labelFont = small; valueFont = large; columns = count; BackColor = Color.White;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        internal void SetItems(string[] labels, string[] numbers) { names = labels; values = numbers; AccessibleName = String.Join("; ", labels) + ": " + String.Join("; ", numbers); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int rows = Math.Max(1, (int)Math.Ceiling(names.Length / (double)columns));
            float gap = 8 * scale, width = (Width - gap * (columns - 1)) / columns, height = (Height - gap * (rows - 1)) / rows;
            for (int i = 0; i < names.Length; i++)
            {
                RectangleF card = new RectangleF((i % columns) * (width + gap), (i / columns) * (height + gap), width - 1, height - 1);
                using (GraphicsPath path = QuotaAppearance.RoundRectangle(card, 9 * scale))
                using (Brush fill = new SolidBrush(Color.FromArgb(250, 250, 251)))
                using (Pen border = new Pen(QuotaAppearance.Line)) { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path); }
                Rectangle label = Rectangle.Round(new RectangleF(card.X + 11 * scale, card.Y + 9 * scale, width - 20 * scale, 18 * scale));
                Rectangle value = Rectangle.Round(new RectangleF(card.X + 11 * scale, card.Y + 30 * scale, width - 20 * scale, 29 * scale));
                TextRenderer.DrawText(e.Graphics, names[i], labelFont, label, QuotaAppearance.Muted, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(e.Graphics, i < values.Length ? values[i] : "—", valueFont, value, QuotaAppearance.Text, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }
    }

    internal sealed class QuotaSummaryView : Control
    {
        private readonly float scale;
        private readonly Font regular, small, large;
        private readonly ToolTip tooltip = new ToolTip();
        private readonly List<RectangleF> areas = new List<RectangleF>();
        private QuotaState state;
        private Color accent;
        private bool english;
        private int hovered = -1;
        internal int DesiredHeight { get; private set; }
        internal QuotaSummaryView(float factor, Font normal, Font little, Font value)
        {
            scale = factor; regular = normal; small = little; large = value;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White; DesiredHeight = 112; tooltip.InitialDelay = 100; tooltip.AutoPopDelay = 15000;
        }
        internal void SetData(QuotaState next, Color color, bool useEnglish)
        {
            state = next; accent = color; english = useEnglish;
            int count = state == null || state.Windows == null ? 0 : state.Windows.Count;
            DesiredHeight = count == 0 ? 96 : 28 + (int)Math.Ceiling(count / 2d) * 94;
            if (state != null && (!String.IsNullOrWhiteSpace(state.Credits) || state.ResetCredits.HasValue)) DesiredHeight += 25;
            AccessibleName = english ? "Account quota" : "账户额度"; areas.Clear(); hovered = -1; tooltip.SetToolTip(this, ""); Invalidate();
        }
        private string WindowName(QuotaWindow window)
        {
            string label = window.Label ?? ""; if (!english) return label;
            if (label.IndexOf("周", StringComparison.Ordinal) >= 0) return "Weekly";
            if (label.IndexOf("小时", StringComparison.Ordinal) >= 0) return label.Replace("小时", "hours");
            return String.IsNullOrWhiteSpace(label) ? "Quota" : label;
        }
        private string ResetText(DateTime? reset)
        {
            if (!reset.HasValue) return english ? "Reset time unknown" : "暂无重置时间";
            TimeSpan left = reset.Value - DateTime.UtcNow.AddHours(8);
            if (left.TotalSeconds <= 0) return english ? "Reset due" : "等待重置";
            if (english) return "Resets in " + UsageText.Duration(left.TotalSeconds, true, false);
            return QuotaAppearance.ResetCountdown(reset);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; areas.Clear();
            TextRenderer.DrawText(e.Graphics, english ? "Account quota" : "账户额度", regular, new Rectangle(0, 0, Width, (int)(22 * scale)), QuotaAppearance.Text, TextFormatFlags.NoPadding);
            int count = state == null || state.Windows == null ? 0 : state.Windows.Count;
            if (count == 0)
            {
                string text = state != null && !String.IsNullOrWhiteSpace(state.Error) ? state.Error : english ? "Quota is not available yet." : "暂时没有额度数据。";
                TextRenderer.DrawText(e.Graphics, text, small, new Rectangle(0, (int)(34 * scale), Width, (int)(55 * scale)), QuotaAppearance.Muted, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding); return;
            }
            int columns = Math.Min(2, count);
            float gap = 10 * scale, cardWidth = (Width - gap * (columns - 1)) / columns;
            for (int i = 0; i < count; i++)
            {
                QuotaWindow window = state.Windows[i]; if (window == null) continue;
                RectangleF card = new RectangleF((i % columns) * (cardWidth + gap), 29 * scale + (i / columns) * 94 * scale, cardWidth, 85 * scale); areas.Add(card);
                using (GraphicsPath path = QuotaAppearance.RoundRectangle(card, 9 * scale))
                using (Brush fill = new SolidBrush(QuotaAppearance.Blend(accent, Color.White, .93f))) e.Graphics.FillPath(fill, path);
                Draw(e.Graphics, WindowName(window) + (english ? " remaining" : "剩余"), small, new RectangleF(card.X + 11 * scale, card.Y + 8 * scale, cardWidth - 22 * scale, 16 * scale), QuotaAppearance.Muted);
                Draw(e.Graphics, QuotaAppearance.Percentage(window.Remaining), large, new RectangleF(card.X + 11 * scale, card.Y + 27 * scale, cardWidth - 22 * scale, 27 * scale), state.Stale ? QuotaAppearance.Muted : QuotaAppearance.Text);
                float meterY = card.Y + 60 * scale;
                using (Pen pen = new Pen(QuotaAppearance.Blend(accent, Color.White, .75f), 3 * scale)) e.Graphics.DrawLine(pen, card.X + 12 * scale, meterY, card.Right - 12 * scale, meterY);
                if (window.Remaining.HasValue && !Double.IsNaN(window.Remaining.Value) && !Double.IsInfinity(window.Remaining.Value))
                    using (Pen pen = new Pen(state.Stale ? QuotaAppearance.Muted : accent, 3 * scale)) e.Graphics.DrawLine(pen, card.X + 12 * scale, meterY, card.X + 12 * scale + (cardWidth - 24 * scale) * (float)Math.Max(0, Math.Min(100, window.Remaining.Value)) / 100, meterY);
                Draw(e.Graphics, ResetText(QuotaAppearance.ResetTime(window.ResetAt)), small, new RectangleF(card.X + 11 * scale, card.Y + 67 * scale, cardWidth - 22 * scale, 16 * scale), QuotaAppearance.Muted);
            }
            string extras = "";
            if (!String.IsNullOrWhiteSpace(state.Credits)) extras = (english ? "Credits " : "积分 ") + state.Credits;
            if (state.ResetCredits.HasValue) extras += (extras.Length > 0 ? "  ·  " : "") + (english ? "Resets " : "重置次数 ") + state.ResetCredits.Value;
            if (extras.Length > 0) Draw(e.Graphics, extras, small, new RectangleF(0, Height - 21 * scale, Width, 20 * scale), QuotaAppearance.Muted);
        }
        private void Draw(Graphics g, string text, Font font, RectangleF rect, Color color) { TextRenderer.DrawText(g, text, font, Rectangle.Round(rect), color, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); int next = -1;
            for (int i = 0; i < areas.Count; i++) if (areas[i].Contains(e.Location)) { next = i; break; }
            if (state == null || state.Windows == null || next >= state.Windows.Count) next = -1;
            if (next == hovered) return; hovered = next;
            if (next < 0 || state == null) { tooltip.SetToolTip(this, ""); return; }
            QuotaWindow window = state.Windows[next]; DateTime? reset = QuotaAppearance.ResetTime(window.ResetAt);
            double? used = window.Used.HasValue ? window.Used : window.Remaining.HasValue ? 100 - window.Remaining : null;
            tooltip.SetToolTip(this, WindowName(window) + "\n" + (english ? "Remaining " : "剩余 ") + QuotaAppearance.Percentage(window.Remaining) + "\n" + (english ? "Used " : "已使用 ") + QuotaAppearance.Percentage(used) + "\n" + (english ? "Reset " : "重置 ") + (reset.HasValue ? reset.Value.ToString("yyyy-MM-dd HH:mm") + " UTC+8" : "—"));
        }
        protected override void Dispose(bool disposing) { if (disposing) tooltip.Dispose(); base.Dispose(disposing); }
    }
}
