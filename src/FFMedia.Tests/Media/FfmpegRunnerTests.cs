using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFMedia.Core.Binaries;
using FFMedia.Core.Processes;
using FFMedia.Media;
using Xunit;

namespace FFMedia.Tests.Media;

public class FfmpegRunnerTests
{
    private sealed class FakeRunner : IProcessRunner
    {
        private readonly ProcessResult _result;
        private readonly string[] _stdoutLines;
        public List<string> Arguments { get; } = new();
        public string? FileName { get; private set; }

        public FakeRunner(ProcessResult result, params string[] stdoutLines)
        {
            _result = result;
            _stdoutLines = stdoutLines;
        }

        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
            IProgress<string>? onOutputLine = null, CancellationToken ct = default)
        {
            FileName = fileName;
            Arguments.AddRange(arguments);
            foreach (var line in _stdoutLines)
            {
                onOutputLine?.Report(line);
            }

            return Task.FromResult(_result);
        }
    }

    private sealed class StubBinaryProvider : IBinaryProvider
    {
        public bool Present { get; set; } = true;
        public string GetPath(ExternalBinary binary) => $@"C:\bin\{binary}.exe";
        public bool Exists(ExternalBinary binary) => Present;
    }

    [Fact]
    public async Task RunAsync_PrependsAndAppendsStandardFlags()
    {
        var runner = new FakeRunner(new ProcessResult(0, "", ""));
        var ffmpeg = new FfmpegRunner(runner, new StubBinaryProvider());

        var result = await ffmpeg.RunAsync(["-i", "a.mp4", "out.mkv"]);

        Assert.True(result.IsSuccess);
        Assert.Equal(@"C:\bin\Ffmpeg.exe", runner.FileName);
        Assert.Equal("-hide_banner", runner.Arguments[0]);
        Assert.Contains("-nostdin", runner.Arguments);
        Assert.Contains("-y", runner.Arguments);
        Assert.Contains("-nostats", runner.Arguments);
        var progressFlag = runner.Arguments.IndexOf("-progress");
        Assert.True(progressFlag >= 0);
        Assert.Equal("pipe:1", runner.Arguments[progressFlag + 1]);
        // caller args survive, in order
        Assert.True(runner.Arguments.IndexOf("-i") < runner.Arguments.IndexOf("a.mp4"));
    }

    // Note: the sink below is a synchronous IProgress<T>. Do not use BCL Progress<T> here —
    // it posts to the captured SynchronizationContext, so reports would arrive after the await
    // and the assertions would race. Same reason the download queue uses a sync adapter (SDD §12).
    [Fact]
    public async Task RunAsync_ForwardsProgressSynchronously()
    {
        var runner = new FakeRunner(
            new ProcessResult(0, "", ""),
            "out_time_us=1000000", "speed=3.0x", "progress=continue",
            "out_time_us=2000000", "progress=end");
        var ffmpeg = new FfmpegRunner(runner, new StubBinaryProvider());
        var seen = new List<FfmpegProgress>();
        var sink = new SynchronousProgress<FfmpegProgress>(seen.Add);

        await ffmpeg.RunAsync(["-i", "a.mp4"], sink);

        Assert.Equal(2, seen.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), seen[0].Position);
        Assert.Equal(3.0, seen[0].Speed);
        Assert.True(seen[1].IsFinal);
    }

    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public SynchronousProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }

    [Fact]
    public async Task RunAsync_FailsWhenBinaryMissing()
    {
        var ffmpeg = new FfmpegRunner(
            new FakeRunner(new ProcessResult(0, "", "")), new StubBinaryProvider { Present = false });

        var result = await ffmpeg.RunAsync(["-i", "a.mp4"]);

        Assert.False(result.IsSuccess);
        Assert.Contains("fetch-binaries", result.Error!);
    }

    [Fact]
    public async Task RunAsync_ReturnsStderrTail_OnNonZeroExit()
    {
        var stderr = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"line {i}"));
        var ffmpeg = new FfmpegRunner(new FakeRunner(new ProcessResult(1, "", stderr)), new StubBinaryProvider());

        var result = await ffmpeg.RunAsync(["-i", "a.mp4"]);

        Assert.False(result.IsSuccess);
        Assert.Contains("line 20", result.Error!);
        Assert.Contains("line 11", result.Error!);
        Assert.DoesNotContain("line 10", result.Error!);
    }

    [Fact]
    public async Task RunAsync_FailsWhenLaunchThrows()
    {
        var ffmpeg = new FfmpegRunner(new ThrowingRunner(), new StubBinaryProvider());

        var result = await ffmpeg.RunAsync(["-i", "a.mp4"]);

        Assert.False(result.IsSuccess);
        Assert.Contains("Could not run ffmpeg", result.Error!);
    }

    private sealed class ThrowingRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
            IProgress<string>? onOutputLine = null, CancellationToken ct = default)
            => throw new System.ComponentModel.Win32Exception("The system cannot find the file specified.");
    }
}
