using System.Text;
using FFMedia.Tools.VideoMerger.Models;

namespace FFMedia.Tools.VideoMerger.Services;

/// <summary>Pure builder for the ffmpeg <c>concat</c> demuxer list file and the stream-copy
/// merge arguments. <c>-hide_banner -nostdin -y</c> and the <c>-progress</c> flags are added by
/// <see cref="FFMedia.Media.IFfmpegRunner"/>, not here.</summary>
public static class ConcatArgsBuilder
{
    /// <summary>Renders the <c>-f concat</c> list file content: one <c>file '&lt;path&gt;'</c> line
    /// per segment, LF-terminated. Paths are single-quoted; an embedded apostrophe is escaped the
    /// shell way — close quote, escaped quote, reopen quote (<c>'\''</c>) — which is how ffmpeg's
    /// own list-file tokenizer (<c>av_get_token</c>) unescapes it. Backslashes need no escaping:
    /// that tokenizer copies everything inside a quoted span literally.</summary>
    public static string BuildListFile(IReadOnlyList<string> segmentPaths)
    {
        ArgumentNullException.ThrowIfNull(segmentPaths);
        if (segmentPaths.Count == 0)
        {
            throw new ArgumentException("At least one segment is required.", nameof(segmentPaths));
        }

        var builder = new StringBuilder();
        foreach (var path in segmentPaths)
        {
            builder.Append("file '").Append(path.Replace("'", @"'\''", StringComparison.Ordinal)).Append("'\n");
        }

        return builder.ToString();
    }

    /// <summary>Builds the stream-copy merge argv: <c>-f concat -safe 0 -i &lt;list&gt; -c copy
    /// [-movflags +faststart] &lt;output&gt;</c>. No re-encode — this is the fast path.</summary>
    public static IReadOnlyList<string> BuildArgs(string listFilePath, string outputPath, MergeContainer container)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var args = new List<string>
        {
            "-f", "concat",
            "-safe", "0",
            "-i", listFilePath,
            "-c", "copy",
        };

        if (container == MergeContainer.Mp4)
        {
            args.AddRange(["-movflags", "+faststart"]);
        }

        args.Add(outputPath);
        return args;
    }
}
