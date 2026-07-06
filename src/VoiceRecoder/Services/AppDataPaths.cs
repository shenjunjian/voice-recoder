namespace VoiceRecoder.Services;

internal static class AppDataPaths
{
    public static string DataRoot { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceRecoder");

    public static string LogsDirectory { get; } = Path.Combine(DataRoot, "logs");

    public static string SettingsFile { get; } = Path.Combine(DataRoot, "settings.json");

    public static string GetLogFilePath(DateOnly date) =>
        Path.Combine(LogsDirectory, $"{date:yyyy-MM-dd}.json");
}
