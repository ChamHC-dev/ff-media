namespace FFMedia.Tools.VideoMerger.Models;

/// <summary>How a clip is conformed when its aspect ratio differs from the merge target.</summary>
public enum FitMode
{
    /// <summary>Scale to fit and pad with black bars. Never crops, never distorts.</summary>
    Fit,

    /// <summary>Scale to cover, then centre-crop. Fills the frame; loses edges.</summary>
    Fill,

    /// <summary>Scale to the exact target. Distorts.</summary>
    Stretch,
}
