using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceRecoder.Services;
using Windows.Graphics;

namespace VoiceRecoder.Views;

public sealed partial class HistoryWindow : Window
{
    private readonly LogRepository _logRepository = new();
    private bool _suppressDatePickerChange;

    public HistoryWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(860, 600));

        Activated += OnActivated;
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnActivated;
        await LoadInitialDataAsync();
    }

    private async Task LoadInitialDataAsync()
    {
        var datesWithLogs = await _logRepository.GetDatesWithLogsAsync();
        DatesListView.ItemsSource = datesWithLogs
            .Select(date => date.ToString("yyyy-MM-dd"))
            .ToList();

        var today = DateOnly.FromDateTime(DateTime.Now);
        _suppressDatePickerChange = true;
        DatePicker.Date = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue));
        _suppressDatePickerChange = false;

        await LoadEntriesForDateAsync(today);
    }

    private async void DatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_suppressDatePickerChange || args.NewDate is null)
        {
            return;
        }

        DatesListView.SelectedItem = null;
        await LoadEntriesForDateAsync(DateOnly.FromDateTime(args.NewDate.Value.DateTime));
    }

    private async void DatesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DatesListView.SelectedItem is not string dateText ||
            !DateOnly.TryParseExact(dateText, "yyyy-MM-dd", out var date))
        {
            return;
        }

        _suppressDatePickerChange = true;
        DatePicker.Date = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue));
        _suppressDatePickerChange = false;

        await LoadEntriesForDateAsync(date);
    }

    private async Task LoadEntriesForDateAsync(DateOnly date)
    {
        SelectedDateText.Text = date.ToString("yyyy年M月d日");
        var entries = await _logRepository.GetEntriesAsync(date);

        EntriesListView.ItemsSource = entries
            .Select(entry => new HistoryEntryItem
            {
                TimestampText = entry.Timestamp.LocalDateTime.ToString("HH:mm:ss"),
                Text = entry.Text
            })
            .ToList();
        var hasEntries = entries.Count > 0;
        EntriesListView.Visibility = hasEntries ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = hasEntries ? Visibility.Collapsed : Visibility.Visible;
    }
}
