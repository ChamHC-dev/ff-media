using FFMedia.Tools.VideoMerger.Models;

namespace FFMedia.Tools.VideoMerger.Services;

/// <summary>Persists the machine's measured encode throughput across runs.</summary>
public interface ISpeedProfileStore
{
    SpeedProfile Load();

    void Save(SpeedProfile profile);
}
