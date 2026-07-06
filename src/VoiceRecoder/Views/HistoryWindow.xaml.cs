using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VoiceRecoder.Services;
using Windows.Graphics;

namespace VoiceRecoder.Views;

public sealed partial class HistoryWindow : Window
{
    private readonly LogRepository _logRepository = new();
    private bool _suppressDatePickerChange;
    private DateOnly _currentDate = DateOnly.FromDateTime(DateTime.Now);

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
        _currentDate = date;
        SelectedDateText.Text = date.ToString("yyyy年M月d日");
        var entries = await _logRepository.GetEntriesAsync(date);

        EntriesListView.ItemsSource = entries
            .Select(entry => new HistoryEntryItem
            {
                Id = entry.Id,
                TimestampText = entry.Timestamp.LocalDateTime.ToString("HH:mm:ss"),
                Text = entry.Text
            })
            .ToList();
        var hasEntries = entries.Count > 0;
        EntriesListView.Visibility = hasEntries ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = hasEntries ? Visibility.Collapsed : Visibility.Visible;
    }

    private static Button? FindDeleteButton(DependencyObject parent)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button { Name: "DeleteButton" } button)
            {
                return button;
            }

            var nested = FindDeleteButton(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void EntryItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is DependencyObject root &&
            FindDeleteButton(root) is Button deleteButton)
        {
            deleteButton.Visibility = Visibility.Visible;
        }
    }

    private void EntryItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is DependencyObject root &&
            FindDeleteButton(root) is Button deleteButton)
        {
            deleteButton.Visibility = Visibility.Collapsed;
        }
    }

    private async void DeleteEntryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: HistoryEntryItem item })
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "删除记录",
            Content = "确定要删除这条语音日志吗？",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await DeleteEntryAsync(item);
        }
    }

    private async void EntryItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HistoryEntryItem item })
        {
            return;
        }

        e.Handled = true;
        await DeleteEntryAsync(item);
    }

    private async Task DeleteEntryAsync(HistoryEntryItem item)
    {
        await _logRepository.DeleteEntryAsync(_currentDate, item.Id);
        await RefreshAfterDeleteAsync();
    }

    private async Task RefreshAfterDeleteAsync()
    {
        var datesWithLogs = await _logRepository.GetDatesWithLogsAsync();
        DatesListView.ItemsSource = datesWithLogs
            .Select(date => date.ToString("yyyy-MM-dd"))
            .ToList();

        await LoadEntriesForDateAsync(_currentDate);
    }
}
