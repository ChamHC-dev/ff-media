using FFMedia.Core.History;
using FFMedia.Core.Notifications;
using FFMedia.Core.Results;
using FFMedia.Core.Settings;
using FFMedia.Media;
using FFMedia.Tools.VideoMerger.Models;
using FFMedia.Tools.VideoMerger.Services;
using FFMedia.Tools.VideoMerger.ViewModels;
using Xunit;

namespace FFMedia.Tests.VideoMerger;

public class MergerViewModelTests
{
    // ---- fakes -------------------------------------------------------------

    private sealed class FakeAnalyzer : IMediaAnalyzer
    {
        private readonly Dictionary<string, Result<MediaInfo>> _byPath = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Probed { get; } = new();

        public void Returns(string path, MediaInfo info) => _byPath[path] = Result<MediaInfo>.Success(info);

        public void Rejects(string path, string error) => _byPath[path] = Result<MediaInfo>.Failure(error);

        public Task<Result<MediaInfo>> AnalyzeAsync(string filePath, CancellationToken ct = default)
        {
            Probed.Add(filePath);
            return Task.FromResult(_byPath.TryGetValue(filePath, out var r)
                ? r
                : Result<MediaInfo>.Failure("not configured"));
        }
    }

    private sealed class FakeMergeService : IMergeService
    {
        public MergeRequest? Request { get; private set; }

        public Task<Result<string>> MergeAsync(
            MergeRequest request, IProgress<MergeProgress>? progress = null, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            Request = request;
            return Task.FromResult(Result<string>.Success(request.OutputPath));
        }
    }

    private sealed class FakeSpeedStore : ISpeedProfileStore
    {
        public SpeedProfile Profile { get; set; } = new();

        public SpeedProfile Load() => Profile;

        public void Save(SpeedProfile profile) => Profile = profile;
    }

    /// <summary>Mirrors the real <see cref="ISettingsService"/>: <c>Changed</c> is
    /// <c>EventHandler&lt;AppSettings&gt;</c>, not a bare <c>EventHandler</c>.</summary>
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

        public event EventHandler? Changed;

        public IReadOnlyList<HistoryEntry> Query() => Entries;

        public void Append(HistoryEntry entry)
        {
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

    private static MediaInfo Info(int width = 1920, int height = 1080, string codec = "h264", double seconds = 5)
        => new(TimeSpan.FromSeconds(seconds), "mov,mp4,m4a",
            new VideoStreamInfo(width, height, new FrameRate(30, 1), codec, "yuv420p", 0),
            new AudioStreamInfo("aac", 48000, 2));

    /// <summary>A probe that succeeds but found no video track — an audio file.</summary>
    private static MediaInfo AudioOnly()
        => new(TimeSpan.FromSeconds(30), "mov,mp4,m4a", null, new AudioStreamInfo("aac", 48000, 2));

    private sealed record Harness(
        MergerViewModel Vm, FakeAnalyzer Analyzer, FakeMergeService Merger,
        FakeHistory History, FakeNotifications Notifications, FakeSettings Settings);

    private static Harness Build()
    {
        var analyzer = new FakeAnalyzer();
        var merger = new FakeMergeService();
        var history = new FakeHistory();
        var notifications = new FakeNotifications();
        var settings = new FakeSettings();
        var vm = new MergerViewModel(
            analyzer, merger, new FakeSpeedStore(), settings, history, notifications);
        return new Harness(vm, analyzer, merger, history, notifications, settings);
    }

    /// <summary>A harness holding exactly these clips, in this order, by file name.</summary>
    private static async Task<Harness> BuildWithAsync(params string[] names)
    {
        var h = Build();
        foreach (var name in names)
        {
            h.Analyzer.Returns($@"C:\{name}", Info());
        }

        await h.Vm.AddClipsAsync(names.Select(name => $@"C:\{name}"));
        return h;
    }

    private static async Task<Harness> BuildWithClipsAsync(int count)
    {
        var h = Build();
        for (var i = 0; i < count; i++)
        {
            h.Analyzer.Returns($@"C:\{i}.mp4", Info());
        }

        await h.Vm.AddClipsAsync(Enumerable.Range(0, count).Select(i => $@"C:\{i}.mp4"));
        return h;
    }

    private static List<string> Names(MergerViewModel vm) => vm.Clips.Select(c => c.FileName).ToList();

    // ---- adding ------------------------------------------------------------

    [Fact]
    public async Task AddClipsAsync_ProbesEachFileAndAddsItInOrder()
    {
        var h = Build();
        h.Analyzer.Returns(@"C:\a.mp4", Info());
        h.Analyzer.Returns(@"C:\b.mp4", Info(1280, 720, "vp9"));

        await h.Vm.AddClipsAsync([@"C:\a.mp4", @"C:\b.mp4"]);

        Assert.Equal(new[] { "a.mp4", "b.mp4" }, Names(h.Vm));
        Assert.Equal(new[] { @"C:\a.mp4", @"C:\b.mp4" }, h.Analyzer.Probed);
        Assert.Equal(@"C:\b.mp4", h.Vm.Clips[1].SourcePath);
        Assert.Equal("1280x720 · 30 fps · vp9 · 0:05", h.Vm.Clips[1].Details); // the probe really reached the row
        Assert.Empty(h.Notifications.Sent);
    }

    [Fact]
    public void Constructor_SeedsTheOutputFolderFromSettings()
    {
        var h = Build();

        Assert.Equal(@"C:\out", h.Vm.OutputFolder);
    }

    [Fact]
    public async Task AddClipsAsync_RejectsAFileTheAnalyzerCannotRead_RatherThanPoisoningTheMerge()
    {
        // Spec §8: a bad file is rejected AT ADD TIME. Letting it into the list would fail the
        // whole merge later, after the user has spent minutes ordering clips.
        var h = Build();
        h.Analyzer.Returns(@"C:\good.mp4", Info());
        h.Analyzer.Rejects(@"C:\notes.txt", "no video stream");

        await h.Vm.AddClipsAsync([@"C:\good.mp4", @"C:\notes.txt"]);

        Assert.Equal(new[] { "good.mp4" }, Names(h.Vm));
        var notification = Assert.Single(h.Notifications.Sent);
        Assert.Equal("Not a video", notification.Title);
        Assert.Equal("notes.txt could not be read as a video and was not added.", notification.Message);
        Assert.Equal(NotificationSeverity.Warning, notification.Severity);
    }

    [Fact]
    public async Task AddClipsAsync_RejectsAnAudioFile_EvenThoughTheProbeSucceeded()
    {
        // The probe reads voiceover.m4a perfectly well — it simply has no video track. Concat would
        // fail on it (or silently mangle the layout), so it must never reach the list.
        var h = Build();
        h.Analyzer.Returns(@"C:\good.mp4", Info());
        h.Analyzer.Returns(@"C:\voiceover.m4a", AudioOnly());

        await h.Vm.AddClipsAsync([@"C:\voiceover.m4a", @"C:\good.mp4"]);

        Assert.Equal(new[] { "good.mp4" }, Names(h.Vm));
        var notification = Assert.Single(h.Notifications.Sent);
        Assert.Equal("voiceover.m4a could not be read as a video and was not added.", notification.Message);
        Assert.Equal(NotificationSeverity.Warning, notification.Severity);
    }

    [Fact]
    public async Task AddClipsAsync_IgnoresAFileAlreadyInTheList_WithoutReprobingIt()
    {
        var h = Build();
        h.Analyzer.Returns(@"C:\a.mp4", Info());

        await h.Vm.AddClipsAsync([@"C:\a.mp4"]);
        await h.Vm.AddClipsAsync([@"C:\a.mp4"]);

        Assert.Single(h.Vm.Clips);
        Assert.Single(h.Analyzer.Probed);
        Assert.Empty(h.Notifications.Sent); // a duplicate is not an error — say nothing
    }

    [Fact]
    public async Task AddClipsAsync_IgnoresADuplicateWithinTheSameBatch()
    {
        var h = Build();
        h.Analyzer.Returns(@"C:\a.mp4", Info());

        await h.Vm.AddClipsAsync([@"C:\a.mp4", @"C:\A.MP4"]); // Windows paths are case-insensitive

        Assert.Single(h.Vm.Clips);
        Assert.Single(h.Analyzer.Probed);
    }

    [Fact]
    public async Task AddClipsAsync_RejectsNullPaths()
    {
        var h = Build();

        await Assert.ThrowsAsync<ArgumentNullException>(() => h.Vm.AddClipsAsync(null!));
    }

    [Fact]
    public void Constructor_RejectsEveryNullDependency()
    {
        var analyzer = new FakeAnalyzer();
        var merger = new FakeMergeService();
        var speeds = new FakeSpeedStore();
        var settings = new FakeSettings();
        var history = new FakeHistory();
        var notifications = new FakeNotifications();

        Assert.Throws<ArgumentNullException>(() =>
            new MergerViewModel(null!, merger, speeds, settings, history, notifications));
        Assert.Throws<ArgumentNullException>(() =>
            new MergerViewModel(analyzer, null!, speeds, settings, history, notifications));
        Assert.Throws<ArgumentNullException>(() =>
            new MergerViewModel(analyzer, merger, null!, settings, history, notifications));
        Assert.Throws<ArgumentNullException>(() =>
            new MergerViewModel(analyzer, merger, speeds, null!, history, notifications));
        Assert.Throws<ArgumentNullException>(() =>
            new MergerViewModel(analyzer, merger, speeds, settings, null!, notifications));
        Assert.Throws<ArgumentNullException>(() =>
            new MergerViewModel(analyzer, merger, speeds, settings, history, null!));
    }

    // ---- reordering --------------------------------------------------------

    [Fact]
    public async Task MoveUpAndDown_ReorderTheList_AndStopAtTheEnds()
    {
        var h = Build();
        h.Analyzer.Returns(@"C:\a.mp4", Info());
        h.Analyzer.Returns(@"C:\b.mp4", Info());
        await h.Vm.AddClipsAsync([@"C:\a.mp4", @"C:\b.mp4"]);

        h.Vm.MoveDown(h.Vm.Clips[0]);
        Assert.Equal(new[] { "b.mp4", "a.mp4" }, Names(h.Vm));

        h.Vm.MoveUp(h.Vm.Clips[1]);
        Assert.Equal(new[] { "a.mp4", "b.mp4" }, Names(h.Vm));

        h.Vm.MoveUp(h.Vm.Clips[0]);   // already first — a no-op, not a crash
        h.Vm.MoveDown(h.Vm.Clips[1]); // already last
        Assert.Equal(new[] { "a.mp4", "b.mp4" }, Names(h.Vm));
    }

    [Fact]
    public async Task MoveUp_AtTheTop_DoesNothing_RatherThanWrappingToTheBottom()
    {
        // Asserted on its own, with THREE clips. The previous test could not catch a wrap-around:
        // with two clips, a wrapping MoveUp followed by a wrapping MoveDown cancel out, and the
        // list looks untouched. The bug it hides is loud — clicking "move up" on the top clip
        // teleports it to the bottom.
        var h = await BuildWithAsync("a.mp4", "b.mp4", "c.mp4");

        h.Vm.MoveUp(h.Vm.Clips[0]);

        Assert.Equal(new[] { "a.mp4", "b.mp4", "c.mp4" }, Names(h.Vm));
    }

    [Fact]
    public async Task MoveDown_AtTheBottom_DoesNothing_RatherThanWrappingToTheTop()
    {
        var h = await BuildWithAsync("a.mp4", "b.mp4", "c.mp4");

        h.Vm.MoveDown(h.Vm.Clips[2]);

        Assert.Equal(new[] { "a.mp4", "b.mp4", "c.mp4" }, Names(h.Vm));
    }

    [Fact]
    public async Task Shuffle_HonorsALockSetByBindingIsLockedDirectly_NotJustSetLock()
    {
        // The page's lock toggle two-way binds a checkbox to IsLocked — the obvious thing to do, and
        // it does NOT go through SetLock, so LockedIndex stays null. Ordering.Shuffle reads only
        // LockedIndex, so without a resync the "locked" row would be shuffled like any other and the
        // lock then re-pinned to wherever it randomly landed: worse than the lock doing nothing.
        var h = await BuildWithAsync("a.mp4", "b.mp4", "c.mp4", "d.mp4", "e.mp4", "f.mp4");
        h.Vm.Clips[2].IsLocked = true; // straight at the property, as a binding would
        Assert.Null(h.Vm.Clips[2].LockedIndex);

        for (var seed = 0; seed < 25; seed++)
        {
            h.Vm.ShuffleSeed = seed;
            h.Vm.Shuffle();

            Assert.Equal("c.mp4", h.Vm.Clips[2].FileName);
            Assert.Equal(6, Names(h.Vm).Distinct().Count());
        }
    }

    [Fact]
    public async Task MoveUpOrDown_OnAClipThatIsNotInTheList_IsANoOp()
    {
        var h = await BuildWithClipsAsync(2);
        var stranger = new MergeClipViewModel(new MergeClip(@"C:\stranger.mp4", Info()));

        h.Vm.MoveUp(stranger);
        h.Vm.MoveDown(stranger);

        Assert.Equal(new[] { "0.mp4", "1.mp4" }, Names(h.Vm));
    }

    [Fact]
    public async Task RemoveClip_DropsIt()
    {
        var h = Build();
        h.Analyzer.Returns(@"C:\a.mp4", Info());
        h.Analyzer.Returns(@"C:\b.mp4", Info());
        await h.Vm.AddClipsAsync([@"C:\a.mp4", @"C:\b.mp4"]);

        h.Vm.RemoveClip(h.Vm.Clips[0]);

        Assert.Equal(new[] { "b.mp4" }, Names(h.Vm));
    }

    [Fact]
    public async Task RemoveClip_LetsTheSameFileBeAddedAgain()
    {
        var h = await BuildWithClipsAsync(1);

        h.Vm.RemoveClip(h.Vm.Clips[0]);
        await h.Vm.AddClipsAsync([@"C:\0.mp4"]);

        Assert.Equal(new[] { "0.mp4" }, Names(h.Vm));
        Assert.Equal(2, h.Analyzer.Probed.Count); // it is genuinely re-probed, not resurrected
    }

    // ---- shuffle -----------------------------------------------------------

    [Fact]
    public async Task Shuffle_KeepsLockedClipsAtTheirIndex()
    {
        var h = await BuildWithClipsAsync(6);

        h.Vm.Clips[2].SetLock(locked: true, index: 2);
        h.Vm.Clips[5].SetLock(locked: true, index: 5);

        for (var seed = 0; seed < 25; seed++)
        {
            h.Vm.ShuffleSeed = seed;
            h.Vm.Shuffle();

            Assert.Equal("2.mp4", h.Vm.Clips[2].FileName);
            Assert.Equal("5.mp4", h.Vm.Clips[5].FileName);
            Assert.Equal(6, h.Vm.Clips.Select(c => c.FileName).Distinct().Count()); // nothing lost or duplicated
        }
    }

    [Fact]
    public async Task Shuffle_ActuallyReordersTheUnlockedClips()
    {
        // Guards the direction the lock test cannot: a Shuffle() that did nothing at all would
        // satisfy every "locked clip stayed put" assertion above.
        var h = await BuildWithClipsAsync(6);

        var orders = new HashSet<string>(StringComparer.Ordinal);
        for (var seed = 0; seed < 25; seed++)
        {
            h.Vm.ShuffleSeed = seed;
            h.Vm.Shuffle();
            orders.Add(string.Join(",", Names(h.Vm)));
        }

        Assert.True(orders.Count > 1, "Shuffle never changed the order.");
    }

    [Fact]
    public async Task Shuffle_IsDeterministicForAGivenSeed()
    {
        var a = await BuildWithClipsAsync(6);
        var b = await BuildWithClipsAsync(6);

        a.Vm.ShuffleSeed = 4242;
        a.Vm.Shuffle();
        b.Vm.ShuffleSeed = 4242;
        b.Vm.Shuffle();

        Assert.Equal(Names(a.Vm), Names(b.Vm));
    }

    [Fact]
    public async Task Shuffle_OnZeroOrOneClip_IsANoOp()
    {
        var empty = Build();
        empty.Vm.ShuffleSeed = 7;
        empty.Vm.Shuffle();
        Assert.Empty(empty.Vm.Clips);

        var single = await BuildWithClipsAsync(1);
        single.Vm.ShuffleSeed = 7;
        single.Vm.Shuffle();
        Assert.Equal(new[] { "0.mp4" }, Names(single.Vm));
    }

    // ---- the lock/index invariant: locks pin an OCCUPIED slot ---------------

    [Fact]
    public async Task RemovingAClipAboveALockedOne_ResyncsTheLock_SoShuffleDoesNotPinAStaleSlot()
    {
        // "5.mp4" is locked to index 5. Delete "0.mp4" and it slides to index 4 — but its lock still
        // says 5. Shuffle would then pin it to a row the user can see it is not in (or, with a second
        // lock, pin two clips to one slot and throw).
        var h = await BuildWithClipsAsync(6);
        h.Vm.Clips[5].SetLock(locked: true, index: 5);

        h.Vm.RemoveClip(h.Vm.Clips[0]);

        Assert.Equal(4, h.Vm.Clips[4].LockedIndex);

        for (var seed = 0; seed < 25; seed++)
        {
            h.Vm.ShuffleSeed = seed;
            h.Vm.Shuffle(); // must not throw
            Assert.Equal("5.mp4", h.Vm.Clips[4].FileName);
            Assert.Equal(5, h.Vm.Clips.Count);
        }
    }

    [Fact]
    public async Task RemovingAClip_CannotLeaveTwoLocksOnTheSameIndex()
    {
        // Without a resync, deleting index 0 leaves locks on 2 and 3 pointing at 2 and 3 while the
        // rows now sit at 1 and 2 — and the next structural edit collapses them onto one slot.
        // Ordering.Shuffle throws ArgumentException when two clips claim the same index.
        var h = await BuildWithClipsAsync(5);
        h.Vm.Clips[2].SetLock(locked: true, index: 2);
        h.Vm.Clips[3].SetLock(locked: true, index: 3);

        h.Vm.RemoveClip(h.Vm.Clips[0]);
        h.Vm.ShuffleSeed = 1;
        h.Vm.Shuffle();

        Assert.Equal(new[] { 1, 2 }, h.Vm.Clips.Where(c => c.IsLocked).Select(c => c.LockedIndex!.Value).ToArray());
        Assert.Equal("2.mp4", h.Vm.Clips[1].FileName);
        Assert.Equal("3.mp4", h.Vm.Clips[2].FileName);
    }

    [Fact]
    public async Task MovingAClipPastALockedOne_ResyncsTheLockToWhereTheRowNowSits()
    {
        // The lock pins the SLOT, not the clip: dragging a neighbour past a locked row moves that
        // row, and the lock must follow it to its new index or the next shuffle teleports it back.
        var h = await BuildWithClipsAsync(3);
        h.Vm.Clips[1].SetLock(locked: true, index: 1); // "1.mp4" pinned in the middle

        h.Vm.MoveUp(h.Vm.Clips[1]); // the user drags the locked row itself up

        Assert.Equal("1.mp4", h.Vm.Clips[0].FileName);
        Assert.Equal(0, h.Vm.Clips[0].LockedIndex);

        for (var seed = 0; seed < 25; seed++)
        {
            h.Vm.ShuffleSeed = seed;
            h.Vm.Shuffle();
            Assert.Equal("1.mp4", h.Vm.Clips[0].FileName);
        }
    }

    [Fact]
    public async Task AddingAClip_LeavesExistingLocksWhereTheyAre()
    {
        // Appends land below, so no existing row shifts — but the resync must not invent a lock on an
        // unlocked row either.
        var h = await BuildWithClipsAsync(3);
        h.Vm.Clips[0].SetLock(locked: true, index: 0);
        h.Analyzer.Returns(@"C:\new.mp4", Info());

        await h.Vm.AddClipsAsync([@"C:\new.mp4"]);

        Assert.Equal(0, h.Vm.Clips[0].LockedIndex);
        Assert.Equal(1, h.Vm.Clips.Count(c => c.IsLocked));
        Assert.All(h.Vm.Clips.Skip(1), c => Assert.Null(c.LockedIndex));
    }

    [Fact]
    public async Task UnlockingARow_FreesItToMove()
    {
        var h = await BuildWithClipsAsync(6);
        h.Vm.Clips[0].SetLock(locked: true, index: 0);
        h.Vm.Clips[0].SetLock(locked: false, index: 0);

        var orders = new HashSet<string>(StringComparer.Ordinal);
        for (var seed = 0; seed < 25; seed++)
        {
            h.Vm.ShuffleSeed = seed;
            h.Vm.Shuffle();
            orders.Add(h.Vm.Clips[0].FileName);
        }

        Assert.True(orders.Count > 1, "An unlocked row stayed pinned to index 0.");
    }
}
