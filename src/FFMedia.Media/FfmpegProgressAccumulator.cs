using System.Globalization;

namespace FFMedia.Media;

/// <summary>Accumulates ffmpeg's <c>-progress pipe:1</c> key=value lines, emitting one
/// <see cref="FfmpegProgress"/> per <c>progress=</c> terminator. Deterministic and IO-free.</summary>
public sealed class FfmpegProgressAccumulator
{
    private TimeSpan _position;
    private double _speed;

    /// <summary>Feeds one stdout line. Returns a snapshot on a <c>progress=</c> line, else null.</summary>
    public FfmpegProgress? Add(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var separator = line.IndexOf('=');
        if (separator <= 0)
        {
            return null;
        }

        var key = line[..separator].Trim();
        var value = line[(separator + 1)..].Trim();

        switch (key)
        {
            case "out_time_us" or "out_time_ms":
                // Despite the name, ffmpeg reports out_time_ms in MICROseconds too. ffmpeg also
                // emits a large negative sentinel (~long.MinValue) before a real position is
                // known; ignore anything negative and keep the last known position.
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var micros) && micros >= 0)
                {
                    _position = TimeSpan.FromMicroseconds(micros);
                }

                return null;

            case "speed":
                var trimmed = value.TrimEnd('x');
                _speed = double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) && speed > 0
                    ? speed
                    : 0;
                return null;

            case "progress":
                return new FfmpegProgress(_position, _speed, value == "end");

            default:
                return null;
        }
    }
}
