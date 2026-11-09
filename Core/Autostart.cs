using Microsoft.Win32;

namespace pxdToolkit.Core;

/// <summary>Start pxd Toolkit with Windows (in the tray) through a per-user Run entry.</summary>
public static class Autostart
{
    private const string Name = "pxdToolkit";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string? Command => Environment.ProcessPath is { } exe ? $"\"{exe}\" --minimized" : null;

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string v && string.Equals(v, Command, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Command is { } cmd)
            key.SetValue(Name, cmd);
        else
            key.DeleteValue(Name, throwOnMissingValue: false);
    }
}
