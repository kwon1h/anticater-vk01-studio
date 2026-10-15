using System;
using Microsoft.Win32;

internal static class Startup
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static bool Enabled
    {
        get { using (var key = Registry.CurrentUser.OpenSubKey(Key)) return key != null && string.Equals(key.GetValue("VK01ClickControl") as string, Command, StringComparison.OrdinalIgnoreCase); }
    }
    private static string Command { get { return "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --background"; } }
    internal static void Set(bool enabled)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(Key))
        {
            if (enabled) key.SetValue("VK01ClickControl", Command); else key.DeleteValue("VK01ClickControl", false);
        }
    }
}
