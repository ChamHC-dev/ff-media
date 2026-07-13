using FFMedia.Core.History;
using FFMedia.Core.Media;
using FFMedia.Core.Notifications;
using FFMedia.Core.Results;
using FFMedia.Core.Settings;
using FFMedia.Media;
using FFMedia.Tools.GifMaker.Models;
using FFMedia.Tools.GifMaker.Services;
using FFMedia.Tools.GifMaker.ViewModels;
using Xunit;

namespace FFMedia.Tests.GifMaker;

public class GifMakerViewModelTests
{
    // ---- fakes -------------------------------------------------------------

    private sealed class FakeAnalyzer : IMediaAnalyzer
    {
        private readonly Dictionary<string, Result<MediaInfo>> _byPath = new(StringComparer.OrdinalIgnoreCase);

        public void Returns(string path, MediaInfo info) => _byPath[path] = Result<MediaInfo>.Success(info);

        public void Rejects(string path, string error) => _byPath[path] = Result<MediaInfo>.Failure(error);

        public Task<Result<MediaInfo>> AnalyzeAsync(string filePath, CancellationToken ct = default)
            => Task.FromResult(_byPath.TryGetValue(filePath, out var r)
                ? r
                : Result<MediaInfo>.Failure("not configured"));
    }

    private sealed class FakeGifService : IGifService
    {
        public GifRequest? Request { get; private set; }

        public int Calls { get; private set; }

        public Func<GifRequest, IProgress<GifProgress>?, CancellationToken, Task<Result<string>>> Behavior
        { get; set; } = (request, _, _) => Task.FromResult(Result<string>.Success(request.OutputPath));

        public Task<Result<string>> CreateAsync(
            GifRequest request, IProgress<GifProgress>? progress = null, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            Request = request;
            Calls++;
            return Behavior(request, progress, ct);
        }
    }

    private sealed class FakeStore : IGifSizeProfileStore
    {
        public GifSizeProfile Profile { get; set; } = new();

        public GifSizeProfile Load() => Profile;

        public void Save(GifSizeProfile profile) => Profile = profile;
    }

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; private set; } = AppSettings.Default with { DefaultOutputFolder = @"C:\out" };

        public event EventHandler<AppSettings>? Changed;

        public void Save(AppSettings settings)
        {
            Current = settings;
            Changed?.Invoke(this, settings);
        }
    }

    private sealed class FakeHistory : IHistoryService
    {
        public List<HistoryEntry> Entries { get; } = new();

        /// <summary>Set to simulate a locked or unwritable history.json.</summary>
        public Exception? AppendThrows { get; set; }

        public event EventHandler? Changed;

        public IReadOnlyList<HistoryEntry> Query() => Entries;

        public void Append(HistoryEntry entry)
        {
            if (AppendThrows is not null)
            {
                throw AppendThrows;
            }

            Entries.Add(entry);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Clear()
        {
            Entries.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeNotifications : INotificationService
    {
        public List<Notification> Sent { get; } = new();

        public void Notify(Notification notification) => Sent.Add(notification);
    }

    // ---- helpers -----------------------------------------------------------

    private const string VideoPath = @"C:\video.mp4";

    private static MediaInfo Info(int width = 1920, int height = 1080, int fps = 30, double seconds = 10)
        => new(TimeSpan.FromSeconds(seconds), "mov,mp4,m4a",
            new VideoStreamInfo(width, height, new FrameRate(fps, 1), "h264", "yuv420p", 0), null);

    private static MediaInfo AudioOnly(double seconds = 10)
        => new(TimeSpan.FromSeconds(seconds), "mov,mp4,m4a", null, new AudioStreamInfo("aac", 48000, 2));

    private sealed record Harness(
        GifMakerViewModel Vm, FakeAnalyzer Analyzer, FakeGifService Service,
        FakeHistory History, FakeNotifications Notifications, FakeStore Store);

    private static Harness Build()
    {
        var analyzer = new FakeAnalyzer();
        var service = new FakeGifService();
        var store = new FakeStore();
        var history = new FakeHistory();
        var notifications = new FakeNotifications();
        var settings = new FakeSettings();
        var vm = new GifMakerViewModel(analyzer, service, store, settings, history, notifications);
        return new Harness(vm, analyzer, service, history, notifications, store);
    }

    /// <summary>A harness with a video already loaded at <see cref="VideoPath"/>.</summary>
    private static async Task<Harness> BuildLoadedAsync(
        int width = 1920, int height = 1080, int fps = 30, double seconds = 10)
    {
        var h = Build();
        h.Analyzer.Returns(VideoPath, Info(width, height, fps, seconds));
        await h.Vm.LoadVideoAsync(VideoPath);
        return h;
    }

    // ---- loading -------------------------------------------------------------

    [Fact]
    public async Task LoadVideoAsync_SetsTheBoundsFromTheVideo_AndDefaultsToItsOwnSizeAndRate()
    {
        // Deliberately unusual numbers (not 1920x1080/30fps) so a VM that hardcodes a "sensible
        // default" instead of reading Bounds cannot pass by coincidence.
        var h = Build();
        h.Analyzer.Returns(@"C:\clip.mov", Info(width: 852, height: 480, fps: 24, seconds: 12));

        await h.Vm.LoadVideoAsync(@"C:\clip.mov");

        Assert.True(h.Vm.SourceLoaded);
        Assert.Equal(new Resolution(852, 480), h.Vm.Bounds.Sizes[0]);
        Assert.Equal(new FrameRate(24, 1), h.Vm.Bounds.FrameRates[0]);
        Assert.Equal(h.Vm.Bounds.Sizes[0], h.Vm.SelectedSize);
        Assert.Equal(h.Vm.Bounds.FrameRates[0], h.Vm.SelectedFrameRate);
    }

    [Fact]
    public async Task LoadVideoAsync_RejectsAFileWithNoVideoTrack_WithTheAnalyzersOwnReason()
    {
        var h = Build();

        // Case 1: the probe FAILS outright (e.g. ffprobe.exe is missing). The exact reason must
        // survive verbatim -- never collapsed into a generic "not a video" message that blames the
        // user's perfectly good file for a missing binary (CLAUDE.md, 2026-07-12).
        h.Analyzer.Rejects(@"C:\a.mp4", "Could not run ffprobe: The system cannot find the file specified.");
        await h.Vm.LoadVideoAsync(@"C:\a.mp4");

        var first = Assert.Single(h.Notifications.Sent);
        Assert.Contains("Could not run ffprobe", first.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("no video track", first.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(NotificationSeverity.Warning, first.Severity);
        Assert.False(h.Vm.SourceLoaded);

        // Case 2: the probe SUCCEEDS but the file has no video track (an audio file). A different
        // problem entirely, so it must be a different message.
        h.Analyzer.Returns(@"C:\b.mp3", AudioOnly());
        await h.Vm.LoadVideoAsync(@"C:\b.mp3");

        Assert.Equal(2, h.Notifications.Sent.Count);
        var second = h.Notifications.Sent[1];
        Assert.Contains("no video track", second.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(h.Vm.SourceLoaded);
    }

    [Fact]
    public async Task LoadVideoAsync_DefaultsTheRangeToTheWholeVideo()
    {
        var h = Build();
        h.Analyzer.Returns(VideoPath, Info(seconds: 37));

        await h.Vm.LoadVideoAsync(VideoPath);

        Assert.Equal(TimeSpan.Zero, TrimParsing.TryParse(h.Vm.StartText));
        Assert.Equal(TimeSpan.FromSeconds(37), TrimParsing.TryParse(h.Vm.EndText));
    }

    [Fact]
    public async Task Bounds_NeverOfferASizeOrRateAboveTheSource()
    {
        var h = Build();
        h.Analyzer.Returns(VideoPath, Info(width: 1920, height: 1080, fps: 30));

        await h.Vm.LoadVideoAsync(VideoPath);

        Assert.All(h.Vm.Bounds.Sizes, size => Assert.True(size.Width <= 1920 && size.Height <= 1080));
        Assert.All(h.Vm.Bounds.FrameRates, rate => Assert.True(rate.Value <= 30));
    }

    // ---- the estimate ----------------------------------------------------------

    [Fact]
    public async Task Estimate_UpdatesWhenTheRangeChanges()
    {
        var h = await BuildLoadedAsync(seconds: 20);
        var before = h.Vm.EstimateText;
        Assert.False(string.IsNullOrEmpty(before));

        h.Vm.EndText = "0:05"; // shrink from the full 20s default range down to 5s

        Assert.NotEqual(before, h.Vm.EstimateText);
    }

    [Fact]
    public async Task Estimate_UpdatesWhenTheSizeOrFrameRateChanges()
    {
        var h = await BuildLoadedAsync(width: 1920, height: 1080, fps: 30, seconds: 20);

        var beforeSize = h.Vm.EstimateText;
        h.Vm.SelectedSize = h.Vm.Bounds.Sizes[^1]; // the smallest offered size
        Assert.NotEqual(beforeSize, h.Vm.EstimateText);

        var beforeRate = h.Vm.EstimateText;
        h.Vm.SelectedFrameRate = h.Vm.Bounds.FrameRates[^1]; // the slowest offered rate
        Assert.NotEqual(beforeRate, h.Vm.EstimateText);
    }

    [Fact]
    public async Task ShowSizeWarning_IsTrue_WhenTheEstimateExceedsTheThreshold()
    {
        // A full 1080p/30fps/10s GIF is comfortably over 5 MB with the seed profile.
        var big = await BuildLoadedAsync(width: 1920, height: 1080, fps: 30, seconds: 10);
        Assert.True(big.Vm.ShowSizeWarning);

        // A tiny GIF is comfortably under it -- both directions must genuinely move the flag.
        var small = await BuildLoadedAsync(width: 320, height: 180, fps: 10, seconds: 1);
        Assert.False(small.Vm.ShowSizeWarning);
    }

    // ---- the range hint ----------------------------------------------------

    [Fact]
    public async Task RangeHint_ExplainsAnInvalidRange_RatherThanSilentlyDisablingCreate()
    {
        var h = await BuildLoadedAsync(seconds: 10);

        h.Vm.StartText = "garbage";
        Assert.False(h.Vm.CanCreate);
        var unparseableHint = h.Vm.RangeHint;
        Assert.False(string.IsNullOrWhiteSpace(unparseableHint));

        h.Vm.StartText = "0:05";
        h.Vm.EndText = "0:02"; // end before start
        Assert.False(h.Vm.CanCreate);
        var endBeforeStartHint = h.Vm.RangeHint;
        Assert.NotEqual(unparseableHint, endBeforeStartHint);

        h.Vm.StartText = "0:00";
        h.Vm.EndText = "0:59"; // past the 10s video
        Assert.False(h.Vm.CanCreate);
        var pastEndHint = h.Vm.RangeHint;
        Assert.NotEqual(endBeforeStartHint, pastEndHint);

        Assert.Contains("start", unparseableHint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("after", endBeforeStartHint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("past the end", pastEndHint, StringComparison.OrdinalIgnoreCase);
    }

    // ---- CanCreate ----------------------------------------------------------

    [Fact]
    public async Task CanCreate_IsFalse_UntilAVideoIsLoadedAndTheRangeIsValid()
    {
        var h = Build();
        Assert.False(h.Vm.CanCreate); // no video yet

        h.Analyzer.Returns(VideoPath, Info(seconds: 10));
        await h.Vm.LoadVideoAsync(VideoPath);
        Assert.True(h.Vm.CanCreate); // the whole-video default range is valid

        h.Vm.EndText = "abc";
        Assert.False(h.Vm.CanCreate);

        h.Vm.EndText = "0:08";
        Assert.True(h.Vm.CanCreate);

        h.Vm.OutputFileName = "";
        Assert.False(h.Vm.CanCreate);
    }

    // ---- creating -----------------------------------------------------------

    [Fact]
    public async Task CreateAsync_PassesTheChosenSizeRangeAndRate_ToTheService()
    {
        var h = await BuildLoadedAsync(width: 1920, height: 1080, fps: 30, seconds: 10);
        h.Vm.SelectedSize = h.Vm.Bounds.Sizes[1]; // not the default
        h.Vm.SelectedFrameRate = h.Vm.Bounds.FrameRates[1];
        h.Vm.StartText = "0:02";
        h.Vm.EndText = "0:07";

        await h.Vm.CreateCommand.ExecuteAsync(null);

        var request = h.Service.Request;
        Assert.NotNull(request);
        Assert.Equal(VideoPath, request!.SourcePath);
        Assert.Equal(h.Vm.SelectedSize, request.Size);
        Assert.Equal(h.Vm.SelectedFrameRate, request.Fps);
        Assert.Equal(TimeSpan.FromSeconds(2), request.Start);
        Assert.Equal(TimeSpan.FromSeconds(7), request.End);
    }

    [Fact]
    public async Task CreateAsync_OnSuccess_WritesAHistoryRowWithSourceGif_AndNotifies()
    {
        var h = await BuildLoadedAsync(seconds: 10);

        await h.Vm.CreateCommand.ExecuteAsync(null);

        var entry = Assert.Single(h.History.Entries);
        Assert.Equal(HistorySource.Gif, entry.Source);
        Assert.Equal("Completed", entry.Status);
        Assert.Equal("", entry.Url); // a GIF made from a local file has no URL
        Assert.Contains("GIF", entry.Format, StringComparison.Ordinal);

        var notification = Assert.Single(h.Notifications.Sent);
        Assert.Equal(NotificationSeverity.Success, notification.Severity);
    }

    [Fact]
    public async Task CreateAsync_OnFailure_NotifiesTheServicesReason_AndWritesNoHistory()
    {
        var h = await BuildLoadedAsync(seconds: 10);
        const string reason = "The video could not be read. It may be corrupt, or not really a video.";
        h.Service.Behavior = (_, _, _) => Task.FromResult(Result<string>.Failure(reason));

        await h.Vm.CreateCommand.ExecuteAsync(null);

        Assert.Empty(h.History.Entries);
        var notification = Assert.Single(h.Notifications.Sent);
        Assert.Equal(NotificationSeverity.Error, notification.Severity);
        Assert.Equal(reason, notification.Message);
    }

    [Fact]
    public async Task WhileRendering_TheParametersAreFrozen()
    {
        var h = await BuildLoadedAsync(seconds: 10);
        var gate = new TaskCompletionSource();
        h.Service.Behavior = async (request, _, _) =>
        {
            await gate.Task;
            return Result<string>.Success(request.OutputPath);
        };

        var rendering = h.Vm.CreateCommand.ExecuteAsync(null);
        Assert.True(h.Vm.IsRendering);
        Assert.False(h.Vm.CanEditParameters);
        Assert.False(h.Vm.CreateCommand.CanExecute(null));
        Assert.True(h.Vm.CanCancel);

        gate.SetResult();
        await rendering;

        Assert.False(h.Vm.IsRendering);
        Assert.True(h.Vm.CanEditParameters);
        Assert.True(h.Vm.CreateCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancel_StopsTheService_AndThaws()
    {
        var h = await BuildLoadedAsync(seconds: 10);
        var gate = new TaskCompletionSource();
        CancellationToken? seenToken = null;
        h.Service.Behavior = async (request, _, ct) =>
        {
            seenToken = ct;
            await gate.Task.WaitAsync(ct); // throws once ct is cancelled -- never completes otherwise
            return Result<string>.Success(request.OutputPath);
        };

        var rendering = h.Vm.CreateCommand.ExecuteAsync(null);
        Assert.True(h.Vm.IsRendering);

        h.Vm.Cancel();
        await rendering;

        Assert.NotNull(seenToken);
        Assert.True(seenToken!.Value.IsCancellationRequested); // Cancel reached the SERVICE's token
        Assert.False(h.Vm.IsRendering);
        Assert.True(h.Vm.CanEditParameters); // thawed

        var notification = Assert.Single(h.Notifications.Sent);
        Assert.Equal(NotificationSeverity.Info, notification.Severity); // canceled, not an error
        Assert.Empty(h.History.Entries);
    }

    [Fact]
    public async Task ABrokenHistorySink_DoesNotReportAGoodGifAsFailed()
    {
        var h = await BuildLoadedAsync(seconds: 10);
        h.History.AppendThrows = new IOException("history.json is locked");

        await h.Vm.CreateCommand.ExecuteAsync(null);

        Assert.Contains(h.Notifications.Sent, n => n.Severity == NotificationSeverity.Success);
        Assert.DoesNotContain(h.Notifications.Sent, n => n.Severity == NotificationSeverity.Error);
        Assert.Contains(h.Notifications.Sent, n => n.Severity == NotificationSeverity.Warning);
    }
}
