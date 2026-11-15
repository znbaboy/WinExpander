using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace WinExpander;

public sealed partial class SnippetsPage : Page
{
    public MainViewModel ViewModel => MainViewModel.Instance;

    private readonly DispatcherQueueTimer _previewTimer;
    private bool _previewReady;

    public SnippetsPage()
    {
        InitializeComponent();

        _previewTimer = DispatcherQueue.CreateTimer();
        _previewTimer.Interval = TimeSpan.FromMilliseconds(150);
        _previewTimer.IsRepeating = false;
        _previewTimer.Tick += (_, _) => UpdatePreview();

        ActualThemeChanged += (_, _) => UpdatePreview();
        ViewModel.PropertyChanged += OnViewModelChanged;
        _ = InitPreviewAsync();

        if (ViewModel.Selected == null && ViewModel.Snippets.Count > 0)
            ViewModel.Selected = ViewModel.Snippets[0];
        SyncListSelection();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.Selected)) return;
        SyncListSelection();
        UpdatePreview();
        if (ViewModel.Selected is { Trigger: ";new", Text: "" })
        {
            TriggerBox.Focus(FocusState.Programmatic);
            TriggerBox.SelectAll();
        }
    }

    private void SyncListSelection()
    {
        if (!ReferenceEquals(SnippetList.SelectedItem, ViewModel.Selected))
            SnippetList.SelectedItem = ViewModel.Selected;
        if (ViewModel.Selected != null) SnippetList.ScrollIntoView(ViewModel.Selected);
    }

    private void SnippetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SnippetList.SelectedItem is Snippet s) ViewModel.Selected = s;
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    // ---------- Markdown toolbar ----------

    private void Format_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected == null) return;
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
        bool dark = ActualTheme == ElementTheme.Dark;
        Preview.CoreWebView2.Profile.PreferredColorScheme =
            dark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
        Preview.NavigateToString(PreviewPage.Build(Markdown.ToHtml(ViewModel.Selected?.Text ?? ""), dark));
    }
}
