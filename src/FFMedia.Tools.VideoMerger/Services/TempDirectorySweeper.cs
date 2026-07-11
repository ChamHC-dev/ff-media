using System.IO;

namespace FFMedia.Tools.VideoMerger.Services;

/// <summary>Removes <c>merge-*</c> temp directories orphaned by a crash, a power loss, or a hard
/// kill. <see cref="MergeService"/> cleans up its own working directory on every normal exit path,
/// so anything left here is debris from a process that never got to run its <c>finally</c>.</summary>
public static class TempDirectorySweeper
{
    public const string DirectoryPrefix = "merge-";

    /// <summary>Deletes orphaned <c>merge-*</c> directories last written more than
    /// <paramref name="olderThan"/> before <paramref name="utcNow"/>, and returns how many it
    /// actually removed.</summary>
    /// <remarks><para><paramref name="olderThan"/> is what keeps this from deleting the working
    /// directory of a merge that is <em>running right now</em> — quite possibly in another instance
    /// of the app. Age is the only evidence we have that a directory is abandoned rather than busy,
    /// so it must be generous.</para>
    /// <para>Never throws for an unsweepable directory: a locked or ACL-protected one is skipped and
    /// left for a later launch, and the sweep continues. Time is injected so the tests need not
    /// sleep.</para></remarks>
    public static int SweepOrphans(string tempRoot, TimeSpan olderThan, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tempRoot);
        ArgumentOutOfRangeException.ThrowIfLessThan(olderThan, TimeSpan.Zero);

        if (!Directory.Exists(tempRoot))
        {
            return 0;
        }

        string[] candidates;
        try
        {
            candidates = Directory.GetDirectories(tempRoot, DirectoryPrefix + "*");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0; // the root itself is unreadable — nothing to do, and not worth crashing launch
        }

        var removed = 0;
        foreach (var directory in candidates)
        {
            try
            {
                var info = new DirectoryInfo(directory);

                // A junction or symlink would make a recursive delete escape the temp root and eat
                // whatever it points at. We only ever create real directories here, so a reparse
                // point is not ours: leave it alone.
                if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                // Strictly older-than, so a directory sitting exactly on the cutoff survives. The
                // boundary is asymmetric on purpose: a false positive deletes a running merge's
                // intermediates, a false negative leaves debris for one more launch.
                if (utcNow - info.LastWriteTimeUtc <= olderThan)
                {
                    continue;
                }

                info.Delete(recursive: true);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use by a running merge, or not ours to delete. Leave it for next launch.
            }
        }

        return removed;
    }
}
