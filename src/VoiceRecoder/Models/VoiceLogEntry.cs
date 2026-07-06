namespace VoiceRecoder.Models;

public sealed class VoiceLogEntry
{
    public required string Id { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required string Text { get; init; }

    public static VoiceLogEntry Create(string text, DateTimeOffset? timestamp = null) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = timestamp ?? DateTimeOffset.Now,
            Text = text
        };
}
