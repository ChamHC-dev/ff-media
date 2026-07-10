using FFMedia.Core.Binaries;
using FFMedia.Core.Processes;
using FFMedia.Core.Results;

namespace FFMedia.Media;

/// <summary>Runs the bundled <c>ffmpeg.exe</c> through the <see cref="IProcessRunner"/> seam,
/// translating its <c>-progress</c> stdout stream into <see cref="FfmpegProgress"/> snapshots.</summary>
public sealed class FfmpegRunner : IFfmpegRunner
{
    private const int StderrTailLines = 10;

    private readonly IProcessRunner _runner;
    private readonly IBinaryProvider _binaries;

    public FfmpegRunner(IProcessRunner runner, IBinaryProvider binaries)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(binaries);
        _runner = runner;
        _binaries = binaries;
    }

    public async Task<Result> RunAsync(
        IReadOnlyList<string> arguments,
        IProgress<FfmpegProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ct.ThrowIfCancellationRequested();

        if (!_binaries.Exists(ExternalBinary.Ffmpeg))
        {
            return Result.Failure("ffmpeg.exe is missing. Run build/fetch-binaries.ps1.");
        }

        var full = new List<string>(arguments.Count + 6) { "-hide_banner", "-nostdin", "-y" };
        full.AddRange(arguments);
        full.AddRange(["-progress", "pipe:1", "-nostats"]);

        var accumulator = new FfmpegProgressAccumulator();
        IProgress<string>? lineSink = progress is null
            ? null
            : new LineSink(line =>
            {
                var snapshot = accumulator.Add(line);
                if (snapshot is not null)
                {
                    progress.Report(snapshot);
                }
            });

        ProcessResult process;
        try
        {
            process = await _runner.RunAsync(_binaries.GetPath(ExternalBinary.Ffmpeg), full, lineSink, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result.Failure($"Could not run ffmpeg: {ex.Message}");
        }

        return process.ExitCode == 0
            ? Result.Success()
            : Result.Failure($"ffmpeg failed (exit {process.ExitCode}):\n{Tail(process.StandardError)}");
    }

    private static string Tail(string stderr)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length <= StderrTailLines
            ? string.Join('\n', lines)
            : string.Join('\n', lines[^StderrTailLines..]);
    }

    /// <summary>Reports synchronously on the calling thread — a late callback must never race past
    /// the process exit (same rationale as the download queue's progress adapter, SDD §12).</summary>
    private sealed class LineSink : IProgress<string>
    {
        private readonly Action<string> _handler;
        public LineSink(Action<string> handler) => _handler = handler;
        public void Report(string value) => _handler(value);
    }
}
