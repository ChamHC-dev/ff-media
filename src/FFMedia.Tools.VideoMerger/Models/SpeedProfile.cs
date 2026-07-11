namespace FFMedia.Tools.VideoMerger.Models;

/// <summary>One measured throughput bucket: encoded video-seconds per wall-clock second.</summary>
public sealed class SpeedSample
{
    public double Average { get; set; }

    public int Count { get; set; }
}

/// <summary>Rolling average of this machine's real encode throughput, keyed by codec + resolution
/// bucket. Persisted to encode-speed.json so the merge-time estimate improves with use.</summary>
/// <remarks>Every read path is defensive: the backing file is user-visible JSON, so a hand-edited or
/// half-written sample must degrade to the seeded constant rather than feed a zero, a negative, or a
/// NaN into the user-facing estimate.</remarks>
public sealed class SpeedProfile
{
    /// <summary>Floor on the newest reading's weight: it lands at 1/(n+1) while the first ten
    /// samples accumulate, then 1/10 forever. Strictly this makes the average an exponential
    /// decay rather than a true window — an eleventh-from-last run still carries ~4% — but it
    /// keeps the state O(1) and lets the last ten runs dominate, which is what the estimate needs.</summary>
    private const int Window = 10;

    private const double MaxBand = 0.35;
    private const double MinBand = 0.15;
    private const double BandNarrowingPerSample = 0.04;

    /// <summary>Conservative starting guesses (H.264, encoded seconds per wall second).</summary>
    private static readonly IReadOnlyDictionary<string, double> SeedFactors = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["SD"] = 8.0,
        ["HD1080"] = 3.5,
        ["UHD4K"] = 0.8,
        ["HUGE"] = 0.3,
    };

    private Dictionary<string, SpeedSample> _samples = new(StringComparer.Ordinal);

    /// <summary>Measured buckets, keyed by <see cref="KeyFor"/>. Never null — deserializing a file
    /// whose "Samples" is <c>null</c> would otherwise hand us a null dictionary.</summary>
    public Dictionary<string, SpeedSample> Samples
    {
        get => _samples;
        set => _samples = value ?? new Dictionary<string, SpeedSample>(StringComparer.Ordinal);
    }

    public static string KeyFor(MergeTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return $"{target.VideoCodec}/{Bucket(target.PixelCount)}";
    }

    /// <summary>Measured average for this target, or the seeded constant when never measured
    /// (or when the persisted measurement is not a usable positive number).</summary>
    public double GetFactor(MergeTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (MeasuredCount(target) > 0)
        {
            return Samples[KeyFor(target)].Average;
        }

        var seed = SeedFactors[Bucket(target.PixelCount)];
        return target.VideoCodec == MergeVideoCodec.H265 ? seed / 2 : seed;
    }

    /// <summary>Folds a real measured <c>speed=</c> value into the rolling average. Non-positive,
    /// NaN and infinite readings are dropped — ffmpeg emits <c>speed=0x</c> / <c>N/A</c> early in a
    /// run, and a single NaN folded in would poison the persisted average permanently.</summary>
    public void Record(MergeTarget target, double measuredSpeed)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!IsUsable(measuredSpeed))
        {
            return;
        }

        var key = KeyFor(target);
        if (!Samples.TryGetValue(key, out var sample) || sample is null)
        {
            sample = new SpeedSample();
            Samples[key] = sample;
        }

        // Discard (rather than average with) a nonsense persisted state: a bad average or a
        // count outside [0, Window] would otherwise divide by zero or by a negative weight.
        var count = Math.Clamp(sample.Count, 0, Window);
        var average = sample.Average;
        if (!IsUsable(average))
        {
            average = 0;
            count = 0;
        }

        // The newest reading is weighted 1/(n+1) while the window fills, then 1/Window forever —
        // an exponential decay that lets the last ten runs dominate without keeping a list.
        var weight = Math.Min(count + 1, Window);
        sample.Average = average + ((measuredSpeed - average) / weight);
        sample.Count = weight;
    }

    /// <summary>Relative half-width of the estimate range: ±35 % with no data, narrowing 4 points
    /// per sample to a ±15 % floor. The estimate is honest about being a guess.</summary>
    public double BandFor(MergeTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Math.Clamp(MaxBand - (BandNarrowingPerSample * MeasuredCount(target)), MinBand, MaxBand);
    }

    /// <summary>How many real measurements back this target — 0 unless the persisted bucket is
    /// usable as a whole. GetFactor and BandFor must agree on this: if the stored average is
    /// nonsense we fall back to the seed, and a band narrowed around a value we never measured
    /// would claim ±19% confidence in a pure guess.</summary>
    private int MeasuredCount(MergeTarget target)
        => Samples.TryGetValue(KeyFor(target), out var sample)
            && sample is not null
            && sample.Count > 0
            && IsUsable(sample.Average)
                ? Math.Clamp(sample.Count, 0, Window)
                : 0;

    private static bool IsUsable(double speed) => double.IsFinite(speed) && speed > 0;

    private static string Bucket(long pixels) => pixels switch
    {
        <= 921_600 => "SD",
        <= 2_073_600 => "HD1080",
        <= 8_294_400 => "UHD4K",
        _ => "HUGE",
    };
}
