namespace FFMedia.Media;

/// <summary>A single progress snapshot from ffmpeg's <c>-progress</c> stream.</summary>
/// <param name="Position">How far into the output ffmpeg has written.</param>
/// <param name="Speed">Encode throughput as a multiple of realtime (ffmpeg's <c>speed=2.5x</c>); 0 when unknown.</param>
/// <param name="IsFinal">True for the terminal <c>progress=end</c> block.</param>
public sealed record FfmpegProgress(TimeSpan Position, double Speed, bool IsFinal);
