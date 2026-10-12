using Microsoft.Win32;

namespace BtVolumeGuard;

/// <summary>
/// "Start with Windows": a per-user value under HKCU\...\Run pointing at this executable.
/// Changed only when the user clicks the tray menu item.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BtVolumeGuard";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    /// <summary>True if the Run value points at this executable.</summary>
    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value
            && value.Equals(Command, StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, Command);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
