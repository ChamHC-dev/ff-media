using System.IO;
using FFMedia.Core.Persistence;
using FFMedia.Tools.VideoMerger.Models;
using Microsoft.Extensions.Logging;

namespace FFMedia.Tools.VideoMerger.Services;

/// <summary>JSON-backed <see cref="ISpeedProfileStore"/> at &lt;dataDirectory&gt;\encode-speed.json,
/// reusing Core's atomic <see cref="JsonStore{T}"/> (temp-file write + corrupt-file quarantine).</summary>
public sealed class SpeedProfileStore : ISpeedProfileStore
{
    public const string FileName = "encode-speed.json";

    private readonly JsonStore<SpeedProfile> _store;

    public SpeedProfileStore(string dataDirectory, ILogger<SpeedProfileStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(logger);
        _store = new JsonStore<SpeedProfile>(Path.Combine(dataDirectory, FileName), logger);
    }

    public SpeedProfile Load() => _store.Load(() => new SpeedProfile());

    public void Save(SpeedProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _store.Save(profile);
    }
}
