using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFMedia.Core.History;
using FFMedia.Core.Notifications;
using FFMedia.Core.Settings;
using FFMedia.Media;
using FFMedia.Tools.VideoMerger.Models;
using FFMedia.Tools.VideoMerger.Services;

namespace FFMedia.Tools.VideoMerger.ViewModels;

/// <summary>The Video Merger page's brain. Headless and fully unit-testable: every dependency is an
/// interface, and no clip is ever probed twice — the probe result rides along in the row.</summary>
public partial class MergerViewModel : ObservableObject
{
    private readonly IMediaAnalyzer _analyzer;
    private readonly IMergeService _merger;
    private readonly ISpeedProfileStore _speeds;
    private readonly IHistoryService _history;
    private readonly INotificationService _notifications;

    public MergerViewModel(
        IMediaAnalyzer analyzer,
        IMergeService merger,
        ISpeedProfileStore speeds,
        ISettingsService settings,
        IHistoryService history,
        INotificationService notifications)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(merger);
        ArgumentNullException.ThrowIfNull(speeds);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(notifications);

        _analyzer = analyzer;
        _merger = merger;
        _speeds = speeds;
        _history = history;
        _notifications = notifications;

        OutputFolder = settings.Current.DefaultOutputFolder;
    }

    /// <summary>The clip list, in the order it will be concatenated. Bound directly to the page.</summary>
    public ObservableCollection<MergeClipViewModel> Clips { get; } = [];

    /// <summary>Seeds the shuffle. Settable so tests are deterministic; the UI re-seeds it from the
    /// clock on every Shuffle click.</summary>
    public int ShuffleSeed { get; set; } = Environment.TickCount;

    [ObservableProperty] private string _outputFolder;

    /// <summary>Probes each path and appends it. A file the analyzer cannot read — or one with no
    /// video track (an audio file) — is rejected here, at add time (spec §8): letting it into the
    /// list would fail the whole merge much later, after the user has ordered everything.</summary>
    [RelayCommand]
    public async Task AddClipsAsync(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        foreach (var path in paths)
        {
            if (Clips.Any(c => string.Equals(c.SourcePath, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue; // already in the list — do not probe it again
            }

            var probe = await _analyzer.AnalyzeAsync(path).ConfigureAwait(true);
            if (!probe.IsSuccess || probe.Value is null || probe.Value.Video is null)
            {
                _notifications.Notify(new Notification(
                    "Not a video",
                    $"{Path.GetFileName(path)} could not be read as a video and was not added.",
                    NotificationSeverity.Warning));
                continue;
            }

            Clips.Add(new MergeClipViewModel(new MergeClip(path, probe.Value)));
        }

        Recompute();
    }

    [RelayCommand]
    public void RemoveClip(MergeClipViewModel clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        if (Clips.Remove(clip))
        {
            ResyncLocks();
            Recompute();
        }
    }

    [RelayCommand]
    public void MoveUp(MergeClipViewModel clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        var index = Clips.IndexOf(clip);
        if (index > 0)
        {
            Clips.Move(index, index - 1);
            ResyncLocks();
        }
    }

    [RelayCommand]
    public void MoveDown(MergeClipViewModel clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        var index = Clips.IndexOf(clip);
        if (index >= 0 && index < Clips.Count - 1)
        {
            Clips.Move(index, index + 1);
            ResyncLocks();
        }
    }

    /// <summary>Randomizes the order, leaving every locked row in the slot it occupies.</summary>
    [RelayCommand]
    public void Shuffle()
    {
        if (Clips.Count < 2)
        {
            return; // nothing to permute — and Ordering would be asked to shuffle a single slot
        }

        // Capture the locks BEFORE consulting them, not just after we rearrange. The page's lock
        // toggle two-way binds a checkbox straight to IsLocked, which does not go through SetLock,
        // so a freshly-ticked row has IsLocked = true but LockedIndex = null. Ordering.Shuffle reads
        // only LockedIndex — so without this the "locked" row would be shuffled like any other and
        // the lock then re-pinned to wherever it randomly landed. That is worse than the lock doing
        // nothing: the user asked for one thing and got the opposite.
        ResyncLocks();

        var shuffled = Ordering.Shuffle([.. Clips], c => c.LockedIndex, ShuffleSeed);

        // Selection-sort the live collection into the shuffled order: everything below i is already
        // final, so Move() only ever disturbs rows the loop has yet to place. Moving (rather than
        // clearing and re-adding) keeps the bound ListView's selection and virtualization intact.
        for (var i = 0; i < shuffled.Count; i++)
        {
            var current = Clips.IndexOf(shuffled[i]);
            if (current != i)
            {
                Clips.Move(current, i);
            }
        }

        ResyncLocks();
    }

    /// <summary>A locked row is pinned to the index it currently OCCUPIES. Removing or moving a
    /// *neighbour* shifts it, so every lock's index is re-captured after any structural change —
    /// otherwise <see cref="Ordering.Shuffle"/> would pin a row to a stale slot, or pin two rows to
    /// the same slot, which throws.</summary>
    private void ResyncLocks()
    {
        for (var i = 0; i < Clips.Count; i++)
        {
            if (Clips[i].IsLocked)
            {
                Clips[i].SetLock(locked: true, index: i);
            }
        }
    }

    /// <summary>Re-derives the target, conformance and estimate. Filled in by Task 6.</summary>
    private void Recompute()
    {
    }
}
