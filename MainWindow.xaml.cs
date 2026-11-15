using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace TextExpander;

public sealed partial class MainWindow : Window
{
    public ObservableCollection<Snippet> Snippets { get; } = SnippetStore.Load();

    private readonly Expander _expander;
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly DispatcherQueueTimer _previewTimer;
    private Snippet? _current;
    private bool _loading;
    private bool _previewReady;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 720));

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WindowMinSize.Apply(hwnd, 760, 480);
        ClipboardHelper.Owner = hwnd;
        _expander = new Expander(() => Snippets, DispatcherQueue, hwnd);
        EnabledToggle.IsOn = true; // after _expander exists (Toggled fires immediately)
        PlainModeRadios.SelectedIndex = (int)AppSettings.Current.PlainText;

        _saveTimer = CreateTimer(600, Save);
        _previewTimer = CreateTimer(150, UpdatePreview);
        Root.ActualThemeChanged += (_, _) => UpdatePreview();
        _ = InitPreviewAsync();

        if (Snippets.Count > 0) SnippetList.SelectedIndex = 0;
        else ShowEditor();

        Closed += (_, _) =>
        {
            Save();
            _expander.Dispose();
        };
    }

    private DispatcherQueueTimer CreateTimer(int ms, Action action)
    {
        var t = DispatcherQueue.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(ms);
        t.IsRepeating = false;
        t.Tick += (_, _) => action();
        return t;
    }

    private static void Restart(DispatcherQueueTimer t) { t.Stop(); t.Start(); }

    // ---------- Snippet list ----------

    private void SnippetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _current = SnippetList.SelectedItem as Snippet;
        ShowEditor();
    }

    private void ShowEditor()
    {
        _loading = true;
        TriggerBox.Text = _current?.Trigger ?? "";
        Editor.Text = _current?.Text ?? "";
        _loading = false;

        EditorPane.Visibility = _current != null ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = _current != null ? Visibility.Collapsed : Visibility.Visible;
        SavedText.Text = "";
        UpdatePreview();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var s = new Snippet { Trigger = ";new" };
        Snippets.Add(s);
        SnippetList.SelectedItem = s;
        SnippetList.ScrollIntoView(s);
        TriggerBox.Focus(FocusState.Programmatic);
        TriggerBox.SelectAll();
        Save();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        int i = Snippets.IndexOf(_current);
        Snippets.Remove(_current);
        if (Snippets.Count > 0) SnippetList.SelectedIndex = Math.Min(i, Snippets.Count - 1);
        else { _current = null; ShowEditor(); }
        Save();
    }

    private void EnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_expander != null) _expander.Enabled = EnabledToggle.IsOn;
    }

    private void PlainMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PlainModeRadios.SelectedIndex < 0) return;
        AppSettings.Current.PlainText = (PlainTextMode)PlainModeRadios.SelectedIndex;
        AppSettings.Current.Save();
    }

    // ---------- Editing ----------

    private void TriggerBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _current == null) return;
        _current.Trigger = TriggerBox.Text.Trim();
        QueueSave();
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _current == null) return;
        _current.Text = Editor.Text.Replace("\r\n", "\n").Replace('\r', '\n'); // TextBox uses \r internally
        QueueSave();
        Restart(_previewTimer);
    }

    private void QueueSave()
    {
        SavedText.Text = "Saving…";
        Restart(_saveTimer);
    }

    private void Save()
    {
        try
        {
            SnippetStore.Save(Snippets);
            SavedText.Text = "Saved";
        }
        catch (Exception ex)
        {
            SavedText.Text = "Couldn't save";
            Program.Log(ex);
        }
    }

    private void Format_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        switch ((sender as FrameworkElement)?.Tag as string)
        {
            case "bold": Wrap("**", "**", "bold text"); break;
            case "italic": Wrap("*", "*", "italic text"); break;
            case "strike": Wrap("~~", "~~", "text"); break;
            case "code": Wrap("`", "`", "code"); break;
            case "link": Wrap("[", "](https://)", "link text"); break;
            case "heading": PrefixLine("## "); break;
            case "list": PrefixLine("- "); break;
            case "numbered": PrefixLine("1. "); break;
            case "task": PrefixLine("- [ ] "); break;
            case "quote": PrefixLine("> "); break;
            case "table": Insert("\r| Column | Column |\r|---|---|\r| Cell | Cell |\r"); break;
        }
    }

    private void Wrap(string before, string after, string placeholder)
    {
        int start = Editor.SelectionStart;
        string sel = Editor.SelectionLength > 0 ? Editor.SelectedText : placeholder;
        Editor.SelectedText = before + sel + after;
        Editor.Select(start + before.Length, sel.Length);
        Editor.Focus(FocusState.Programmatic);
    }

    private void PrefixLine(string prefix)
    {
        string text = Editor.Text;
        int caret = Editor.SelectionStart;
        int lineStart = caret == 0 ? 0 : text.LastIndexOfAny(new[] { '\r', '\n' }, caret - 1) + 1;
        Editor.Text = text.Insert(lineStart, prefix);
        Editor.Select(caret + prefix.Length, 0);
        Editor.Focus(FocusState.Programmatic);
    }

    private void Insert(string s)
    {
        int start = Editor.SelectionStart;
        Editor.SelectedText = s;
        Editor.Select(start + s.Length, 0);
        Editor.Focus(FocusState.Programmatic);
    }

    // ---------- Preview ----------

    private async Task InitPreviewAsync()
    {
        try
        {
            await Preview.EnsureCoreWebView2Async();
            var core = Preview.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NavigationStarting += OnPreviewNavigating;
            core.NewWindowRequested += (_, a) => { a.Handled = true; OpenExternal(a.Uri); };
            _previewReady = true;
            UpdatePreview();
        }
        catch (Exception ex)
        {
            Program.Log(ex);
            Preview.Visibility = Visibility.Collapsed;
            PreviewError.Visibility = Visibility.Visible;
        }
    }

    // Links in the preview open in the default browser
    private void OnPreviewNavigating(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs a)
    {
        if (a.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
            a.Uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            a.Cancel = true;
            OpenExternal(a.Uri);
        }
    }

    private static void OpenExternal(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var u))
            _ = Windows.System.Launcher.LaunchUriAsync(u);
    }

    private void UpdatePreview()
    {
        if (!_previewReady) return;
        bool dark = Root.ActualTheme == ElementTheme.Dark;
        Preview.NavigateToString(PreviewPage.Build(Markdown.ToHtml(_current?.Text ?? ""), dark));
    }
}
