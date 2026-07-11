using System;
using System.IO;
using System.Linq;
using FFMedia.Tools.VideoMerger.Services;
using Xunit;

namespace FFMedia.Tests.VideoMerger;

public class TempDirectorySweeperTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ffmedia-sweep-" + Guid.NewGuid().ToString("N"));

    public TempDirectorySweeperTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private string MakeDirectory(string name, DateTime writeTimeUtc)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        Directory.SetLastWriteTimeUtc(path, writeTimeUtc);
        return path;
    }

    private string[] Remaining()
        => [.. Directory.GetDirectories(_root).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal)!];

    [Fact]
    public void SweepOrphans_RemovesOldMergeDirectories_AndNothingElse()
    {
        // One fixture per decision the sweeper makes, all present at once: age alone must not be
        // enough (something-else is ancient), and the prefix alone must not be enough (merge-recent
        // could still belong to a merge running right now, possibly in another instance).
        var now = DateTime.UtcNow;
        MakeDirectory("merge-old", now.AddHours(-25));
        MakeDirectory("merge-ancient", now.AddDays(-9));
        MakeDirectory("merge-recent", now.AddHours(-1));
        MakeDirectory("something-else", now.AddDays(-9));

        var removed = TempDirectorySweeper.SweepOrphans(_root, TimeSpan.FromHours(24), now);

        Assert.Equal(2, removed);
        Assert.Equal(["merge-recent", "something-else"], Remaining());
    }

    [Fact]
    public void SweepOrphans_DeletesTheDirectoryContents_NotJustAnEmptyShell()
    {
        // The whole point is reclaiming disk: an orphan holds gigabytes of intermediates.
        var now = DateTime.UtcNow;
        var orphan = MakeDirectory("merge-old", now);
        File.WriteAllText(Path.Combine(orphan, "0001.mp4"), "segment");
        Directory.CreateDirectory(Path.Combine(orphan, "nested"));
        File.WriteAllText(Path.Combine(orphan, "nested", "list.txt"), "file 'x'");
        Directory.SetLastWriteTimeUtc(orphan, now.AddHours(-25));

        Assert.Equal(1, TempDirectorySweeper.SweepOrphans(_root, TimeSpan.FromHours(24), now));
        Assert.False(Directory.Exists(orphan));
    }

    [Fact]
    public void SweepOrphans_KeepsADirectoryExactlyAtTheAgeLimit()
    {
        // Strictly older-than: at exactly the cutoff we keep it. The boundary matters because the
        // cost of a false positive (deleting a live merge's intermediates) far exceeds the cost of
        // leaving debris one more launch.
        var now = DateTime.UtcNow;
        var edge = MakeDirectory("merge-edge", now.AddHours(-24));

        Assert.Equal(0, TempDirectorySweeper.SweepOrphans(_root, TimeSpan.FromHours(24), now));
        Assert.True(Directory.Exists(edge));
    }

    [Fact]
    public void SweepOrphans_SkipsALockedDirectory_AndKeepsGoing()
    {
        // A merge still running holds its files open. The sweep must not throw, must not abandon the
        // remaining candidates, and must not count the one it failed to remove.
        var now = DateTime.UtcNow;
        var locked = MakeDirectory("merge-locked", now);
        var sweepable = MakeDirectory("merge-sweepable", now.AddHours(-25));

        using (var handle = new FileStream(
            Path.Combine(locked, "busy.mp4"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            handle.WriteByte(1);
            Directory.SetLastWriteTimeUtc(locked, now.AddHours(-25));

            var removed = TempDirectorySweeper.SweepOrphans(_root, TimeSpan.FromHours(24), now);

            Assert.Equal(1, removed);
            Assert.Equal(["merge-locked"], Remaining());
            Assert.False(Directory.Exists(sweepable));
        }
    }

    [Fact]
    public void SweepOrphans_MissingRootIsNoOp()
    {
        Assert.Equal(0, TempDirectorySweeper.SweepOrphans(
            Path.Combine(_root, "nope"), TimeSpan.FromHours(24), DateTime.UtcNow));
    }

    [Fact]
    public void SweepOrphans_EmptyRootIsNoOp()
    {
        Assert.Equal(0, TempDirectorySweeper.SweepOrphans(_root, TimeSpan.FromHours(24), DateTime.UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SweepOrphans_RejectsABlankRoot(string root)
    {
        Assert.Throws<ArgumentException>(() =>
            TempDirectorySweeper.SweepOrphans(root, TimeSpan.FromHours(24), DateTime.UtcNow));
    }

    [Fact]
    public void SweepOrphans_RejectsANullRoot()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TempDirectorySweeper.SweepOrphans(null!, TimeSpan.FromHours(24), DateTime.UtcNow));
    }

    [Fact]
    public void SweepOrphans_RejectsANegativeAgeLimit()
    {
        // A negative limit would make every directory "old enough", including one being written to.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TempDirectorySweeper.SweepOrphans(_root, TimeSpan.FromHours(-1), DateTime.UtcNow));
    }
}
