namespace FFMedia.Core.Presets;

/// <summary>Versioned on-disk shape for presets.</summary>
public sealed record PresetDocument(int Version, IReadOnlyList<Preset> Presets)
{
    public static PresetDocument Empty { get; } = new(1, Array.Empty<Preset>());
}
