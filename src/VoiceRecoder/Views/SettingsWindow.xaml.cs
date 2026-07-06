using Microsoft.UI.Xaml;
using VoiceRecoder.Models;
using VoiceRecoder.Services;
using Windows.Graphics;

namespace VoiceRecoder.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly SettingsRepository _settingsRepository = new();
    private AppSettings _settings = new();

    public SettingsWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(520, 420));

        Activated += OnActivated;
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnActivated;
        await LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        _settings = await _settingsRepository.GetAsync();
        LanguageTextBox.Text = _settings.Language;
        ApiKeyPasswordBox.Password = _settings.ApiKey;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.Language = string.IsNullOrWhiteSpace(LanguageTextBox.Text)
            ? "zh-CN"
            : LanguageTextBox.Text.Trim();
        _settings.ApiKey = ApiKeyPasswordBox.Password.Trim();

        await _settingsRepository.SaveAsync(_settings);
        Close();
    }
}
