using FFMedia.Core.Tools;

namespace FFMedia.Tools.GifMaker.Navigation;

/// <summary>Maps this tool's id to the page the shell navigates to. Mirrors the Video Merger's own
/// <c>ToolPage</c> (and the YouTube Downloader's before it): the shell discovers pages through
/// <see cref="IToolPage"/> and is not modified when a tool is added.</summary>
public sealed class ToolPage : IToolPage
{
    public ToolPage(string toolId, Type pageType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        ArgumentNullException.ThrowIfNull(pageType);

        ToolId = toolId;
        PageType = pageType;
    }

    public string ToolId { get; }

    public Type PageType { get; }
}
