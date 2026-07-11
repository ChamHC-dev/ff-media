using FFMedia.Core.Results;

namespace FFMedia.Media;

/// <summary>Runs the bundled ffmpeg with a caller-supplied argument list, streaming progress.</summary>
public interface IFfmpegRunner
{
    /// <summary>Runs ffmpeg. Standard flags (<c>-hide_banner -nostdin -y</c>) are prepended and
    /// <c>-progress pipe:1 -nostats</c> appended by the implementation. Returns a failure carrying
    /// ffmpeg's stderr tail on a non-zero exit; cancellation propagates.</summary>
    Task<Result> RunAsync(
        IReadOnlyList<string> arguments,
        IProgress<FfmpegProgress>? progress = null,
        CancellationToken ct = default);
}
