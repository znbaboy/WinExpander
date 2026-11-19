using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace WinExpander;

/// <summary>App-wide state shared by the window, pages, tray and expander. Create on the UI thread.</summary>
public partial class MainViewModel : ObservableObject
{
    private static MainViewModel? _instance;
    public static MainViewModel Instance => _instance ??= new MainViewModel();

    public ObservableCollection<Snippet> Snippets { get; } = SnippetStore.Load();

    private readonly DispatcherQueueTimer _saveTimer;
    private readonly bool _loading = true;

    // ---------- Snippets ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSnippetCommand))]
    private Snippet? _selected;

    public bool HasSelection => Selected != null;

    /// <summary>Bumped on any snippet change so the expander can cache its trigger table.</summary>
    public int SnippetsVersion { get; private set; }

    /// <summary>False while the window is hidden in the tray; pages release heavy resources.</summary>
    public bool IsWindowVisible { get; private set; } = true;
    public event Action<bool>? WindowVisibilityChanged;

    public void SetWindowVisible(bool visible)
    {
        if (IsWindowVisible == visible) return;
        IsWindowVisible = visible;
        WindowVisibilityChanged?.Invoke(visible);
    }

    [ObservableProperty] private bool _expansionEnabled = true;
    [ObservableProperty] private string _saveStatus = "";

    // ---------- Settings ----------

    [ObservableProperty] private int _themeIndex;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private int _plainTextIndex;

    public string Version => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "";

    private MainViewModel()
    {
        _saveTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(600);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => SaveNow();

        foreach (var s in Snippets) s.PropertyChanged += OnSnippetChanged;
        Snippets.CollectionChanged += OnSnippetsChanged;

        var settings = AppSettings.Current;
        Startup.MigrateLegacy();
        ThemeIndex = (int)settings.Theme;
        CloseToTray = settings.CloseToTray;
        StartWithWindows = Startup.IsEnabled;
        PlainTextIndex = (int)settings.PlainText;
        _loading = false;
    }

    private void OnSnippetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Snippet.Trigger) or nameof(Snippet.Text)) SnippetsVersion++;
        QueueSave();
    }

    private void OnSnippetsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (Snippet s in e.NewItems ?? Array.Empty<Snippet>()) s.PropertyChanged += OnSnippetChanged;
        foreach (Snippet s in e.OldItems ?? Array.Empty<Snippet>()) s.PropertyChanged -= OnSnippetChanged;
        SnippetsVersion++;
        QueueSave();
    }

    /// <summary>Raised when a page asks to show the Settings page.</summary>
    public event Action? OpenSettingsRequested;

    [RelayCommand]
    private void OpenSettings() => OpenSettingsRequested?.Invoke();

    [RelayCommand]
    private void AddSnippet()
    {
        var s = new Snippet { Trigger = ";new" };
        Snippets.Add(s);
        Selected = s;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteSnippet()
    {
        if (Selected == null) return;
        int i = Snippets.IndexOf(Selected);
        Snippets.Remove(Selected);
        Selected = Snippets.Count > 0 ? Snippets[Math.Min(i, Snippets.Count - 1)] : null;
    }

    public void QueueSave()
    {
        SaveStatus = "Saving…";
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            SnippetStore.Save(Snippets);
            SaveStatus = "Saved";
        }
        catch (Exception ex)
        {
            SaveStatus = "Couldn't save";
            Program.Log(ex);
        }
    }

    // ---------- Settings persistence ----------

    partial void OnThemeIndexChanged(int value)
    {
        if (_loading || value < 0) return;
        AppSettings.Current.Theme = (AppTheme)value;
        AppSettings.Current.Save();
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        if (_loading) return;
        AppSettings.Current.CloseToTray = value;
        AppSettings.Current.Save();
    }

    partial void OnPlainTextIndexChanged(int value)
    {
        if (_loading || value < 0) return;
        AppSettings.Current.PlainText = (PlainTextMode)value;
        AppSettings.Current.Save();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading) return;
        try { Startup.Set(value); }
        catch (Exception ex)
        {
            Program.Log(ex);
            // Revert to the real state on the next dispatcher tick
            DispatcherQueue.GetForCurrentThread().TryEnqueue(() => StartWithWindows = Startup.IsEnabled);
        }
    }
}
