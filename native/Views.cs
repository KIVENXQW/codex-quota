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
        internal static readonly Color Muted = Color.FromArgb(107, 103, 116);
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
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(0, 0, Width, Height);
                Region = new Region(path);
            }
            if (previous != null) previous.Dispose();
        }

        private sealed class BadgeButton : Button
        {
            private float scale = 1;
            private bool stale;
            private bool hover;
            private string percentage = "—";
            private double? remaining;
            private Color accent = QuotaAppearance.Green;
            private bool english;

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
                Invalidate();
            }

            internal void SetAppearance(UserPreferences prefs) { accent = prefs.AccentColor; english = prefs.Language == "en"; UpdateAccessibleName(); Invalidate(); }

            internal void SetState(QuotaState state)
            {
                QuotaWindow primary = state == null ? null : state.Primary;
                remaining = primary == null ? null : TrayRingIcon.Normalize(primary.Remaining);
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
                Color background = stale ? Color.FromArgb(240, 240, 243) : QuotaAppearance.Blend(accent, Color.White, .92f);
                e.Graphics.Clear(background);
                // Reuse the approved B glass ring at the avatar's fixed footprint.
                // Its 40 px rendering keeps the central quota around 14 px, with
                // the ring carrying the amount instead of a two-line number card.
                int side = Math.Min(Width, Height);
                using (Bitmap glass = TrayRingIcon.Render(side, remaining, stale, accent, false, false))
                    e.Graphics.DrawImageUnscaled(glass, (Width - side) / 2, (Height - side) / 2);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                if (hover)
                {
                    using (Pen reflection = new Pen(Color.FromArgb(210, Color.White), Math.Max(1, scale)))
                        e.Graphics.DrawEllipse(reflection, side * .09f, side * .07f, side * .82f, side * .82f);
                }
                if (Focused && ShowFocusCues)
                {
                    using (Pen focus = new Pen(QuotaAppearance.Darken(accent, .5f), Math.Max(1, scale)))
                    {
                        focus.DashStyle = DashStyle.Dot;
                        e.Graphics.DrawEllipse(focus, 1.5f * scale, 1.5f * scale, Width - 3 * scale, Height - 3 * scale);
                    }
                }
            }
        }
    }

    internal sealed class RoundedButton : Button
    {
        internal Color Accent = QuotaAppearance.Green;
        internal bool Minimal;
        internal string IconKind;
        private bool selected;
        internal bool Selected
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                GlassSegmentRail rail = Parent as GlassSegmentRail;
                if (value && rail != null) rail.SelectButton(this);
                Invalidate();
            }
        }
        private bool hover;
        internal RoundedButton()
        {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand; BackColor = Color.White;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            // Button starts opaque: clear that flag so each WM_PAINT buffer first
            // receives its own translated parent background instead of stale pixels.
            SetStyle(ControlStyles.Opaque, false);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override bool IsInputKey(Keys keyData) { Keys key = keyData & Keys.KeyCode; return Parent is GlassSegmentRail && (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down) || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            GlassSegmentRail rail = Parent as GlassSegmentRail;
            if (rail != null && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down))
            {
                RoundedButton next = rail.AdjacentButton(this, e.KeyCode == Keys.Left || e.KeyCode == Keys.Up ? -1 : 1);
                if (next != null && next.Enabled) { next.Focus(); next.OnClick(EventArgs.Empty); }
                e.Handled = true; e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if(Minimal) {
                Color ink=!Enabled?Color.FromArgb(165,158,170):Selected?QuotaAppearance.Darken(Accent,.3f):hover?MinimalMaterial.Ink:MinimalMaterial.Muted;
                if(!String.IsNullOrEmpty(IconKind))MinimalMaterial.Icon(e.Graphics,IconKind,ClientRectangle,ink);
                else {
                    GlassSegmentRail rail=Parent as GlassSegmentRail;
                    bool navigation=rail!=null&&rail.Name=="mainNavigation";
                    Rectangle text=ClientRectangle;if(navigation){text.X+=4;text.Width-=4;text.Height-=4;}
                    using(var font=new Font(Font,Selected?FontStyle.Bold:FontStyle.Regular))TextRenderer.DrawText(e.Graphics,Text,font,text,Selected&&navigation?MinimalMaterial.Ink:ink,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding|(navigation?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter));
                }
                if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-3,-3));
                return;
            }
            bool segmented = Parent is GlassSegmentRail;
            float factor = Math.Max(1, Font.Size / 13f), radius = Math.Min(14 * factor, Height * .36f);
            if (!segmented)
                GlassMaterial.Surface(e.Graphics, new RectangleF(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 5)), radius,
                    Selected ? QuotaAppearance.Blend(Accent, Color.White, hover ? .81f : .88f) : hover ? Color.White : Color.FromArgb(245, 243, 250), factor, true);
            else if (hover && !Selected)
                using (GraphicsPath path = QuotaAppearance.RoundRectangle(new RectangleF(1, 1, Width - 2, Height - 2), radius))
                using (Brush fill = new SolidBrush(Color.FromArgb(45, Color.White))) e.Graphics.FillPath(fill, path);
            Color textColor = !Enabled ? Color.FromArgb(158, 153, 162) : segmented ? (Selected ? QuotaAppearance.Text : QuotaAppearance.Muted) : Selected ? QuotaAppearance.Darken(Accent, .45f) : QuotaAppearance.Text;
            using (Font textFont = new Font(Font, Selected && segmented ? FontStyle.Bold : Font.Style))
                TextRenderer.DrawText(e.Graphics, Text, textFont, ClientRectangle, textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), QuotaAppearance.Darken(Accent, .4f), BackColor);
        }
    }

    public sealed class QuotaPopup : Form
    {
        private readonly float scale;
        private readonly Font regular, small, valueFont, headingFont;
        private readonly Label title, footer, rangeTitle, chartTitle, heatTitle, recordTitle, scope, homeDate, homeTasks, homeNote, heatRange;
        private readonly RoundedButton appearance, refresh, close, back, day, week, month, tokens, time, chinese, englishButton, homeTab, historyTab, updateEntry, homeDay, homeWeek, homeMonth, homeAll, heatPrevious, heatNext;
        private readonly Panel mainPage, homePage, settingsPage, updatePage;
        private readonly Label settingsLanguage, settingsAccent, settingsCustom, settingsHint, settingsCorner, settingsTransparency, transparencyValue, settingsSaved;
        private readonly RoundedButton cornerCard, cornerCapsule, cornerTray;
        private readonly RoundedButton themeMinimal, themeGlass;
        private readonly Label settingsTheme;
        private readonly GlassSegmentRail themeRail;
        private readonly GlassSegmentRail navigationRail, homePeriodRail, historyPeriodRail, metricRail, languageRail, cornerRail;
        private readonly GlassTransparencySlider glassTransparency;
        private readonly AccentPalette palette;
        private readonly QuotaSummaryView quota;
        private readonly MetricCards overview, summary, records;
        private readonly MinimalOverview minimalOverview;
        private readonly Label historyTotals;
        private readonly UsageBarChart bars;
        private readonly TokenHeatmap heatmap;
        private readonly GlassUpdateView updateView;
        private readonly Timer animation;
        private readonly Timer outsideClickDelay;
        private readonly Stopwatch animationClock = new Stopwatch();
        private readonly PointerHook pointerHook;
        private IntPtr pointerHookHandle;
        private Point targetLocation, requestedPoint;
        private QuotaState state;
        private UserPreferences preferences = new UserPreferences();
        private string period = "day", homePeriod = "day", page = "home", returnPage = "home";
        private string appliedLanguage, appliedAccent, appliedPanelStyle;
        private int heatPage = 3, motionOffset;
        private bool timeMetric, metricSelected, layingOut, applyingPreferences, appearanceInitialized, preserveWindowLocation, outsidePress;
        public event EventHandler RefreshRequested;
        public event EventHandler PreferencesChanged;
        public event EventHandler OpenUpdateRequested;
        public UserPreferences Preferences { get { return preferences; } }
        public GlassUpdateView UpdateView { get { return updateView; } }
        private bool English { get { return preferences.Language == "en"; } }
        private bool Minimal { get { return preferences.PanelStyle != "glass"; } }
        private int Px(int value) { return (int)Math.Round(value * scale); }
        private string L(string zh, string en) { return English ? en : zh; }
        private Color Surface { get { return Minimal ? MinimalMaterial.Surface : QuotaAppearance.Blend(preferences.AccentColor, Color.FromArgb(247,247,251), .94f); } }

        public QuotaPopup()
        {
            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero)) scale = Math.Max(1, graphics.DpiX / 96f);
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false; TopMost = true; KeyPreview = true; DoubleBuffered = true;
            BackColor = Color.FromArgb(246,243,249); ForeColor = QuotaAppearance.Text; Text = "Codex";
            regular = new Font("Microsoft YaHei UI", 13 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            small = new Font("Microsoft YaHei UI", 12 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            valueFont = new Font("Microsoft YaHei UI", 25 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            headingFont = new Font("Microsoft YaHei UI", 15 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            Font = regular;
            title = LabelFor("Codex", headingFont, QuotaAppearance.Text); Controls.Add(title);
            appearance = ButtonFor("", delegate { Navigate("settings"); }); Controls.Add(appearance);
            refresh = ButtonFor("", delegate { EventHandler handler = RefreshRequested; if (handler != null) handler(this, EventArgs.Empty); }); Controls.Add(refresh);
            close = ButtonFor("×", delegate { Hide(); }); Controls.Add(close);
            back = ButtonFor("", delegate { Navigate(returnPage); }); Controls.Add(back);
            updateEntry = ButtonFor("", delegate { ShowUpdates(); }); updateEntry.Name = "updateEntry"; Controls.Add(updateEntry);
            homeTab = ButtonFor("", delegate { Navigate("home"); }); homeTab.Name = "homeTab"; Controls.Add(homeTab);
            historyTab = ButtonFor("", delegate { Navigate("history"); }); historyTab.Name = "historyTab"; Controls.Add(historyTab);
            navigationRail = new GlassSegmentRail(scale, "mainNavigation", homeTab, historyTab); Controls.Add(navigationRail);
            homePage = PagePanel("homePage"); mainPage = PagePanel("historyPage"); settingsPage = PagePanel("settingsPage"); updatePage = PagePanel("updatePage");
            footer = LabelFor("", small, QuotaAppearance.Muted); Controls.Add(footer);
            homeDate = LabelFor("", small, QuotaAppearance.Muted); homePage.Controls.Add(homeDate);
            homeDay = ButtonFor("", delegate { SetHomePeriod("day"); }); homePage.Controls.Add(homeDay);
            homeWeek = ButtonFor("", delegate { SetHomePeriod("week"); }); homePage.Controls.Add(homeWeek);
            homeMonth = ButtonFor("", delegate { SetHomePeriod("month"); }); homePage.Controls.Add(homeMonth);
            homeAll = ButtonFor("", delegate { SetHomePeriod("all"); }); homePage.Controls.Add(homeAll);
            homePeriodRail = new GlassSegmentRail(scale, "homePeriods", homeDay, homeWeek, homeMonth, homeAll); homePage.Controls.Add(homePeriodRail);
            overview = new MetricCards(scale, small, valueFont, 2); overview.Name = "overview"; homePage.Controls.Add(overview);
            minimalOverview = new MinimalOverview(scale); minimalOverview.Name="minimalOverview";homePage.Controls.Add(minimalOverview);
            homeTasks = LabelFor("", regular, QuotaAppearance.Text); homePage.Controls.Add(homeTasks);
            quota = new QuotaSummaryView(scale, regular, small, valueFont); homePage.Controls.Add(quota);
            homeNote = LabelFor("", small, QuotaAppearance.Muted); homeNote.TextAlign = ContentAlignment.TopLeft; homePage.Controls.Add(homeNote);
            rangeTitle = LabelFor("", regular, QuotaAppearance.Text); mainPage.Controls.Add(rangeTitle);
            historyTotals=LabelFor("",small,MinimalMaterial.Muted);historyTotals.Name="historyTotals";mainPage.Controls.Add(historyTotals);
            summary = new MetricCards(scale, small, valueFont, 3); mainPage.Controls.Add(summary);
            chartTitle = LabelFor("", regular, QuotaAppearance.Text); mainPage.Controls.Add(chartTitle);
            day = ButtonFor("", delegate { SetPeriod("day"); }); mainPage.Controls.Add(day);
            week = ButtonFor("", delegate { SetPeriod("week"); }); mainPage.Controls.Add(week);
            month = ButtonFor("", delegate { SetPeriod("month"); }); mainPage.Controls.Add(month);
            tokens = ButtonFor("Tokens", delegate { metricSelected=true;timeMetric = false; UpdateData(); }); mainPage.Controls.Add(tokens);
            time = ButtonFor("", delegate { metricSelected=true;timeMetric = true; UpdateData(); }); mainPage.Controls.Add(time);
            historyPeriodRail = new GlassSegmentRail(scale, "historyPeriods", day, week, month); mainPage.Controls.Add(historyPeriodRail);
            metricRail = new GlassSegmentRail(scale, "historyMetrics", tokens, time); mainPage.Controls.Add(metricRail);
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
            settingsTheme=LabelFor("",regular,QuotaAppearance.Text);settingsPage.Controls.Add(settingsTheme);
            themeMinimal=ButtonFor("",delegate{preferences.PanelStyle="minimal";NotifyPreferences();});themeMinimal.Name="themeMinimal";
            themeGlass=ButtonFor("",delegate{preferences.PanelStyle="glass";NotifyPreferences();});themeGlass.Name="themeGlass";
            themeRail=new GlassSegmentRail(scale,"themeChoices",themeMinimal,themeGlass);settingsPage.Controls.Add(themeRail);
            chinese = ButtonFor("中文", delegate { preferences.Language = "zh"; NotifyPreferences(); }); settingsPage.Controls.Add(chinese);
            englishButton = ButtonFor("English", delegate { preferences.Language = "en"; NotifyPreferences(); }); settingsPage.Controls.Add(englishButton);
            languageRail = new GlassSegmentRail(scale, "languageChoices", chinese, englishButton); settingsPage.Controls.Add(languageRail);
            settingsCorner = LabelFor("", regular, QuotaAppearance.Text); settingsPage.Controls.Add(settingsCorner);
            cornerCard = ButtonFor("", delegate { preferences.CornerStyle = "card"; NotifyPreferences(); }); cornerCard.Name = "cornerCard";
            cornerCapsule = ButtonFor("", delegate { preferences.CornerStyle = "capsule"; NotifyPreferences(); }); cornerCapsule.Name = "cornerCapsule";
            cornerTray = ButtonFor("", delegate { preferences.CornerStyle = "tray"; NotifyPreferences(); }); cornerTray.Name = "cornerTray";
            cornerRail = new GlassSegmentRail(scale, "cornerChoices", cornerCard, cornerCapsule, cornerTray); settingsPage.Controls.Add(cornerRail);
            settingsTransparency = LabelFor("", regular, QuotaAppearance.Text); settingsPage.Controls.Add(settingsTransparency);
            transparencyValue = LabelFor("", regular, QuotaAppearance.Text); transparencyValue.Name = "transparencyValue"; transparencyValue.TextAlign = ContentAlignment.MiddleRight; settingsPage.Controls.Add(transparencyValue);
            glassTransparency = new GlassTransparencySlider(scale); glassTransparency.Name = "glassTransparency"; settingsPage.Controls.Add(glassTransparency);
            glassTransparency.ValueChanged += delegate { if (!applyingPreferences) { preferences.GlassTransparency = glassTransparency.Value; NotifyPreferences(); } };
            settingsAccent = LabelFor("", regular, QuotaAppearance.Text); settingsPage.Controls.Add(settingsAccent);
            settingsCustom = LabelFor("", small, QuotaAppearance.Muted); settingsPage.Controls.Add(settingsCustom);
            palette = new AccentPalette(scale, regular);
            palette.SelectedColorChanged += delegate {
                Color color = palette.SelectedColor;
                preferences.AccentHtml = "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2"); NotifyPreferences();
            };
            settingsPage.Controls.Add(palette);
            settingsHint = LabelFor("", small, QuotaAppearance.Muted); settingsHint.TextAlign = ContentAlignment.TopLeft; settingsPage.Controls.Add(settingsHint);
            settingsSaved = LabelFor("", small, QuotaAppearance.Muted); settingsSaved.TextAlign = ContentAlignment.TopLeft; settingsPage.Controls.Add(settingsSaved);
            updateView = new GlassUpdateView(preferences); updatePage.Controls.Add(updateView);
            updateView.OpenRequested += delegate { ShowUpdates(); };
            updateView.BackRequested += delegate { Navigate(returnPage); };
            updateView.StateChanged += delegate { UpdateHeader(); LayoutPage(); };
            animation = new Timer(); animation.Interval = 15; animation.Tick += Animate;
            outsideClickDelay = new Timer(); outsideClickDelay.Interval = 35; outsideClickDelay.Tick += DismissAfterOutsideClick;
            pointerHook = ObserveNativePointer;
            VisibleChanged += delegate {
                if (Visible) StartWatchingOutsideClicks();
                else { StopWatchingOutsideClicks(); animation.Stop(); animationClock.Reset(); motionOffset = 0; Opacity = 1; }
            };
            // A non-activating avatar/corner entry can hand focus back while the
            // pointer crosses into this window. Focus loss alone is not dismissal.
            Deactivate += delegate {
                if (Visible && pointerHookHandle == IntPtr.Zero && Control.MouseButtons != MouseButtons.None && !PointerInside(Cursor.Position)) Hide();
            };
            KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Escape) { if (page == "settings" || page == "update") Navigate(returnPage); else Hide(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            ApplyPreferences(new UserPreferences());
        }
        private Panel PagePanel(string name) { var panel = new GlassPagePanel { Name = name, AutoScroll = true }; Controls.Add(panel); return panel; }
        private Label LabelFor(string text, Font font, Color color) {
            return new Label { Text = text, Font = font, ForeColor = color, BackColor = Color.Transparent, AutoEllipsis = true, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft };
        }
        private RoundedButton ButtonFor(string text, EventHandler handler) { var button = new RoundedButton { Text = text, Font = regular }; button.Click += handler; return button; }
        protected override CreateParams CreateParams { get { CreateParams p = base.CreateParams; p.ExStyle |= 0x00000080; p.ClassStyle |= 0x00020000; return p; } }
        private delegate IntPtr PointerHook(int code, IntPtr message, IntPtr data);
        private Func<IntPtr, Point, Point?> physicalToLogical = LogicalPointerForWindow;
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct PointerData { internal Point Position; internal uint MouseData, Flags, Time; internal IntPtr ExtraInfo; }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int hook, PointerHook callback, IntPtr module, uint thread);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PhysicalToLogicalPointForPerMonitorDPI(IntPtr window, ref Point point);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)] private static extern IntPtr GetModuleHandle(string module);
        private static Point? LogicalPointerForWindow(IntPtr window, Point physicalPoint) {
            return PhysicalToLogicalPointForPerMonitorDPI(window, ref physicalPoint) ? (Point?)physicalPoint : null;
        }
        private void StartWatchingOutsideClicks() {
            outsidePress = false; outsideClickDelay.Stop();
            if (TopLevel && pointerHookHandle == IntPtr.Zero) pointerHookHandle = SetWindowsHookEx(14, pointerHook, GetModuleHandle(null), 0);
        }
        private void StopWatchingOutsideClicks() {
            outsidePress = false; outsideClickDelay.Stop();
            if (pointerHookHandle != IntPtr.Zero) { UnhookWindowsHookEx(pointerHookHandle); pointerHookHandle = IntPtr.Zero; }
        }
        private IntPtr ObserveNativePointer(int code, IntPtr message, IntPtr data) {
            if (code >= 0 && Visible) {
                int kind = message.ToInt32();
                if (kind == 0x0201 || kind == 0x0202 || kind == 0x0204 || kind == 0x0205 || kind == 0x0207 || kind == 0x0208 || kind == 0x020B || kind == 0x020C) {
                    PointerData pointer = (PointerData)System.Runtime.InteropServices.Marshal.PtrToStructure(data, typeof(PointerData));
                    ObservePointerButton(kind, pointer.Position, true);
                }
            }
            return CallNextHookEx(pointerHookHandle, code, message, data);
        }
        private bool PointerInside(Point point) {
            return PointerInsideScreenSpace(point, false); // Cursor.Position is already in this process's logical space.
        }
        private bool PointerInsideScreenSpace(Point point, bool physicalCoordinates) {
            // MSLLHOOKSTRUCT.pt is per-monitor physical. Each window can have a
            // different transform; never reuse the main window's conversion for an owned one.
            Point? logical = physicalCoordinates ? physicalToLogical(Handle, point) : (Point?)point;
            if (logical.HasValue && RectangleToScreen(ClientRectangle).Contains(logical.Value)) return true;
            foreach (Form owned in OwnedForms) if (owned.Visible) {
                logical = physicalCoordinates ? physicalToLogical(owned.Handle, point) : (Point?)point;
                if (logical.HasValue && owned.Bounds.Contains(logical.Value)) return true;
            }
            return false;
        }
        private void ObservePointerButton(int message, Point point, bool physicalCoordinates) {
            if (!Visible) return;
            bool down = message == 0x0201 || message == 0x0204 || message == 0x0207 || message == 0x020B;
            if (down) { outsideClickDelay.Stop(); outsidePress = !PointerInsideScreenSpace(point, physicalCoordinates); }
            else if (outsidePress) {
                // Let avatar/corner/tray click handlers finish their own toggle first.
                // Only the original press location determines an outside click.
                outsideClickDelay.Stop(); outsideClickDelay.Start();
            }
        }
        private void DismissAfterOutsideClick(object sender, EventArgs e) {
            outsideClickDelay.Stop();
            if (Visible && outsidePress) Hide();
            outsidePress = false;
        }
        public void ApplyPreferences(UserPreferences prefs) {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<UserPreferences>(ApplyPreferences), prefs); return; }
            UserPreferences next = new UserPreferences { Language = prefs != null && prefs.Language == "en" ? "en" : "zh", AccentHtml = prefs == null ? "#DA91AA" : prefs.AccentHtml, CornerStyle = prefs == null ? "card" : prefs.CornerStyle, GlassTransparency = prefs == null ? 45 : prefs.GlassTransparency, PanelStyle=prefs==null?"minimal":prefs.PanelStyle };
            next.Normalize();
            bool cornerOnly = appearanceInitialized && next.Language == appliedLanguage && next.AccentHtml == appliedAccent && next.PanelStyle==appliedPanelStyle;
            preferences = next;
            applyingPreferences = true;
            glassTransparency.Value = preferences.GlassTransparency;
            applyingPreferences = false;
            if (cornerOnly) { UpdateSettings(); return; }
            BackColor = Surface;
            palette.SetLanguage(English); palette.SetColor(preferences.AccentColor);
            palette.BackColor = Surface;
            updateView.ApplyPreferences(preferences);
            foreach (RoundedButton button in Buttons()) { button.Minimal=Minimal;button.Accent = preferences.AccentColor; button.BackColor = Color.Transparent; button.Invalidate(); }
            appearance.IconKind=Minimal?"appearance":null;refresh.IconKind=Minimal?"refresh":null;close.IconKind=Minimal?"close":null;
            foreach (GlassSegmentRail rail in new[] { navigationRail, homePeriodRail, historyPeriodRail, metricRail, languageRail, cornerRail, themeRail }) { rail.Minimal=Minimal;rail.Accent = preferences.AccentColor; rail.Invalidate(true); }
            foreach (MetricCards cards in new[] { overview, summary, records }) { cards.Minimal=Minimal;cards.Accent = preferences.AccentColor; }
            bars.BackColor = heatmap.BackColor = Surface;
            heatmap.Compact=Minimal;
            glassTransparency.Accent = preferences.AccentColor;
            appearanceInitialized = true;
            appliedLanguage = preferences.Language; appliedAccent = preferences.AccentHtml;appliedPanelStyle=preferences.PanelStyle;
            UpdateData();
        }
        private RoundedButton[] Buttons() { return new[] { appearance,refresh,close,back,day,week,month,tokens,time,chinese,englishButton,homeTab,historyTab,updateEntry,homeDay,homeWeek,homeMonth,homeAll,heatPrevious,heatNext,cornerCard,cornerCapsule,cornerTray,themeMinimal,themeGlass }; }
        private void NotifyPreferences() { ApplyPreferences(preferences); EventHandler handler = PreferencesChanged; if (handler != null) handler(this, EventArgs.Empty); }
        public void SetState(QuotaState next) {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<QuotaState>(SetState), next); return; }
            state = next; UpdateData();
        }
        private void SetPeriod(string next) { period = next; UpdateData(); }
        private void SetHomePeriod(string next) { homePeriod = next; UpdateData(); }
        private void UpdateHeader() {
            title.Text = page == "settings" ? L("外观", "Appearance") : Minimal?L("Codex 额度","Codex quota"):L("Codex 用量", "Codex usage"); AccessibleName = title.Text;
            appearance.Text = L("外观", "Style"); refresh.Text = L("刷新", "Refresh"); back.Text = L("‹ 返回", "‹ Back");
            close.AccessibleName = L("关闭详情", "Close details"); back.AccessibleName = L("返回面板", "Back to panel");
            appearance.AccessibleName=L("外观","Appearance");refresh.AccessibleName=L("立即刷新","Refresh now");
            homeTab.Text = L("首页", "Home"); historyTab.Text = Minimal?L("历史","History"):L("历史统计", "History");
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
            if(Minimal&&!metricSelected)timeMetric=!totals.Tokens.HasValue&&totals.WorkSeconds.HasValue;
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
                new[] { tokenHint, quotaHint, workHint, L("缺少相同范围的用量数据", "No matching usage data") });
            overview.SetQuota(primary == null ? null : primary.Remaining, state == null || state.Stale || !state.Available);
            minimalOverview.SetData(state,totalTokens,work,tasks,tokenHint,workHint,homePeriod=="day"?L("今天 · 本机","Today · this PC"):L("本机记录","This PC"),homeTotals.WorkPartial,preferences);
            homeDate.Text = homePeriod == "all" ? L("账户累计与本机已记录活动", "Account totals and recorded local activity") : homeStart.ToString("yyyy/MM/dd") + (homeStart != today ? " — " + today.ToString("MM/dd") : "") + "  UTC+8";
            if(Minimal)homeDate.Text=homePeriod=="all"?L("累计记录","All records"):homeStart==today?today.ToString("yyyy / MM / dd"):homeStart.ToString("MM/dd")+" — "+today.ToString("MM/dd");
            homeTasks.Text = (tasks.HasValue ? tasks.Value.ToString("N0") : "—") + L(" 个已完成任务", " completed tasks") + (homePeriod == "all" ? L(" · 本机记录", " · this PC") : homeTotals.WorkPartial ? L(" · 部分记录", " · partial coverage") : "");
            quota.SetData(state, preferences.AccentColor, English);
            homeNote.Text = L("Token 为账户统计，工作时长为本机记录；范围不同，暂不换算每小时消耗。逐任务 Token 与模型占比尚无可验证数据。", "Tokens cover account activity; work time covers this PC. Their scopes differ, so a per-hour rate is unavailable. Per-task Tokens and model shares are not available yet.");
            day.Text = L("日", "Day"); week.Text = L("周", "Week"); month.Text = L("月", "Month"); time.Text = L("时长", "Time");
            day.Selected = period == "day"; week.Selected = period == "week"; month.Selected = period == "month"; tokens.Selected = !timeMetric; time.Selected = timeMetric;
            rangeTitle.Text = period == "day" ? L("最近 7 天", "Last 7 days") : period == "week" ? L("最近 4 周", "Last 4 weeks") : L("最近 6 个月", "Last 6 months");
            summary.SetItems(new[] { "Tokens" + (totals.TokenPartial ? " *" : ""), L("AI 工作时长", "AI work time") + (totals.WorkPartial ? " *" : ""), L("完成任务", "Tasks done") + (totals.WorkPartial ? " *" : "") },
                new[] { UsageText.Number(totals.Tokens, true), UsageText.Duration(totals.WorkSeconds, English, false), totals.Tasks.HasValue ? totals.Tasks.Value.ToString("N0") : "—" });
            historyTotals.Text=UsageText.Number(totals.Tokens,true)+(totals.Tokens.HasValue&&totals.TokenPartial?" *":"")+" Tokens · "+UsageText.Duration(totals.WorkSeconds,English,false)+(totals.WorkSeconds.HasValue&&totals.WorkPartial?" *":"")+" · "+(totals.Tasks.HasValue?totals.Tasks.Value.ToString("N0"):"—")+L(" 回合"," turns")+(totals.Tasks.HasValue&&totals.WorkPartial?" *":"");
            historyTotals.AccessibleName=rangeTitle.Text+"; Tokens: "+UsageText.Number(totals.Tokens,false)+"; "+L("AI 工作时长: ","AI work time: ")+UsageText.Duration(totals.WorkSeconds,English,true)+"; "+L("完成回合: ","Completed turns: ")+(totals.Tasks.HasValue?totals.Tasks.Value.ToString("N0"):"—");
            historyTotals.AccessibleDescription=ScopeText(totals);
            chartTitle.Text = Minimal?(timeMetric?L("工作时长","Work time"):L("Token 用量","Token usage")):L("用量趋势", "Usage trend"); bars.SetData(buckets, timeMetric, preferences.AccentColor, English);
            heatTitle.Text = L("Token 活动", "Token activity");
            DateTime heatStart = today.AddDays(-364 + heatPage * 91), heatEnd = heatPage == 3 ? today : heatStart.AddDays(90);
            heatRange.Text = heatStart.ToString("yyyy/MM/dd") + " — " + heatEnd.ToString("MM/dd") + "  ·  " + (heatPage + 1) + "/4";
            heatPrevious.Enabled = heatPage > 0; heatNext.Enabled = heatPage < 3;
            heatPrevious.AccessibleName = L("前一段活动", "Previous activity period"); heatNext.AccessibleName = L("后一段活动", "Next activity period");
            var annualScale=HeatmapScale.Create(UsageAnalytics.BuildDaily(state,today.AddDays(-364),today));
            heatmap.SetData(UsageAnalytics.BuildDaily(state, heatStart, heatEnd), preferences.AccentColor, English,annualScale);
            heatmap.AccessibleName = L("Token 活动：", "Token activity: ") + heatRange.Text;
            recordTitle.Text = L("累计与记录", "Totals and records");
            string activityMark = hasActivity && state.Activity.Status != "ready" ? " *" : "";
            records.SetItems(new[] { L("累计 Tokens", "All-time Tokens"), L("单日峰值", "Peak daily Tokens"), L("本机工作时长", "Recorded work time") + activityMark, L("本机完成任务", "Recorded tasks") + activityMark, L("最长单次工作", "Longest AI turn"), L("连续天数 · 当前 / 最长", "Streak · current / best") },
                new[] { UsageText.Number(state == null ? null : state.LifetimeTokens, true), UsageText.Number(state == null ? null : state.PeakDailyTokens, true), UsageText.Duration(hasActivity ? (double?)state.Activity.TotalWorkSeconds : null, English, false), hasActivity ? state.Activity.CompletedTasks.ToString("N0") : "—", UsageText.Duration(state == null ? null : state.LongestRunningTurnSec, English, false), (state == null || !state.CurrentStreakDays.HasValue ? "—" : state.CurrentStreakDays.Value.ToString()) + " / " + (state == null || !state.LongestStreakDays.HasValue ? "—" : state.LongestStreakDays.Value.ToString()) });
            scope.Text = ScopeText(totals); footer.Text = FooterText();
            UpdateSettings();
            foreach (RoundedButton button in Buttons()) button.Invalidate();
            LayoutPage(); Invalidate();
        }
        private void UpdateSettings() {
            settingsTheme.Text=L("面板主题","Panel theme");themeMinimal.Text=L("简约","Minimal");themeGlass.Text=L("玻璃","Glass");themeMinimal.Selected=Minimal;themeGlass.Selected=!Minimal;
            settingsLanguage.Text = L("语言", "Language"); settingsAccent.Text = L("主题色", "Accent color"); settingsCustom.Text = L("预设色与自定义调色", "Presets and custom palette");
            settingsCorner.Text = L("右下角显示", "Corner display");
            cornerCard.Text = L("双行玻璃卡", "Glass card"); cornerCapsule.Text = L("玻璃胶囊", "Glass capsule"); cornerTray.Text = L("极简数字托盘", "Number in tray");
            cornerCard.Selected = preferences.CornerStyle == "card"; cornerCapsule.Selected = preferences.CornerStyle == "capsule"; cornerTray.Selected = preferences.CornerStyle == "tray";
            settingsTransparency.Text = L("角落玻璃透明度", "Corner glass transparency");
            glassTransparency.AccessibleName = settingsTransparency.Text;
            glassTransparency.Enabled = preferences.CornerStyle != "tray";
            transparencyValue.Text = preferences.GlassTransparency.ToString() + "%";
            settingsHint.Text = preferences.CornerStyle == "tray" ? L("数字托盘没有玻璃背景。选择玻璃卡或胶囊后可调整透明度。", "The tray number has no glass background. Choose a card or capsule to adjust transparency.") : L("仅调整角落浮窗的玻璃背景，数值和文字始终清晰。数值越大，背景越透明。", "Adjusts the corner glass background only; text stays clear. Higher values make the background more transparent.");
            settingsSaved.Text = L("配色同步到头像标记、图表和右下角入口。设置自动保存在本机。", "Your accent colors the badge, charts and corner display. Preferences are saved on this PC.");
            chinese.Selected = !English; englishButton.Selected = English;
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
                if(Minimal){LayoutMinimalPage();return;}
                minimalOverview.Visible=false;historyTotals.Visible=false;overview.Visible=quota.Visible=homeTasks.Visible=homeNote.Visible=true;summary.Visible=recordTitle.Visible=records.Visible=true;
                var panels = new[] { homePage, mainPage, settingsPage, updatePage };
                var scrolls = new Point[panels.Length];
                for (int i = 0; i < panels.Length; i++) { scrolls[i] = new Point(-panels[i].AutoScrollPosition.X,-panels[i].AutoScrollPosition.Y); panels[i].SuspendLayout(); panels[i].AutoScrollPosition = Point.Empty; }
                Rectangle work = Screen.FromPoint(Visible ? targetLocation : requestedPoint).WorkingArea;
                Size = new Size(Math.Min(Px(448), Math.Max(1, work.Width-Px(16))), Math.Min(Px(732),Math.Max(1,work.Height-Px(16))));
                title.SetBounds(Px(18),Px(14),Px(110),Px(32));
                int right = Width-Px(14);
                close.SetBounds(right-Px(32),Px(12),Px(32),Px(36)); right-=Px(38);
                refresh.SetBounds(right-Px(56),Px(12),Px(56),Px(36)); right-=Px(62);
                appearance.Visible=page!="settings";
                appearance.SetBounds(right-Px(50),Px(12),Px(50),Px(36)); if(page!="settings")right-=Px(56);
                updateEntry.SetBounds(right-Px(68),Px(12),Px(68),Px(36)); updateEntry.Visible=updateView.UpdateAvailable||updateView.Busy;
                title.Width=Math.Max(Px(20),(updateView.UpdateAvailable||updateView.Busy?updateEntry.Left:page=="settings"?refresh.Left:appearance.Left)-title.Left-Px(8));
                bool auxiliary=page=="settings"||page=="update";
                navigationRail.SetBounds(Px(14),Px(59),Width-Px(28),Px(49));
                navigationRail.Visible=homeTab.Visible=historyTab.Visible=!auxiliary;
                back.SetBounds(Px(16),Px(59),Px(88),Px(42)); back.Visible=page=="settings";
                int bodyTop = page=="update"?Px(58):Px(122);
                foreach(var panel in panels)panel.SetBounds(Px(20)+(panel==ActivePage?motionOffset:0),bodyTop,Math.Max(1,Width-Px(40)),Math.Max(1,Height-bodyTop-Px(42)));
                homePage.Visible=page=="home";mainPage.Visible=page=="history";settingsPage.Visible=page=="settings";updatePage.Visible=page=="update";
                footer.SetBounds(Px(18),Height-Px(31),Width-Px(36),Px(22));
                int width=Math.Max(1,homePage.Width-Px(2));
                int estimatedNoteHeight=Math.Max(Px(60),TextRenderer.MeasureText(homeNote.Text,small,new Size(width,0),TextFormatFlags.WordBreak).Height+Px(10));
                bool homeScroll=Px(360+quota.DesiredHeight+15+8)+estimatedNoteHeight>homePage.Height;
                if(homeScroll)width=Math.Max(1,width-SystemInformation.VerticalScrollBarWidth);
                homePeriodRail.SetBounds(0,0,width,Px(44));
                homeDate.SetBounds(0,Px(51),width,Px(24));overview.SetBounds(0,Px(82),width,Px(226));homeTasks.SetBounds(Px(2),Px(317),width-Px(4),Px(28));
                quota.SetBounds(0,Px(360),width,Px(quota.DesiredHeight));
                int homeNoteHeight=Math.Max(Px(60),TextRenderer.MeasureText(homeNote.Text,small,new Size(width,0),TextFormatFlags.WordBreak).Height+Px(10));
                homeNote.SetBounds(Px(2),quota.Bottom+Px(15),width-Px(4),homeNoteHeight);homePage.AutoScrollMinSize=new Size(0,homeNote.Bottom+Px(8));
                width=Math.Max(1,mainPage.Width-SystemInformation.VerticalScrollBarWidth-Px(2));
                int y=0;
                rangeTitle.SetBounds(0,y,Math.Max(Px(100),width-Px(156)),Px(34));
                historyPeriodRail.SetBounds(width-Px(150),y,Px(150),Px(36));
                y+=Px(48);summary.SetBounds(0,y,width,Px(86));y+=Px(105);
                chartTitle.SetBounds(0,y,Math.Max(Px(100),width-Px(138)),Px(34));metricRail.SetBounds(width-Px(136),y,Px(136),Px(36));
                y+=Px(43);bars.SetBounds(0,y,width,Px(174));y+=Px(195);
                heatTitle.SetBounds(0,y,width-Px(96),Px(34));heatPrevious.SetBounds(width-Px(90),y,Px(40),Px(34));heatNext.SetBounds(width-Px(42),y,Px(40),Px(34));y+=Px(39);
                heatRange.SetBounds(0,y,width,Px(20));y+=Px(31);heatmap.SetBounds(0,y,width,Px(248));y+=Px(267);
                recordTitle.SetBounds(0,y,width,Px(24));y+=Px(34);records.SetBounds(0,y,width,Px(241));y+=Px(259);
                int scopeHeight=Math.Max(Px(70),TextRenderer.MeasureText(scope.Text,small,new Size(width,0),TextFormatFlags.WordBreak).Height+Px(12));scope.SetBounds(0,y,width,scopeHeight);y+=scopeHeight+Px(10);mainPage.AutoScrollMinSize=new Size(0,y);
                settingsCorner.SetBounds(0,0,width,Px(24));cornerRail.SetBounds(0,Px(33),width,Px(46));
                settingsTransparency.SetBounds(0,Px(102),width-Px(52),Px(25));transparencyValue.SetBounds(width-Px(48),Px(102),Px(48),Px(25));
                glassTransparency.SetBounds(0,Px(135),width,Px(38));settingsHint.SetBounds(0,Px(182),width,Px(61));
                settingsLanguage.SetBounds(0,Px(260),width,Px(24));languageRail.SetBounds(0,Px(292),Px(220),Px(44));
                settingsAccent.SetBounds(0,Px(366),width,Px(25));settingsCustom.SetBounds(0,Px(395),width,Px(23));palette.SetBounds(0,Px(429),width,Px(296));settingsSaved.SetBounds(0,Px(744),width,Px(64));settingsPage.AutoScrollMinSize=new Size(0,Px(816));
                LayoutThemeSettings(width);
                updateView.SetBounds(0,0,updatePage.Width,updatePage.Height);updatePage.AutoScrollMinSize=Size.Empty;
                for(int i=0;i<panels.Length;i++){panels[i].ResumeLayout();panels[i].AutoScrollPosition=scrolls[i];}
                Region old=Region;using(GraphicsPath path=QuotaAppearance.RoundRectangle(new RectangleF(0,0,Width,Height),Px(22)))Region=new Region(path);if(old!=null)old.Dispose();
                if(Visible&&!animation.Enabled&&!preserveWindowLocation){targetLocation=ClampPoint(requestedPoint,work);Location=targetLocation;}
            } finally { layingOut=false; }
        }
        private void LayoutThemeSettings(int width) {
            settingsTheme.SetBounds(0,0,width,Px(24));themeRail.SetBounds(0,Px(31),Px(220),Px(39));
            settingsCorner.SetBounds(0,Px(93),width,Px(24));cornerRail.SetBounds(0,Px(124),width,Px(42));
            settingsTransparency.SetBounds(0,Px(188),width-Px(52),Px(25));transparencyValue.SetBounds(width-Px(48),Px(188),Px(48),Px(25));
            glassTransparency.SetBounds(0,Px(220),width,Px(38));settingsHint.SetBounds(0,Px(266),width,Px(61));
            settingsLanguage.SetBounds(0,Px(347),width,Px(24));languageRail.SetBounds(0,Px(378),Px(220),Px(39));
            settingsAccent.SetBounds(0,Px(443),width,Px(25));settingsCustom.SetBounds(0,Px(473),width,Px(23));palette.SetBounds(0,Px(506),width,Px(296));settingsSaved.SetBounds(0,Px(821),width,Px(64));settingsPage.AutoScrollMinSize=new Size(0,Px(893));
        }
        private void LayoutMinimalPage() {
            var panels=new[]{homePage,mainPage,settingsPage,updatePage};var scrolls=new Point[panels.Length];
            for(int i=0;i<panels.Length;i++){scrolls[i]=new Point(-panels[i].AutoScrollPosition.X,-panels[i].AutoScrollPosition.Y);panels[i].SuspendLayout();panels[i].AutoScrollPosition=Point.Empty;}
            Rectangle work=Screen.FromPoint(Visible?targetLocation:requestedPoint).WorkingArea;
            Size=new Size(Math.Min(Px(410),Math.Max(1,work.Width-Px(16))),Math.Min(Px(646),Math.Max(1,work.Height-Px(16))));
            int right=Width-Px(20);close.SetBounds(right-Px(24),Px(18),Px(24),Px(24));right-=Px(34);
            bool showAppearance=page!="settings";appearance.Visible=showAppearance;appearance.SetBounds(right-Px(24),Px(18),Px(24),Px(24));if(showAppearance)right-=Px(34);
            refresh.SetBounds(right-Px(24),Px(18),Px(24),Px(24));right-=Px(34);
            bool showUpdate=updateView.UpdateAvailable||updateView.Busy;updateEntry.Visible=showUpdate;updateEntry.SetBounds(right-Px(65),Px(16),Px(65),Px(28));if(showUpdate)right-=Px(72);
            title.SetBounds(Px(24),Px(17),Math.Max(Px(30),right-Px(30)),Px(28));
            bool auxiliary=page=="settings"||page=="update";
            navigationRail.SetBounds(Px(20),Px(61),Width-Px(40),Px(41));navigationRail.Visible=homeTab.Visible=historyTab.Visible=!auxiliary;
            back.SetBounds(Px(20),Px(65),Px(80),Px(30));back.Visible=page=="settings";
            int bodyTop=page=="update"?Px(59):Px(122);
            foreach(var panel in panels)panel.SetBounds(Px(20)+(panel==ActivePage?motionOffset:0),bodyTop,Math.Max(1,Width-Px(40)),Math.Max(1,Height-bodyTop-Px(40)));
            homePage.Visible=page=="home";mainPage.Visible=page=="history";settingsPage.Visible=page=="settings";updatePage.Visible=page=="update";
            footer.SetBounds(Px(24),Height-Px(30),Width-Px(48),Px(20));
            minimalOverview.Visible=true;overview.Visible=quota.Visible=homeTasks.Visible=homeNote.Visible=false;
            int width=homePage.Width-Px(2);bool needsScroll=Px(minimalOverview.DesiredHeight+40)>homePage.Height;if(needsScroll)width-=SystemInformation.VerticalScrollBarWidth;width=Math.Max(1,width);
            homeDate.SetBounds(Px(4),0,Px(136),Px(26));homePeriodRail.SetBounds(Math.Max(Px(136),width-Px(224)),0,Math.Min(Px(224),width-Px(136)),Px(27));
            minimalOverview.SetBounds(Px(4),Px(40),Math.Max(1,width-Px(8)),Px(minimalOverview.DesiredHeight));homePage.AutoScrollMinSize=new Size(0,minimalOverview.Bottom);
            width=Math.Max(1,mainPage.Width-SystemInformation.VerticalScrollBarWidth-Px(2));
            rangeTitle.SetBounds(Px(4),0,width-Px(146),Px(27));historyPeriodRail.SetBounds(width-Px(138),0,Px(138),Px(27));
            historyTotals.Visible=true;historyTotals.SetBounds(Px(4),Px(27),width-Px(4),Px(16));
            summary.Visible=false;chartTitle.SetBounds(Px(4),Px(43),width-Px(142),Px(26));metricRail.SetBounds(width-Px(134),Px(43),Px(134),Px(26));
            bars.SetBounds(0,Px(78),width,Px(162));
            heatTitle.SetBounds(Px(4),Px(248),width-Px(82),Px(28));heatPrevious.SetBounds(width-Px(72),Px(248),Px(30),Px(28));heatNext.SetBounds(width-Px(34),Px(248),Px(30),Px(28));
            heatRange.SetBounds(Px(4),Px(277),width-Px(4),Px(21));heatmap.SetBounds(0,Px(305),width,Px(170));
            recordTitle.Visible=records.Visible=true;recordTitle.SetBounds(Px(4),Px(521),width-Px(4),Px(25));records.SetBounds(0,Px(557),width,Px(241));
            int scopeHeight=Math.Max(Px(70),TextRenderer.MeasureText(scope.Text,small,new Size(width,0),TextFormatFlags.WordBreak).Height+Px(12));scope.SetBounds(Px(4),Px(817),width-Px(4),scopeHeight);mainPage.AutoScrollMinSize=new Size(0,scope.Bottom+Px(10));
            LayoutThemeSettings(width);
            updateView.SetBounds(0,0,updatePage.Width,updatePage.Height);updatePage.AutoScrollMinSize=Size.Empty;
            for(int i=0;i<panels.Length;i++){panels[i].ResumeLayout();panels[i].AutoScrollPosition=scrolls[i];}
            Region previous=Region;using(var path=QuotaAppearance.RoundRectangle(new RectangleF(0,0,Width,Height),Px(16)))Region=new Region(path);if(previous!=null)previous.Dispose();
            if(Visible&&!animation.Enabled&&!preserveWindowLocation){targetLocation=ClampPoint(requestedPoint,work);Location=targetLocation;}
        }
        private Point ClampPoint(Point point,Rectangle work) { return new Point(Math.Max(work.Left+Px(8),Math.Min(point.X,work.Right-Width-Px(8))),Math.Max(work.Top+Px(8),Math.Min(point.Y,work.Bottom-Height-Px(8)))); }
        private void Navigate(string next) {
            if(page==next)return;
            if(next=="settings"||next=="update") { if(page=="home"||page=="history")returnPage=page; }
            animation.Stop(); motionOffset = 0; page=next; Opacity=1;
            preserveWindowLocation = true;
            try { UpdateHeader(); LayoutPage(); }
            finally { preserveWindowLocation = false; }
            if(Visible&&SystemInformation.IsMenuAnimationEnabled) StartPageMotion();
        }
        public void ShowUpdates() {
            Navigate("update");
            if(!Visible){EventHandler handler=OpenUpdateRequested;if(handler!=null)handler(this,EventArgs.Empty);}
        }
        public void ShowAt(Point point) {
            bool animate=!Visible&&SystemInformation.IsMenuAnimationEnabled;
            animation.Stop(); motionOffset=0; requestedPoint=point;LayoutPage();targetLocation=ClampPoint(point,Screen.FromPoint(point).WorkingArea);
            Location=targetLocation;Opacity=1;
            if(animate)StartPageMotion();
            if(!Visible)Show();Activate();
        }
        private Panel ActivePage { get { return page=="home"?homePage:page=="history"?mainPage:page=="settings"?settingsPage:updatePage; } }
        private void StartPageMotion() {
            motionOffset=Px(2);ActivePage.Left=Px(20)+motionOffset;
            animationClock.Restart();animation.Start();
        }
        private void Animate(object sender,EventArgs e) {
            if(!animation.Enabled)return;
            double progress=Math.Min(1,animationClock.Elapsed.TotalMilliseconds/120),eased=1-Math.Pow(1-progress,3);
            motionOffset=(int)Math.Round(Px(2)*(1-eased));ActivePage.Left=Px(20)+motionOffset;
            if(progress>=1){animation.Stop();animationClock.Reset();motionOffset=0;ActivePage.Left=Px(20);}
        }
        protected override void OnFormClosing(FormClosingEventArgs e){if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}base.OnFormClosing(e);}
        protected override void OnPaintBackground(PaintEventArgs e) {
            if(Width<1||Height<1)return;
            if(Minimal){using(var fill=new LinearGradientBrush(ClientRectangle,Color.FromArgb(255,254,255),Color.FromArgb(250,249,252),125f))e.Graphics.FillRectangle(fill,ClientRectangle);return;}
            using(var fill=new LinearGradientBrush(ClientRectangle,QuotaAppearance.Blend(preferences.AccentColor,Color.FromArgb(246,243,249),.86f),Color.FromArgb(246,247,251),55f))e.Graphics.FillRectangle(fill,ClientRectangle);
            using(var cloud=new GraphicsPath()){
                cloud.AddEllipse(new RectangleF(Width*.3f,-Height*.26f,Width*1.25f,Height*.8f));
                using(var light=new PathGradientBrush(cloud)){light.CenterColor=Color.FromArgb(105,222,229,246);light.SurroundColors=new[]{Color.FromArgb(0,239,242,250)};e.Graphics.FillPath(light,cloud);}
            }
            using(var footerFill=new SolidBrush(Color.FromArgb(95,Color.White)))e.Graphics.FillRectangle(footerFill,0,Height-Px(41),Width,Px(41));
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            if(Minimal){using(var line=new Pen(MinimalMaterial.Line))e.Graphics.DrawLine(line,Px(24),Height-Px(39),Width-Px(24),Height-Px(39));return;}
            using(Pen border=new Pen(Color.FromArgb(180,Color.White)))using(GraphicsPath path=QuotaAppearance.RoundRectangle(new RectangleF(.8f,.8f,Width-1.6f,Height-1.6f),Px(22)))e.Graphics.DrawPath(border,path);
            using(Pen line=new Pen(Color.FromArgb(22,preferences.AccentColor)))e.Graphics.DrawLine(line,Px(18),Height-Px(39),Width-Px(18),Height-Px(39));
        }
        protected override void Dispose(bool disposing) { if(disposing){StopWatchingOutsideClicks();outsideClickDelay.Dispose();animation.Dispose();}base.Dispose(disposing);if(disposing){regular.Dispose();small.Dispose();valueFont.Dispose();headingFont.Dispose();} }
    }

    internal sealed class MetricCards : Control
    {
        internal bool Minimal;
        private readonly float scale;
        private readonly Font labelFont, valueFont;
        private readonly int columns;
        private string[] names = new string[0], values = new string[0], hints = new string[0];
        private double? quotaValue;
        private bool hasQuotaMeter, quotaStale;
        internal Color Accent = QuotaAppearance.Green;
        internal MetricCards(float factor, Font small, Font large, int count)
        {
            scale = factor; labelFont = small; valueFont = large; columns = count;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }
        internal void SetItems(string[] labels, string[] numbers) { SetItems(labels, numbers, new string[0]); }
        internal void SetItems(string[] labels, string[] numbers, string[] notes) { names = labels; values = numbers; hints = notes; AccessibleName = String.Join("; ", labels) + ": " + String.Join("; ", numbers) + ". " + String.Join("; ", notes); Invalidate(); }
        internal void SetQuota(double? remaining, bool stale) { hasQuotaMeter = true; quotaValue = remaining; quotaStale = stale; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (Width < 5 || Height < 8) return;
            int rows = Math.Max(1, (int)Math.Ceiling(names.Length / (double)columns));
            RectangleF surface = new RectangleF(1.5f * scale, 1.5f * scale, Width - 3 * scale, Height - 7 * scale);
            if(!Minimal)GlassMaterial.Surface(e.Graphics, surface, 18 * scale, QuotaAppearance.Blend(Accent, Color.FromArgb(250, 246, 251), .96f), scale, true);
            float width = surface.Width / columns, height = surface.Height / rows;
            using (Pen line = new Pen(Color.FromArgb(225, 221, 231), scale))
            {
                for (int column = 1; column < columns; column++) e.Graphics.DrawLine(line, surface.X + column * width, surface.Y, surface.X + column * width, surface.Bottom);
                for (int row = 1; row < rows; row++) e.Graphics.DrawLine(line, surface.X, surface.Y + row * height, surface.Right, surface.Y + row * height);
            }
            for (int i = 0; i < names.Length; i++)
            {
                RectangleF cell = new RectangleF(surface.X + (i % columns) * width, surface.Y + (i / columns) * height, width, height);
                float inset = (columns > 2 ? 11 : 15) * scale;
                Rectangle label = Rectangle.Round(new RectangleF(cell.X + inset, cell.Y + 14 * scale, width - inset * 2, 20 * scale));
                Rectangle value = Rectangle.Round(new RectangleF(cell.X + inset, cell.Y + 37 * scale, width - inset * 2, 34 * scale));
                TextRenderer.DrawText(e.Graphics, names[i], labelFont, label, QuotaAppearance.Muted, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                string number = i < values.Length ? values[i] : "—";
                float fontSize = valueFont.Size;
                while (fontSize > 18 * scale)
                {
                    using (Font trial = new Font(valueFont.FontFamily, fontSize, valueFont.Style, GraphicsUnit.Pixel))
                        if (TextRenderer.MeasureText(e.Graphics, number, trial, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width <= value.Width) break;
                    fontSize -= scale;
                }
                using (Font fitted = new Font(valueFont.FontFamily, Minimal?Math.Min(fontSize,23*scale):fontSize, Minimal?FontStyle.Regular:valueFont.Style, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(e.Graphics, number, fitted, value, hasQuotaMeter && i == 1 && quotaStale ? QuotaAppearance.Muted : QuotaAppearance.Text, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                if (hasQuotaMeter && i == 1)
                {
                    float y = cell.Y + 77 * scale;
                    using (Pen track = new Pen(Color.FromArgb(233, 227, 234), 4 * scale))
                    {
                        track.StartCap = track.EndCap = LineCap.Round;
                        e.Graphics.DrawLine(track, cell.X + inset + 2 * scale, y, cell.Right - inset - 2 * scale, y);
                    }
                    if (quotaValue.HasValue && !Double.IsNaN(quotaValue.Value) && !Double.IsInfinity(quotaValue.Value) && quotaValue.Value > 0)
                        using (Pen meter = new Pen(quotaStale ? Color.FromArgb(185, 179, 189) : Color.FromArgb(165, 158, 176), 4 * scale))
                        {
                            meter.StartCap = meter.EndCap = LineCap.Round;
                            float left = cell.X + inset + 2 * scale, meterWidth = width - inset * 2 - 4 * scale;
                            e.Graphics.DrawLine(meter, left, y, left + meterWidth * (float)Math.Min(100, quotaValue.Value) / 100, y);
                        }
                }
                if (i < hints.Length)
                {
                    float top = hasQuotaMeter && i == 1 ? 88 * scale : 75 * scale;
                    Rectangle note = Rectangle.Round(new RectangleF(cell.X + inset, cell.Y + top, width - inset * 2, Math.Max(18 * scale, height - top - 3 * scale)));
                    TextRenderer.DrawText(e.Graphics, hints[i], labelFont, note, QuotaAppearance.Muted, TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                }
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
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; DesiredHeight = 112; tooltip.InitialDelay = 100; tooltip.AutoPopDelay = 15000;
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
            float cardWidth = Width / (float)columns;
            int rows = (int)Math.Ceiling(count / (double)columns);
            RectangleF allCards = new RectangleF(1, 29 * scale, Width - 3, rows * 94 * scale - 8 * scale);
            GlassMaterial.Surface(e.Graphics, allCards, 15 * scale, QuotaAppearance.Blend(accent, Color.FromArgb(250, 247, 252), .97f), scale, false);
            using (Pen separator = new Pen(Color.FromArgb(225, 221, 231), scale))
            {
                if (columns == 2) e.Graphics.DrawLine(separator, cardWidth, allCards.Y, cardWidth, allCards.Bottom);
                for (int row = 1; row < rows; row++) e.Graphics.DrawLine(separator, allCards.X, allCards.Y + row * 94 * scale, allCards.Right, allCards.Y + row * 94 * scale);
            }
            for (int i = 0; i < count; i++)
            {
                QuotaWindow window = state.Windows[i]; if (window == null) continue;
                RectangleF card = new RectangleF((i % columns) * cardWidth, 29 * scale + (i / columns) * 94 * scale, cardWidth, 85 * scale); areas.Add(card);
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
