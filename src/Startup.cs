using System;
using Microsoft.Win32;

internal static class Startup
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static bool Registered
    {
        get { using (var key = Registry.CurrentUser.OpenSubKey(Key)) return key != null && string.Equals(key.GetValue("VK01ClickControl") as string, Command, StringComparison.OrdinalIgnoreCase); }
    }
    internal static bool BlockedByWindows
    {
        get
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"))
            {
                var value = key == null ? null : key.GetValue("VK01ClickControl") as byte[];
                return value != null && value.Length > 0 && (value[0] == 3 || value[0] == 7);
            }
        }
    }
    internal static bool Enabled { get { return Registered && !BlockedByWindows; } }
    private static string Command { get { return "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --background"; } }
    internal static void Set(bool enabled)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(Key))
        {
            if (enabled) key.SetValue("VK01ClickControl", Command); else key.DeleteValue("VK01ClickControl", false);
        }
    }
}
