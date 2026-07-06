using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace VoiceRecoder;

public partial class App : Application
{
    public TaskbarIcon? TrayIcon { get; private set; }

    public Window? Window { get; set; }

    public bool HandleClosedEvents { get; set; } = true;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        InitializeTrayIcon();
    }

    private void InitializeTrayIcon()
    {
        var exitApplicationCommand = (XamlUICommand)Resources["ExitApplicationCommand"]!;
        exitApplicationCommand.ExecuteRequested += ExitApplicationCommand_ExecuteRequested;

        TrayIcon = (TaskbarIcon)Resources["TrayIcon"]!;
        TrayIcon.ForceCreate();
    }

    private void ExitApplicationCommand_ExecuteRequested(object? _, ExecuteRequestedEventArgs args)
    {
        HandleClosedEvents = false;
        TrayIcon?.Dispose();
        Window?.Close();

        // https://github.com/HavenDV/H.NotifyIcon/issues/66
        if (Window == null)
        {
            Environment.Exit(0);
        }
    }
}
