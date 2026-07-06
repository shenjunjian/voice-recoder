namespace VoiceRecoder.Models;

public sealed class DailyLog
{
    public required string Date { get; init; }

    public List<VoiceLogEntry> Entries { get; set; } = [];

    public static DailyLog Create(DateOnly date) =>
        new()
        {
            Date = date.ToString("yyyy-MM-dd"),
            Entries = []
        };
}
