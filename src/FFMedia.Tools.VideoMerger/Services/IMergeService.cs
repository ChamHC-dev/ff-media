using FFMedia.Core.Results;
using FFMedia.Tools.VideoMerger.Models;

namespace FFMedia.Tools.VideoMerger.Services;

/// <summary>Normalizes the non-conforming clips and concatenates everything into one file.</summary>
public interface IMergeService
{
    /// <summary>Runs the merge. Returns the output path on success; a friendly failure otherwise
    /// (including cancellation). Never throws for an expected failure.</summary>
    Task<Result<string>> MergeAsync(
        MergeRequest request,
        IProgress<MergeProgress>? progress = null,
        CancellationToken ct = default);
}
