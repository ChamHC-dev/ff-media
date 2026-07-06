namespace FFMedia.Core.Presets;

/// <summary>Persisted, named download presets. Config-agnostic — payloads are opaque strings.</summary>
public interface IPresetService
{
    /// <summary>All saved presets.</summary>
    IReadOnlyList<Preset> List();

    /// <summary>Add or replace a preset (matched by <see cref="Preset.Name"/>) and persist.</summary>
    void Save(Preset preset);

    /// <summary>Remove the preset with the given name (no-op if absent) and persist.</summary>
    void Delete(string name);

    /// <summary>Raised after the preset set changes (save or delete).</summary>
    event EventHandler? Changed;
}
