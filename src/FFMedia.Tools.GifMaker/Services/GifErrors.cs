namespace FFMedia.Tools.GifMaker.Services;

/// <summary>Turns ffmpeg's stderr into something the user can act on. A static per-module mapper,
/// matching <c>MergeErrors</c> and <c>YtDlpErrors</c>.</summary>
public static class GifErrors
{
    public static string Explain(string? ffmpegError)
    {
        if (string.IsNullOrWhiteSpace(ffmpegError))
        {
            return "ffmpeg failed without saying why.";
        }

        if (ffmpegError.Contains("No such file", StringComparison.OrdinalIgnoreCase)
            || ffmpegError.Contains("cannot find the file", StringComparison.OrdinalIgnoreCase))
        {
            return "The video could not be found. It may have been moved or renamed since you added it.";
        }

        if (ffmpegError.Contains("Invalid data found", StringComparison.OrdinalIgnoreCase))
        {
            return "The video could not be read. It may be corrupt, or not really a video.";
        }

        if (ffmpegError.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
        {
            return "The GIF could not be written. Check that the output folder exists and is writable.";
        }

        if (ffmpegError.Contains("No space left", StringComparison.OrdinalIgnoreCase))
        {
            return "There is not enough disk space to write the GIF.";
        }

        return ffmpegError;
    }
}
