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
        private IntPtr attachedParent;
        private Point attachedPosition;
        public event EventHandler ActivatedByUser;

        public IntPtr AttachedParent
        {
            get { return !IsDisposed && IsHandleCreated && BadgeAttachment.IsWindow(attachedParent) && BadgeAttachment.IsChildOf(Handle, attachedParent) ? attachedParent : IntPtr.Zero; }
        }

        public Rectangle ScreenBounds
        {
            get { return !IsDisposed && IsHandleCreated ? BadgeAttachment.ScreenBounds(Handle) : Rectangle.Empty; }
        }

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
                if (attachedParent != IntPtr.Zero)
                {
                    parameters.Parent = attachedParent;
                    parameters.Style = (parameters.Style & ~unchecked((int)0x80000000)) | 0x40000000; // WS_CHILD, never WS_POPUP
                    parameters.ExStyle &= ~(0x00000008 | 0x00040000); // no WS_EX_TOPMOST / WS_EX_APPWINDOW on a child
                    parameters.ExStyle |= 0x00080000; // WS_EX_LAYERED: own surface over the GPU-rendered host
                    parameters.X = attachedPosition.X;
                    parameters.Y = attachedPosition.Y;
                }
                return parameters;
            }
        }

        public bool TryAttach(IntPtr parent, Point clientPosition)
        {
            if (IsDisposed || Disposing || parent == IntPtr.Zero || !BadgeAttachment.IsWindow(parent)) return false;
            BadgeAttachment.DpiScope scope;
            if (!BadgeAttachment.TryEnterDpiContext(parent, out scope)) return false;
            using (scope)
            {
                try
                {
                    bool rebuild = attachedParent != parent || !IsHandleCreated;
                    if (!rebuild) rebuild = !BadgeAttachment.IsChildOf(Handle, parent) || !BadgeAttachment.MatchesDpi(Handle, scope.Context);
                    if (rebuild)
                    {
                        // Never cross-process SetParent an HWND created under a different DPI mode.
                        Hide();
                        if (IsHandleCreated) DestroyHandle();
                        attachedParent = parent;
                        attachedPosition = clientPosition;
                        TopMost = false;
                        TopLevel = false;
                    }
                    attachedPosition = clientPosition;
                    SetScale(BadgeAttachment.ScaleForWindow(parent));
                    IntPtr window = Handle;
                    // Force the native button to be created in the same target DPI scope too.
                    IntPtr buttonWindow = badge.Handle;
                    if (!BadgeAttachment.InitializeSurface(window)) { Detach(); return false; }
                    if (!BadgeAttachment.IsChildOf(window, parent) || !BadgeAttachment.MatchesDpi(window, scope.Context) || !BadgeAttachment.MatchesDpi(buttonWindow, scope.Context))
                    {
                        Detach();
                        return false;
                    }
                    return BadgeAttachment.PositionChild(window, clientPosition, Size);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    Detach();
                    return false;
                }
            }
        }

        public void Detach()
        {
            if (IsDisposed || Disposing) return;
            Hide();
            if (IsHandleCreated) DestroyHandle();
            attachedParent = IntPtr.Zero;
            attachedPosition = Point.Empty;
            TopLevel = true;
            TopMost = true;
        }

        public void EnsureFront()
        {
            if (AttachedParent != IntPtr.Zero) BadgeAttachment.EnsureAboveSiblings(Handle);
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
                labelFont = new Font("Microsoft YaHei UI", 10 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
                valueFont = new Font("Microsoft YaHei UI", 13 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
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
                using (var glass = new LinearGradientBrush(ClientRectangle, QuotaAppearance.Blend(background, Color.White, .64f), background, 100f)) e.Graphics.FillRectangle(glass, ClientRectangle);
                using (var shine = new Pen(Color.FromArgb(190, Color.White))) e.Graphics.DrawLine(shine, 7 * scale, 1 * scale, Width - 7 * scale, 1 * scale);
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
            float radius = Math.Min(14, Height * .36f);
            Color fill = Selected ? QuotaAppearance.Blend(Accent, Color.White, hover ? .65f : .78f) : hover ? Color.White : Color.FromArgb(249, 248, 252);
            using (Brush shadow = new SolidBrush(Color.FromArgb(16, 65, 45, 65)))
            using (GraphicsPath path = QuotaAppearance.RoundRectangle(new RectangleF(1, 3, Width - 2, Height - 4), radius)) e.Graphics.FillPath(shadow, path);
            using (Brush brush = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)), Color.White, fill, 90f))
            using (GraphicsPath path = QuotaAppearance.RoundRectangle(new RectangleF(.5f, .5f, Width - 2, Height - 3), radius))
            using (Pen edge = new Pen(Color.FromArgb(220, Color.White))) { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(edge, path); }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, !Enabled ? Color.FromArgb(158, 153, 162) : Selected ? QuotaAppearance.Darken(Accent, .55f) : QuotaAppearance.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3));
        }
    }

    public sealed class QuotaPopup : Form
    {
        private readonly float scale;
        private readonly Font regular, small, valueFont;
        private readonly Label title, footer, rangeTitle, chartTitle, heatTitle, recordTitle, scope, homeDate, homeTasks, homeNote, heatRange;
        private readonly RoundedButton appearance, refresh, close, back, day, week, month, tokens, time, chinese, englishButton, homeTab, historyTab, updateEntry, homeDay, homeWeek, homeMonth, homeAll, heatPrevious, heatNext;
        private readonly Panel mainPage, homePage, settingsPage, updatePage;
        private readonly Label settingsLanguage, settingsAccent, settingsCustom, settingsHint;
        private readonly AccentPalette palette;
        private readonly QuotaSummaryView quota;
        private readonly MetricCards overview, summary, records;
        private readonly UsageBarChart bars;
        private readonly TokenHeatmap heatmap;
        private readonly GlassUpdateView updateView;
        private readonly Timer animation;
        private readonly Stopwatch animationClock = new Stopwatch();
        private Point targetLocation, requestedPoint;
        private QuotaState state;
        private UserPreferences preferences = new UserPreferences();
        private string period = "day", homePeriod = "day", page = "home", returnPage = "home";
        private int heatPage = 3, motionDistance;
        private bool timeMetric, pageMotion, layingOut;
        public event EventHandler RefreshRequested;
        public event EventHandler PreferencesChanged;
        public event EventHandler OpenUpdateRequested;
        public UserPreferences Preferences { get { return preferences; } }
        public GlassUpdateView UpdateView { get { return updateView; } }
        private bool English { get { return preferences.Language == "en"; } }
        private int Px(int value) { return (int)Math.Round(value * scale); }
        private string L(string zh, string en) { return English ? en : zh; }
        private Color Surface { get { return QuotaAppearance.Blend(preferences.AccentColor, Color.FromArgb(247,247,251), .94f); } }

        public QuotaPopup()
        {
            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero)) scale = Math.Max(1, graphics.DpiX / 96f);
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false; TopMost = true; KeyPreview = true; DoubleBuffered = true;
            BackColor = Color.FromArgb(246,243,249); ForeColor = QuotaAppearance.Text; Text = "Codex";
            regular = new Font("Microsoft YaHei UI", 13 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            small = new Font("Microsoft YaHei UI", 11 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            valueFont = new Font("Microsoft YaHei UI", 22 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            Font = regular;
            title = LabelFor("Codex", regular, QuotaAppearance.Text); Controls.Add(title);
            appearance = ButtonFor("", delegate { Navigate("settings"); }); Controls.Add(appearance);
            refresh = ButtonFor("", delegate { EventHandler handler = RefreshRequested; if (handler != null) handler(this, EventArgs.Empty); }); Controls.Add(refresh);
            close = ButtonFor("×", delegate { Hide(); }); Controls.Add(close);
            back = ButtonFor("", delegate { Navigate(returnPage); }); Controls.Add(back);
            updateEntry = ButtonFor("", delegate { ShowUpdates(); }); updateEntry.Name = "updateEntry"; Controls.Add(updateEntry);
            homeTab = ButtonFor("", delegate { Navigate("home"); }); homeTab.Name = "homeTab"; Controls.Add(homeTab);
            historyTab = ButtonFor("", delegate { Navigate("history"); }); historyTab.Name = "historyTab"; Controls.Add(historyTab);
            homePage = PagePanel("homePage"); mainPage = PagePanel("historyPage"); settingsPage = PagePanel("settingsPage"); updatePage = PagePanel("updatePage");
            footer = LabelFor("", small, QuotaAppearance.Muted); Controls.Add(footer);
            homeDate = LabelFor("", small, QuotaAppearance.Muted); homePage.Controls.Add(homeDate);
            homeDay = ButtonFor("", delegate { SetHomePeriod("day"); }); homePage.Controls.Add(homeDay);
            homeWeek = ButtonFor("", delegate { SetHomePeriod("week"); }); homePage.Controls.Add(homeWeek);
            homeMonth = ButtonFor("", delegate { SetHomePeriod("month"); }); homePage.Controls.Add(homeMonth);
            homeAll = ButtonFor("", delegate { SetHomePeriod("all"); }); homePage.Controls.Add(homeAll);
            overview = new MetricCards(scale, small, valueFont, 2); overview.Name = "overview"; homePage.Controls.Add(overview);
            homeTasks = LabelFor("", regular, QuotaAppearance.Text); homePage.Controls.Add(homeTasks);
            quota = new QuotaSummaryView(scale, regular, small, valueFont); homePage.Controls.Add(quota);
            homeNote = LabelFor("", small, QuotaAppearance.Muted); homeNote.TextAlign = ContentAlignment.TopLeft; homePage.Controls.Add(homeNote);
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
            heatRange = LabelFor("", small, QuotaAppearance.Muted); mainPage.Controls.Add(heatRange);
            heatPrevious = ButtonFor("‹", delegate { if (heatPage > 0) { heatPage--; UpdateData(); } }); mainPage.Controls.Add(heatPrevious);
            heatNext = ButtonFor("›", delegate { if (heatPage < 3) { heatPage++; UpdateData(); } }); mainPage.Controls.Add(heatNext);
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
            palette.SelectedColorChanged += delegate {
                Color color = palette.SelectedColor;
                preferences.AccentHtml = "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2"); NotifyPreferences();
            };
            settingsPage.Controls.Add(palette);
            settingsHint = LabelFor("", small, QuotaAppearance.Muted); settingsHint.TextAlign = ContentAlignment.TopLeft; settingsPage.Controls.Add(settingsHint);
            updateView = new GlassUpdateView(preferences); updatePage.Controls.Add(updateView);
            updateView.OpenRequested += delegate { ShowUpdates(); };
            updateView.BackRequested += delegate { Navigate(returnPage); };
            updateView.StateChanged += delegate { UpdateHeader(); LayoutPage(); };
            animation = new Timer(); animation.Interval = 15; animation.Tick += Animate;
            VisibleChanged += delegate { if (!Visible) { animation.Stop(); animationClock.Reset(); Opacity = 1; } };
            Deactivate += delegate { if (Visible) Hide(); };
            KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Escape) { if (page == "settings" || page == "update") Navigate(returnPage); else Hide(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            ApplyPreferences(new UserPreferences());
        }
        private Panel PagePanel(string name) { var panel = new Panel { Name = name, AutoScroll = true, BackColor = Color.FromArgb(247,244,249) }; Controls.Add(panel); return panel; }
        private Label LabelFor(string text, Font font, Color color) {
            return new Label { Text = text, Font = font, ForeColor = color, BackColor = Color.Transparent, AutoEllipsis = true, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft };
        }
        private RoundedButton ButtonFor(string text, EventHandler handler) { var button = new RoundedButton { Text = text, Font = regular }; button.Click += handler; return button; }
        protected override CreateParams CreateParams { get { CreateParams p = base.CreateParams; p.ExStyle |= 0x00000080; p.ClassStyle |= 0x00020000; return p; } }
        public void ApplyPreferences(UserPreferences prefs) {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<UserPreferences>(ApplyPreferences), prefs); return; }
            preferences = new UserPreferences { Language = prefs != null && prefs.Language == "en" ? "en" : "zh", AccentHtml = prefs == null ? "#DA91AA" : prefs.AccentHtml };
            BackColor = Surface;
            foreach (Panel panel in new[] { homePage, mainPage, settingsPage, updatePage }) panel.BackColor = Surface;
            palette.SetLanguage(English); palette.SetColor(preferences.AccentColor);
            updateView.ApplyPreferences(preferences);
            foreach (RoundedButton button in Buttons()) { button.Accent = preferences.AccentColor; button.BackColor = Surface; button.Invalidate(); }
            foreach (MetricCards cards in new[] { overview, summary, records }) { cards.Accent = preferences.AccentColor; cards.BackColor = Surface; }
            quota.BackColor = bars.BackColor = heatmap.BackColor = Surface;
            UpdateData();
        }
        private RoundedButton[] Buttons() { return new[] { appearance,refresh,close,back,day,week,month,tokens,time,chinese,englishButton,homeTab,historyTab,updateEntry,homeDay,homeWeek,homeMonth,homeAll,heatPrevious,heatNext }; }
        private void NotifyPreferences() { ApplyPreferences(preferences); EventHandler handler = PreferencesChanged; if (handler != null) handler(this, EventArgs.Empty); }
        public void SetState(QuotaState next) {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<QuotaState>(SetState), next); return; }
            state = next; UpdateData();
        }
        private void SetPeriod(string next) { period = next; UpdateData(); }
        private void SetHomePeriod(string next) { homePeriod = next; UpdateData(); }
        private void UpdateHeader() {
            title.Text = page == "settings" ? L("外观", "Appearance") : L("Codex 用量", "Codex usage"); AccessibleName = title.Text;
            appearance.Text = L("外观", "Style"); refresh.Text = L("刷新", "Refresh"); back.Text = L("‹ 返回", "‹ Back");
            close.AccessibleName = L("关闭详情", "Close details"); back.AccessibleName = L("返回面板", "Back to panel");
            homeTab.Text = L("首页", "Home"); historyTab.Text = L("历史统计", "History");
            homeTab.Selected = page == "home"; historyTab.Selected = page == "history";
            updateEntry.Text = updateView.Busy ? updateView.StatusText : L("有更新", "Update");
            updateEntry.AccessibleName = updateView.StatusText;
            updateEntry.Selected = true;
            foreach (RoundedButton button in new[] { homeTab, historyTab, updateEntry }) button.Invalidate();
        }
        private void UpdateData() {
            DateTime today = DateTime.UtcNow.AddHours(8).Date;
            List<ChartBucket> buckets = UsageAnalytics.BuildBuckets(state, period, today);
            UsageSummary totals = UsageAnalytics.Summarize(buckets);
            UpdateHeader();
            homeDay.Text = L("今天", "Today"); homeWeek.Text = L("近 7 天", "7 days"); homeMonth.Text = L("近 30 天", "30 days"); homeAll.Text = L("累计", "All");
            homeDay.Selected = homePeriod == "day"; homeWeek.Selected = homePeriod == "week"; homeMonth.Selected = homePeriod == "month"; homeAll.Selected = homePeriod == "all";
            DateTime homeStart = homePeriod == "week" ? today.AddDays(-6) : homePeriod == "month" ? today.AddDays(-29) : today;
            var homeTotals = UsageAnalytics.Summarize(UsageAnalytics.BuildDaily(state, homeStart, today));
            bool hasActivity = state != null && state.Activity != null && (state.Activity.Status == "ready" || state.Activity.DailyBuckets.Count > 0);
            long? totalTokens = homePeriod == "all" ? (state == null ? null : state.LifetimeTokens) : homeTotals.Tokens;
            double? work = homePeriod == "all" ? (hasActivity ? (double?)state.Activity.TotalWorkSeconds : null) : homeTotals.WorkSeconds;
            int? tasks = homePeriod == "all" ? (hasActivity ? (int?)state.Activity.CompletedTasks : null) : homeTotals.Tasks;
            string tokenHint = homePeriod == "all" ? L("账户累计", "Account lifetime") : homeTotals.TokenPartial ? L("账户记录 · 部分", "Account · partial") : L("账户记录", "Account usage");
            string workHint = homePeriod == "all" ? L("本机已记录 · 最近90天内", "This PC · within 90 days") : L("本机已完成回合 · 并行去重", "This PC · overlaps merged");
            var primary = state == null ? null : state.Primary;
            DateTime? reset = primary == null ? null : QuotaAppearance.ResetTime(primary.ResetAt);
            string quotaHint = reset.HasValue ? L("重置 ", "Resets ") + reset.Value.ToString("MM/dd HH:mm") : L("重置时间暂不可用", "Reset time unavailable");
            overview.SetItems(new[] { L("Token 总消耗", "Total Tokens"), L("账户剩余额度", "Quota remaining"), L("AI 工作时长", "AI work time"), L("平均消耗速度", "Tokens per work hour") },
                new[] { UsageText.Number(totalTokens, true), primary == null ? "—" : QuotaAppearance.Percentage(primary.Remaining), UsageText.Duration(work, English, false), "—" },
                new[] { tokenHint, quotaHint, workHint, L("缺少相同范围的用量数据", "Matching usage data unavailable") });
            homeDate.Text = homePeriod == "all" ? L("账户累计与本机已记录活动", "Account totals and recorded local activity") : homeStart.ToString("yyyy/MM/dd") + (homeStart != today ? " — " + today.ToString("MM/dd") : "") + "  UTC+8";
            homeTasks.Text = (tasks.HasValue ? tasks.Value.ToString("N0") : "—") + L(" 个已完成任务", " completed tasks") + (homePeriod == "all" ? L(" · 本机记录", " · this PC") : homeTotals.WorkPartial ? L(" · 部分记录", " · partial coverage") : "");
            quota.SetData(state, preferences.AccentColor, English);
            homeNote.Text = L("Token 为账户统计，工作时长为本机记录；范围不同，暂不换算每小时消耗。逐任务 Token 与模型占比尚无可验证数据。", "Tokens cover account activity; work time covers this PC. Their scopes differ, so a per-hour rate is unavailable. Per-task Tokens and model shares are not available yet.");
            day.Text = L("日", "Day"); week.Text = L("周", "Week"); month.Text = L("月", "Month"); time.Text = L("时长", "Time");
            day.Selected = period == "day"; week.Selected = period == "week"; month.Selected = period == "month"; tokens.Selected = !timeMetric; time.Selected = timeMetric;
            rangeTitle.Text = period == "day" ? L("最近 7 天", "Last 7 days") : period == "week" ? L("最近 4 周", "Last 4 weeks") : L("最近 6 个月", "Last 6 months");
            summary.SetItems(new[] { "Tokens" + (totals.TokenPartial ? " *" : ""), L("AI 工作时长", "AI work time") + (totals.WorkPartial ? " *" : ""), L("完成任务", "Tasks done") + (totals.WorkPartial ? " *" : "") },
                new[] { UsageText.Number(totals.Tokens, true), UsageText.Duration(totals.WorkSeconds, English, false), totals.Tasks.HasValue ? totals.Tasks.Value.ToString("N0") : "—" });
            chartTitle.Text = L("用量趋势", "Usage trend"); bars.SetData(buckets, timeMetric, preferences.AccentColor, English);
            heatTitle.Text = L("Token 活动", "Token activity");
            DateTime heatStart = today.AddDays(-364 + heatPage * 91), heatEnd = heatPage == 3 ? today : heatStart.AddDays(90);
            heatRange.Text = heatStart.ToString("yyyy/MM/dd") + " — " + heatEnd.ToString("MM/dd") + "  ·  " + (heatPage + 1) + "/4";
            heatPrevious.Enabled = heatPage > 0; heatNext.Enabled = heatPage < 3;
            heatPrevious.AccessibleName = L("前一段活动", "Previous activity period"); heatNext.AccessibleName = L("后一段活动", "Next activity period");
            heatmap.SetData(UsageAnalytics.BuildDaily(state, heatStart, heatEnd), preferences.AccentColor, English);
            heatmap.AccessibleName = L("Token 活动：", "Token activity: ") + heatRange.Text;
            recordTitle.Text = L("累计与记录", "Totals and records");
            string activityMark = hasActivity && state.Activity.Status != "ready" ? " *" : "";
            records.SetItems(new[] { L("累计 Tokens", "All-time Tokens"), L("单日峰值", "Peak daily Tokens"), L("本机工作时长", "Recorded work time") + activityMark, L("本机完成任务", "Recorded tasks") + activityMark, L("最长单次工作", "Longest AI turn"), L("连续天数 · 当前 / 最长", "Streak · current / best") },
                new[] { UsageText.Number(state == null ? null : state.LifetimeTokens, true), UsageText.Number(state == null ? null : state.PeakDailyTokens, true), UsageText.Duration(hasActivity ? (double?)state.Activity.TotalWorkSeconds : null, English, false), hasActivity ? state.Activity.CompletedTasks.ToString("N0") : "—", UsageText.Duration(state == null ? null : state.LongestRunningTurnSec, English, false), (state == null || !state.CurrentStreakDays.HasValue ? "—" : state.CurrentStreakDays.Value.ToString()) + " / " + (state == null || !state.LongestStreakDays.HasValue ? "—" : state.LongestStreakDays.Value.ToString()) });
            scope.Text = ScopeText(totals); footer.Text = FooterText();
            settingsLanguage.Text = L("语言", "Language"); settingsAccent.Text = L("主题色", "Accent color"); settingsCustom.Text = L("预设色与自定义调色", "Presets and custom palette");
            settingsHint.Text = L("配色同步到头像标记、图表和系统托盘。设置自动保存在本机。", "Your accent colors the badge, charts and tray. Preferences are saved on this PC.");
            chinese.Selected = !English; englishButton.Selected = English;
            foreach (RoundedButton button in Buttons()) button.Invalidate();
            LayoutPage(); Invalidate();
        }
        private string ScopeText(UsageSummary totals) {
            string text = L("Tokens：账户统计。工作时长与完成任务：本机采集。", "Tokens: account usage. Work time and tasks: this PC.");
            WorkActivity activity = state == null ? null : state.Activity;
            if (activity == null || activity.Status == "unavailable") text += "\n" + L("本机工作记录暂不可用；缺失数据以 — 显示。", "Local activity unavailable; missing data is shown as —.");
            else if (!String.IsNullOrWhiteSpace(activity.CoveredFrom)) text += "\n" + L("本机记录覆盖：", "Local coverage: ") + activity.CoveredFrom + " – " + activity.CoveredTo;
            if (totals.TokenPartial || totals.WorkPartial) text += "\n" + L("* 部分时段缺少记录；悬停图表可查看精确明细。", "* Partial coverage. Hover a chart for exact details.");
            return text;
        }
        private string FooterText() {
            if (state == null) return L("正在读取用量…", "Reading usage…");
            DateTimeOffset updated;
            string stamp = DateTimeOffset.TryParse(state.UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out updated) ? updated.ToOffset(TimeSpan.FromHours(8)).ToString("HH:mm:ss") : "—";
            return (state.Stale || !state.Available ? L("缓存 / 暂不可用 · ", "Cached / unavailable · ") : "") + stamp + L(" 更新 · 北京时间", " updated · UTC+8");
        }
        private void LayoutPage() {
            if (layingOut || updateView == null) return; layingOut = true;
            try {
                var panels = new[] { homePage, mainPage, settingsPage, updatePage };
                var scrolls = new Point[panels.Length];
                for (int i = 0; i < panels.Length; i++) { scrolls[i] = new Point(-panels[i].AutoScrollPosition.X,-panels[i].AutoScrollPosition.Y); panels[i].SuspendLayout(); panels[i].AutoScrollPosition = Point.Empty; }
                Rectangle work = Screen.FromPoint(Visible ? targetLocation : requestedPoint).WorkingArea;
                Size = new Size(Math.Min(Px(448), Math.Max(Px(300), work.Width-Px(16))), Math.Min(Px(664),Math.Max(Px(360),work.Height-Px(16))));
                title.SetBounds(Px(18),Px(14),Px(110),Px(32));
                int right = Width-Px(14);
                close.SetBounds(right-Px(32),Px(12),Px(32),Px(36)); right-=Px(38);
                refresh.SetBounds(right-Px(56),Px(12),Px(56),Px(36)); right-=Px(62);
                appearance.SetBounds(right-Px(50),Px(12),Px(50),Px(36)); right-=Px(56);
                updateEntry.SetBounds(right-Px(68),Px(12),Px(68),Px(36)); updateEntry.Visible=updateView.UpdateAvailable||updateView.Busy;
                title.Width=Math.Max(Px(20),(updateView.UpdateAvailable||updateView.Busy?updateEntry.Left:appearance.Left)-title.Left-Px(8));
                bool auxiliary=page=="settings"||page=="update";
                homeTab.SetBounds(Px(15),Px(59),(Width-Px(36))/2,Px(44)); historyTab.SetBounds(homeTab.Right+Px(6),Px(59),(Width-Px(36))/2,Px(44));
                homeTab.Visible=historyTab.Visible=!auxiliary;
                back.SetBounds(Px(16),Px(59),Px(88),Px(42)); back.Visible=page=="settings";
                int bodyTop = page=="update"?Px(58):Px(116);
                foreach(var panel in panels)panel.SetBounds(Px(16),bodyTop,Width-Px(32),Height-bodyTop-Px(42));
                homePage.Visible=page=="home";mainPage.Visible=page=="history";settingsPage.Visible=page=="settings";updatePage.Visible=page=="update";
                footer.SetBounds(Px(18),Height-Px(31),Width-Px(36),Px(22));
                int width=homePage.Width-SystemInformation.VerticalScrollBarWidth-Px(2);
                int buttonWidth=(width-Px(18))/4;
                homeDay.SetBounds(0,0,buttonWidth,Px(36));homeWeek.SetBounds(buttonWidth+Px(6),0,buttonWidth,Px(36));homeMonth.SetBounds((buttonWidth+Px(6))*2,0,buttonWidth,Px(36));homeAll.SetBounds((buttonWidth+Px(6))*3,0,buttonWidth,Px(36));
                homeDate.SetBounds(0,Px(43),width,Px(20));overview.SetBounds(0,Px(72),width,Px(196));homeTasks.SetBounds(Px(2),Px(281),width-Px(4),Px(30));
                quota.SetBounds(0,Px(327),width,Px(quota.DesiredHeight));
                int homeNoteHeight=Math.Max(Px(54),TextRenderer.MeasureText(homeNote.Text,small,new Size(width,0),TextFormatFlags.WordBreak).Height+Px(10));
                homeNote.SetBounds(Px(2),quota.Bottom+Px(15),width-Px(4),homeNoteHeight);homePage.AutoScrollMinSize=new Size(0,homeNote.Bottom+Px(8));
                int y=0;
                rangeTitle.SetBounds(0,y,Math.Max(Px(100),width-Px(156)),Px(34));
                int segment=Px(48);day.SetBounds(width-segment*3,y,segment-Px(4),Px(34));week.SetBounds(width-segment*2,y,segment-Px(4),Px(34));month.SetBounds(width-segment,y,segment,Px(34));
                y+=Px(46);summary.SetBounds(0,y,width,Px(78));y+=Px(99);
                chartTitle.SetBounds(0,y,Math.Max(Px(100),width-Px(138)),Px(34));tokens.SetBounds(width-Px(133),y,Px(64),Px(34));time.SetBounds(width-Px(63),y,Px(63),Px(34));
                y+=Px(43);bars.SetBounds(0,y,width,Px(174));y+=Px(195);
                heatTitle.SetBounds(0,y,width-Px(96),Px(34));heatPrevious.SetBounds(width-Px(90),y,Px(40),Px(34));heatNext.SetBounds(width-Px(42),y,Px(40),Px(34));y+=Px(39);
                heatRange.SetBounds(0,y,width,Px(20));y+=Px(31);heatmap.SetBounds(0,y,width,Px(220));y+=Px(239);
                recordTitle.SetBounds(0,y,width,Px(24));y+=Px(34);records.SetBounds(0,y,width,Px(241));y+=Px(259);
                int scopeHeight=Math.Max(Px(70),TextRenderer.MeasureText(scope.Text,small,new Size(width,0),TextFormatFlags.WordBreak).Height+Px(12));scope.SetBounds(0,y,width,scopeHeight);y+=scopeHeight+Px(10);mainPage.AutoScrollMinSize=new Size(0,y);
                settingsLanguage.SetBounds(0,0,width,Px(24));chinese.SetBounds(0,Px(34),Px(88),Px(40));englishButton.SetBounds(Px(98),Px(34),Px(96),Px(40));
                settingsAccent.SetBounds(0,Px(102),width,Px(25));settingsCustom.SetBounds(0,Px(132),width,Px(23));palette.SetBounds(0,Px(165),width,Px(296));settingsHint.SetBounds(0,Px(482),width,Px(65));settingsPage.AutoScrollMinSize=new Size(0,Px(555));
                updateView.SetBounds(0,0,updatePage.Width,updatePage.Height);updatePage.AutoScrollMinSize=Size.Empty;
                for(int i=0;i<panels.Length;i++){panels[i].ResumeLayout();panels[i].AutoScrollPosition=scrolls[i];}
                Region old=Region;using(GraphicsPath path=QuotaAppearance.RoundRectangle(new RectangleF(0,0,Width,Height),Px(22)))Region=new Region(path);if(old!=null)old.Dispose();
                if(Visible&&!animation.Enabled){targetLocation=ClampPoint(requestedPoint,work);Location=targetLocation;}
            } finally { layingOut=false; }
        }
        private Point ClampPoint(Point point,Rectangle work) { return new Point(Math.Max(work.Left+Px(8),Math.Min(point.X,work.Right-Width-Px(8))),Math.Max(work.Top+Px(8),Math.Min(point.Y,work.Bottom-Height-Px(8)))); }
        private void Navigate(string next) {
            if(page==next)return;
            if(next=="settings"||next=="update") { if(page=="home"||page=="history")returnPage=page; }
            page=next;animation.Stop();Opacity=1;UpdateHeader();LayoutPage();
            if(Visible&&SystemInformation.IsMenuAnimationEnabled) { pageMotion=true;motionDistance=Px(5);animationClock.Restart();animation.Start(); }
        }
        public void ShowUpdates() {
            Navigate("update");
            if(!Visible){EventHandler handler=OpenUpdateRequested;if(handler!=null)handler(this,EventArgs.Empty);}
        }
        public void ShowAt(Point point) {
            requestedPoint=point;LayoutPage();targetLocation=ClampPoint(point,Screen.FromPoint(point).WorkingArea);
            bool animate=!Visible&&SystemInformation.IsMenuAnimationEnabled;animation.Stop();pageMotion=false;motionDistance=Px(8);
            Location=animate?new Point(targetLocation.X,targetLocation.Y+motionDistance):targetLocation;Opacity=animate?.72:1;
            if(!Visible)Show();Activate();if(animate){animationClock.Restart();animation.Start();}
        }
        private void Animate(object sender,EventArgs e) {
            double progress=Math.Min(1,animationClock.Elapsed.TotalMilliseconds/200),eased=1-Math.Pow(1-progress,3);
            Opacity=.82+.18*eased;
            if(!pageMotion)Location=new Point(targetLocation.X,targetLocation.Y+(int)Math.Round(motionDistance*(1-eased)));
            else {Panel active=page=="home"?homePage:page=="history"?mainPage:page=="settings"?settingsPage:updatePage;active.Left=Px(16)+(int)Math.Round(motionDistance*(1-eased));}
            if(progress>=1){animation.Stop();animationClock.Reset();Location=targetLocation;Opacity=1;LayoutPage();}
        }
        protected override void OnFormClosing(FormClosingEventArgs e){if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}base.OnFormClosing(e);}
        protected override void OnPaintBackground(PaintEventArgs e) {
            using(var fill=new LinearGradientBrush(ClientRectangle,QuotaAppearance.Blend(preferences.AccentColor,Color.White,.84f),Color.FromArgb(242,244,250),45f))e.Graphics.FillRectangle(fill,ClientRectangle);
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(Pen border=new Pen(Color.FromArgb(180,Color.White)))using(GraphicsPath path=QuotaAppearance.RoundRectangle(new RectangleF(.8f,.8f,Width-1.6f,Height-1.6f),Px(22)))e.Graphics.DrawPath(border,path);
            using(Pen line=new Pen(Color.FromArgb(22,preferences.AccentColor)))e.Graphics.DrawLine(line,Px(18),Height-Px(39),Width-Px(18),Height-Px(39));
        }
        protected override void Dispose(bool disposing) { if(disposing)animation.Dispose();base.Dispose(disposing);if(disposing){regular.Dispose();small.Dispose();valueFont.Dispose();} }
    }

    internal sealed class MetricCards : Control
    {
        private readonly float scale;
        private readonly Font labelFont, valueFont;
        private readonly int columns;
        private string[] names = new string[0], values = new string[0], hints = new string[0];
        internal Color Accent = QuotaAppearance.Green;
        internal MetricCards(float factor, Font small, Font large, int count)
        {
            scale = factor; labelFont = small; valueFont = large; columns = count; BackColor = Color.White;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        internal void SetItems(string[] labels, string[] numbers) { SetItems(labels, numbers, new string[0]); }
        internal void SetItems(string[] labels, string[] numbers, string[] notes) { names = labels; values = numbers; hints = notes; AccessibleName = String.Join("; ", labels) + ": " + String.Join("; ", numbers) + ". " + String.Join("; ", notes); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int rows = Math.Max(1, (int)Math.Ceiling(names.Length / (double)columns));
            float gap = 8 * scale, width = (Width - gap * (columns - 1)) / columns, height = (Height - gap * (rows - 1)) / rows;
            for (int i = 0; i < names.Length; i++)
            {
                RectangleF card = new RectangleF((i % columns) * (width + gap), (i / columns) * (height + gap), width - 1, height - 1);
                using (GraphicsPath path = QuotaAppearance.RoundRectangle(card, 14 * scale))
                using (Brush fill = new LinearGradientBrush(card, Color.White, QuotaAppearance.Blend(Accent, Color.White, .96f), 90f))
                using (Pen border = new Pen(Color.FromArgb(235, Color.White))) { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path); }
                Rectangle label = Rectangle.Round(new RectangleF(card.X + 11 * scale, card.Y + 9 * scale, width - 20 * scale, 18 * scale));
                Rectangle value = Rectangle.Round(new RectangleF(card.X + 11 * scale, card.Y + 30 * scale, width - 20 * scale, 29 * scale));
                TextRenderer.DrawText(e.Graphics, names[i], labelFont, label, QuotaAppearance.Muted, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(e.Graphics, i < values.Length ? values[i] : "—", valueFont, value, QuotaAppearance.Text, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                if(i < hints.Length) TextRenderer.DrawText(e.Graphics, hints[i], labelFont, Rectangle.Round(new RectangleF(card.X + 11 * scale, card.Bottom - 23 * scale, width - 20 * scale, 18 * scale)), QuotaAppearance.Muted, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
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
                using (GraphicsPath path = QuotaAppearance.RoundRectangle(card, 14 * scale))
                using (Brush fill = new LinearGradientBrush(card, Color.White, QuotaAppearance.Blend(accent, Color.White, .92f), 90f)) e.Graphics.FillPath(fill, path);
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
