using VoiceRecoder.Models;

#pragma warning disable CS0067

namespace VoiceRecoder.Services;

public sealed class QwenSpeechService : ISpeechRecognitionService
{
    private readonly string _apiKey;
    private readonly string _language;

    public QwenSpeechService(string apiKey, string language)
    {
        _apiKey = apiKey;
        _language = language;
    }

    public SpeechProvider Provider => SpeechProvider.Qwen;

    public event EventHandler<string>? PartialResult;

    public event EventHandler<string>? FinalResult;

    public event EventHandler<string>? Error;

    public bool IsAvailable() => !string.IsNullOrWhiteSpace(_apiKey);

    public Task StartAsync(CancellationToken cancellationToken) =>
        Task.FromException(new NotImplementedException());

    public Task StopAsync() =>
        Task.FromException(new NotImplementedException());
}
