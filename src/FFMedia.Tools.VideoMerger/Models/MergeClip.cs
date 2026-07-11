using FFMedia.Media;

namespace FFMedia.Tools.VideoMerger.Models;

/// <summary>A source clip together with what ffprobe found in it.</summary>
public sealed record MergeClip(string SourcePath, MediaInfo Info);
