using FFMedia.Media;

namespace FFMedia.Tools.GifMaker.Models;

/// <summary>One GIF to make. <paramref name="Start"/> and <paramref name="End"/> are positions on the
/// SOURCE's timeline, which is exactly what ffmpeg's <c>-ss</c>/<c>-to</c> take.</summary>
public sealed record GifRequest(
    string SourcePath,
    TimeSpan Start,
    TimeSpan End,
    Resolution Size,
    FrameRate Fps,
    string OutputPath)
{
    public TimeSpan Duration => End - Start;

    /// <summary>How many frames the GIF will hold. Drives both the estimate and the progress weighting.</summary>
    public int FrameCount => Math.Max(1, (int)Math.Round(Duration.TotalSeconds * Fps.Value));
}
