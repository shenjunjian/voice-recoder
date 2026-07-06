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

    private ISpeechRecognitionService? _speechService;
    private CancellationTokenSource? _recordingCts;
    private readonly StringBuilder _finalizedText = new();
    private string _partialText = string.Empty;
    private bool _isRecording;
    private bool _isBusy;

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

        await StartRecordingAsync();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRecording || _isBusy)
        {
            return;
        }

        _finalizedText.Clear();
        _partialText = string.Empty;
        TranscriptTextBox.Text = string.Empty;
        HideError();
        SetStatus("就绪", StatusKind.Ready);
    }

    private async Task StartRecordingAsync()
    {
        _isBusy = true;
        ToggleButton.IsEnabled = false;
        HideError();

        try
        {
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
            ToggleButton.Content = "停止并保存";
            ClearButton.IsEnabled = false;
            SetStatus("录音中…", StatusKind.Recording);
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
            await CleanupSpeechServiceAsync();

            _isRecording = false;
            ToggleButton.Content = "开始录制";
            ClearButton.IsEnabled = true;

            if (saveEntry)
            {
                var text = TranscriptTextBox.Text.Trim();
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
        RunOnUiThread(() =>
        {
            _partialText = text;
            UpdateTranscriptText();
        });
    }

    private void OnFinalResult(object? sender, string text)
    {
        RunOnUiThread(() =>
        {
            if (_finalizedText.Length > 0)
            {
                _finalizedText.Append(' ');
            }

            _finalizedText.Append(text);
            _partialText = string.Empty;
            UpdateTranscriptText();
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

    private void UpdateTranscriptText()
    {
        TranscriptTextBox.Text = _finalizedText.ToString() +
            (_partialText.Length > 0 ? _partialText : string.Empty);
        TranscriptTextBox.SelectionStart = TranscriptTextBox.Text.Length;
    }

    private async Task CleanupSpeechServiceAsync()
    {
        if (_speechService is null)
        {
            return;
        }

        var speechService = _speechService;
        _speechService = null;

        UnsubscribeSpeechEvents(speechService);

        try
        {
            await speechService.StopAsync();
        }
        catch
        {
            // Ignore cleanup failures after stop or window close.
        }

        _recordingCts?.Dispose();
        _recordingCts = null;
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        try
        {
            var text = TranscriptTextBox.Text.Trim();

            if (_isRecording)
            {
                await CleanupSpeechServiceAsync();
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
}
