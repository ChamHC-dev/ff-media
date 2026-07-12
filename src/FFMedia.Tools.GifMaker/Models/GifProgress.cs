namespace FFMedia.Tools.GifMaker.Models;

public enum GifPhase
{
    /// <summary>Pass 1 — building a palette from the clip's own colours.</summary>
    Analyzing,

    /// <summary>Pass 2 — rendering the GIF through that palette.</summary>
    Rendering,
}

public sealed record GifProgress(GifPhase Phase, double Percent);
