using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using CodexQuota;

class LiquidGlassViewTests
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static Control Named(Control parent, string name)
    {
        Control[] found = parent.Controls.Find(name, true);
        Check(found.Length == 1, "Missing or duplicated control: " + name);
        return found[0];
    }
    static object Field(object instance, string name) { return instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(instance); }
    static object Property(object instance, string name) { return instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(instance, null); }
    static void Click(Control control) { typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { EventArgs.Empty }); }
    static void Key(Control control, Keys key) { control.GetType().GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { new KeyEventArgs(key) }); }
    [STAThread] static int Main()
    {
        try { Run(); Console.WriteLine("Liquid glass views: " + checks + " checks PASS"); return 0; }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + (error.InnerException ?? error).Message); return 1; }
    }
    static void Run()
    {
        using (var popup = new QuotaPopup())
        {
            Control home = Named(popup, "homeTab"), history = Named(popup, "historyTab");
            Check(home.Parent == history.Parent && home.Parent != popup, "Main tabs must share one inset navigation rail");
            Check(home.Bounds.Right <= history.Left && history.Right <= history.Parent.Width, "Tabs must fit inside the shared rail without overlap");
            Key(home, Keys.Right);
            Check((string)Field(popup, "page") == "history", "Right arrow on Home navigates to History");
            Key(history, Keys.Left);
            Check((string)Field(popup, "page") == "home", "Left arrow on History navigates to Home");

            Control overview = Named(popup, "overview");
            Check(((Font)Field(overview, "valueFont")).Bold, "Metric values must have the approved bold hierarchy");
            var state = new QuotaState { Available = true, UpdatedAt = DateTime.UtcNow.ToString("o"), Activity = new WorkActivity { Status = "ready" } };
            state.Windows.Add(new QuotaWindow { Label = "本周", Remaining = 100 });
            string today = DateTime.UtcNow.AddHours(8).ToString("yyyy-MM-dd");
            state.DailyTokens.Add(new TokenDay { Date = today, Tokens = 2060000 });
            state.Activity.DailyBuckets.Add(new WorkDay { Date = today, WorkSeconds = 9720, CompletedTasks = 4 });
            popup.SetState(state);
            string[] values = (string[])Field(overview, "values");
            Check(values[0] == "2.1M" && values[1] == "100%", "The continuous overview retains real token and 100% quota values");
            Check(values[3] == "—", "Different account/local scopes never fabricate a consumption rate");

            Click((Control)Field(popup, "appearance"));
            Control card = Named(popup, "cornerCard"), capsule = Named(popup, "cornerCapsule"), tray = Named(popup, "cornerTray");
            Control slider = Named(popup, "glassTransparency");
            Check(card.Parent == capsule.Parent && capsule.Parent == tray.Parent, "Corner choices form a single keyboard-operable segment group");
            Check((int)Property(slider, "Minimum") == 10 && (int)Property(slider, "Maximum") == 80, "Transparency must be restricted to 10–80 percent");
            Check((int)Property(slider, "Value") == 45 && slider.Enabled, "Card starts with 45% background transparency");
            int changes = 0;
            popup.PreferencesChanged += delegate { changes++; };
            Click(tray);
            Check((string)Field(popup.Preferences, "CornerStyle") == "tray" && !slider.Enabled, "Tray selection updates preferences and disables background transparency");
            Click(capsule);
            Check((string)Field(popup.Preferences, "CornerStyle") == "capsule" && slider.Enabled, "Capsule selection restores the glass slider");
            Key(slider, Keys.End);
            Check((int)Field(popup.Preferences, "GlassTransparency") == 80 && changes == 3, "Keyboard transparency changes publish exactly one preference update");
            Check(Named(popup, "transparencyValue").Text == "80%", "The percentage readout follows the slider");
            popup.ApplyPreferences(popup.Preferences);
            Check(changes == 3 && (string)Field(popup.Preferences, "CornerStyle") == "capsule", "Applying preferences preserves corner settings without firing feedback events");
            popup.ApplyPreferences(new UserPreferences { Language = "en", AccentHtml = "#477ABB" });
            Check(card.Text == "Glass card" && tray.Text == "Number in tray", "Corner choices translate to English");
            Check(slider.AccessibleName == "Corner glass transparency", "The slider exposes its purpose to accessibility clients");
            Click((Control)Field(popup, "chinese"));
            Check(home.Text == "首页", "Changing the language from settings refreshes the whole interface");
            Click((Control)Field(popup, "englishButton"));
            Check(home.Text == "Home", "Language can be switched back without losing live settings");
            Control settingPage = Named(popup, "settingsPage");
            Check(slider.Bottom <= ((Control)Field(popup, "settingsHint")).Top, "Slider and explanatory text do not overlap");
            Check(card.Parent.Right <= settingPage.ClientSize.Width, "The mode selector fits the settings viewport");
            using (var bitmap = new Bitmap(popup.Width, popup.Height)) popup.DrawToBitmap(bitmap, popup.ClientRectangle);
            popup.SetState(new QuotaState());
            Click(home);
            values = (string[])Field(overview, "values");
            Check(values[0] == "—" && values[1] == "—" && values[3] == "—", "Missing usage, quota and rates remain explicitly unavailable");
            Check(popup.Width <= Screen.PrimaryScreen.WorkingArea.Width && popup.Height <= Screen.PrimaryScreen.WorkingArea.Height, "The popup stays within the working area");
        }
    }
}
