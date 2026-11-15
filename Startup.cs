using Microsoft.Win32;

namespace TextExpander;

/// <summary>"Start with Windows" via HKCU\...\Run (per-user, no admin). Starts hidden in the tray.</summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WinTextExpander";
    public const string TrayArg = "--tray";

    private static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "TextExpander.exe");

    /// <summary>True only if the Run entry points at this exe (portable app may have moved).</summary>
    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string cmd && cmd.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{ExePath}\" {TrayArg}");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
