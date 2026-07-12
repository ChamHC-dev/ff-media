using FFMedia.Tools.YouTubeDownloader.Models;

namespace FFMedia.Tools.YouTubeDownloader.Services;

/// <summary>Builds a download-specific <see cref="TrimRange"/> from user-entered trim timestamps.</summary>
public static class TrimParsing
{
    /// <summary>A <see cref="TrimRange"/> only when both parse and End &gt; Start; otherwise null.</summary>
    public static TrimRange? ParseRange(string? start, string? end)
    {
        return FFMedia.Core.Media.TrimParsing.TryParse(start) is { } s &&
            FFMedia.Core.Media.TrimParsing.TryParse(end) is { } e && e > s
            ? new TrimRange(s, e)
            : null;
    }
}
