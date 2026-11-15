using Microsoft.UI.Xaml;

namespace TextExpander;

public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        // Launched at Windows startup: stay hidden in the tray
        if (!Environment.GetCommandLineArgs().Contains(Startup.TrayArg))
            _window.Activate();
    }
}
