using System.Text;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VoiceRecoder.Models;
using VoiceRecoder.Services;
using Windows.Graphics;

namespace VoiceRecoder.Views;

public sealed partial class RecordingWindow : Window
{
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly SpeechServiceFactory _speechServiceFactory = new();
    private readonly LogRepository _logRepository = new();
    private readonly SettingsRepository _settingsRepository = new();
    private readonly PermissionService _permissionService = new();

    private ISpeechRecognitionService? _speechService;
    private CancellationTokenSource? _recordingCts;
    private readonly StringBuilder _finalizedText = new();
    private string _partialText = string.Empty;
    private readonly object _transcriptLock = new();
    private DispatcherQueueTimer? _noSpeechHintTimer;
    private bool _isRecording;
    private bool _isBusy;
    private bool _speechDetected;

    public RecordingWindow()
    {
        InitializeComponent();

        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(720, 520));

        Closed += OnWindowClosed;
        SetStatus("就绪", StatusKind.Ready);
    }

    private enum StatusKind
    {
        Ready,
        Recording,
        Error
    }

    public Task BeginRecordingAsync()
    {
        var completion = new TaskCompletionSource();

        if (!_dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (!_isRecording && !_isBusy)
                {
                    ClearTranscriptForNewSession();
                    await StartRecordingAsync();
                }

                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }))
        {
            completion.TrySetException(new InvalidOperationException("Unable to schedule recording start."));
        }

        return completion.Task;
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        if (_isRecording)
        {
            await StopRecordingAsync(saveEntry: true);
            return;
        }

        ClearTranscriptForNewSession();
        await StartRecordingAsync();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRecording || _isBusy)
        {
            return;
        }

        ClearTranscriptForNewSession();
        HideError();
        SetStatus("就绪", StatusKind.Ready);
    }

    private void ClearTranscriptForNewSession()
    {
        lock (_transcriptLock)
        {
            _finalizedText.Clear();
            _partialText = string.Empty;
        }

        TranscriptTextBox.Text = string.Empty;
    }

    private async Task StartRecordingAsync()
    {
        _isBusy = true;
        ToggleButton.IsEnabled = false;
        HideError();

        try
        {
            if (!await ShowFirstRunGuideIfNeededAsync())
            {
                return;
            }

            if (!await _permissionService.EnsureMicrophoneAccessAsync())
            {
                ShowError(PermissionService.MicrophoneDeniedMessage);
                return;
            }

            var settings = await _settingsRepository.GetAsync();
            _speechService = await _speechServiceFactory.CreateAsync();

            if (settings.Provider == SpeechProvider.Qwen)
            {
                ShowError("Qwen API 识别尚未实现，请先在托盘菜单切换到 Windows 内置识别。");
                return;
            }

            if (!_speechService.IsAvailable())
            {
                ShowError("当前语音识别不可用。请确认已安装中文语音包，并在系统设置中开启在线语音识别。");
                return;
            }

            _recordingCts = new CancellationTokenSource();
            SubscribeSpeechEvents(_speechService);

            await _speechService.StartAsync(_recordingCts.Token);

            _isRecording = true;
            _speechDetected = false;
            ToggleButton.Content = "停止并保存";
            ClearButton.IsEnabled = false;
            SetStatus("录音中…", StatusKind.Recording);
            StartNoSpeechHintTimer();
        }
        catch (Exception ex)
        {
            await CleanupSpeechServiceAsync();
            ShowError(ex.Message);
            SetStatus("错误", StatusKind.Error);
        }
        finally
        {
            _isBusy = false;
            ToggleButton.IsEnabled = true;
        }
    }

    private async Task StopRecordingAsync(bool saveEntry)
    {
        _isBusy = true;
        ToggleButton.IsEnabled = false;

        try
        {
            if (_speechService is not null)
            {
                try
                {
                    await _speechService.StopAsync();
                }
                catch
                {
                    // Ignore stop failures; finalize whatever was recognized.
                }
            }

            FinalizePartialTranscript();
            UpdateTranscriptText();
            var text = GetTranscriptText();

            await CleanupSpeechServiceAsync(stopService: false);

            StopNoSpeechHintTimer();

            _isRecording = false;
            ToggleButton.Content = "开始录制";
            ClearButton.IsEnabled = true;

            if (saveEntry)
            {
                text = text.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    await _logRepository.AddEntryAsync(text);
                    SetStatus("已保存到今日日志", StatusKind.Ready);
                }
                else
                {
                    SetStatus("未识别到内容", StatusKind.Ready);
                }
            }
            else
            {
                SetStatus("就绪", StatusKind.Ready);
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            SetStatus("错误", StatusKind.Error);
        }
        finally
        {
            _isBusy = false;
            ToggleButton.IsEnabled = true;
        }
    }

    private void SubscribeSpeechEvents(ISpeechRecognitionService speechService)
    {
        speechService.PartialResult += OnPartialResult;
        speechService.FinalResult += OnFinalResult;
        speechService.Error += OnSpeechError;
    }

    private void UnsubscribeSpeechEvents(ISpeechRecognitionService speechService)
    {
        speechService.PartialResult -= OnPartialResult;
        speechService.FinalResult -= OnFinalResult;
        speechService.Error -= OnSpeechError;
    }

    private void OnPartialResult(object? sender, string text)
    {
        lock (_transcriptLock)
        {
            _partialText = text;
        }

        _speechDetected = true;
        RunOnUiThread(() =>
        {
            UpdateTranscriptText();
            SetStatus("录音中… 已检测到语音", StatusKind.Recording);
        });
    }

    private void OnFinalResult(object? sender, string text)
    {
        lock (_transcriptLock)
        {
            if (_finalizedText.Length > 0)
            {
                _finalizedText.Append(' ');
            }

            _finalizedText.Append(text);
            _partialText = string.Empty;
        }

        _speechDetected = true;
        RunOnUiThread(() =>
        {
            UpdateTranscriptText();
            SetStatus("录音中… 已检测到语音", StatusKind.Recording);
        });
    }

    private void OnSpeechError(object? sender, string message)
    {
        RunOnUiThread(() =>
        {
            ShowError(message);
            SetStatus("错误", StatusKind.Error);
        });
    }

    private void FinalizePartialTranscript()
    {
        lock (_transcriptLock)
        {
            if (_partialText.Length == 0)
            {
                return;
            }

            if (_finalizedText.Length > 0)
            {
                _finalizedText.Append(' ');
            }

            _finalizedText.Append(_partialText);
            _partialText = string.Empty;
        }
    }

    private void UpdateTranscriptText()
    {
        string display;
        lock (_transcriptLock)
        {
            display = _finalizedText.ToString() +
                (_partialText.Length > 0 ? _partialText : string.Empty);
        }

        TranscriptTextBox.Text = display;
        TranscriptTextBox.SelectionStart = display.Length;
    }

    private string GetTranscriptText()
    {
        lock (_transcriptLock)
        {
            var builderText = _finalizedText.ToString();
            if (_partialText.Length > 0)
            {
                if (builderText.Length > 0)
                {
                    builderText += ' ';
                }

                builderText += _partialText;
            }

            builderText = builderText.Trim();
            var textBoxText = TranscriptTextBox.Text.Trim();
            return builderText.Length >= textBoxText.Length ? builderText : textBoxText;
        }
    }

    private async Task CleanupSpeechServiceAsync(bool stopService = true)
    {
        if (_speechService is null)
        {
            return;
        }

        var speechService = _speechService;
        _speechService = null;

        UnsubscribeSpeechEvents(speechService);

        if (stopService)
        {
            try
            {
                await speechService.StopAsync();
            }
            catch
            {
                // Ignore cleanup failures after stop or window close.
            }
        }

        _recordingCts?.Dispose();
        _recordingCts = null;
    }

    private void StartNoSpeechHintTimer()
    {
        StopNoSpeechHintTimer();

        _noSpeechHintTimer = _dispatcherQueue.CreateTimer();
        _noSpeechHintTimer.Interval = TimeSpan.FromSeconds(4);
        _noSpeechHintTimer.Tick += OnNoSpeechHintTimerTick;
        _noSpeechHintTimer.Start();
    }

    private void StopNoSpeechHintTimer()
    {
        if (_noSpeechHintTimer is null)
        {
            return;
        }

        _noSpeechHintTimer.Tick -= OnNoSpeechHintTimerTick;
        _noSpeechHintTimer.Stop();
        _noSpeechHintTimer = null;
    }

    private void OnNoSpeechHintTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (!_isRecording || _speechDetected)
        {
            return;
        }

        ShowError(
            "未检测到语音输入。请检查：\n" +
            "1. 麦克风是否连接且未被静音\n" +
            "2. 系统「设置 → 隐私 → 麦克风」已允许本应用\n" +
            "3. 已安装中文语音包并开启在线语音识别");
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        try
        {
            if (_isRecording)
            {
                if (_speechService is not null)
                {
                    try
                    {
                        await _speechService.StopAsync();
                    }
                    catch
                    {
                        // Ignore cleanup failures after stop or window close.
                    }
                }

                FinalizePartialTranscript();
                var text = GetTranscriptText();

                await CleanupSpeechServiceAsync(stopService: false);
                StopNoSpeechHintTimer();
                _isRecording = false;

                if (!string.IsNullOrEmpty(text))
                {
                    await _logRepository.AddEntryAsync(text);
                }
            }
            else
            {
                await CleanupSpeechServiceAsync();
            }
        }
        catch
        {
            // Window is closing; ignore persistence failures.
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcherQueue.TryEnqueue(() => action());
    }

    private void SetStatus(string message, StatusKind kind)
    {
        StatusText.Text = message;
        StatusIndicator.Fill = kind switch
        {
            StatusKind.Recording => new SolidColorBrush(Colors.Crimson),
            StatusKind.Error => new SolidColorBrush(Colors.OrangeRed),
            _ => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };
    }

    private void ShowError(string message)
    {
        ErrorInfoBar.Message = message;
        ErrorInfoBar.IsOpen = true;
    }

    private void HideError()
    {
        ErrorInfoBar.IsOpen = false;
    }

    private async Task<bool> ShowFirstRunGuideIfNeededAsync()
    {
        var settings = await _settingsRepository.GetAsync();
        if (settings.FirstRunGuideCompleted)
        {
            return true;
        }

        var dialog = new ContentDialog
        {
            Title = "欢迎使用语音日志",
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = PermissionService.FirstRunGuideText,
                    TextWrapping = TextWrapping.WrapWholeWords
                }
            },
            PrimaryButtonText = "我知道了",
            XamlRoot = Content.XamlRoot
        };

        await dialog.ShowAsync();

        settings.FirstRunGuideCompleted = true;
        await _settingsRepository.SaveAsync(settings);
        return true;
    }
}
