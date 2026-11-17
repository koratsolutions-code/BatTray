using BatTray.App.Diagnostics;
using Microsoft.Win32;

namespace BatTray.App.Services;

/// <summary>"Start with Windows" through the per-user Run key (no admin rights needed).</summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BatTray";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
                key.SetValue(ValueName, Command);
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            Log.Info($"Start with Windows {(enabled ? "enabled" : "disabled")}.");
        }
        catch (Exception ex)
        {
            Log.Error("Could not change the start-with-Windows setting.", ex);
        }
    }

    /// <summary>If enabled from an older install location, points the entry at the current executable.</summary>
    public static void RefreshPathIfEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        if (key?.GetValue(ValueName) is string value && !string.Equals(value, Command, StringComparison.OrdinalIgnoreCase))
            SetEnabled(true);
    }
}
