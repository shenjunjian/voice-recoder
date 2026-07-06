using VoiceRecoder.Models;

namespace VoiceRecoder.Services;

public interface ISpeechRecognitionService
{
    SpeechProvider Provider { get; }

    event EventHandler<string>? PartialResult;

    event EventHandler<string>? FinalResult;

    event EventHandler<string>? Error;

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync();

    bool IsAvailable();
}
