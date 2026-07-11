using FFMedia.Media;

namespace FFMedia.Tools.VideoMerger.Models;

/// <summary>The standardization target every clip is conformed to before concatenation.</summary>
public sealed record MergeTarget(
    int Width,
    int Height,
    FrameRate FrameRate,
    MergeVideoCodec VideoCodec,
    int Crf,
    MergeContainer Container,
    MergeAudioCodec AudioCodec,
    int AudioSampleRate,
    int AudioChannels,
    FitMode FitMode)
{
    /// <summary>Bits per pixel per frame, a rough x264 CRF-20 constant used only for size estimation.</summary>
    private const double BitsPerPixel = 0.08;

    private const long AudioBitsPerSecond = 192_000;

    public static MergeTarget Default { get; } = new(
        1920, 1080, new FrameRate(30, 1), MergeVideoCodec.H264, 20,
        MergeContainer.Mp4, MergeAudioCodec.Aac, 48_000, 2, FitMode.Fit);

    public long PixelCount => (long)Width * Height;

    /// <summary>Heuristic output bitrate, used to size temp files and the disk-space guard.
    /// H.265 is assumed ~35 % more efficient than H.264 at the same perceived quality.</summary>
    public long EstimatedBitsPerSecond
    {
        get
        {
            var videoBits = PixelCount * FrameRate.Value * BitsPerPixel;
            if (VideoCodec == MergeVideoCodec.H265)
            {
                videoBits *= 0.65;
            }

            return (long)videoBits + AudioBitsPerSecond;
        }
    }
}
