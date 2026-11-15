using Microsoft.Win32;

namespace WinExpander;

/// <summary>"Start with Windows" via HKCU\...\Run (per-user, no admin). Starts hidden in the tray.</summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WinExpander";
    public const string TrayArg = "--tray";

    private static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "WinExpander.exe");

    /// <summary>True only if the Run entry points at this exe (portable app may have moved).</summary>
    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string cmd && cmd.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Moves a "start with Windows" entry from the old app name (Text Expander) to this exe.</summary>
    public static void MigrateLegacy()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue("WinTextExpander") == null) return;
            key.DeleteValue("WinTextExpander", throwOnMissingValue: false);
            Set(true);
        }
        catch (Exception ex) { Program.Log(ex); }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{ExePath}\" {TrayArg}");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
