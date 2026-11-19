using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace WinExpander;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private readonly IntPtr _hwnd;
    private readonly Expander _expander;
    private readonly TrayIcon _tray;
    private bool _exiting;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = MainViewModel.Instance;
        if (Environment.GetCommandLineArgs().Contains(Startup.TrayArg))
        {
            ViewModel.SetWindowVisible(false);
            TrimMemorySoon();
        }

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 720));

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WindowHook.Apply(_hwnd, 760, 480);
        ClipboardHelper.Owner = _hwnd;

        _expander = new Expander(ViewModel, DispatcherQueue, _hwnd)
        {
            Enabled = ViewModel.ExpansionEnabled
        };

        _tray = new TrayIcon(_hwnd, Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"))
        {
            IsPaused = () => !ViewModel.ExpansionEnabled
        };
        _tray.OpenRequested += ShowFromTray;
        _tray.PauseToggled += () => ViewModel.ExpansionEnabled = !ViewModel.ExpansionEnabled;
        _tray.ExitRequested += ExitApp;
        WindowHook.Handler = _tray.HandleMessage;

        ViewModel.PropertyChanged += OnViewModelChanged;
        ViewModel.OpenSettingsRequested += () => Nav.SelectedItem = Nav.SettingsItem;
        ApplyTheme();
        Root.ActualThemeChanged += (_, _) => UpdateCaptionButtons();

        Nav.SelectedItem = SnippetsItem;
        // Hidden arg used by CI screenshots (SettingsItem exists only after the template loads)
        if (Environment.GetCommandLineArgs().Contains("--settings"))
            Nav.Loaded += (_, _) => Nav.SelectedItem = Nav.SettingsItem;

        AppWindow.Closing += (_, e) =>
        {
            if (!_exiting && ViewModel.CloseToTray)
            {
                e.Cancel = true;
                ViewModel.SaveNow();
                AppWindow.Hide();
                ViewModel.SetWindowVisible(false);
                TrimMemorySoon();
            }
        };

        Closed += (_, _) =>
        {
            ViewModel.SaveNow();
            _expander.Dispose();
            _tray.Dispose();
        };
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.ExpansionEnabled):
                _expander.Enabled = ViewModel.ExpansionEnabled;
                _tray.SetTooltip(ViewModel.ExpansionEnabled ? "WinExpander" : "WinExpander (paused)");
                break;
            case nameof(MainViewModel.ThemeIndex):
                ApplyTheme();
                break;
        }
    }

    // ---------- Navigation ----------

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = args.IsSettingsSelected ? typeof(SettingsPage) : typeof(SnippetsPage);
        if (ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
    }

    // ---------- Tray / window ----------

    private void ShowFromTray()
    {
        ViewModel.SetWindowVisible(true);
        Activate();
        if (AppWindow.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Minimized) p.Restore();
        SetForegroundWindow(_hwnd);
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    /// <summary>After hiding to the tray, give memory back to Windows once the preview has shut down.</summary>
    private async void TrimMemorySoon()
    {
        await Task.Delay(3000);
        if (ViewModel.IsWindowVisible) return;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);

    // ---------- Theme ----------

    private void ApplyTheme()
    {
        var theme = (AppTheme)Math.Max(0, ViewModel.ThemeIndex);
        Root.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        TrayIcon.SetMenuTheme(theme);
        UpdateCaptionButtons();
    }

    // Min / max / close buttons don't follow RequestedTheme on their own
    private void UpdateCaptionButtons()
    {
        var tb = AppWindow.TitleBar;
        bool dark = Root.ActualTheme == ElementTheme.Dark;
        var fg = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        tb.ButtonForegroundColor = fg;
        tb.ButtonHoverForegroundColor = fg;
        tb.ButtonPressedForegroundColor = fg;
        tb.ButtonInactiveForegroundColor = dark
            ? Windows.UI.Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)
            : Windows.UI.Color.FromArgb(0x80, 0x00, 0x00, 0x00);
        tb.ButtonHoverBackgroundColor = dark
            ? Windows.UI.Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)
            : Windows.UI.Color.FromArgb(0x0F, 0x00, 0x00, 0x00);
        tb.ButtonPressedBackgroundColor = dark
            ? Windows.UI.Color.FromArgb(0x0B, 0xFF, 0xFF, 0xFF)
            : Windows.UI.Color.FromArgb(0x0A, 0x00, 0x00, 0x00);
    }
}
