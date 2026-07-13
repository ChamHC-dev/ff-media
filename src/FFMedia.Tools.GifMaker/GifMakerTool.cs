using FFMedia.Core.Tools;

namespace FFMedia.Tools.GifMaker;

/// <summary>FFMedia's third tool: turn part of a local video into a GIF.</summary>
public sealed class GifMakerTool : ITool
{
    public string Id => "gif-maker";

    public string DisplayName => "GIF Maker";

    public string Description => "Turn part of a video into a GIF.";

    /// <summary>A WPF-UI <c>SymbolRegular</c> name. VERIFIED to exist against Wpf.Ui.dll 4.3.0 (searched
    /// the assembly's string heap directly). The shell falls back to <c>Apps24</c> on an unparseable
    /// name (see <c>MainWindowViewModel</c>), so a typo here degrades SILENTLY —
    /// <c>GifMakerServiceCollectionTests</c> parses it back to keep it honest.</summary>
    public string IconGlyph => "Gif24";

    /// <summary>The pane sorts ASCENDING: YouTube Downloader 10, Video Merger 20, GIF Maker 30 — this
    /// is the third tool.</summary>
    public int SortOrder => 30;
}
