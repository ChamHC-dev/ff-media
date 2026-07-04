using FFMedia.Core.Results;
using FFMedia.Tools.YouTubeDownloader.Infrastructure;
using FFMedia.Tools.YouTubeDownloader.Models;

namespace FFMedia.Tools.YouTubeDownloader.Services;

public sealed class YtDlpMediaProbe : IMediaProbe
{
    private readonly IYoutubeDlFactory _factory;

    public YtDlpMediaProbe(IYoutubeDlFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public async Task<Result<MediaInfo>> ProbeAsync(string url, CancellationToken ct)
    {
        var ytdl = _factory.Create();
        var res = await ytdl.RunVideoDataFetch(url, ct: ct);
        if (!res.Success)
            return Result<MediaInfo>.Failure(string.Join(Environment.NewLine, res.ErrorOutput));

        var v = res.Data;
        var duration = v.Duration is > 0 ? TimeSpan.FromSeconds(v.Duration.Value) : (TimeSpan?)null;
        return Result<MediaInfo>.Success(new MediaInfo(v.Title, duration, v.Thumbnail, v.Uploader));
    }
}
