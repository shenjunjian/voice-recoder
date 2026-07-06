using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VoiceRecoder.Models;
using VoiceRecoder.Services;
using VoiceRecoder.Views;

namespace VoiceRecoder;

public partial class App : Application
{
    public TaskbarIcon? TrayIcon { get; private set; }

    public Window? Window { get; set; }

    public bool HandleClosedEvents { get; set; } = true;

    private readonly SettingsRepository _settingsRepository = new();

    private RecordingWindow? _recordingWindow;
    private HistoryWindow? _historyWindow;
    private SettingsWindow? _settingsWindow;

    private RadioMenuFlyoutItem? _windowsProviderItem;
    private RadioMenuFlyoutItem? _qwenProviderItem;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        InitializeHostWindow();
        InitializeTrayIcon();
    }

    public void ShowRecordingWindow()
    {
        EnsureRecordingWindow();
        _recordingWindow!.Activate();
    }

    public async Task ShowRecordingWindowAndStartAsync()
    {
        EnsureRecordingWindow();
        _recordingWindow!.Activate();
        await _recordingWindow.BeginRecordingAsync();
    }

    private void EnsureRecordingWindow()
    {
        if (_recordingWindow is not null)
        {
            return;
        }

        _recordingWindow = new RecordingWindow();
        _recordingWindow.Closed += (_, _) => _recordingWindow = null;
    }

    public void ShowHistoryWindow()
    {
        if (_historyWindow is null)
        {
            _historyWindow = new HistoryWindow();
            _historyWindow.Closed += (_, _) => _historyWindow = null;
        }

        _historyWindow.Activate();
    }

    public void ShowSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Activate();
    }

    private void InitializeHostWindow()
    {
        Window = new Window
        {
            Title = "语音日志",
            Content = new Grid()
        };

        Window.Closed += (_, args) =>
        {
            if (HandleClosedEvents)
            {
                args.Handled = true;
                Window.Hide();
            }
        };

        Window.Hide();
    }

    private void InitializeTrayIcon()
    {
        var startRecordingCommand = (XamlUICommand)Resources["StartRecordingCommand"]!;
        startRecordingCommand.ExecuteRequested += async (_, _) => await ShowRecordingWindowAndStartAsync();

        var openRecordingWindowCommand = (XamlUICommand)Resources["OpenRecordingWindowCommand"]!;
        openRecordingWindowCommand.ExecuteRequested += (_, _) => ShowRecordingWindow();

        var viewHistoryCommand = (XamlUICommand)Resources["ViewHistoryCommand"]!;
        viewHistoryCommand.ExecuteRequested += (_, _) => ShowHistoryWindow();

        var openSettingsCommand = (XamlUICommand)Resources["OpenSettingsCommand"]!;
        openSettingsCommand.ExecuteRequested += (_, _) => ShowSettingsWindow();

        var exitApplicationCommand = (XamlUICommand)Resources["ExitApplicationCommand"]!;
        exitApplicationCommand.ExecuteRequested += ExitApplicationCommand_ExecuteRequested;

        TrayIcon = (TaskbarIcon)Resources["TrayIcon"]!;
        TrayIcon.ToolTipText = "语音日志 — 左键打开录制，右键菜单";
        TrayIcon.LeftClickCommand = openRecordingWindowCommand;
        TrayIcon.ContextFlyout = BuildTrayMenu(
            startRecordingCommand,
            viewHistoryCommand,
            openSettingsCommand,
            exitApplicationCommand);
        TrayIcon.ForceCreate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Unhandled exception: {e.Exception}");
        e.Handled = true;
    }

    private MenuFlyout BuildTrayMenu(
        XamlUICommand startRecordingCommand,
        XamlUICommand viewHistoryCommand,
        XamlUICommand openSettingsCommand,
        XamlUICommand exitApplicationCommand)
    {
        var menu = new MenuFlyout();
        menu.Opening += TrayMenu_Opening;

        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "开始录制",
            Command = startRecordingCommand
        });
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "查看记录",
            Command = viewHistoryCommand
        });
        menu.Items.Add(new MenuFlyoutSeparator());

        var providerSubMenu = new MenuFlyoutSubItem { Text = "识别方案" };
        _windowsProviderItem = new RadioMenuFlyoutItem
        {
            Text = "Windows 内置",
            GroupName = "SpeechProvider"
        };
        _windowsProviderItem.Click += async (_, _) => await SetProviderAsync(SpeechProvider.Windows);

        _qwenProviderItem = new RadioMenuFlyoutItem
        {
            Text = "Qwen API",
            GroupName = "SpeechProvider"
        };
        _qwenProviderItem.Click += async (_, _) => await SetProviderAsync(SpeechProvider.Qwen);

        providerSubMenu.Items.Add(_windowsProviderItem);
        providerSubMenu.Items.Add(_qwenProviderItem);
        menu.Items.Add(providerSubMenu);

        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "设置...",
            Command = openSettingsCommand
        });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "退出",
            Command = exitApplicationCommand
        });

        return menu;
    }

    private async void TrayMenu_Opening(object? sender, object e)
    {
        var settings = await _settingsRepository.GetAsync();
        UpdateProviderSelection(settings.Provider);
    }

    private void UpdateProviderSelection(SpeechProvider provider)
    {
        if (_windowsProviderItem is not null)
        {
            _windowsProviderItem.IsChecked = provider == SpeechProvider.Windows;
        }

        if (_qwenProviderItem is not null)
        {
            _qwenProviderItem.IsChecked = provider == SpeechProvider.Qwen;
        }
    }

    private async Task SetProviderAsync(SpeechProvider provider)
    {
        var settings = await _settingsRepository.GetAsync();
        if (settings.Provider == provider)
        {
            UpdateProviderSelection(provider);
            return;
        }

        settings.Provider = provider;
        await _settingsRepository.SaveAsync(settings);
        UpdateProviderSelection(provider);
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
