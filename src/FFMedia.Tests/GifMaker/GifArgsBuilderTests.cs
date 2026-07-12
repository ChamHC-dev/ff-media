using FFMedia.Media;
using FFMedia.Tools.GifMaker.Models;
using FFMedia.Tools.GifMaker.Services;
using Xunit;

namespace FFMedia.Tests.GifMaker;

public class GifArgsBuilderTests
{
    private static GifRequest Request(int width = 480, int height = 270, int fps = 15)
        => new(@"C:\in.mp4", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
            new Resolution(width, height), new FrameRate(fps, 1), @"C:\out.gif");

    [Fact]
    public void PalettePass_SeeksBeforeTheInput_SoALongVideoIsNotDecodedFromZero()
    {
        var args = GifArgsBuilder.PalettePass(Request(), @"C:\tmp\palette.png");

        var ss = args.ToList().IndexOf("-ss");
        var to = args.ToList().IndexOf("-to");
        var i = args.ToList().IndexOf("-i");

        Assert.True(ss >= 0 && to >= 0 && i >= 0);
        Assert.True(ss < i, "-ss must come BEFORE -i, or ffmpeg decodes the whole file and throws most of it away");
        Assert.True(to < i, "-to must come BEFORE -i, alongside -ss");
    }

    [Fact]
    public void PalettePass_PassesStartAndEndStraightThrough_BecauseMinusToIsAbsolute()
    {
        // VERIFIED against ffmpeg 8.1: -ss 2 -to 5 yields exactly 3.0s. -to is a POSITION on the source
        // timeline, not a duration from the seek point. Computing End-Start here would produce a GIF of
        // the wrong length -- shorter than asked, and silently so.
        var args = GifArgsBuilder.PalettePass(Request(), @"C:\tmp\palette.png");

        Assert.Equal("2", args[args.ToList().IndexOf("-ss") + 1]);
        Assert.Equal("5", args[args.ToList().IndexOf("-to") + 1]);
    }

    [Fact]
    public void PalettePass_BuildsThePaletteFromTheClipsOwnColours()
    {
        var args = GifArgsBuilder.PalettePass(Request(fps: 15), @"C:\tmp\palette.png");
        var vf = args[args.ToList().IndexOf("-vf") + 1];

        Assert.Contains("fps=15", vf, StringComparison.Ordinal);
        Assert.Contains("scale=480:-2", vf, StringComparison.Ordinal);
        Assert.Contains("palettegen", vf, StringComparison.Ordinal);
        Assert.Equal(@"C:\tmp\palette.png", args[^1]);
    }

    [Fact]
    public void RenderPass_TakesTheVideoAndThePaletteAsTwoInputs()
    {
        var args = GifArgsBuilder.RenderPass(Request(), @"C:\tmp\palette.png");

        var inputs = args.Select((a, idx) => (a, idx)).Where(x => x.a == "-i").Select(x => args[x.idx + 1]).ToList();
        Assert.Equal(new[] { @"C:\in.mp4", @"C:\tmp\palette.png" }, inputs);
    }

    [Fact]
    public void RenderPass_AppliesThePalette_AndLoopsForever()
    {
        var args = GifArgsBuilder.RenderPass(Request(), @"C:\tmp\palette.png");
        var lavfi = args[args.ToList().IndexOf("-lavfi") + 1];

        Assert.Contains("paletteuse", lavfi, StringComparison.Ordinal);
        Assert.Contains("[x][1:v]", lavfi, StringComparison.Ordinal); // the palette is the SECOND input
        Assert.Equal("0", args[args.ToList().IndexOf("-loop") + 1]);           // 0 = loop forever
        Assert.Equal(@"C:\out.gif", args[^1]);
    }

    [Fact]
    public void NeitherPass_PassesFlagsTheRunnerAlreadyAdds()
    {
        // IFfmpegRunner PREPENDS -hide_banner -nostdin -y and APPENDS -progress pipe:1 -nostats.
        // Repeating them here is at best noise and at worst a conflicting duplicate.
        foreach (var args in new[]
                 {
                     GifArgsBuilder.PalettePass(Request(), @"C:\tmp\p.png"),
                     GifArgsBuilder.RenderPass(Request(), @"C:\tmp\p.png"),
                 })
        {
            Assert.DoesNotContain("-y", args);
            Assert.DoesNotContain("-progress", args);
            Assert.DoesNotContain("-hide_banner", args);
        }
    }

    [Fact]
    public void BothPasses_ScaleToTheSameSizeAndRate_OrThePaletteDescribesADifferentImage()
    {
        // The palette must be generated from EXACTLY the frames it will be applied to. If the two
        // passes scaled differently, the palette would be optimal for an image that is never rendered.
        var request = Request(320, 180, 12);
        var paletteVf = GifArgsBuilder.PalettePass(request, @"C:\tmp\p.png")[
            GifArgsBuilder.PalettePass(request, @"C:\tmp\p.png").ToList().IndexOf("-vf") + 1];
        var renderLavfi = GifArgsBuilder.RenderPass(request, @"C:\tmp\p.png")[
            GifArgsBuilder.RenderPass(request, @"C:\tmp\p.png").ToList().IndexOf("-lavfi") + 1];

        Assert.Contains("fps=12", paletteVf, StringComparison.Ordinal);
        Assert.Contains("scale=320:-2", paletteVf, StringComparison.Ordinal);
        Assert.Contains("fps=12", renderLavfi, StringComparison.Ordinal);
        Assert.Contains("scale=320:-2", renderLavfi, StringComparison.Ordinal);
    }
}
