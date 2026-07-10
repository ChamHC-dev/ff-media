using System.Globalization;
using FFMedia.Media;
using FFMedia.Tools.VideoMerger.Models;

namespace FFMedia.Tools.VideoMerger.Services;

/// <summary>Pure builder for the ffmpeg arguments that re-encode one non-conforming clip to the
/// merge target. <c>-hide_banner -nostdin -y</c> and the <c>-progress</c> flags are added by
/// <see cref="IFfmpegRunner"/>, not here.</summary>
public static class NormalizeArgsBuilder
{
    public static IReadOnlyList<string> Build(string sourcePath, MediaInfo info, MergeTarget target, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var args = new List<string> { "-i", sourcePath };
        var silent = !info.HasAudio;

        if (silent)
        {
            args.AddRange(["-f", "lavfi", "-i", AnullsrcSpec(target)]);
        }

        args.AddRange(["-map", "0:v:0", "-map", silent ? "1:a:0" : "0:a:0"]);
        args.AddRange(["-vf", VideoFilter(target)]);
        args.AddRange(["-c:v", VideoEncoder(target.VideoCodec)]);
        args.AddRange(["-crf", target.Crf.ToString(CultureInfo.InvariantCulture)]);
        args.AddRange(["-preset", "medium"]);
        args.AddRange(["-pix_fmt", ConformanceCheck.TargetPixelFormat]);
        args.AddRange(["-c:a", AudioEncoder(target.AudioCodec)]);
        args.AddRange(["-b:a", "192k"]);
        args.AddRange(["-ar", target.AudioSampleRate.ToString(CultureInfo.InvariantCulture)]);
        args.AddRange(["-ac", target.AudioChannels.ToString(CultureInfo.InvariantCulture)]);

        if (silent)
        {
            // anullsrc is infinite; bound it to the video stream's length.
            args.Add("-shortest");
        }

        args.Add(outputPath);
        return args;
    }

    private static string VideoFilter(MergeTarget target)
    {
        var w = target.Width.ToString(CultureInfo.InvariantCulture);
        var h = target.Height.ToString(CultureInfo.InvariantCulture);

        var fit = target.FitMode switch
        {
            FitMode.Fit => $"scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2",
            FitMode.Fill => $"scale={w}:{h}:force_original_aspect_ratio=increase,crop={w}:{h}",
            FitMode.Stretch => $"scale={w}:{h}",
            _ => throw new ArgumentOutOfRangeException(nameof(target), target.FitMode, "Unknown fit mode."),
        };

        return $"{fit},fps={target.FrameRate.ToFfmpegString()},setsar=1";
    }

    private static string AnullsrcSpec(MergeTarget target)
        => $"anullsrc=channel_layout={ChannelLayout(target.AudioChannels)}:sample_rate={target.AudioSampleRate.ToString(CultureInfo.InvariantCulture)}";

    private static string ChannelLayout(int channels) => channels switch
    {
        1 => "mono",
        2 => "stereo",
        6 => "5.1",
        8 => "7.1",
        _ => "stereo",
    };

    private static string VideoEncoder(MergeVideoCodec codec)
        => codec == MergeVideoCodec.H264 ? "libx264" : "libx265";

    private static string AudioEncoder(MergeAudioCodec codec)
        => codec == MergeAudioCodec.Aac ? "aac" : "libopus";
}
