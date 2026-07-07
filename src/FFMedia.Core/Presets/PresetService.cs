using System.IO;
using FFMedia.Core.Persistence;
using Microsoft.Extensions.Logging;

namespace FFMedia.Core.Presets;

/// <summary>JSON-file-backed <see cref="IPresetService"/> (presets.json under the data directory).</summary>
public sealed class PresetService : IPresetService
{
    private readonly JsonStore<PresetDocument> _store;
    private PresetDocument _document;

    public PresetService(string dataDirectory, ILogger<PresetService> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(logger);
        _store = new JsonStore<PresetDocument>(Path.Combine(dataDirectory, "presets.json"), logger);
        _document = _store.Load(() => PresetDocument.Empty);
    }

    public IReadOnlyList<Preset> List() => _document.Presets;

    public void Save(Preset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var presets = _document.Presets
            .Where(p => !string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase))
            .Append(preset)
            .ToList();
        Commit(presets);
    }

    public void Delete(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var presets = _document.Presets
            .Where(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Commit(presets);
    }

    private void Commit(IReadOnlyList<Preset> presets)
    {
        _document = _document with { Presets = presets };
        _store.Save(_document);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;
}
