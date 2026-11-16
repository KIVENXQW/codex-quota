using System;
using System.IO;
using Microsoft.Win32;

namespace Kiven.WeeklyQuota
{
    internal sealed class WeeklyLoginStartup
    {
        internal const string ValueName = "Kiven.CodexWeekly";
        internal const string LoginArgument = "--login-startup";
        private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Approval = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private readonly RegistryKey user;
        private readonly string command;

        internal WeeklyLoginStartup(RegistryKey user, string executable)
        {
            this.user = user;
            string path = Path.GetFullPath(executable);
            if (!String.Equals(Path.GetFileName(path), "CodexWeekly.exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("极简版启动程序应为 CodexWeekly.exe。");
            command = "\"" + path + "\" " + LoginArgument;
        }

        private static bool IsWeeklyCommand(object entry)
        {
            string text = entry as string;
            string suffix = "\" " + LoginArgument;
            if (text == null || !text.StartsWith("\"", StringComparison.Ordinal) ||
                !text.EndsWith(suffix, StringComparison.Ordinal) || text.Length <= suffix.Length + 1) return false;
            string path = text.Substring(1, text.Length - suffix.Length - 1);
            return path.IndexOf('"') < 0 && Path.IsPathRooted(path) &&
                String.Equals(Path.GetFileName(path), "CodexWeekly.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static object Read(RegistryKey root, string subkey)
        {
            using (RegistryKey key = root.OpenSubKey(subkey))
                return key == null ? null : key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }

        private bool DisabledByWindows()
        {
            byte[] approval = Read(user, Approval) as byte[];
            return approval != null && approval.Length >= 1 && (approval[0] == 3 || approval[0] == 7);
        }

        internal bool Enabled
        {
            get
            {
                object registered = Read(user, Run);
                if (registered == null) return false;
                if (!IsWeeklyCommand(registered))
                    throw new InvalidOperationException("极简版启动项名称已被其他程序占用。");
                return !DisabledByWindows();
            }
        }

        internal void SetEnabled(bool enabled)
        {
            object existing = Read(user, Run);
            if (existing != null && !IsWeeklyCommand(existing))
                throw new InvalidOperationException("极简版启动项名称已被其他程序占用。");
            if (enabled)
            {
                using (RegistryKey key = user.CreateSubKey(Run))
                    key.SetValue(ValueName, command, RegistryValueKind.String);
                // An explicit re-enable also releases this entry's Task Manager disable flag.
                if (DisabledByWindows())
                    using (RegistryKey approval = user.OpenSubKey(Approval, true))
                        if (approval != null) approval.DeleteValue(ValueName, false);
            }
            else if (existing != null)
            {
                using (RegistryKey key = user.OpenSubKey(Run, true))
                    if (key != null) key.DeleteValue(ValueName, false);
            }
        }
    }
}
