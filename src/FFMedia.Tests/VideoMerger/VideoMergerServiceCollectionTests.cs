using System;
using System.IO;
using FFMedia.Core;
using FFMedia.Media;
using FFMedia.Tools.VideoMerger;
using FFMedia.Tools.VideoMerger.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FFMedia.Tests.VideoMerger;

public class VideoMergerServiceCollectionTests
{
    private static ServiceProvider Build()
    {
        var temp = Path.GetTempPath();
        return new ServiceCollection()
            .AddFFMediaCore(binariesDirectory: temp, dataDirectory: temp)
            .AddVideoMergerEngine(dataDirectory: temp, tempRoot: temp, maxConcurrency: 2)
            .BuildServiceProvider();
    }

    [Fact]
    public void AddVideoMergerEngine_ResolvesTheWholeEngine()
    {
        // Resolution, not registration: a factory that compiles but cannot construct its
        // dependencies fails only at runtime, in front of the user.
        using var provider = Build();

        Assert.NotNull(provider.GetRequiredService<IMergeService>());
        Assert.NotNull(provider.GetRequiredService<IMediaAnalyzer>());
        Assert.NotNull(provider.GetRequiredService<IFfmpegRunner>());
        Assert.NotNull(provider.GetRequiredService<ISpeedProfileStore>());
    }

    [Fact]
    public void AddVideoMergerEngine_RegistersTheEngineAsSingletons()
    {
        // MergeService holds the one-merge-at-a-time contract (spec D8) and SpeedProfileStore backs
        // a file — a transient registration would give each caller its own.
        using var provider = Build();

        Assert.Same(provider.GetRequiredService<IMergeService>(), provider.GetRequiredService<IMergeService>());
        Assert.Same(
            provider.GetRequiredService<ISpeedProfileStore>(), provider.GetRequiredService<ISpeedProfileStore>());
    }

    [Fact]
    public void AddVideoMergerEngine_RegistersNoTool_SoTheShellHasNothingToNavigateTo()
    {
        // PR 1 is the engine only. Registering an ITool here would put a nav item in the shell
        // pointing at a page that does not exist yet.
        using var provider = Build();

        Assert.Empty(provider.GetRequiredService<Core.Tools.IToolRegistry>().Tools);
    }

    [Fact]
    public void AddVideoMergerEngine_RejectsZeroConcurrency()
    {
        var temp = Path.GetTempPath();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCollection().AddVideoMergerEngine(temp, temp, maxConcurrency: 0));
    }

    [Theory]
    [InlineData("", "C:\\temp")]
    [InlineData("   ", "C:\\temp")]
    [InlineData("C:\\data", "")]
    [InlineData("C:\\data", "   ")]
    public void AddVideoMergerEngine_RejectsBlankDirectories(string dataDirectory, string tempRoot)
    {
        Assert.Throws<ArgumentException>(() =>
            new ServiceCollection().AddVideoMergerEngine(dataDirectory, tempRoot, maxConcurrency: 1));
    }
}
