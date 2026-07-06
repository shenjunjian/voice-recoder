using VoiceRecoder.Models;

namespace VoiceRecoder.Services;

public sealed class SpeechServiceFactory
{
    private readonly SettingsRepository _settingsRepository;

    public SpeechServiceFactory(SettingsRepository settingsRepository)
    {
        _settingsRepository = settingsRepository;
    }

    public SpeechServiceFactory()
        : this(new SettingsRepository())
    {
    }

    public async Task<ISpeechRecognitionService> CreateAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepository.GetAsync(cancellationToken);
        return Create(settings);
    }

    public ISpeechRecognitionService Create(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.Provider switch
        {
            SpeechProvider.Windows => new WindowsSpeechService(settings.Language),
            SpeechProvider.Qwen => new QwenSpeechService(settings.ApiKey, settings.Language),
            _ => throw new ArgumentOutOfRangeException(
                nameof(settings),
                settings.Provider,
                "Unknown speech provider.")
        };
    }
}
