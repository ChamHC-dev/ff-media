using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFMedia.Core.History;
using FFMedia.Core.Notifications;

namespace FFMedia.App.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly IHistoryService _history;
    private readonly INotificationService _notifications;

    public HistoryViewModel(IHistoryService history, INotificationService notifications)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(notifications);
        _history = history;
        _notifications = notifications;
        _history.Changed += (_, _) => Application.Current?.Dispatcher.Invoke(Refresh);
        Refresh();
    }

    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    [ObservableProperty] private string _filterText = string.Empty;

    partial void OnFilterTextChanged(string value) => Refresh();

    private void Refresh()
    {
        var filter = FilterText?.Trim() ?? string.Empty;
        var matches = _history.Query().Where(e =>
            filter.Length == 0
            || (e.Title?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
            || (e.Url?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
            || (e.Format?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false));

        Entries.Clear();
        foreach (var entry in matches)
            Entries.Add(entry);
    }

    [RelayCommand]
    private void Clear() => _history.Clear();

    [RelayCommand]
    private void OpenFile(HistoryEntry? entry)
    {
        if (entry?.OutputPath is not { } path)
        {
            return;
        }

        if (!File.Exists(path))
        {
            _notifications.Notify(new Notification(
                "File not found",
                "It may have been moved, renamed, or deleted.",
                NotificationSeverity.Warning));
            return;
        }

        TryStart(() => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }), "open the file");
    }

    [RelayCommand]
    private void OpenFolder(HistoryEntry? entry)
    {
        if (entry?.OutputPath is not { } path)
        {
            return;
        }

        // File present → reveal and select it.
        if (File.Exists(path))
        {
            TryStart(
                () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }),
                "open the folder");
            return;
        }

        // File gone but its folder remains → open the folder so the user can look.
        var dir = Path.GetDirectoryName(path);
        if (dir is not null && Directory.Exists(dir))
        {
            _notifications.Notify(new Notification(
                "File not found", "The file is gone; opening its folder instead.", NotificationSeverity.Warning));
            TryStart(
                () => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true }),
                "open the folder");
            return;
        }

        _notifications.Notify(new Notification(
            "Folder not found", "The folder may have been moved or deleted.", NotificationSeverity.Warning));
    }

    private void TryStart(Action start, string action)
    {
        try
        {
            start();
        }
        catch (Exception ex)
        {
            _notifications.Notify(new Notification(
                "Couldn't open", $"Failed to {action}: {ex.Message}", NotificationSeverity.Error));
        }
    }
}
