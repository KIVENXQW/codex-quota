using System;
using System.IO;
using Microsoft.Win32;

namespace Kiven.WeeklyQuota
{
    internal static class WeeklyLoginStartupTests
    {
        private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Approval = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        private static void Expect(bool ok, string message)
        {
            if (!ok) throw new Exception(message);
        }

        public static int Main()
        {
            string isolated = @"Software\KivenWeeklyTests\" + Guid.NewGuid().ToString("N");
            try
            {
                using (RegistryKey root = Registry.CurrentUser.CreateSubKey(isolated))
                {
                    string path = Path.Combine(Path.GetTempPath(), "周额度 test", "CodexWeekly.exe");
                    WeeklyLoginStartup setting = new WeeklyLoginStartup(root, path);
                    Expect(!setting.Enabled, "Startup must be opt-in");
                    using (RegistryKey key = root.OpenSubKey(Run)) Expect(key == null, "Reading made a startup entry");
                    using (RegistryKey key = root.CreateSubKey(Run)) key.SetValue("CodexQuota", "other-edition");
                    setting.SetEnabled(true);
                    Expect(setting.Enabled, "Enable failed");
                    using (RegistryKey key = root.OpenSubKey(Run))
                        Expect((string)key.GetValue(WeeklyLoginStartup.ValueName) == "\"" + path + "\" --login-startup", "Wrong path or argument");
                    using (RegistryKey key = root.CreateSubKey(Approval)) {
                        key.SetValue(WeeklyLoginStartup.ValueName, new byte[] { 3,0,0,0,0,0,0,0,0,0,0,0 });
                        key.SetValue("CodexQuota", new byte[] { 3 });
                    }
                    Expect(!setting.Enabled, "Task Manager disabled state was ignored");
                    setting.SetEnabled(true);
                    Expect(setting.Enabled, "Explicit enable did not restore this edition");
                    setting.SetEnabled(false);
                    setting.SetEnabled(false);
                    Expect(!setting.Enabled, "Disable failed");
                    using (RegistryKey key = root.OpenSubKey(Run, true)) {
                        Expect(key.GetValue(WeeklyLoginStartup.ValueName) == null, "Disabled command remains");
                        Expect((string)key.GetValue("CodexQuota") == "other-edition", "Changed other edition");
                        key.SetValue(WeeklyLoginStartup.ValueName, "foreign.exe");
                    }
                    foreach (bool value in new bool[] { true, false }) {
                        try { setting.SetEnabled(value); throw new Exception("Clobbered foreign startup entry"); }
                        catch (InvalidOperationException) { }
                    }
                    using (RegistryKey key = root.OpenSubKey(Approval))
                        Expect(((byte[])key.GetValue("CodexQuota"))[0] == 3, "Changed other edition's disable flag");
                }
                Console.WriteLine("Weekly opt-in startup: registry state, quoted path, enable/disable, Windows disable and cross-edition isolation passed.");
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
            finally { Registry.CurrentUser.DeleteSubKeyTree(isolated, false); }
        }
    }
}
