using System;
using System.Collections.Generic;
using FFMedia.Media;
using FFMedia.Tools.VideoMerger.Models;
using FFMedia.Tools.VideoMerger.Services;
using Xunit;

namespace FFMedia.Tests.VideoMerger;

public class MergeEstimatorTests
{
    private static readonly MergeTarget Target = MergeTarget.Default;

    /// <summary>The heuristic output bitrate of <see cref="MergeTarget.Default"/>
    /// (1920x1080 @ 30 fps x 0.08 bpp + 192 kbps audio). Pinned by
    /// <see cref="Estimate_DefaultTargetBitrateIsTheNumberTheseTestsAssume"/> so that a change to
    /// the bitrate heuristic fails loudly here rather than silently shifting every expectation.</summary>
    private const double Bps = 5_168_640;

    /// <summary>Assumed stream-copy throughput of the concat pass (200 MiB/s).</summary>
    private const double CopyBps = 200.0 * 1024 * 1024;

    private static MergeClip Conforming(string path, double seconds, string container = "mov,mp4,m4a") => new(path, new MediaInfo(
        TimeSpan.FromSeconds(seconds), container,
        new VideoStreamInfo(1920, 1080, new FrameRate(30, 1), "h264", "yuv420p", 0),
        new AudioStreamInfo("aac", 48000, 2)));

    private static MergeClip NonConforming(string path, double seconds) => new(path, new MediaInfo(
        TimeSpan.FromSeconds(seconds), "matroska,webm",
        new VideoStreamInfo(1280, 720, new FrameRate(60, 1), "vp9", "yuv420p", 0),
        null));

    private static MergeClip WithDuration(MergeClip clip, TimeSpan duration)
        => clip with { Info = clip.Info with { Duration = duration } };

    /// <summary>Copy time of a stream-copied concat of <paramref name="outputSeconds"/> of output.</summary>
    private static double ConcatSeconds(double outputSeconds) => outputSeconds * Bps / 8.0 / CopyBps;

    [Fact]
    public void Estimate_DefaultTargetBitrateIsTheNumberTheseTestsAssume()
    {
        Assert.Equal(5_168_640L, Target.EstimatedBitsPerSecond);
    }

    [Fact]
    public void Estimate_OutputDurationIsTheExactSum()
    {
        var estimate = MergeEstimator.Estimate(
            [Conforming("a", 10), NonConforming("b", 5.5)], Target, new SpeedProfile());

        Assert.Equal(TimeSpan.FromSeconds(15.5), estimate.OutputDuration);
    }

    [Fact]
    public void Estimate_AllConforming_IsFastPath()
    {
        var estimate = MergeEstimator.Estimate([Conforming("a", 10), Conforming("b", 20)], Target, new SpeedProfile());

        Assert.True(estimate.IsFastPath);
        Assert.Equal(0, estimate.ReencodeCount);
        Assert.Equal(0L, estimate.TempBytesEstimate);
    }

    [Fact]
    public void Estimate_Conformance_IsDecidedByConformanceCheck_NotByContainer()
    {
        // ConformanceCheck ignores the container (the concat demuxer only cares about stream
        // layout), so an .mkv whose streams already match the target must stay on the fast path.
        var estimate = MergeEstimator.Estimate(
            [Conforming("a", 10, container: "matroska,webm")], Target, new SpeedProfile());

        Assert.True(estimate.IsFastPath);
        Assert.Equal(0, estimate.ReencodeCount);
    }

    [Fact]
    public void Estimate_CountsAClipThatDiffersOnlyInSampleRate_AsAReencode()
    {
        // The estimator must agree with ConformanceCheck exactly, or the ETA describes a
        // different plan than the one that runs.
        var offSampleRate = new MergeClip("a", new MediaInfo(
            TimeSpan.FromSeconds(10), "mov,mp4,m4a",
            new VideoStreamInfo(1920, 1080, new FrameRate(30, 1), "h264", "yuv420p", 0),
            new AudioStreamInfo("aac", 44100, 2)));

        var estimate = MergeEstimator.Estimate([offSampleRate], Target, new SpeedProfile());

        Assert.False(estimate.IsFastPath);
        Assert.Equal(1, estimate.ReencodeCount);
        Assert.Equal(6_460_800L, estimate.TempBytesEstimate); // 10 s x 5,168,640 bps / 8
    }

    [Fact]
    public void Estimate_CountsOnlyNonConformingClips()
    {
        var estimate = MergeEstimator.Estimate(
            [Conforming("a", 10), NonConforming("b", 5), NonConforming("c", 5)], Target, new SpeedProfile());

        Assert.False(estimate.IsFastPath);
        Assert.Equal(2, estimate.ReencodeCount);
        Assert.Equal(6_460_800L, estimate.TempBytesEstimate); // (5 + 5) s of re-encode, not 20 s
    }

    [Fact]
    public void Estimate_UsesTheSeedFactorAndTheWidestBand_WhenNothingWasEverMeasured()
    {
        // No samples: seed factor for HD1080/H264 is 3.5x realtime, band is the full +/-35 %.
        var estimate = MergeEstimator.Estimate([NonConforming("a", 100)], Target, new SpeedProfile());

        var point = (100.0 / 3.5) + ConcatSeconds(100);
        Assert.Equal(point * 0.65, estimate.LowEta.TotalSeconds, 6);
        Assert.Equal(point * 1.35, estimate.HighEta.TotalSeconds, 6);
    }

    [Fact]
    public void Estimate_EncodeTimeScalesWithMeasuredSpeed()
    {
        var slow = new SpeedProfile();
        slow.Record(Target, 1.0); // 1x realtime
        var fast = new SpeedProfile();
        fast.Record(Target, 10.0);

        var slowEstimate = MergeEstimator.Estimate([NonConforming("a", 100)], Target, slow);
        var fastEstimate = MergeEstimator.Estimate([NonConforming("a", 100)], Target, fast);

        // One sample each => band 0.31 for both; only the encode term moves.
        var slowPoint = (100.0 / 1.0) + ConcatSeconds(100);
        var fastPoint = (100.0 / 10.0) + ConcatSeconds(100);
        Assert.Equal(slowPoint * 1.31, slowEstimate.HighEta.TotalSeconds, 6);
        Assert.Equal(fastPoint * 1.31, fastEstimate.HighEta.TotalSeconds, 6);
        Assert.True(slowEstimate.HighEta > fastEstimate.HighEta);
    }

    [Fact]
    public void Estimate_BandBracketsThePointEstimate()
    {
        var profile = new SpeedProfile();
        profile.Record(Target, 2.0); // 100 s of video / 2.0 = 50 s encode

        var estimate = MergeEstimator.Estimate([NonConforming("a", 100)], Target, profile);

        var point = 50.0 + ConcatSeconds(100);
        Assert.Equal(point * 0.69, estimate.LowEta.TotalSeconds, 6); // band after one sample = 0.31
        Assert.Equal(point * 1.31, estimate.HighEta.TotalSeconds, 6);
        Assert.True(estimate.LowEta < estimate.HighEta);
    }

    [Fact]
    public void Estimate_TempBytesCoversOnlyNonConformingClips()
    {
        var estimate = MergeEstimator.Estimate([Conforming("a", 100), NonConforming("b", 10)], Target, new SpeedProfile());

        Assert.Equal(6_460_800L, estimate.TempBytesEstimate); // 10 s x 5,168,640 / 8 — the 100 s clip is free
    }

    [Fact]
    public void Estimate_FastPathEtaIsTheCopyTimeAlone_NotZero()
    {
        var estimate = MergeEstimator.Estimate([Conforming("a", 600)], Target, new SpeedProfile());

        var point = ConcatSeconds(600); // ~1.85 s: no encode term at all
        Assert.Equal(point * 0.65, estimate.LowEta.TotalSeconds, 6);
        Assert.Equal(point * 1.35, estimate.HighEta.TotalSeconds, 6);
        Assert.True(estimate.HighEta > TimeSpan.Zero);
        Assert.True(estimate.HighEta < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Estimate_ClampsNegativeProbeDurationsToZero()
    {
        // A malformed probe can report a negative duration; it must not shorten the merge,
        // drive a negative ETA, or subtract from the temp-space requirement.
        var estimate = MergeEstimator.Estimate(
            [Conforming("a", 10), WithDuration(NonConforming("b", 0), TimeSpan.FromSeconds(-5))],
            Target,
            new SpeedProfile());

        Assert.Equal(TimeSpan.FromSeconds(10), estimate.OutputDuration);
        Assert.Equal(1, estimate.ReencodeCount); // still needs re-encoding, it just costs nothing
        Assert.Equal(0L, estimate.TempBytesEstimate);
        Assert.Equal(ConcatSeconds(10) * 0.65, estimate.LowEta.TotalSeconds, 6);
    }

    [Fact]
    public void Estimate_SaturatesInsteadOfOverflowing_OnAbsurdDurations()
    {
        var estimate = MergeEstimator.Estimate(
            [
                WithDuration(NonConforming("a", 0), TimeSpan.MaxValue),
                WithDuration(NonConforming("b", 0), TimeSpan.MaxValue),
            ],
            Target,
            new SpeedProfile());

        // Summing the ticks with LINQ's checked Enumerable.Sum would throw OverflowException here.
        Assert.Equal(TimeSpan.MaxValue, estimate.OutputDuration);

        // Big, but still a positive number of bytes — never a wrapped-around long.MinValue.
        var reencodeSeconds = 2 * (TimeSpan.MaxValue.Ticks / (double)TimeSpan.TicksPerSecond);
        Assert.Equal((long)(reencodeSeconds * 5_168_640 / 8.0), estimate.TempBytesEstimate);
        Assert.True(estimate.TempBytesEstimate > 0);

        Assert.True(estimate.HighEta > TimeSpan.Zero);
    }

    [Fact]
    public void Estimate_ClampsToTimeSpanMax_WhenTheMeasuredSpeedIsAbsurdlySlow()
    {
        // GetFactor only guarantees a positive, finite factor — a denormal one still divides
        // 100 s into +infinity, which TimeSpan.FromSeconds would throw on.
        var profile = new SpeedProfile();
        profile.Record(Target, double.Epsilon);

        var estimate = MergeEstimator.Estimate([NonConforming("a", 100)], Target, profile);

        Assert.Equal(TimeSpan.MaxValue, estimate.LowEta);
        Assert.Equal(TimeSpan.MaxValue, estimate.HighEta);
    }

    [Fact]
    public void Estimate_RejectsEmptyList()
    {
        Assert.Throws<ArgumentException>(() =>
            MergeEstimator.Estimate(new List<MergeClip>(), Target, new SpeedProfile()));
    }

    [Fact]
    public void Estimate_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => MergeEstimator.Estimate(null!, Target, new SpeedProfile()));
        Assert.Throws<ArgumentNullException>(() => MergeEstimator.Estimate([Conforming("a", 1)], null!, new SpeedProfile()));
        Assert.Throws<ArgumentNullException>(() => MergeEstimator.Estimate([Conforming("a", 1)], Target, null!));
    }
}
