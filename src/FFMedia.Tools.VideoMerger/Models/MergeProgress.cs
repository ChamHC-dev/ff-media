namespace FFMedia.Tools.VideoMerger.Models;

/// <summary>A weighted snapshot of merge progress. When anything needs re-encoding the normalize
/// phase owns 95 % of the bar and the stream-copy concat the last 5 %; on the fast path there is
/// nothing to encode, so the concat owns the whole bar.</summary>
/// <param name="OverallPercent">0–100, and never decreasing across a single merge — a bar that
/// retreats when a fast clip finishes ahead of a slow one reads as a bug to the user.</param>
public sealed record MergeProgress(MergeJobStatus Status, double OverallPercent, string? CurrentClip);
