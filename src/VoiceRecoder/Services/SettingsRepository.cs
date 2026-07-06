using System.Text.Json;
using VoiceRecoder.Models;

namespace VoiceRecoder.Services;

public sealed class SettingsRepository
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<AppSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);

        try
        {
            if (!File.Exists(AppDataPaths.SettingsFile))
            {
                return new AppSettings();
            }

            await using var stream = File.OpenRead(AppDataPaths.SettingsFile);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions.Default, cancellationToken)
                ?? new AppSettings();
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await Gate.WaitAsync(cancellationToken);

        try
        {
            Directory.CreateDirectory(AppDataPaths.DataRoot);

            await using var stream = File.Create(AppDataPaths.SettingsFile);
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions.Default, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }
}
