namespace FFMedia.Tools.VideoMerger.Models;

/// <summary>Lifecycle of a single merge. Only one merge runs at a time (spec D7).</summary>
public enum MergeJobStatus
{
    Idle,
    Normalizing,
    Concatenating,
    Completed,
    Canceled,
    Failed,
}
