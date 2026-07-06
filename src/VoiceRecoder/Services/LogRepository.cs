using System.Text.Json;
using VoiceRecoder.Models;

namespace VoiceRecoder.Services;

public sealed class LogRepository
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<DailyLog> GetDailyLogAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var filePath = AppDataPaths.GetLogFilePath(date);

        if (!File.Exists(filePath))
        {
            return DailyLog.Create(date);
        }

        await using var stream = File.OpenRead(filePath);
        var dailyLog = await JsonSerializer.DeserializeAsync<DailyLog>(stream, JsonOptions.Default, cancellationToken)
            ?? DailyLog.Create(date);

        dailyLog.Entries ??= [];
        return dailyLog;
    }

    public async Task<IReadOnlyList<VoiceLogEntry>> GetEntriesAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var dailyLog = await GetDailyLogAsync(date, cancellationToken);
        return dailyLog.Entries
            .OrderByDescending(entry => entry.Timestamp)
            .ToList();
    }

    public async Task<IReadOnlyList<DateOnly>> GetDatesWithLogsAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppDataPaths.LogsDirectory);

        var dates = new List<DateOnly>();

        foreach (var filePath in Directory.EnumerateFiles(AppDataPaths.LogsDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (DateOnly.TryParseExact(fileName, "yyyy-MM-dd", out var date))
            {
                dates.Add(date);
            }
        }

        dates.Sort();
        dates.Reverse();
        return dates;
    }

    public Task<VoiceLogEntry> AddEntryAsync(
        string text,
        DateTimeOffset? timestamp = null,
        CancellationToken cancellationToken = default)
    {
        var entry = VoiceLogEntry.Create(text, timestamp);
        var date = DateOnly.FromDateTime(entry.Timestamp.LocalDateTime);
        return AddEntryAsync(date, entry, cancellationToken);
    }

    public async Task<VoiceLogEntry> AddEntryAsync(
        DateOnly date,
        VoiceLogEntry entry,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);

        try
        {
            Directory.CreateDirectory(AppDataPaths.LogsDirectory);

            var dailyLog = await GetDailyLogAsync(date, cancellationToken);
            dailyLog.Entries.Add(entry);

            var filePath = AppDataPaths.GetLogFilePath(date);
            await using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, dailyLog, JsonOptions.Default, cancellationToken);

            return entry;
        }
        finally
        {
            Gate.Release();
        }
    }
}
