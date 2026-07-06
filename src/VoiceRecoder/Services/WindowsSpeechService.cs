using VoiceRecoder.Models;
using Windows.Globalization;
using Windows.Media.SpeechRecognition;

namespace VoiceRecoder.Services;

public sealed class WindowsSpeechService : ISpeechRecognitionService
{
    private readonly string _language;
    private readonly object _gate = new();

    private SpeechRecognizer? _recognizer;
    private CancellationTokenRegistration _cancellationRegistration;
    private TaskCompletionSource? _sessionCompletedTcs;
    private bool _isRunning;

    public WindowsSpeechService(string language)
    {
        _language = string.IsNullOrWhiteSpace(language) ? "zh-CN" : language;
    }

    public SpeechProvider Provider => SpeechProvider.Windows;

    public event EventHandler<string>? PartialResult;

    public event EventHandler<string>? FinalResult;

    public event EventHandler<string>? Error;

    public bool IsAvailable()
    {
        try
        {
            var language = new Language(_language);
            return SpeechRecognizer.SupportedTopicLanguages
                .Any(supported => supported.LanguageTag.Equals(language.LanguageTag, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_isRunning)
            {
                throw new InvalidOperationException("Speech recognition is already running.");
            }

            _isRunning = true;
        }

        try
        {
            _sessionCompletedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _cancellationRegistration = cancellationToken.Register(static state =>
            {
                var service = (WindowsSpeechService)state!;
                _ = service.StopAsync();
            }, this);

            var language = new Language(_language);
            var recognizer = new SpeechRecognizer(language);
            recognizer.UIOptions.IsReadBackEnabled = false;

            var dictationConstraint = new SpeechRecognitionTopicConstraint(
                SpeechRecognitionScenario.Dictation,
                "dictation");
            recognizer.Constraints.Add(dictationConstraint);

            var compilationResult = await recognizer.CompileConstraintsAsync();
            if (compilationResult.Status != SpeechRecognitionResultStatus.Success)
            {
                throw new InvalidOperationException(
                    $"Speech constraints compilation failed: {compilationResult.Status}");
            }

            recognizer.ContinuousRecognitionSession.ResultGenerated += OnResultGenerated;
            recognizer.ContinuousRecognitionSession.Completed += OnCompleted;

            lock (_gate)
            {
                _recognizer = recognizer;
            }

            await recognizer.ContinuousRecognitionSession.StartAsync();
        }
        catch (Exception ex)
        {
            await StopInternalAsync();
            RaiseError(ex.Message);
            throw;
        }
    }

    public Task StopAsync() => StopInternalAsync();

    private async Task StopInternalAsync()
    {
        SpeechRecognizer? recognizer;

        lock (_gate)
        {
            if (!_isRunning && _recognizer is null)
            {
                return;
            }

            recognizer = _recognizer;
            _recognizer = null;
            _isRunning = false;
        }

        await _cancellationRegistration.DisposeAsync();

        if (recognizer is null)
        {
            return;
        }

        recognizer.ContinuousRecognitionSession.ResultGenerated -= OnResultGenerated;
        recognizer.ContinuousRecognitionSession.Completed -= OnCompleted;

        try
        {
            await recognizer.ContinuousRecognitionSession.StopAsync();
        }
        catch
        {
            // Session may already be stopped after completion or cancellation.
        }

        var completed = _sessionCompletedTcs;
        if (completed is not null)
        {
            try
            {
                await completed.Task.WaitAsync(TimeSpan.FromMilliseconds(500));
            }
            catch (TimeoutException)
            {
                // Result events may already have been delivered before Completed.
            }
        }

        recognizer.Dispose();
    }

    private void OnResultGenerated(
        SpeechContinuousRecognitionSession session,
        SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        var status = args.Result.Status;
        if (status != SpeechRecognitionResultStatus.Success)
        {
            var message = DescribeResultStatus(status);
            if (message is not null)
            {
                RaiseError(message);
            }

            return;
        }

        var text = args.Result.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        PartialResult?.Invoke(this, text);

        if (args.Result.Confidence is SpeechRecognitionConfidence.High
            or SpeechRecognitionConfidence.Medium)
        {
            FinalResult?.Invoke(this, text);
        }
    }

    private void OnCompleted(
        SpeechContinuousRecognitionSession session,
        SpeechContinuousRecognitionCompletedEventArgs args)
    {
        _sessionCompletedTcs?.TrySetResult();

        bool shouldRestart;
        lock (_gate)
        {
            shouldRestart = _isRunning && _recognizer is not null;
        }

        if (!shouldRestart)
        {
            return;
        }

        if (args.Status != SpeechRecognitionResultStatus.Success)
        {
            var message = DescribeSessionStatus(args.Status);
            if (message is not null)
            {
                RaiseError(message);
            }
        }

        _ = TryRestartSessionAsync();
    }

    private async Task TryRestartSessionAsync()
    {
        SpeechRecognizer? recognizer;

        lock (_gate)
        {
            if (!_isRunning || _recognizer is null)
            {
                return;
            }

            recognizer = _recognizer;
        }

        try
        {
            _sessionCompletedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await recognizer.ContinuousRecognitionSession.StartAsync();
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (!_isRunning)
                {
                    return;
                }
            }

            RaiseError($"语音识别重启失败：{ex.Message}");
        }
    }

    private static string? DescribeResultStatus(SpeechRecognitionResultStatus status) =>
        status switch
        {
            SpeechRecognitionResultStatus.MicrophoneUnavailable =>
                "麦克风不可用，请检查是否已连接并在系统设置中允许本应用访问麦克风。",
            SpeechRecognitionResultStatus.NetworkFailure =>
                "语音识别需要网络连接，请检查网络并在「设置 → 隐私 → 语音」中开启在线语音识别。",
            SpeechRecognitionResultStatus.UserCanceled or SpeechRecognitionResultStatus.Success =>
                null,
            _ => $"语音识别返回异常状态：{status}"
        };

    private static string? DescribeSessionStatus(SpeechRecognitionResultStatus status) =>
        status switch
        {
            SpeechRecognitionResultStatus.UserCanceled => null,
            SpeechRecognitionResultStatus.Success => null,
            _ => DescribeResultStatus(status)
        };

    private void RaiseError(string message)
    {
        Error?.Invoke(this, message);
    }
}
