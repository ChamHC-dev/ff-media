using System;
using System.Linq;
using FFMedia.Media;
using FFMedia.Tools.VideoMerger.Models;
using FFMedia.Tools.VideoMerger.Services;
using Xunit;

namespace FFMedia.Tests.VideoMerger;

public class NormalizeArgsBuilderTests
{
    private static readonly MergeTarget Target = MergeTarget.Default with { FrameRate = new FrameRate(30000, 1001) };

    private static MediaInfo Clip(bool withAudio = true) => new(
        TimeSpan.FromSeconds(5),
        "mov,mp4,m4a",
        new VideoStreamInfo(1280, 720, new FrameRate(24, 1), "h264", "yuv420p", 0),
        withAudio ? new AudioStreamInfo("aac", 44100, 2) : null);

    private static string Filter(System.Collections.Generic.IReadOnlyList<string> args)
        => args[args.ToList().IndexOf("-vf") + 1];

    [Fact]
    public void Build_Fit_ScalesAndPads()
    {
        var args = NormalizeArgsBuilder.Build(@"C:\a.mp4", Clip(), Target with { FitMode = FitMode.Fit }, @"C:\t\000.mkv");

        Assert.Equal(
            "scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,fps=30000/1001,setsar=1",
            Filter(args));
    }

    [Fact]
    public void Build_Fill_ScalesAndCrops()
    {
        var args = NormalizeArgsBuilder.Build(@"C:\a.mp4", Clip(), Target with { FitMode = FitMode.Fill }, @"C:\t\000.mkv");

        Assert.Equal(
            "scale=1920:1080:force_original_aspect_ratio=increase,crop=1920:1080,fps=30000/1001,setsar=1",
            Filter(args));
    }

    [Fact]
    public void Build_Stretch_ScalesOnly()
    {
        var args = NormalizeArgsBuilder.Build(@"C:\a.mp4", Clip(), Target with { FitMode = FitMode.Stretch }, @"C:\t\000.mkv");

        Assert.Equal("scale=1920:1080,fps=30000/1001,setsar=1", Filter(args));
    }

    [Fact]
    public void Build_EncodesVideoAndAudioToTarget()
    {
        var args = NormalizeArgsBuilder.Build(@"C:\a.mp4", Clip(), Target, @"C:\t\000.mkv");

        Assert.Contains("libx264", args);
        Assert.Contains("-crf", args);
        Assert.Contains("20", args);
        Assert.Contains("yuv420p", args);
        Assert.Contains("aac", args);
        Assert.Contains("-ar", args);
        Assert.Contains("48000", args);
        Assert.Contains("-ac", args);
        Assert.Contains("2", args);
        Assert.Equal(@"C:\t\000.mkv", args[^1]);
    }

    [Fact]
    public void Build_UsesLibx265_ForH265Target()
    {
        var args = NormalizeArgsBuilder.Build(@"C:\a.mp4", Clip(), Target with { VideoCodec = MergeVideoCodec.H265 }, @"C:\t\000.mkv");

        Assert.Contains("libx265", args);
        Assert.DoesNotContain("libx264", args);
    }

    [Fact]
    public void Build_UsesLibopus_ForOpusTarget()
    {
        var args = NormalizeArgsBuilder.Build(@"C:\a.mp4", Clip(), Target with { AudioCodec = MergeAudioCodec.Opus }, @"C:\t\000.mkv");

        Assert.Contains("libopus", args);
        Assert.DoesNotContain("aac", args);
    }

    [Fact]
    public void Build_SilentClip_AddsAnullsrcInputAndShortest()
    {
        var args = NormalizeArgsBuilder.Build(@"C:\a.mp4", Clip(withAudio: false), Target, @"C:\t\000.mkv");

        Assert.Contains("lavfi", args);
        Assert.Contains(args, a => a.StartsWith("anullsrc=", StringComparison.Ordinal));
        Assert.Contains("anullsrc=channel_layout=stereo:sample_rate=48000", args);
        Assert.Contains("-shortest", args);
        Assert.Contains("1:a:0", args);
    }

    [Fact]
    public void Build_ClipWithAudio_MapsItsOwnAudioAndOmitsShortest()
    {
        var args = NormalizeArgsBuilder.Build(@"C:\a.mp4", Clip(), Target, @"C:\t\000.mkv");

        Assert.Contains("0:a:0", args);
        Assert.DoesNotContain("-shortest", args);
        Assert.DoesNotContain("lavfi", args);
    }

    [Theory]
    [InlineData(1, "mono")]
    [InlineData(2, "stereo")]
    [InlineData(6, "5.1")]
    public void Build_MapsChannelCountToLayout(int channels, string layout)
    {
        var args = NormalizeArgsBuilder.Build(
            @"C:\a.mp4", Clip(withAudio: false), Target with { AudioChannels = channels }, @"C:\t\000.mkv");

        Assert.Contains($"anullsrc=channel_layout={layout}:sample_rate=48000", args);
    }

    [Fact]
    public void Build_AlwaysMapsFirstVideoStream()
    {
        var args = NormalizeArgsBuilder.Build(@"C:\a.mp4", Clip(), Target, @"C:\t\000.mkv");

        Assert.Contains("0:v:0", args);
    }

    [Fact]
    public void Build_SourceIsTheFirstInput()
    {
        var args = NormalizeArgsBuilder.Build(@"C:\a.mp4", Clip(), Target, @"C:\t\000.mkv");

        Assert.Equal("-i", args[0]);
        Assert.Equal(@"C:\a.mp4", args[1]);
    }
}
