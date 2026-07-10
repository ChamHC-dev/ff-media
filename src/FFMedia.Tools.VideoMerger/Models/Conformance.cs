namespace FFMedia.Tools.VideoMerger.Models;

/// <summary>Whether a clip already matches the merge target, and if not, why. A conforming clip
/// is concatenated as-is (no re-encode) — this drives the fast path, the UI badge, and the ETA.</summary>
public sealed record Conformance(bool IsConforming, IReadOnlyList<string> Mismatches);
