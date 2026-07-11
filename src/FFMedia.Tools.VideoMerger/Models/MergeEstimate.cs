namespace FFMedia.Tools.VideoMerger.Models;

/// <summary>What the user sees before committing to a merge. <see cref="OutputDuration"/> is exact;
/// the ETA is a range, and is replaced by ffmpeg's real figure once merging starts.</summary>
public sealed record MergeEstimate(
    TimeSpan OutputDuration,
    TimeSpan LowEta,
    TimeSpan HighEta,
    long TempBytesEstimate,
    int ReencodeCount,
    bool IsFastPath);
