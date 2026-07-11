namespace FFMedia.Tools.VideoMerger.Models;

/// <summary>Everything <c>IMergeService</c> needs: the clips in final order, the standardization
/// target, and where the merged file goes.</summary>
public sealed record MergeRequest(IReadOnlyList<MergeClip> Clips, MergeTarget Target, string OutputPath);
