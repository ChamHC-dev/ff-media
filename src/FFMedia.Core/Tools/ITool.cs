namespace FFMedia.Core.Tools;

/// <summary>A self-contained FFMedia feature hosted by the app shell.</summary>
public interface ITool
{
    /// <summary>Stable identifier, e.g. "youtube-downloader".</summary>
    string Id { get; }

    /// <summary>Human-readable name shown in the navigation pane.</summary>
    string DisplayName { get; }

    /// <summary>Short description of what the tool does.</summary>
    string Description { get; }

    /// <summary>Navigation icon as a WPF-UI <c>SymbolRegular</c> name, e.g. "ArrowDownload24"
    /// (kept as a string so Core stays UI-agnostic; the shell resolves it to a SymbolIcon).</summary>
    string IconGlyph { get; }

    /// <summary>Relative ordering in the navigation pane (ascending).</summary>
    int SortOrder { get; }
}
