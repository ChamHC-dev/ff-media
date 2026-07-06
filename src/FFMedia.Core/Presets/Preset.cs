namespace FFMedia.Core.Presets;

/// <summary>A named, reusable download configuration. <see cref="Payload"/> is an opaque
/// serialized config owned by the tool module — Core stays config-agnostic.</summary>
public sealed record Preset(string Name, string Payload);
