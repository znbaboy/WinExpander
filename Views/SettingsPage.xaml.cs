using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinExpander;

public sealed partial class SettingsPage : Page
{
    public MainViewModel ViewModel => MainViewModel.Instance;
    public string VersionText => $"Version {ViewModel.Version}";
    public string DataFolder => SnippetStore.Folder;

    public SettingsPage() => InitializeComponent();

    private void OpenRepo_Click(object sender, RoutedEventArgs e) =>
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/znbaboy/WinExpander"));

    private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{SnippetStore.Folder}\"") { UseShellExecute = true });
}
