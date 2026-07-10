using System;
using System.Collections.Generic;
using FFMedia.Media;
using FFMedia.Tools.VideoMerger.Models;
using FFMedia.Tools.VideoMerger.Services;
using Xunit;

namespace FFMedia.Tests.VideoMerger;

public class MergeTargetDerivationTests
{
    private static MediaInfo Clip(
        int width, int height, int fpsNum, int fpsDen = 1,
        string videoCodec = "h264", string container = "mov,mp4,m4a",
        string? audioCodec = "aac", int sampleRate = 48000, int channels = 2)
        => new(
            TimeSpan.FromSeconds(10),
            container,
            new VideoStreamInfo(width, height, new FrameRate(fpsNum, fpsDen), videoCodec, "yuv420p", 0),
            audioCodec is null ? null : new AudioStreamInfo(audioCodec, sampleRate, channels));

    [Fact]
    public void Derive_TakesLargestFrameArea()
    {
        var target = MergeTargetDerivation.Derive([Clip(1280, 720, 30), Clip(1920, 1080, 24)]);

        Assert.Equal(1920, target.Width);
        Assert.Equal(1080, target.Height);
    }

    [Fact]
    public void Derive_TakesHighestFrameRate()
    {
        var target = MergeTargetDerivation.Derive([Clip(1920, 1080, 24), Clip(1280, 720, 60)]);

        Assert.Equal(60, target.FrameRate.Value);
    }

    [Fact]
    public void Derive_SnapsNearStandardRateToTheStandardRate()
    {
        // 29.97 reported as an ugly 2997/100 snaps to the exact 30000/1001.
        var target = MergeTargetDerivation.Derive([Clip(1920, 1080, 2997, 100)]);

        Assert.Equal(new FrameRate(30000, 1001), target.FrameRate);
    }

    [Fact]
    public void Derive_KeepsExactRate_WhenFarFromAnyStandardRate()
    {
        var target = MergeTargetDerivation.Derive([Clip(1920, 1080, 12, 1)]);

        Assert.Equal(new FrameRate(12, 1), target.FrameRate);
    }

    [Fact]
    public void Derive_TakesMaxAudioSampleRateAndChannels()
    {
        var target = MergeTargetDerivation.Derive(
        [
            Clip(1920, 1080, 30, sampleRate: 44100, channels: 2),
            Clip(1920, 1080, 30, sampleRate: 48000, channels: 6),
        ]);

        Assert.Equal(48000, target.AudioSampleRate);
        Assert.Equal(6, target.AudioChannels);
    }

    [Fact]
    public void Derive_UsesDefaultAudioSpec_WhenNoClipHasAudio()
    {
        var target = MergeTargetDerivation.Derive([Clip(1920, 1080, 30, audioCodec: null)]);

        Assert.Equal(48000, target.AudioSampleRate);
        Assert.Equal(2, target.AudioChannels);
    }

    [Fact]
    public void Derive_PicksMkv_WhenMostClipsAreMatroska()
    {
        var target = MergeTargetDerivation.Derive(
        [
            Clip(1920, 1080, 30, container: "matroska,webm"),
            Clip(1920, 1080, 30, container: "matroska,webm"),
            Clip(1920, 1080, 30, container: "mov,mp4,m4a"),
        ]);

        Assert.Equal(MergeContainer.Mkv, target.Container);
    }

    [Fact]
    public void Derive_PicksMp4_ByDefault()
    {
        var target = MergeTargetDerivation.Derive([Clip(1920, 1080, 30)]);

        Assert.Equal(MergeContainer.Mp4, target.Container);
        Assert.Equal(MergeVideoCodec.H264, target.VideoCodec);
        Assert.Equal(MergeAudioCodec.Aac, target.AudioCodec);
        Assert.Equal(FitMode.Fit, target.FitMode);
    }

    [Fact]
    public void Derive_PicksH265_WhenMostClipsAreHevc()
    {
        var target = MergeTargetDerivation.Derive(
        [
            Clip(1920, 1080, 30, videoCodec: "hevc"),
            Clip(1920, 1080, 30, videoCodec: "hevc"),
            Clip(1920, 1080, 30, videoCodec: "h264"),
        ]);

        Assert.Equal(MergeVideoCodec.H265, target.VideoCodec);
    }

    [Fact]
    public void Derive_TieBreaksToH264_WhenCodecVoteIsSplitEvenly()
    {
        var target = MergeTargetDerivation.Derive(
        [
            Clip(1920, 1080, 30, videoCodec: "hevc"),
            Clip(1920, 1080, 30, videoCodec: "h264"),
        ]);

        Assert.Equal(MergeVideoCodec.H264, target.VideoCodec);
    }

    [Fact]
    public void Derive_TieBreaksToMp4_WhenContainerVoteIsSplitEvenly()
    {
        var target = MergeTargetDerivation.Derive(
        [
            Clip(1920, 1080, 30, container: "matroska,webm"),
            Clip(1920, 1080, 30, container: "mov,mp4,m4a"),
        ]);

        Assert.Equal(MergeContainer.Mp4, target.Container);
    }

    [Fact]
    public void Derive_RejectsEmptyList()
    {
        Assert.Throws<ArgumentException>(() => MergeTargetDerivation.Derive(new List<MediaInfo>()));
    }

    [Fact]
    public void Derive_IgnoresClipsWithoutVideo()
    {
        var audioOnly = new MediaInfo(TimeSpan.FromSeconds(5), "mp3", null, new AudioStreamInfo("mp3", 44100, 2));
        var target = MergeTargetDerivation.Derive([audioOnly, Clip(1280, 720, 30)]);

        Assert.Equal(1280, target.Width);
    }

    [Fact]
    public void Derive_RejectsList_WhenNoClipHasVideo()
    {
        var audioOnly1 = new MediaInfo(TimeSpan.FromSeconds(5), "mp3", null, new AudioStreamInfo("mp3", 44100, 2));
        var audioOnly2 = new MediaInfo(TimeSpan.FromSeconds(3), "wav", null, new AudioStreamInfo("pcm_s16le", 44100, 2));

        Assert.Throws<ArgumentException>(() => MergeTargetDerivation.Derive([audioOnly1, audioOnly2]));
    }
}
