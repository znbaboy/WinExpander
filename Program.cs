using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace TextExpander;

/// <summary>
/// Custom entry point so startup failures are logged (crash.log next to the exe)
/// and shown, instead of the app silently exiting.
/// </summary>
public static class Program
{
    private static Mutex? _singleInstance;

    [STAThread]
    public static int Main(string[] args)
    {
        // Single instance: if already running (e.g. hidden in the tray), bring it to the front
        _singleInstance = new Mutex(true, "WinTextExpander.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            PostMessage((IntPtr)0xFFFF /* HWND_BROADCAST */, TrayIcon.WM_SHOWAPP, IntPtr.Zero, IntPtr.Zero);
            return 0;
        }

        // Keep WebView2's cache out of the portable folder; transparent preview background
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER",
            Path.Combine(Path.GetTempPath(), "TextExpander.WebView2"));
        Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "00000000");

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception);
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(_ =>
            {
                var ctx = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(ctx);
                var app = new App();
                app.UnhandledException += (_, e) => { Report(e.Exception); };
            });
            return 0;
        }
        catch (Exception ex)
        {
            Report(ex);
            return 1;
        }
    }

    /// <summary>Logs a non-fatal error.</summary>
    internal static void Log(Exception? ex)
    {
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), $"{DateTime.Now:u}\r\n{ex}\r\n"); } catch { }
    }

    internal static void Report(Exception? ex)
    {
        Log(ex);
        if (Environment.GetEnvironmentVariable("TEXTEXPANDER_NO_DIALOG") != "1")
            MessageBoxW(IntPtr.Zero, ex?.ToString() ?? "Unknown error", "Text Expander crashed", 0x10);
    }

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
