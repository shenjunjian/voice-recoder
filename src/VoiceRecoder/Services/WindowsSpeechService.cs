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

        recognizer.Dispose();
    }

    private void OnResultGenerated(
        SpeechContinuousRecognitionSession session,
        SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        if (args.Result.Status != SpeechRecognitionResultStatus.Success)
        {
            return;
        }

        var text = args.Result.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (args.Result.Confidence is SpeechRecognitionConfidence.High or SpeechRecognitionConfidence.Medium)
        {
            FinalResult?.Invoke(this, text);
            return;
        }

        PartialResult?.Invoke(this, text);
    }

    private void OnCompleted(
        SpeechContinuousRecognitionSession session,
        SpeechContinuousRecognitionCompletedEventArgs args)
    {
        if (args.Status != SpeechRecognitionResultStatus.Success)
        {
            RaiseError($"Recognition session ended: {args.Status}");
        }
    }

    private void RaiseError(string message)
    {
        Error?.Invoke(this, message);
    }
}
