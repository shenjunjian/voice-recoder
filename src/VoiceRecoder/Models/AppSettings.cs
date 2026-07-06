namespace VoiceRecoder.Models;

public sealed class AppSettings
{
    public SpeechProvider Provider { get; set; } = SpeechProvider.Windows;

    public string ApiKey { get; set; } = string.Empty;

    public string Language { get; set; } = "zh-CN";
}
