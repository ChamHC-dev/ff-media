using System;
using System.Collections.Generic;
using FFMedia.Tools.VideoMerger.Models;
using FFMedia.Tools.VideoMerger.Services;
using Xunit;

namespace FFMedia.Tests.VideoMerger;

public class ConcatArgsBuilderTests
{
    [Fact]
    public void BuildListFile_WritesOneQuotedLinePerSegment()
    {
        var content = ConcatArgsBuilder.BuildListFile([@"C:\t\000.mkv", @"C:\clips\b.mp4"]);

        Assert.Equal("file 'C:\\t\\000.mkv'\nfile 'C:\\clips\\b.mp4'\n", content);
    }

    [Fact]
    public void BuildListFile_EscapesSingleQuotesInPaths()
    {
        var content = ConcatArgsBuilder.BuildListFile([@"C:\Bob's clips\a.mp4"]);

        // ffmpeg concat escaping: close the quote, emit an escaped quote, reopen.
        Assert.Equal("file 'C:\\Bob'\\''s clips\\a.mp4'\n", content);
    }

    [Fact]
    public void BuildListFile_RejectsEmptyList()
    {
        Assert.Throws<ArgumentException>(() => ConcatArgsBuilder.BuildListFile(new List<string>()));
    }

    [Fact]
    public void BuildListFile_HandlesPathsWithSpaces()
    {
        var content = ConcatArgsBuilder.BuildListFile([@"C:\my clips\clip one.mp4"]);

        Assert.Equal("file 'C:\\my clips\\clip one.mp4'\n", content);
    }

    [Fact]
    public void BuildArgs_StreamCopiesWithSafeZero()
    {
        var args = ConcatArgsBuilder.BuildArgs(@"C:\t\list.txt", @"C:\out\merged.mkv", MergeContainer.Mkv);

        Assert.Equal(["-f", "concat", "-safe", "0", "-i", @"C:\t\list.txt", "-c", "copy", @"C:\out\merged.mkv"], args);
    }

    [Fact]
    public void BuildArgs_AddsFaststart_ForMp4Only()
    {
        var mp4 = ConcatArgsBuilder.BuildArgs(@"C:\t\list.txt", @"C:\out\merged.mp4", MergeContainer.Mp4);
        var mkv = ConcatArgsBuilder.BuildArgs(@"C:\t\list.txt", @"C:\out\merged.mkv", MergeContainer.Mkv);

        Assert.Equal(
            ["-f", "concat", "-safe", "0", "-i", @"C:\t\list.txt", "-c", "copy", "-movflags", "+faststart", @"C:\out\merged.mp4"],
            mp4);
        Assert.DoesNotContain("-movflags", mkv);
    }
}
