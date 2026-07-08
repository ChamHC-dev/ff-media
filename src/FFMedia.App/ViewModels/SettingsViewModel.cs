using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFMedia.App.Services;
using FFMedia.Core.Settings;
using Microsoft.Win32;

namespace FFMedia.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ThemeService _theme;

    // The download manager reads the concurrency cap once at construction (§12), so a change
    // only takes effect on next launch — compare against the value we started with.
    private readonly int _launchMaxConcurrency;

    public SettingsViewModel(
        ISettingsService settings, ThemeService theme, UpdateViewModel updates, BinaryUpdateViewModel binaries)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(binaries);
        _settings = settings;
        _theme = theme;
        Updates = updates;
        Binaries = binaries;

        var current = settings.Current;
        // Assigning the backing fields directly (not the properties) does NOT fire the
        // On<Property>Changed hooks below, so loading settings never triggers a save.
        _defaultOutputFolder = current.DefaultOutputFolder;
        _maxConcurrency = current.MaxConcurrency;
        _selectedTheme = current.Theme;
        _checkForUpdatesOnStartup = current.CheckForUpdatesOnStartup;
        _checkYtDlpForUpdatesOnStartup = current.CheckYtDlpForUpdatesOnStartup;
        _launchMaxConcurrency = current.MaxConcurrency;
    }

    [ObservableProperty] private string _defaultOutputFolder;
    [ObservableProperty] private int _maxConcurrency;
    [ObservableProperty] private AppTheme _selectedTheme;
    [ObservableProperty] private bool _checkForUpdatesOnStartup;
    [ObservableProperty] private bool _checkYtDlpForUpdatesOnStartup;

    /// <summary>True once the user changes concurrency to a value different from launch —
    /// the UI shows a "restart required" reminder while this holds.</summary>
    [ObservableProperty] private bool _concurrencyRestartPending;

    // Every setting persists immediately as it's edited — there is no Save button.
    partial void OnDefaultOutputFolderChanged(string value) => Persist();
    partial void OnCheckForUpdatesOnStartupChanged(bool value) => Persist();
    partial void OnCheckYtDlpForUpdatesOnStartupChanged(bool value) => Persist();

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        Persist();
        _theme.Apply(value); // theme takes effect immediately
    }

    partial void OnMaxConcurrencyChanged(int value)
    {
        Persist();
        ConcurrencyRestartPending = value != _launchMaxConcurrency;
    }

    /// <summary>Shared update state (also drives the shell banner). Bound by the Settings "check now" UI.</summary>
    public UpdateViewModel Updates { get; }

    /// <summary>Shared binary-update state (also drives the startup yt-dlp check).</summary>
    public BinaryUpdateViewModel Binaries { get; }

    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();

    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog { InitialDirectory = DefaultOutputFolder };
        if (dialog.ShowDialog() == true)
        {
            DefaultOutputFolder = dialog.FolderName; // fires OnDefaultOutputFolderChanged → Persist
        }
    }

    private void Persist() =>
        _settings.Save(_settings.Current with
        {
            DefaultOutputFolder = DefaultOutputFolder,
            MaxConcurrency = Math.Max(1, MaxConcurrency),
            Theme = SelectedTheme,
            CheckForUpdatesOnStartup = CheckForUpdatesOnStartup,
            CheckYtDlpForUpdatesOnStartup = CheckYtDlpForUpdatesOnStartup,
        });
}
