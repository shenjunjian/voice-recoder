using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using VoiceRecoder.Views;

namespace VoiceRecoder;

public partial class App : Application
{
    public TaskbarIcon? TrayIcon { get; private set; }

    public Window? Window { get; set; }

    public bool HandleClosedEvents { get; set; } = true;

    private RecordingWindow? _recordingWindow;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        InitializeTrayIcon();
    }

    public void ShowRecordingWindow()
    {
        if (_recordingWindow is null)
        {
            _recordingWindow = new RecordingWindow();
            _recordingWindow.Closed += (_, _) => _recordingWindow = null;
        }

        _recordingWindow.Activate();
    }

    private void InitializeTrayIcon()
    {
        var startRecordingCommand = (XamlUICommand)Resources["StartRecordingCommand"]!;
        startRecordingCommand.ExecuteRequested += (_, _) => ShowRecordingWindow();

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
