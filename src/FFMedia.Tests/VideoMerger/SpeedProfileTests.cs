using System;
using System.IO;
using FFMedia.Tools.VideoMerger.Models;
using FFMedia.Tools.VideoMerger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FFMedia.Tests.VideoMerger;

public class SpeedProfileTests
{
    private static MergeTarget Hd => MergeTarget.Default;

    private static MergeTarget Uhd => MergeTarget.Default with { Width = 3840, Height = 2160 };

    private static string TempDir()
        => Path.Combine(Path.GetTempPath(), "ffmedia-speed-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void KeyFor_BucketsByCodecAndPixels()
    {
        Assert.Equal("H264/HD1080", SpeedProfile.KeyFor(Hd));
        Assert.Equal("H264/UHD4K", SpeedProfile.KeyFor(Uhd));
        Assert.Equal("H265/HD1080", SpeedProfile.KeyFor(Hd with { VideoCodec = MergeVideoCodec.H265 }));
        Assert.Equal("H264/SD", SpeedProfile.KeyFor(Hd with { Width = 1280, Height = 720 }));
        Assert.Equal("H264/HUGE", SpeedProfile.KeyFor(Hd with { Width = 7680, Height = 4320 }));
    }

    [Theory]
    // Exact bucket boundaries: the comparison is inclusive of the upper edge.
    [InlineData(1280, 720, "SD")]          // 921,600 px — the SD ceiling itself
    [InlineData(1281, 720, "HD1080")]      // 922,320 px — one step over
    [InlineData(1920, 1080, "HD1080")]     // 2,073,600 px — the HD ceiling itself
    [InlineData(1921, 1080, "UHD4K")]      // 2,074,680 px — one step over
    [InlineData(3840, 2160, "UHD4K")]      // 8,294,400 px — the 4K ceiling itself
    [InlineData(3841, 2160, "HUGE")]       // 8,296,560 px — one step over
    public void KeyFor_BucketBoundariesAreInclusiveOfTheirCeiling(int width, int height, string bucket)
    {
        Assert.Equal($"H264/{bucket}", SpeedProfile.KeyFor(Hd with { Width = width, Height = height }));
    }

    [Fact]
    public void GetFactor_UsesSeedConstant_WhenNoSamples()
    {
        var profile = new SpeedProfile();

        Assert.Equal(8.0, profile.GetFactor(Hd with { Width = 1280, Height = 720 }));
        Assert.Equal(3.5, profile.GetFactor(Hd));
        Assert.Equal(0.8, profile.GetFactor(Uhd));
        Assert.Equal(0.3, profile.GetFactor(Hd with { Width = 7680, Height = 4320 }));
    }

    [Fact]
    public void GetFactor_HalvesTheSeed_ForH265()
    {
        var profile = new SpeedProfile();
        var h265 = Hd with { VideoCodec = MergeVideoCodec.H265 };

        Assert.Equal(4.0, profile.GetFactor(h265 with { Width = 1280, Height = 720 }));
        Assert.Equal(1.75, profile.GetFactor(h265));
        Assert.Equal(0.4, profile.GetFactor(h265 with { Width = 3840, Height = 2160 }));
        Assert.Equal(0.15, profile.GetFactor(h265 with { Width = 7680, Height = 4320 }));
    }

    [Fact]
    public void Record_FirstSample_BecomesTheAverage()
    {
        var profile = new SpeedProfile();
        profile.Record(Hd, 5.0);

        Assert.Equal(5.0, profile.GetFactor(Hd));
        Assert.Equal(1, profile.Samples[SpeedProfile.KeyFor(Hd)].Count);
    }

    [Fact]
    public void Record_RollsTheAverage()
    {
        var profile = new SpeedProfile();
        profile.Record(Hd, 4.0);
        profile.Record(Hd, 6.0);

        Assert.Equal(5.0, profile.GetFactor(Hd));
        Assert.Equal(2, profile.Samples[SpeedProfile.KeyFor(Hd)].Count);
    }

    [Fact]
    public void Record_ThirdSample_IsWeightedOneThird()
    {
        var profile = new SpeedProfile();
        profile.Record(Hd, 3.0);
        profile.Record(Hd, 3.0);
        profile.Record(Hd, 6.0);

        // 3, then 3, then 3 + (6 - 3)/3 = 4.
        Assert.Equal(4.0, profile.GetFactor(Hd), 10);
    }

    [Fact]
    public void Record_SaturatesTheWindow_SoOldSamplesDecay()
    {
        var profile = new SpeedProfile();
        for (var i = 0; i < 50; i++)
        {
            profile.Record(Hd, 2.0);
        }

        var sample = profile.Samples[SpeedProfile.KeyFor(Hd)];
        Assert.Equal(2.0, sample.Average, 10);
        Assert.Equal(10, sample.Count); // Count saturates at the window; it cannot grow without bound.

        // Past saturation the newest sample always carries exactly 1/Window of the weight.
        profile.Record(Hd, 12.0);
        Assert.Equal(3.0, profile.GetFactor(Hd), 10); // 2 + (12 - 2)/10
        Assert.Equal(10, profile.Samples[SpeedProfile.KeyFor(Hd)].Count);
    }

    [Fact]
    public void Record_KeysAreIndependent()
    {
        var profile = new SpeedProfile();
        profile.Record(Hd, 9.0);

        Assert.Equal(9.0, profile.GetFactor(Hd));
        Assert.Equal(0.8, profile.GetFactor(Uhd));
        Assert.Equal(1.75, profile.GetFactor(Hd with { VideoCodec = MergeVideoCodec.H265 }));
        Assert.Single(profile.Samples);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Record_IgnoresUnusableSpeeds(double speed)
    {
        var profile = new SpeedProfile();
        profile.Record(Hd, speed);

        Assert.Equal(3.5, profile.GetFactor(Hd)); // still the seed
        Assert.Empty(profile.Samples);            // and nothing was written
    }

    [Fact]
    public void Record_UnusableSpeed_CannotPoisonAnExistingAverage()
    {
        var profile = new SpeedProfile();
        profile.Record(Hd, 4.0);
        profile.Record(Hd, double.NaN);
        profile.Record(Hd, double.PositiveInfinity);
        profile.Record(Hd, 0);

        Assert.Equal(4.0, profile.GetFactor(Hd));
        Assert.Equal(1, profile.Samples[SpeedProfile.KeyFor(Hd)].Count);
    }

    [Fact]
    public void BandFor_NarrowsWithSamples_ToAFloor()
    {
        var profile = new SpeedProfile();
        Assert.Equal(0.35, profile.BandFor(Hd));

        profile.Record(Hd, 3.0);
        Assert.Equal(0.31, profile.BandFor(Hd), 10);

        profile.Record(Hd, 3.0);
        Assert.Equal(0.27, profile.BandFor(Hd), 10);

        for (var i = 0; i < 20; i++)
        {
            profile.Record(Hd, 3.0);
        }

        Assert.Equal(0.15, profile.BandFor(Hd), 10);
    }

    [Fact]
    public void BandFor_IsPerKey()
    {
        var profile = new SpeedProfile();
        profile.Record(Hd, 3.0);

        Assert.Equal(0.31, profile.BandFor(Hd), 10);
        Assert.Equal(0.35, profile.BandFor(Uhd), 10);
    }

    [Fact]
    public void GetFactor_FallsBackToSeed_WhenPersistedSampleIsNonsense()
    {
        // A hand-edited or half-written file must never hand the estimator a zero,
        // a negative, or a NaN — that would divide-by-zero or NaN-poison the ETA.
        var profile = new SpeedProfile
        {
            Samples =
            {
                ["H264/HD1080"] = new SpeedSample { Average = 0, Count = 4 },
                ["H264/UHD4K"] = new SpeedSample { Average = -2, Count = 4 },
            },
        };

        Assert.Equal(3.5, profile.GetFactor(Hd));
        Assert.Equal(0.8, profile.GetFactor(Uhd));
    }

    [Fact]
    public void Record_RepairsANonsensePersistedSample()
    {
        var profile = new SpeedProfile
        {
            Samples = { ["H264/HD1080"] = new SpeedSample { Average = -5, Count = -3 } },
        };

        profile.Record(Hd, 6.0);

        // The bad state is discarded, not averaged with: the new sample becomes the average.
        Assert.Equal(6.0, profile.GetFactor(Hd));
        Assert.Equal(1, profile.Samples["H264/HD1080"].Count);
        Assert.Equal(0.31, profile.BandFor(Hd), 10);
    }

    [Fact]
    public void BandFor_WidensToMax_WhenTheAverageIsUnusable_MatchingGetFactorsFallback()
    {
        // GetFactor and BandFor must agree on what counts as measured. With Count = 4 but an
        // unusable average, GetFactor falls back to the seed — so the band has to widen back to
        // ±35%, or the UI would claim ±19% confidence in a number nobody ever measured.
        var profile = new SpeedProfile
        {
            Samples = { ["H264/HD1080"] = new SpeedSample { Average = 0, Count = 4 } },
        };

        Assert.Equal(3.5, profile.GetFactor(Hd));
        Assert.Equal(0.35, profile.BandFor(Hd), 10);
    }

    [Fact]
    public void Record_ClampsANegativePersistedCount_EvenWhenTheAverageIsUsable()
    {
        // The average is fine here, so the repair path above never fires and the count clamp is
        // the only thing standing between us and weight = 0. Unclamped, `min(-1 + 1, 10)` divides
        // by zero and persists Average = +infinity, which no later sample can undo.
        var profile = new SpeedProfile
        {
            Samples = { ["H264/HD1080"] = new SpeedSample { Average = 3, Count = -1 } },
        };

        profile.Record(Hd, 6.0);

        Assert.Equal(6.0, profile.GetFactor(Hd));
        Assert.True(double.IsFinite(profile.Samples["H264/HD1080"].Average));
    }

    [Fact]
    public void BandFor_IsClampedToTheBand_WhenPersistedCountIsNonsense()
    {
        var profile = new SpeedProfile
        {
            Samples = { ["H264/HD1080"] = new SpeedSample { Average = 3, Count = -100 } },
        };

        Assert.Equal(0.35, profile.BandFor(Hd), 10);
    }

    [Fact]
    public void Samples_NeverNull_EvenIfDeserializedAsNull()
    {
        var profile = new SpeedProfile { Samples = null! };

        Assert.NotNull(profile.Samples);
        Assert.Empty(profile.Samples);
        Assert.Equal(3.5, profile.GetFactor(Hd));
    }

    [Fact]
    public void Store_RoundTripsThroughDisk()
    {
        var directory = TempDir();
        try
        {
            var store = new SpeedProfileStore(directory, NullLogger<SpeedProfileStore>.Instance);
            var profile = store.Load();
            profile.Record(Hd, 4.25);
            store.Save(profile);

            var reloaded = new SpeedProfileStore(directory, NullLogger<SpeedProfileStore>.Instance).Load();

            Assert.Equal(4.25, reloaded.GetFactor(Hd));
            Assert.Equal(1, reloaded.Samples["H264/HD1080"].Count);
            Assert.Equal(0.31, reloaded.BandFor(Hd), 10);
            Assert.True(File.Exists(Path.Combine(directory, SpeedProfileStore.FileName)));
            Assert.Equal("encode-speed.json", SpeedProfileStore.FileName);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Store_ReturnsDefault_WhenFileAbsent()
    {
        var directory = TempDir();
        var store = new SpeedProfileStore(directory, NullLogger<SpeedProfileStore>.Instance);

        var profile = store.Load();

        Assert.Empty(profile.Samples);
        Assert.Equal(3.5, profile.GetFactor(Hd));
        Assert.False(Directory.Exists(directory)); // Load must not create anything.
    }

    [Fact]
    public void Store_QuarantinesACorruptFile_AndReturnsDefault()
    {
        var directory = TempDir();
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, SpeedProfileStore.FileName);
            File.WriteAllText(path, "{ this is not json");

            var profile = new SpeedProfileStore(directory, NullLogger<SpeedProfileStore>.Instance).Load();

            Assert.Empty(profile.Samples);
            Assert.Equal(3.5, profile.GetFactor(Hd));
            Assert.False(File.Exists(path));
            Assert.True(File.Exists(path + ".bak"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Store_RejectsNullArguments()
    {
        Assert.Throws<ArgumentException>(
            () => new SpeedProfileStore("  ", NullLogger<SpeedProfileStore>.Instance));
        Assert.Throws<ArgumentNullException>(
            () => new SpeedProfileStore(TempDir(), null!));
        Assert.Throws<ArgumentNullException>(
            () => new SpeedProfileStore(TempDir(), NullLogger<SpeedProfileStore>.Instance).Save(null!));
    }
}
