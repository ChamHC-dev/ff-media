# Video Preview & Frame Capture (M9) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Watch a video, pause on the frame you want, and click **‹ Set Start** / **Set End ›** to capture that exact moment into the range — instead of typing a timecode blind.

**Architecture:** A new **`FFMedia.Ui`** shared layer holds a `VideoPreview` control and its **headless** `VideoPreviewViewModel`. The VM talks to the player through a narrow `IMediaPlayer` seam (real `MediaElement` in the control; a fake in tests). Playback of *any* format ffmpeg can read comes from a **fast path + proxy fallback**: play the source directly, and only if `MediaElement` rejects it, transcode a small H.264 proxy (`IPreviewProxyService`, in `FFMedia.Media`) and play that. First consumer: the GIF Maker.

**Tech Stack:** C# / .NET 9, WPF + WPF-UI 4.3, CommunityToolkit.Mvvm 8.4.2, xUnit, bundled ffmpeg/ffprobe 8.1.

---

## 🚦 START HERE — orientation for a fresh session

You are picking this up cold. Read this section before Task 1.

**Repo:** `C:\Users\ChamHC\Desktop\Personal Projects\FFMedia` (Windows; PowerShell **and** Git Bash both available).

**Spec (the source of truth for *what*):** `docs/superpowers/specs/2026-07-13-m9-video-preview-design.md` — read it once, fully. This plan is the *how*.

**State when this plan was written:** `main` @ `586407c` is green — `dotnet build FFMedia.sln -c Release` → **0 warnings / 0 errors**; `dotnet test FFMedia.sln -c Release --filter "Category!=Integration"` → **730 passing**; `--filter "Category=Integration"` → **11 passing**.

**Branch for this work:** `feat/m9-video-preview` (off `main`).

**Standing project rules (from `CLAUDE.md` — these override anything else):**
1. **Keep `SDD.md` up to date** in the same change, bumping its version + Changelog. Task 7 does this; do not skip it.
2. **Record progress in `CLAUDE.md`'s Progress Log** (newest at top). Task 7 does this.
3. **Branch per task-group; deliver via PR. Never commit to `main`. Never merge — the user reviews and merges.**

**Verification gate for EVERY task:**
```
dotnet build FFMedia.sln -c Release                                    # must be 0 warnings / 0 errors
dotnet test FFMedia.sln -c Release --filter "Category!=Integration"    # must stay green
```
> **On test counts:** each task below states the tests it *adds*. **Report your actual total; do not treat any printed total as a gate.** In M8, review rounds added 16 tests the plan never predicted, and every downstream "expected count" in that plan went stale. The count only has to *grow by what you added* and never go down.

---

## 🔬 Facts already VERIFIED — build on these, do NOT re-derive or "correct" them

- **WPF's `MediaElement` CANNOT play VP9/WebM.** Tested against real synthesized files, hosting a real `MediaElement` on an STA thread with a real message loop: **MP4/H.264 → `MediaOpened` ✅**, **MKV/H.264 → `MediaOpened` ✅**, **WebM/VP9 → `MediaFailed` ❌** (*"Media file download failed."*). It renders through Windows Media Foundation, so its codec support is *Windows'*, not ours — **and WebM is a format our own downloader produces.** This is the entire reason the proxy fallback exists.
- **`FFMedia.Media` targets `net9.0` — plain, NOT `-windows`, and has NO WPF.** It also sets `TreatWarningsAsErrors`. The proxy service belongs there; **the control must not** (that is why `FFMedia.Ui` exists).
- **`TrimParsing.TryParse` currently REJECTS `1:23.45`.** The bare-seconds form goes through `double.TryParse` (so `83.45` works), but the **colon form parses each part with `int.TryParse`**. Task 1 fixes this. Until it is fixed, a capture button writing `1:23.45` produces an unparseable value and Create greys out.
- **`GifMakerViewModel.FormatTime` truncates to whole seconds** for anything ≥ 1 s (`m\:ss`). Task 1 replaces it with a round-trippable shared formatter.
- **These `SymbolRegular` names all EXIST** (checked with `Enum.TryParse` against the real WPF-UI 4.3.0 assembly): `Play24`, `Pause24`, `ChevronLeft24`, `ChevronRight24`, `Previous24`, `Next24`, `Gif24`, `ArrowReset24`, `Timer24`. **The shell degrades an unparseable icon name to `Apps24` silently** — never use one you have not checked.
- **`IFfmpegRunner.RunAsync` PREPENDS `-hide_banner -nostdin -y` and APPENDS `-progress pipe:1 -nostats`.** Your argument lists must **not** contain `-y`, `-progress`, or `-hide_banner`.

## ⚠️ Hard-won lessons — violating these repeats a shipped bug

- **ffmpeg's exit code cannot be trusted.** Always **re-probe** output and check it is what you asked for.
- **A `StaticResource` key that does not exist compiles clean and throws `XamlParseException` at page LOAD** — in front of the user. Never use a key you have not verified. (`DynamicResource` fails *silently* instead, which is worse.)
- **A `Page` must not contain its own `ScrollViewer`** — the shell provides one, and a nested one silently swallows the mouse wheel.
- **Use `ui:` controls, not their plain WPF namesakes** — WPF-UI styles only its own subclasses; a plain one renders as a **white box on a dark page**.
- **A gesture that is not a command bypasses `CanExecute` entirely.** Freeze-guards go in **both** the `CanExecute` **and** the method body. This bug shipped **twice** in M8.
- **A test only pins an invariant if the fixture varies along the axis the invariant is about.** This has now bitten in *five* separate M8 tasks.

---

## Global Constraints

- **The proxy RESCALES ONLY — it must NEVER re-time.** The captured timestamp is read from the *player's* position, so a proxy whose timeline differed from the source's would make **every captured time a lie**, and the GIF would be cut somewhere other than where the user saw. **No `-r`, no `-ss`, no `-t`, no frame-dropping/duplicating filter. Ever.**
- **The preview is an aid, never a gate.** If the proxy fails or is cancelled, the timecode boxes stay fully usable and the tool still works.
- **Capture is frozen while a GIF is rendering** — gated by `CanExecute` **and** guarded in the method body.
- **A capture that would invert the range is refused with an explanation** — never silently swallowed, never silently reordered. *A disabled or no-op control with no explanation is a dead end.*
- On a failed probe, surface **the analyzer's own reason** — never a generic "not a video".
- `InvariantCulture` in all parsing/formatting (a `,` decimal separator on a German locale must not change what parses).
- **Zero-warning bar:** `dotnet build -c Release` → 0 warnings / 0 errors.
- **Baseline: 730 unit tests, 11 integration tests.**

---

## Exact signatures you will build against (read; do not re-derive)

```csharp
// FFMedia.Media  (net9.0 — NO WPF)
public interface IFfmpegRunner {
    Task<Result> RunAsync(IReadOnlyList<string> arguments, IProgress<FfmpegProgress>? progress = null, CancellationToken ct = default);
}
public interface IMediaAnalyzer {
    Task<Result<MediaInfo>> AnalyzeAsync(string filePath, CancellationToken ct = default);
}
public sealed record MediaInfo(TimeSpan Duration, string ContainerFormat, VideoStreamInfo? Video, AudioStreamInfo? Audio) {
    public bool HasAudio { get; }
}
public sealed record VideoStreamInfo(int Width, int Height, FrameRate FrameRate, string CodecName, string PixelFormat, int Rotation);
public readonly record struct FrameRate(int Numerator, int Denominator) { public double Value { get; } public string ToFfmpegString(); }
public sealed record FfmpegProgress(TimeSpan Position, double Speed, bool IsFinal);

// FFMedia.Core
public sealed class Result    { bool IsSuccess; string? Error; static Result Success(); static Result Failure(string); }
public sealed class Result<T> { bool IsSuccess; T? Value; string? Error; static Result<T> Success(T); static Result<T> Failure(string); }
public static class TrimParsing { public static TimeSpan? TryParse(string? text); }   // Task 1 adds Format()

// FFMedia.Tools.GifMaker — the consumer (Task 5 modifies)
public partial class GifMakerViewModel : ObservableObject {
    public GifMakerViewModel(IMediaAnalyzer analyzer, IGifService gifService, IGifSizeProfileStore profiles,
                             ISettingsService settings, IHistoryService history, INotificationService notifications);
    [ObservableProperty] private string _sourcePath;      // public string SourcePath
    [ObservableProperty] private string _startText;       // public string StartText
    [ObservableProperty] private string _endText;         // public string EndText
    [ObservableProperty] private string _rangeHint;       // public string RangeHint
    [ObservableProperty] private bool   _isRendering;     // public bool IsRendering
    public bool SourceLoaded { get; }
    public bool CanEditParameters => !IsRendering;
    [RelayCommand(CanExecute = nameof(CanEditParameters))] public async Task LoadVideoAsync(string path);
}
```

---

## File Structure

| File | Responsibility |
|---|---|
| `src/FFMedia.Core/Media/TrimParsing.cs` | **Modify.** `TryParse` accepts `1:23.45`; new round-trippable `Format`. |
| `src/FFMedia.Media/Preview/PreviewProxyArgs.cs` | **Pure.** The ffmpeg argument list for a proxy. |
| `src/FFMedia.Media/Preview/PreviewProxyPath.cs` | **Pure.** The cache key / proxy file name for a source. |
| `src/FFMedia.Media/Preview/IPreviewProxyService.cs` + `PreviewProxyService.cs` | Build-or-reuse a proxy; sweep stale ones. |
| `src/FFMedia.Ui/FFMedia.Ui.csproj` | **New project** (net9.0-windows, UseWPF). |
| `src/FFMedia.Ui/Playback/IMediaPlayer.cs` | The seam. Lets the VM be tested with no `MediaElement`. |
| `src/FFMedia.Ui/Playback/MediaElementPlayer.cs` | `IMediaPlayer` over a real `MediaElement`. |
| `src/FFMedia.Ui/ViewModels/VideoPreviewViewModel.cs` | **Headless.** Load, fallback, transport, capture. |
| `src/FFMedia.Ui/Views/VideoPreview.xaml(.cs)` | The `UserControl`. |
| `src/FFMedia.Tools.GifMaker/Views/GifMakerPage.xaml(.cs)` | **Modify.** Host the preview; wire capture. |
| `src/FFMedia.Tools.GifMaker/ViewModels/GifMakerViewModel.cs` | **Modify.** Accept captures; freeze them while rendering. |

---

### Task 1: `TrimParsing` — capture-grade precision

**Why first:** without it the capture button writes an **unparseable** value (`1:23.45` → `null`) and Create greys out. Everything downstream depends on this.

**Files:**
- Modify: `src/FFMedia.Core/Media/TrimParsing.cs`
- Test: `src/FFMedia.Tests/YouTubeDownloader/TrimParsingTests.cs` (existing file — **add** to it; do not rewrite it)

**Interfaces:**
- Produces: `FFMedia.Core.Media.TrimParsing.TryParse(string?) → TimeSpan?` (now accepts a fractional last part in the colon form) and **new** `TrimParsing.Format(TimeSpan) → string`.

- [ ] **Step 1: Write the failing tests**

Append to `src/FFMedia.Tests/YouTubeDownloader/TrimParsingTests.cs` (it already `using`s the Core `TrimParsing` via an alias — **match whatever alias the file already uses**):

```csharp
    [Theory]
    [InlineData("1:23.45", 83.45)]
    [InlineData("0:05.5", 5.5)]
    [InlineData("1:02:03.25", 3723.25)]
    [InlineData("0:00.1", 0.1)]
    public void TryParse_AcceptsAFractionalSecondInTheColonForm(string text, double expectedSeconds)
    {
        // THE WHOLE POINT OF M9. A capture button reads the player's position -- 1:23.45 -- and writes
        // it here. Before this, the colon form parsed each part with int.TryParse, so this returned
        // NULL: the range went invalid and Create greyed out. The feature was broken on arrival.
        var parsed = CoreTrimParsing.TryParse(text);

        Assert.NotNull(parsed);
        Assert.Equal(expectedSeconds, parsed!.Value.TotalSeconds, 3);
    }

    [Theory]
    [InlineData("1:60.5")]   // 60 seconds is not a second
    [InlineData("1:-3.5")]
    [InlineData("1:2:3:4.5")]
    [InlineData("1:aa.5")]
    public void TryParse_StillRejectsNonsense(string text)
        => Assert.Null(CoreTrimParsing.TryParse(text));

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(83, "1:23")]
    [InlineData(83.45, "1:23.45")]
    [InlineData(3723.25, "1:02:03.25")]
    [InlineData(0.5, "0.5")]
    public void Format_RendersATimestampAHumanRecognises(double seconds, string expected)
        => Assert.Equal(expected, CoreTrimParsing.Format(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(0)]
    [InlineData(0.25)]
    [InlineData(7.125)]
    [InlineData(83.45)]
    [InlineData(3723.25)]
    [InlineData(5999.999)]
    public void Format_RoundTripsThroughTryParse_ToTheSameInstant(double seconds)
    {
        // THE INVARIANT. Capture formats a position into the box; the tool parses it straight back out
        // to build the request. If those two disagreed by even a little, the GIF would be cut somewhere
        // other than where the user saw -- silently.
        var original = TimeSpan.FromSeconds(seconds);

        var round = CoreTrimParsing.TryParse(CoreTrimParsing.Format(original));

        Assert.NotNull(round);
        Assert.Equal(original.TotalMilliseconds, round!.Value.TotalMilliseconds, 0);
    }
```

- [ ] **Step 2: Run and verify they FAIL**

Run: `dotnet test FFMedia.sln -c Release --filter "FullyQualifiedName~TrimParsingTests"`
Expected: FAIL — `Format` does not exist; the fractional-colon cases return `null`.

- [ ] **Step 3: Implement**

Replace the colon-form branch of `TryParse` and add `Format`. The rest of the file (the bare-seconds branch, the overflow guards) stays **exactly as it is**:

```csharp
        var parts = text.Split(':');
        if (parts.Length is 2 or 3)
        {
            // The last part carries the SECONDS and may be fractional (a captured frame is rarely on a
            // whole second). The leading parts are whole hours/minutes. Parsing the last part with
            // int.TryParse -- which is what this did before M9 -- is exactly what made "1:23.45"
            // unparseable, and a capture button unable to write its own result.
            var lead = parts[..^1];
            if (!lead.All(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
            {
                return null;
            }

            if (!double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var secs)
                || secs < 0 || secs >= 60)
            {
                return null;
            }

            var n = lead.Select(part => int.Parse(part, CultureInfo.InvariantCulture)).ToArray();
            var (h, m) = n.Length == 2 ? (n[0], n[1]) : (0, n[0]);
            if (h < 0 || m < 0 || m >= 60)
            {
                return null;
            }

            try
            {
                return TimeSpan.FromHours(h) + TimeSpan.FromMinutes(m) + TimeSpan.FromSeconds(secs);
            }
            catch (OverflowException)
            {
                return null;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>Renders a timestamp the way a human writes one — and, critically, one that
    /// <see cref="TryParse"/> reads back as the SAME INSTANT.
    ///
    /// <para>The sub-second part is shown only when it is non-zero, so a hand-typed <c>1:23</c> still
    /// looks like <c>1:23</c> after a round trip, while a frame captured at <c>1:23.45</c> keeps its
    /// fraction. Truncating it — which is what the GIF Maker's own formatter used to do — silently loses
    /// up to a second, in a tool whose entire job is picking an exact moment.</para></summary>
    public static string Format(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        // Under a second there is no minute worth showing; "0.5" is what a human means.
        if (span > TimeSpan.Zero && span.TotalSeconds < 1)
        {
            return span.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        }

        var whole = span.ToString(
            span.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

        var fraction = span.TotalSeconds - Math.Floor(span.TotalSeconds);

        return fraction < 0.0005
            ? whole
            : whole + fraction.ToString(".###", CultureInfo.InvariantCulture);
    }
```

Add `using System.Globalization;` / `using System.Linq;` if they are not already there (they are).

- [ ] **Step 4: Run tests**

Run: `dotnet test FFMedia.sln -c Release --filter "Category!=Integration"`
Expected: PASS. **Every pre-existing `TrimParsing` test must still pass untouched** — this change is purely additive. If one broke, you changed behaviour you should not have.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): TrimParsing accepts a fractional second, and formats round-trippably

A capture button reads the player's position (1:23.45) and writes it into the range box.
The colon form parsed each part with int.TryParse, so that value came back NULL -- the
range went invalid and Create greyed out. The feature would have been broken on arrival."
```

---

### Task 2: The preview proxy (in `FFMedia.Media`)

**Files:**
- Create: `src/FFMedia.Media/Preview/PreviewProxyArgs.cs`, `src/FFMedia.Media/Preview/PreviewProxyPath.cs`, `src/FFMedia.Media/Preview/IPreviewProxyService.cs`, `src/FFMedia.Media/Preview/PreviewProxyService.cs`
- Test: `src/FFMedia.Tests/Media/PreviewProxyTests.cs`

**Interfaces:**
- Consumes: `IFfmpegRunner`, `MediaInfo`, `Result`/`Result<T>`.
- Produces:
  - `static class PreviewProxyArgs { static IReadOnlyList<string> Build(string sourcePath, MediaInfo info, string outputPath); }`
  - `static class PreviewProxyPath { static string For(string sourcePath, string proxyDirectory); }`
  - `interface IPreviewProxyService { Task<Result<string>> GetOrCreateAsync(string sourcePath, MediaInfo info, IProgress<double>? progress = null, CancellationToken ct = default); void SweepStale(); }`

> **`FFMedia.Media` sets `TreatWarningsAsErrors`.** A warning here is a build failure.

- [ ] **Step 1: Write the failing tests**

Create `src/FFMedia.Tests/Media/PreviewProxyTests.cs`:

```csharp
using FFMedia.Core.Results;
using FFMedia.Media;
using FFMedia.Media.Preview;
using Xunit;

namespace FFMedia.Tests.Media;

public class PreviewProxyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ffmedia-proxy-tests-" + Guid.NewGuid().ToString("N"));

    public PreviewProxyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static MediaInfo Info(int w = 1920, int h = 1080, bool audio = true)
        => new(TimeSpan.FromSeconds(30), "matroska,webm",
            new VideoStreamInfo(w, h, new FrameRate(30, 1), "vp9", "yuv420p", 0),
            audio ? new AudioStreamInfo("opus", 48000, 2) : null);

    private sealed class FakeFfmpeg : IFfmpegRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = new();

        public Func<Result> Behavior { get; set; } = Result.Success;

        public int OutputBytes { get; set; } = 2048;

        public Task<Result> RunAsync(
            IReadOnlyList<string> arguments, IProgress<FfmpegProgress>? progress = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add(arguments);
            var result = Behavior();
            if (result.IsSuccess && OutputBytes > 0)
            {
                File.WriteAllBytes(arguments[^1], new byte[OutputBytes]);
            }

            return Task.FromResult(result);
        }
    }

    // ---------- PreviewProxyArgs (pure) ----------

    [Fact]
    public void Args_NeverRetimeTheSource()
    {
        // THE ONE HARD RULE. The captured timestamp is read from the PLAYER's position, so if the proxy's
        // timeline differed from the source's by even a little, EVERY captured time would be a lie and the
        // GIF would be cut somewhere other than where the user saw. Rescale only. Never re-time.
        var args = PreviewProxyArgs.Build(@"C:\in.webm", Info(), @"C:\tmp\p.mp4");

        Assert.DoesNotContain("-r", args);        // no frame-rate change
        Assert.DoesNotContain("-ss", args);       // no seek
        Assert.DoesNotContain("-t", args);        // no duration cap
        Assert.DoesNotContain("-to", args);
        Assert.DoesNotContain("-vsync", args);
    }

    [Fact]
    public void Args_ProduceAFormatMediaElementCanActuallyPlay()
    {
        // MediaElement renders through Windows Media Foundation. VERIFIED: it plays H.264 and FAILS on
        // VP9. The proxy exists precisely to hand it something it can open.
        var args = PreviewProxyArgs.Build(@"C:\in.webm", Info(), @"C:\tmp\p.mp4");
        var joined = string.Join(" ", args);

        Assert.Contains("libx264", joined, StringComparison.Ordinal);
        Assert.Contains("yuv420p", joined, StringComparison.Ordinal);
        Assert.Equal(@"C:\tmp\p.mp4", args[^1]);
    }

    [Fact]
    public void Args_EscapeTheCommaInsideTheScaleExpression()
    {
        // A BARE comma inside min(640,iw) would SPLIT THE FILTERGRAPH -- ffmpeg separates filters with
        // commas -- and the whole -vf argument becomes garbage. It must be escaped.
        var args = PreviewProxyArgs.Build(@"C:\in.webm", Info(), @"C:\tmp\p.mp4");
        var vf = args[args.ToList().IndexOf("-vf") + 1];

        Assert.Contains(@"\,", vf, StringComparison.Ordinal);
        Assert.DoesNotContain("min(640,iw)", vf, StringComparison.Ordinal);
    }

    [Fact]
    public void Args_CapTheWidthButNeverUpscaleATinySource()
    {
        // Upscaling a 320px source to 640 invents pixels, costs encode time, and buys nothing.
        var args = PreviewProxyArgs.Build(@"C:\in.webm", Info(), @"C:\tmp\p.mp4");
        var vf = args[args.ToList().IndexOf("-vf") + 1];

        Assert.Contains("min(640", vf, StringComparison.Ordinal);   // a cap, not a target
        Assert.Contains(":h=-2", vf, StringComparison.Ordinal);     // height derived, and forced EVEN
    }

    [Fact]
    public void Args_DropAudioWhenTheSourceHasNone()
    {
        // Asking ffmpeg to encode an audio stream that does not exist fails the whole run.
        var args = PreviewProxyArgs.Build(@"C:\in.webm", Info(audio: false), @"C:\tmp\p.mp4");

        Assert.Contains("-an", args);
    }

    [Fact]
    public void Args_KeepAudioWhenTheSourceHasIt()
    {
        // The user is scrubbing to FIND a moment, and sound is often how a human finds it.
        var args = PreviewProxyArgs.Build(@"C:\in.webm", Info(audio: true), @"C:\tmp\p.mp4");

        Assert.DoesNotContain("-an", args);
        Assert.Contains("aac", string.Join(" ", args), StringComparison.Ordinal);
    }

    [Fact]
    public void Args_DoNotRepeatTheFlagsTheRunnerAlreadyAdds()
    {
        var args = PreviewProxyArgs.Build(@"C:\in.webm", Info(), @"C:\tmp\p.mp4");

        Assert.DoesNotContain("-y", args);
        Assert.DoesNotContain("-progress", args);
        Assert.DoesNotContain("-hide_banner", args);
    }

    // ---------- PreviewProxyPath (pure) ----------

    [Fact]
    public void Path_ChangesWhenTheSourceChangesOnDisk()
    {
        // A cache keyed on the PATH ALONE would serve a stale proxy of a file the user has since
        // re-encoded or replaced -- they would scrub the OLD video and capture times into the NEW one.
        var file = Path.Combine(_dir, "clip.mp4");
        File.WriteAllBytes(file, new byte[100]);
        var before = PreviewProxyPath.For(file, _dir);

        File.WriteAllBytes(file, new byte[200]);            // same path, different content
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(5));
        var after = PreviewProxyPath.For(file, _dir);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Path_IsStableForAnUnchangedSource()
    {
        var file = Path.Combine(_dir, "clip.mp4");
        File.WriteAllBytes(file, new byte[100]);

        Assert.Equal(PreviewProxyPath.For(file, _dir), PreviewProxyPath.For(file, _dir));
    }

    // ---------- PreviewProxyService ----------

    private (PreviewProxyService Service, FakeFfmpeg Ffmpeg, string Source) Build()
    {
        var ffmpeg = new FakeFfmpeg();
        var source = Path.Combine(_dir, "src.webm");
        File.WriteAllBytes(source, new byte[128]);

        return (new PreviewProxyService(ffmpeg, _dir), ffmpeg, source);
    }

    [Fact]
    public async Task GetOrCreateAsync_BuildsAProxy_AndReturnsItsPath()
    {
        var (service, ffmpeg, source) = Build();

        var result = await service.GetOrCreateAsync(source, Info());

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(File.Exists(result.Value!));
        Assert.Single(ffmpeg.Calls);
    }

    [Fact]
    public async Task GetOrCreateAsync_ReusesACachedProxy_RatherThanTranscodingTwice()
    {
        // Re-opening the same video must not pay the transcode again.
        var (service, ffmpeg, source) = Build();

        var first = await service.GetOrCreateAsync(source, Info());
        var second = await service.GetOrCreateAsync(source, Info());

        Assert.Equal(first.Value, second.Value);
        Assert.Single(ffmpeg.Calls);   // still ONE -- the second call was served from cache
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenFfmpegFails_ReportsFailure_AndLeavesNoHalfWrittenProxy()
    {
        var (service, ffmpeg, source) = Build();
        ffmpeg.Behavior = () => Result.Failure("Error while opening encoder");
        ffmpeg.OutputBytes = 512;   // ffmpeg wrote something before dying

        var result = await service.GetOrCreateAsync(source, Info());

        Assert.False(result.IsSuccess);
        Assert.Empty(Directory.GetFiles(_dir, "*.mp4"));
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenTheProxyIsEmpty_ItFails_RatherThanCachingRubbish()
    {
        // A zero-byte "success" cached forever would poison every future open of this video.
        var (service, ffmpeg, source) = Build();
        ffmpeg.OutputBytes = 0;

        var result = await service.GetOrCreateAsync(source, Info());

        Assert.False(result.IsSuccess);
        Assert.Empty(Directory.GetFiles(_dir, "*.mp4"));
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenCancelled_LeavesNoHalfWrittenProxy()
    {
        var (service, ffmpeg, source) = Build();
        using var cts = new CancellationTokenSource();
        ffmpeg.Behavior = () => { cts.Cancel(); return Result.Success(); };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetOrCreateAsync(source, Info(), progress: null, cts.Token));

        Assert.Empty(Directory.GetFiles(_dir, "*.mp4"));
    }
}
```

> **Check `AudioStreamInfo`'s real constructor** before running — this plan assumes `(string CodecName, int SampleRate, int Channels)`. **Adapt the test to the real record; do not force the record to the plan.**

- [ ] **Step 2: Run and verify they FAIL**

Run: `dotnet test FFMedia.sln -c Release --filter "FullyQualifiedName~PreviewProxyTests"`
Expected: FAIL — the types do not exist.

- [ ] **Step 3: Implement `PreviewProxyArgs`**

`src/FFMedia.Media/Preview/PreviewProxyArgs.cs`:

```csharp
namespace FFMedia.Media.Preview;

/// <summary>Builds the ffmpeg arguments for a **preview proxy**. Pure — no I/O, no process.
///
/// <para><b>Why a proxy exists at all.</b> WPF's <c>MediaElement</c> renders through Windows Media
/// Foundation, so its codec support is <i>Windows'</i>, not ours. Verified against real files: it plays
/// H.264 in both MP4 and MKV, and <b>fails on VP9/WebM</b> — a format <b>our own downloader
/// produces</b>. So an unplayable source is transcoded to something it can definitely open.</para>
///
/// <para><b>The one hard rule: RESCALE ONLY, NEVER RE-TIME.</b> The captured timestamp is read from the
/// <i>player's</i> position. If the proxy's timeline differed from the source's by even a little, every
/// captured time would be a lie and the GIF would be cut somewhere other than where the user saw. So:
/// no <c>-r</c>, no <c>-ss</c>, no <c>-t</c>, no filter that drops or duplicates a frame.</para></summary>
public static class PreviewProxyArgs
{
    /// <summary>Cap the width; derive the height. This is a preview, not a deliverable.</summary>
    private const int MaxWidth = 640;

    public static IReadOnlyList<string> Build(string sourcePath, MediaInfo info, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var args = new List<string>
        {
            "-i", sourcePath,

            // min() CAPS the width rather than setting it, so a 320px source is never upscaled (that
            // would invent pixels and buy nothing). h=-2 derives the height from the source aspect AND
            // forces it even, which libx264 requires. The comma inside min() is ESCAPED: a bare one
            // would split the filtergraph, since ffmpeg separates filters with commas.
            "-vf", $@"scale=w='trunc(min({MaxWidth}\,iw)/2)*2':h=-2",

            "-c:v", "libx264",
            "-preset", "ultrafast",   // disposable preview: spend no time on compression
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart",
        };

        if (info.HasAudio)
        {
            // The user is scrubbing to FIND a moment, and sound is often how a human finds it.
            args.AddRange(["-c:a", "aac", "-b:a", "128k"]);
        }
        else
        {
            // Encoding an audio stream that does not exist fails the entire run.
            args.Add("-an");
        }

        args.Add(outputPath);

        return args;
    }
}
```

- [ ] **Step 4: Implement `PreviewProxyPath`**

`src/FFMedia.Media/Preview/PreviewProxyPath.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace FFMedia.Media.Preview;

/// <summary>Where a source's proxy lives.
///
/// <para>The key folds in the source's <b>last-write time and length</b>, not just its path. Keying on
/// the path alone would serve a <b>stale proxy of a file the user has since replaced or re-encoded</b> —
/// they would scrub the OLD video and capture timestamps into the NEW one.</para></summary>
public static class PreviewProxyPath
{
    public static string For(string sourcePath, string proxyDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(proxyDirectory);

        var file = new FileInfo(sourcePath);
        var identity = $"{file.FullName}|{file.LastWriteTimeUtc.Ticks}|{(file.Exists ? file.Length : 0)}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];

        return Path.Combine(proxyDirectory, $"preview-{hash}.mp4");
    }
}
```

- [ ] **Step 5: Implement the service**

`src/FFMedia.Media/Preview/IPreviewProxyService.cs`:

```csharp
using FFMedia.Core.Results;

namespace FFMedia.Media.Preview;

public interface IPreviewProxyService
{
    /// <summary>Returns a path the player can definitely open — reusing a cached proxy when there is
    /// one, and transcoding otherwise.</summary>
    Task<Result<string>> GetOrCreateAsync(
        string sourcePath, MediaInfo info, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>Deletes proxies nothing is using any more. A hard kill must not leak them forever.</summary>
    void SweepStale();
}
```

`src/FFMedia.Media/Preview/PreviewProxyService.cs`:

```csharp
using FFMedia.Core.Results;

namespace FFMedia.Media.Preview;

/// <summary>Builds — or reuses — a small H.264 proxy of a video the player cannot open.
///
/// <para>Failure here is <b>never fatal</b>: the preview is an aid, not a gate. The caller falls back to
/// typing a timecode, exactly as before M9.</para></summary>
public sealed class PreviewProxyService : IPreviewProxyService
{
    /// <summary>Proxies older than this are assumed abandoned.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(7);

    private readonly IFfmpegRunner _ffmpeg;
    private readonly string _proxyDirectory;

    public PreviewProxyService(IFfmpegRunner ffmpeg, string proxyDirectory)
    {
        ArgumentNullException.ThrowIfNull(ffmpeg);
        ArgumentException.ThrowIfNullOrWhiteSpace(proxyDirectory);

        _ffmpeg = ffmpeg;
        _proxyDirectory = proxyDirectory;
    }

    public async Task<Result<string>> GetOrCreateAsync(
        string sourcePath, MediaInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(info);

        if (!File.Exists(sourcePath))
        {
            return Result<string>.Failure("The video could not be found. It may have been moved or renamed.");
        }

        Directory.CreateDirectory(_proxyDirectory);
        var proxyPath = PreviewProxyPath.For(sourcePath, _proxyDirectory);

        // Cached from a previous open of this exact file. Re-opening must not pay the transcode again.
        if (File.Exists(proxyPath) && new FileInfo(proxyPath).Length > 0)
        {
            return Result<string>.Success(proxyPath);
        }

        var total = info.Duration.TotalSeconds;
        var reporter = progress is null
            ? null
            : new SyncProgress<FfmpegProgress>(p => progress.Report(
                total <= 0 ? 0 : Math.Clamp(p.Position.TotalSeconds / total, 0, 1) * 100));

        try
        {
            var run = await _ffmpeg.RunAsync(
                PreviewProxyArgs.Build(sourcePath, info, proxyPath), reporter, ct).ConfigureAwait(false);

            if (!run.IsSuccess)
            {
                DeleteQuietly(proxyPath);   // never cache a half-written proxy
                return Result<string>.Failure($"The preview could not be prepared: {run.Error}");
            }

            // ffmpeg's exit code is exactly what cannot be trusted. A zero-byte "success" cached forever
            // would poison every future open of this video.
            if (!File.Exists(proxyPath) || new FileInfo(proxyPath).Length == 0)
            {
                DeleteQuietly(proxyPath);
                return Result<string>.Failure("The preview could not be prepared: ffmpeg wrote nothing.");
            }

            return Result<string>.Success(proxyPath);
        }
        catch (OperationCanceledException)
        {
            DeleteQuietly(proxyPath);
            throw;
        }
    }

    public void SweepStale()
    {
        try
        {
            if (!Directory.Exists(_proxyDirectory))
            {
                return;
            }

            var cutoff = DateTime.UtcNow - StaleAfter;
            foreach (var file in Directory.EnumerateFiles(_proxyDirectory, "preview-*.mp4"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    DeleteQuietly(file);
                }
            }
        }
        catch (IOException)
        {
            // Sweeping is housekeeping. It must never break the app.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Reports on the calling thread — ffmpeg's stdout callback thread. The BCL
    /// <see cref="Progress{T}"/> marshals to the captured context and reorders reports.</summary>
    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
```

- [ ] **Step 6: Run tests, then commit**

Run: `dotnet test FFMedia.sln -c Release --filter "Category!=Integration"` → PASS (+14).

```bash
git add -A
git commit -m "feat(media): preview proxy — play what MediaElement cannot

MediaElement renders through Windows Media Foundation and FAILS on VP9/WebM -- a format our
own downloader produces. An unplayable source is transcoded to a small H.264 proxy that
RESCALES ONLY and never re-times: the captured timestamp is read from the player's position,
so a drifted timeline would make every captured time a lie."
```

---

### Task 3: `FFMedia.Ui` + the `IMediaPlayer` seam + `VideoPreviewViewModel`

**Files:**
- Create: `src/FFMedia.Ui/FFMedia.Ui.csproj`, `src/FFMedia.Ui/Playback/IMediaPlayer.cs`, `src/FFMedia.Ui/ViewModels/VideoPreviewViewModel.cs`
- Test: `src/FFMedia.Tests/Ui/VideoPreviewViewModelTests.cs`

**Interfaces:**
- Consumes: `IMediaAnalyzer`, `IPreviewProxyService` (Task 2), `TrimParsing.Format` (Task 1).
- Produces: `IMediaPlayer`, `VideoPreviewViewModel`.

- [ ] **Step 1: Create the project**

```bash
cd "C:/Users/ChamHC/Desktop/Personal Projects/FFMedia"
dotnet new classlib -o src/FFMedia.Ui -f net9.0-windows
rm src/FFMedia.Ui/Class1.cs
dotnet sln FFMedia.sln add src/FFMedia.Ui/FFMedia.Ui.csproj
dotnet add src/FFMedia.Ui reference src/FFMedia.Core src/FFMedia.Media
dotnet add src/FFMedia.Tools.GifMaker reference src/FFMedia.Ui
dotnet add src/FFMedia.Tests reference src/FFMedia.Ui
```

Then make `src/FFMedia.Ui/FFMedia.Ui.csproj` **match `src/FFMedia.Tools.GifMaker/FFMedia.Tools.GifMaker.csproj` exactly** — `net9.0-windows`, `ImplicitUsings`, `Nullable`, `UseWPF`, and the **same two package versions** (`CommunityToolkit.Mvvm` **8.4.2**, `WPF-UI` **4.3.0**). **Copy it; do not invent it.**

- [ ] **Step 2: Write the failing tests**

Create `src/FFMedia.Tests/Ui/VideoPreviewViewModelTests.cs`:

```csharp
using FFMedia.Core.Results;
using FFMedia.Media;
using FFMedia.Media.Preview;
using FFMedia.Ui.Playback;
using FFMedia.Ui.ViewModels;
using Xunit;

namespace FFMedia.Tests.Ui;

public class VideoPreviewViewModelTests
{
    private static MediaInfo Info(double seconds = 30, int fps = 25)
        => new(TimeSpan.FromSeconds(seconds), "matroska,webm",
            new VideoStreamInfo(1920, 1080, new FrameRate(fps, 1), "vp9", "yuv420p", 0), null);

    /// <summary>A player that can be told to reject a given path — which is the whole point: the real
    /// MediaElement rejects VP9/WebM, and the fallback is the entire design.</summary>
    private sealed class FakePlayer : IMediaPlayer
    {
        public List<string> Opened { get; } = new();

        /// <summary>Paths this player refuses, simulating MediaElement's codec limits.</summary>
        public HashSet<string> Unplayable { get; } = new(StringComparer.OrdinalIgnoreCase);

        public TimeSpan Position { get; set; }

        public TimeSpan? Duration { get; private set; }

        public bool IsPlaying { get; private set; }

        public event EventHandler? MediaOpened;

        public event EventHandler<string>? MediaFailed;

        public void Open(string path)
        {
            Opened.Add(path);
            if (Unplayable.Contains(path))
            {
                MediaFailed?.Invoke(this, "Media file download failed.");
                return;
            }

            Duration = TimeSpan.FromSeconds(30);
            MediaOpened?.Invoke(this, EventArgs.Empty);
        }

        public void Play() => IsPlaying = true;

        public void Pause() => IsPlaying = false;
    }

    private sealed class FakeAnalyzer : IMediaAnalyzer
    {
        public Func<string, Result<MediaInfo>> Behavior { get; set; } = _ => Result<MediaInfo>.Success(Info());

        public Task<Result<MediaInfo>> AnalyzeAsync(string filePath, CancellationToken ct = default)
            => Task.FromResult(Behavior(filePath));
    }

    private sealed class FakeProxies : IPreviewProxyService
    {
        public int Calls { get; private set; }

        public Func<string, Result<string>> Behavior { get; set; } = src => Result<string>.Success(src + ".proxy.mp4");

        public Task<Result<string>> GetOrCreateAsync(
            string sourcePath, MediaInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Behavior(sourcePath));
        }

        public void SweepStale() { }
    }

    private static (VideoPreviewViewModel Vm, FakePlayer Player, FakeAnalyzer Analyzer, FakeProxies Proxies) Build()
    {
        var player = new FakePlayer();
        var analyzer = new FakeAnalyzer();
        var proxies = new FakeProxies();

        return (new VideoPreviewViewModel(analyzer, proxies, player), player, analyzer, proxies);
    }

    [Fact]
    public async Task LoadAsync_APlayableSource_PlaysItDirectly_WithNoTranscode()
    {
        // THE FAST PATH. Most videos (MP4/MKV H.264) just play. Paying a transcode for them would be
        // pure waste -- this is the merger's conformance discipline: conforming input is left alone.
        var (vm, player, _, proxies) = Build();

        await vm.LoadAsync(@"C:\clip.mp4");

        Assert.Equal(new[] { @"C:\clip.mp4" }, player.Opened);
        Assert.Equal(0, proxies.Calls);
        Assert.False(vm.IsPreparingProxy);
    }

    [Fact]
    public async Task LoadAsync_WhenThePlayerRejectsTheSource_BuildsAProxy_AndPlaysTHAT()
    {
        // THE WHOLE DESIGN. MediaElement FAILS on VP9/WebM -- a format our own downloader produces -- so
        // without this the preview is simply blank for videos FFMedia itself made.
        var (vm, player, _, proxies) = Build();
        player.Unplayable.Add(@"C:\clip.webm");

        await vm.LoadAsync(@"C:\clip.webm");

        Assert.Equal(1, proxies.Calls);
        Assert.Equal(2, player.Opened.Count);
        Assert.Equal(@"C:\clip.webm", player.Opened[0]);          // tried the source first
        Assert.Equal(@"C:\clip.webm.proxy.mp4", player.Opened[1]); // then the proxy
    }

    [Fact]
    public async Task LoadAsync_WhenTheProxyAlsoFails_SaysSo_AndDoesNotPretendItIsPlaying()
    {
        // The preview is an AID, never a GATE: the tool must still be usable by typing a timecode.
        var (vm, player, _, proxies) = Build();
        player.Unplayable.Add(@"C:\clip.webm");
        proxies.Behavior = _ => Result<string>.Failure("ffmpeg exploded");

        await vm.LoadAsync(@"C:\clip.webm");

        Assert.False(vm.IsReady);
        Assert.Contains("preview", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsPreparingProxy);
    }

    [Fact]
    public async Task LoadAsync_AFailedProbe_ReportsTheANALYZERsOwnReason()
    {
        // NEVER a generic "not a video". That exact mistake blamed a user's perfectly good .mp4 for a
        // MISSING FFPROBE and sent them off to inspect their file (CLAUDE.md, M7).
        var (vm, _, analyzer, _) = Build();
        analyzer.Behavior = _ => Result<MediaInfo>.Failure("Could not run ffprobe: file not found.");

        await vm.LoadAsync(@"C:\clip.mp4");

        Assert.False(vm.IsReady);
        Assert.Contains("ffprobe", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CaptureStart_RaisesTheCurrentPosition()
    {
        var (vm, player, _, _) = Build();
        await vm.LoadAsync(@"C:\clip.mp4");
        player.Position = TimeSpan.FromSeconds(83.45);

        TimeSpan? captured = null;
        vm.StartCaptured += (_, t) => captured = t;
        vm.CaptureStartCommand.Execute(null);

        Assert.Equal(TimeSpan.FromSeconds(83.45), captured);
    }

    [Fact]
    public async Task CaptureEnd_RaisesTheCurrentPosition()
    {
        var (vm, player, _, _) = Build();
        await vm.LoadAsync(@"C:\clip.mp4");
        player.Position = TimeSpan.FromSeconds(12.5);

        TimeSpan? captured = null;
        vm.EndCaptured += (_, t) => captured = t;
        vm.CaptureEndCommand.Execute(null);

        Assert.Equal(TimeSpan.FromSeconds(12.5), captured);
    }

    [Fact]
    public async Task Capture_IsRefused_WhenTheHostHasFrozenIt()
    {
        // A GIF render holds a SNAPSHOT of the request. A page that can still mutate Start/End describes
        // a job that is NOT the one running -- the bug M8 shipped twice. And the guard must live in the
        // METHOD, not only in CanExecute, because A GESTURE THAT IS NOT A COMMAND BYPASSES CanExecute.
        var (vm, player, _, _) = Build();
        await vm.LoadAsync(@"C:\clip.mp4");
        player.Position = TimeSpan.FromSeconds(5);
        vm.CanCapture = false;

        var raised = false;
        vm.StartCaptured += (_, _) => raised = true;
        vm.CaptureStart();                       // called DIRECTLY, bypassing the command
        Assert.False(vm.CaptureStartCommand.CanExecute(null));

        Assert.False(raised);
    }

    [Fact]
    public async Task StepForward_PausesAndAdvancesExactlyOneFrame()
    {
        // "One frame" is 1/fps of the SOURCE -- a 25 fps video steps 40 ms. Stepping a fixed 100 ms would
        // skip frames on a fast video and stall on a slow one.
        var (vm, player, _, _) = Build();
        await vm.LoadAsync(@"C:\clip.mp4");    // 25 fps
        player.Position = TimeSpan.FromSeconds(2);
        vm.Play();

        vm.StepForward();

        Assert.False(player.IsPlaying);        // stepping implies pausing
        Assert.Equal(2.04, player.Position.TotalSeconds, 3);
    }

    [Fact]
    public async Task StepBack_NeverGoesBeforeTheStartOfTheVideo()
    {
        var (vm, player, _, _) = Build();
        await vm.LoadAsync(@"C:\clip.mp4");
        player.Position = TimeSpan.FromMilliseconds(10);

        vm.StepBack();

        Assert.True(player.Position >= TimeSpan.Zero);
    }

    [Fact]
    public async Task LoadingASecondVideo_ReplacesTheFirst_RatherThanStackingOnIt()
    {
        // The VM is long-lived (the GIF Maker's VM is a SINGLETON so state survives navigation), so a
        // second load must fully re-initialise. M8 shipped an NRE on exactly this "load a second video"
        // path because nothing ever tested it.
        var (vm, player, _, _) = Build();
        await vm.LoadAsync(@"C:\first.mp4");

        await vm.LoadAsync(@"C:\second.mp4");

        Assert.Equal(@"C:\second.mp4", player.Opened[^1]);
        Assert.True(vm.IsReady);
    }
}
```

- [ ] **Step 3: Run and verify they FAIL**

Run: `dotnet test FFMedia.sln -c Release --filter "FullyQualifiedName~VideoPreviewViewModelTests"`
Expected: FAIL — the types do not exist.

- [ ] **Step 4: Implement `IMediaPlayer`**

`src/FFMedia.Ui/Playback/IMediaPlayer.cs`:

```csharp
namespace FFMedia.Ui.Playback;

/// <summary>The narrow seam between the ViewModel and an actual video player.
///
/// <para>It exists so <see cref="ViewModels.VideoPreviewViewModel"/> can be tested <b>headlessly</b>: a
/// real <c>MediaElement</c> cannot be driven without a window and a message pump, and the behaviour that
/// most needs testing — <b>the source fails, so fall back to a proxy</b> — is impossible to trigger on
/// demand with a real one.</para></summary>
public interface IMediaPlayer
{
    /// <summary>Where the player currently is. This is the value a capture reads.</summary>
    TimeSpan Position { get; set; }

    TimeSpan? Duration { get; }

    bool IsPlaying { get; }

    /// <summary>Raised when the media opened successfully.</summary>
    event EventHandler? MediaOpened;

    /// <summary>Raised when the player cannot play this file at all — e.g. VP9/WebM, which Windows Media
    /// Foundation does not decode. This is the signal that triggers the proxy fallback.</summary>
    event EventHandler<string>? MediaFailed;

    void Open(string path);

    void Play();

    void Pause();
}
```

- [ ] **Step 5: Implement `VideoPreviewViewModel`**

`src/FFMedia.Ui/ViewModels/VideoPreviewViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFMedia.Core.Media;
using FFMedia.Media;
using FFMedia.Media.Preview;
using FFMedia.Ui.Playback;

namespace FFMedia.Ui.ViewModels;

/// <summary>Drives the video preview: load, play/pause, step, and capture the current moment.
///
/// <para><b>Headless by construction.</b> Every dependency is an interface — including the player itself
/// (<see cref="IMediaPlayer"/>) — so the behaviour that matters most, <i>the source fails so we fall back
/// to a proxy</i>, is provable in a unit test rather than only by hand.</para>
///
/// <para>It does <b>not</b> know what Start and End mean. It raises <see cref="StartCaptured"/> /
/// <see cref="EndCaptured"/> with a position and lets the host decide — which is what keeps this control
/// reusable by the Merger and the Downloader (M10) rather than welded to the GIF Maker.</para></summary>
public partial class VideoPreviewViewModel : ObservableObject
{
    private readonly IMediaAnalyzer _analyzer;
    private readonly IPreviewProxyService _proxies;
    private readonly IMediaPlayer _player;

    private MediaInfo? _info;
    private string _sourcePath = "";
    private TaskCompletionSource<bool>? _openAttempt;

    public VideoPreviewViewModel(IMediaAnalyzer analyzer, IPreviewProxyService proxies, IMediaPlayer player)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(proxies);
        ArgumentNullException.ThrowIfNull(player);

        _analyzer = analyzer;
        _proxies = proxies;
        _player = player;

        _player.MediaOpened += (_, _) => _openAttempt?.TrySetResult(true);
        _player.MediaFailed += (_, _) => _openAttempt?.TrySetResult(false);
    }

    /// <summary>The user captured the current moment as the range's START.</summary>
    public event EventHandler<TimeSpan>? StartCaptured;

    /// <summary>The user captured the current moment as the range's END.</summary>
    public event EventHandler<TimeSpan>? EndCaptured;

    [ObservableProperty] private bool _isReady;

    [ObservableProperty] private bool _isPreparingProxy;

    [ObservableProperty] private double _proxyPercent;

    [ObservableProperty] private string _statusMessage = "";

    /// <summary>Whether the host currently allows capturing. The GIF Maker sets this <c>false</c> while a
    /// render is in flight: the render holds a <b>snapshot</b>, so a page that can still change Start/End
    /// describes a job that is not the one running.</summary>
    [ObservableProperty] private bool _canCapture = true;

    public TimeSpan Position
    {
        get => _player.Position;
        set => _player.Position = value;
    }

    public TimeSpan Duration => _info?.Duration ?? TimeSpan.Zero;

    public bool IsPlaying => _player.IsPlaying;

    /// <summary>Loads a video: probe it, try to play it directly, and fall back to a proxy if the player
    /// cannot decode it.</summary>
    public async Task LoadAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        IsReady = false;
        IsPreparingProxy = false;
        StatusMessage = "";
        _info = null;
        _sourcePath = path;

        var probe = await _analyzer.AnalyzeAsync(path, ct).ConfigureAwait(true);
        if (!probe.IsSuccess || probe.Value is null)
        {
            // The ANALYZER'S OWN REASON -- never a generic "not a video". That mistake blamed a user's
            // perfectly good mp4 for a missing ffprobe binary (CLAUDE.md, M7).
            StatusMessage = probe.Error ?? "That video could not be read.";
            return;
        }

        if (probe.Value.Video is null)
        {
            StatusMessage = "That file has no video track, so there is nothing to preview.";
            return;
        }

        _info = probe.Value;
        OnPropertyChanged(nameof(Duration));

        if (await TryOpenAsync(path).ConfigureAwait(true))
        {
            IsReady = true;
            return;
        }

        // The player cannot decode this file -- VP9/WebM being the case that actually happens, and one
        // OUR OWN DOWNLOADER produces. Transcode something it can open.
        IsPreparingProxy = true;
        ProxyPercent = 0;
        StatusMessage = "Preparing a preview…";

        var progress = new Progress<double>(p => ProxyPercent = p);
        var proxy = await _proxies
            .GetOrCreateAsync(path, _info, progress, ct)
            .ConfigureAwait(true);

        IsPreparingProxy = false;

        if (!proxy.IsSuccess || proxy.Value is null)
        {
            // An AID, never a GATE. The timecode boxes still work.
            StatusMessage = proxy.Error ?? "The preview could not be prepared. You can still type times by hand.";
            return;
        }

        if (!await TryOpenAsync(proxy.Value).ConfigureAwait(true))
        {
            StatusMessage = "The preview could not be played. You can still type times by hand.";
            return;
        }

        StatusMessage = "";
        IsReady = true;
    }

    /// <summary>Hands a path to the player and waits for it to say yes or no.</summary>
    private Task<bool> TryOpenAsync(string path)
    {
        _openAttempt = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _player.Open(path);

        return _openAttempt.Task;
    }

    [RelayCommand]
    public void Play()
    {
        _player.Play();
        OnPropertyChanged(nameof(IsPlaying));
    }

    [RelayCommand]
    public void Pause()
    {
        _player.Pause();
        OnPropertyChanged(nameof(IsPlaying));
    }

    /// <summary>One frame of the SOURCE — 40 ms at 25 fps. A fixed step would skip frames on a fast
    /// video and stall on a slow one.</summary>
    private TimeSpan FrameStep
    {
        get
        {
            var fps = _info?.Video?.FrameRate.Value ?? 0;

            return fps > 0 ? TimeSpan.FromSeconds(1.0 / fps) : TimeSpan.FromMilliseconds(40);
        }
    }

    [RelayCommand]
    public void StepForward()
    {
        Pause();
        var next = _player.Position + FrameStep;
        _player.Position = Duration > TimeSpan.Zero && next > Duration ? Duration : next;
        OnPropertyChanged(nameof(Position));
    }

    [RelayCommand]
    public void StepBack()
    {
        Pause();
        var previous = _player.Position - FrameStep;
        _player.Position = previous < TimeSpan.Zero ? TimeSpan.Zero : previous;
        OnPropertyChanged(nameof(Position));
    }

    [RelayCommand(CanExecute = nameof(CanCapture))]
    public void CaptureStart()
    {
        // Guarded in the METHOD as well as in CanExecute, because a gesture that is not a command
        // bypasses CanExecute entirely -- the bug M8 shipped twice.
        if (!CanCapture || !IsReady)
        {
            return;
        }

        StartCaptured?.Invoke(this, _player.Position);
    }

    [RelayCommand(CanExecute = nameof(CanCapture))]
    public void CaptureEnd()
    {
        if (!CanCapture || !IsReady)
        {
            return;
        }

        EndCaptured?.Invoke(this, _player.Position);
    }

    partial void OnCanCaptureChanged(bool value)
    {
        CaptureStartCommand.NotifyCanExecuteChanged();
        CaptureEndCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The current position, formatted the same way the range boxes are — so what the user reads
    /// under the player is exactly what a capture will write into the box.</summary>
    public string PositionText => TrimParsing.Format(Position);
}
```

- [ ] **Step 6: Run tests, then commit**

Run: `dotnet test FFMedia.sln -c Release --filter "Category!=Integration"` → PASS (+11).

```bash
git add -A
git commit -m "feat(ui): FFMedia.Ui + VideoPreviewViewModel — headless, with the proxy fallback"
```

---

### Task 4: The `VideoPreview` control

**Files:**
- Create: `src/FFMedia.Ui/Playback/MediaElementPlayer.cs`, `src/FFMedia.Ui/Views/VideoPreview.xaml`, `src/FFMedia.Ui/Views/VideoPreview.xaml.cs`
- Test: `src/FFMedia.Tests/Ui/VideoPreviewLoadTests.cs`

**Interfaces:**
- Consumes: `IMediaPlayer`, `VideoPreviewViewModel` (Task 3).
- Produces: `MediaElementPlayer : IMediaPlayer`, `VideoPreview : UserControl`.

**Non-negotiables (each has already shipped as a bug here):**
- **No `ScrollViewer`.** **No unverified resource key** (`StaticResource` throws at page **load**; `DynamicResource` fails **silently**). **`ui:` controls**, not their plain WPF namesakes. **A tooltip on every user-settable control**, naming the trade-off, attached to the **label + control row**.
- **Verified icons you may use:** `Play24`, `Pause24`, `ChevronLeft24`, `ChevronRight24`, `Previous24`, `Next24`, `ArrowReset24`, `Timer24`.
- **`MediaElement.ScrubbingEnabled = true`** — without it, seeking while paused does **not** render the new frame, so the user would capture a timestamp for a frame they never saw.

- [ ] **Step 1: Implement `MediaElementPlayer`**

`src/FFMedia.Ui/Playback/MediaElementPlayer.cs`:

```csharp
using System.Windows.Controls;

namespace FFMedia.Ui.Playback;

/// <summary><see cref="IMediaPlayer"/> over a real WPF <see cref="MediaElement"/>.
///
/// <para><b>This is the only place that knows about <c>MediaElement</c>.</b> Everything else talks to the
/// interface, which is what makes the proxy-fallback logic testable without a window.</para>
///
/// <para><c>ScrubbingEnabled</c> is <b>load-bearing</b>: without it, setting <c>Position</c> while paused
/// does not render the new frame — so the user would capture a timestamp for a frame they never saw.</para></summary>
public sealed class MediaElementPlayer : IMediaPlayer
{
    private readonly MediaElement _element;

    public MediaElementPlayer(MediaElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        _element = element;
        _element.LoadedBehavior = MediaState.Manual;
        _element.ScrubbingEnabled = true;
        _element.MediaOpened += (_, _) => MediaOpened?.Invoke(this, EventArgs.Empty);
        _element.MediaFailed += (_, e) =>
            MediaFailed?.Invoke(this, e.ErrorException?.Message ?? "The player could not open this video.");
    }

    public event EventHandler? MediaOpened;

    public event EventHandler<string>? MediaFailed;

    public TimeSpan Position
    {
        get => _element.Position;
        set => _element.Position = value;
    }

    public TimeSpan? Duration => _element.NaturalDuration.HasTimeSpan
        ? _element.NaturalDuration.TimeSpan
        : null;

    public bool IsPlaying { get; private set; }

    public void Open(string path)
    {
        IsPlaying = false;
        _element.Source = new Uri(path);
    }

    public void Play()
    {
        _element.Play();
        IsPlaying = true;
    }

    public void Pause()
    {
        _element.Pause();
        IsPlaying = false;
    }
}
```

- [ ] **Step 2: Build the control**

`src/FFMedia.Ui/Views/VideoPreview.xaml` — a `UserControl` whose `DataContext` is a `VideoPreviewViewModel`. Layout, top to bottom:

1. The `MediaElement` (fixed height ~260, `Stretch="Uniform"`, black background).
2. A **seek `Slider`** bound to the position (0 → `Duration.TotalSeconds`), plus a **position/duration readout** using `PositionText`.
3. A transport row: **Play** (`Play24`), **Pause** (`Pause24`), **‹ frame** (`ChevronLeft24`), **frame ›** (`ChevronRight24`).
4. The capture row: **`‹ Set Start`** and **`Set End ›`** (`ui:Button`, bound to `CaptureStartCommand` / `CaptureEndCommand`).
5. A `ProgressBar` + `StatusMessage`, shown only while `IsPreparingProxy` (the proxy transcode).

**Tooltips — name the trade-off, not the definition:**
- **Set Start / Set End** — *"Uses the exact moment the video is paused at as the start (or end) of the GIF. Pause on the frame you want, then click."*
- **Frame step** — *"Nudges one frame at a time, so you can land on exactly the frame you want."*
- **Seek slider** — *"Drag to move through the video. The GIF is cut from the part you choose below."*

Wire the code-behind (`VideoPreview.xaml.cs`) to construct a `MediaElementPlayer` over the control's own `MediaElement` and hand it to the VM. Keep code-behind to **only** what needs the visual tree.

> The VM is constructed by the **host** (the GIF Maker page), which cannot supply the `MediaElement` — it does not exist until the control is loaded. So expose a method on the control, e.g. `public void Attach(VideoPreviewViewModel vm)`, which sets `DataContext` and gives the VM its player. **The `IMediaPlayer` must be settable on the VM after construction** — adjust `VideoPreviewViewModel` to take the player via an `AttachPlayer(IMediaPlayer)` call instead of the constructor if that reads more cleanly. **If you change the VM's shape, update Task 3's tests to match and say so in your report.**

- [ ] **Step 3: Write the page-load tests**

Create `src/FFMedia.Tests/Ui/VideoPreviewLoadTests.cs`, `[Collection("wpf")]`, taking `WpfHost` — **copy `src/FFMedia.Tests/GifMaker/GifMakerPageLoadTests.cs` exactly** and adapt:

```csharp
[Fact] public void VideoPreview_LoadsItsXaml_WithTheAppsRealResourceDictionaries() { }
[Fact] public void VideoPreview_DoesNotNestItsOwnScrollViewer() { }
```

- [ ] **Step 4: Build and run**

```
dotnet build FFMedia.sln -c Release                                              # 0 warnings / 0 errors
dotnet test FFMedia.sln -c Release --filter "FullyQualifiedName~VideoPreviewLoadTests"
dotnet test FFMedia.sln -c Release --filter "Category!=Integration"
```

- [ ] **Step 5: Mutation-check the load test**

Temporarily change a `StaticResource` key in `VideoPreview.xaml` to a name that does not exist and re-run `VideoPreviewLoadTests`. **Expected: it FAILS with `XamlParseException`.** Revert. *A page-load test that does not catch a bad key is decoration.*

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(ui): the VideoPreview control — player, transport, and the capture buttons"
```

---

### Task 5: Wire it into the GIF Maker

**Files:**
- Modify: `src/FFMedia.Tools.GifMaker/ViewModels/GifMakerViewModel.cs`, `src/FFMedia.Tools.GifMaker/Views/GifMakerPage.xaml(.cs)`, `src/FFMedia.Tools.GifMaker/ServiceCollectionExtensions.cs`, `src/FFMedia.App/App.xaml.cs`
- Test: `src/FFMedia.Tests/GifMaker/GifMakerViewModelTests.cs` (add), `src/FFMedia.Tests/Views/TooltipCoverageTests.cs`

**What must be true when this task is done:**

1. `GifMakerViewModel` exposes a `VideoPreviewViewModel Preview` (constructor-injected).
2. `LoadVideoAsync` **also** loads the preview.
3. Capture wiring:
   - `Preview.StartCaptured` → set `StartText = TrimParsing.Format(position)`.
   - `Preview.EndCaptured` → set `EndText = TrimParsing.Format(position)`.
   - **A capture that would invert the range is REFUSED with an explanation** in `RangeHint` — never silently swallowed, never silently reordered.
4. **`Preview.CanCapture` is kept in lockstep with `CanEditParameters`** — so capture freezes while a render is in flight. Set it in `OnIsRenderingChanged`.
5. `GifMakerViewModel` now **replaces its private `FormatTime`** with `TrimParsing.Format` (Task 1) — one formatter, so the box and the capture cannot disagree.
6. DI: `AddGifMaker()` registers `VideoPreviewViewModel` (**singleton**, like `GifMakerViewModel`, so a loaded video survives navigation) and `IPreviewProxyService` is registered by `AddGifMakerEngine` (or a new `AddFFMediaUi()` — your call, but **use `TryAddSingleton` for anything cross-cutting**, as `AddGifMakerEngine`/`AddVideoMergerEngine` already do for `IMediaAnalyzer`/`IFfmpegRunner`).

- [ ] **Step 1: Write the failing tests**

Add to `src/FFMedia.Tests/GifMaker/GifMakerViewModelTests.cs`:

```csharp
[Fact] public async Task CapturingAStart_WritesItIntoStartText_WithSubSecondPrecision() { }
[Fact] public async Task CapturingAnEnd_WritesItIntoEndText_AndTheEstimateRecomputes() { }
[Fact] public async Task CapturingAStartAfterTheEnd_IsRefused_AndExplainsWhy() { }
[Fact] public async Task CapturingAnEndBeforeTheStart_IsRefused_AndExplainsWhy() { }
[Fact] public async Task WhileRendering_CaptureIsFrozen() { }
[Fact] public async Task LoadVideoAsync_AlsoLoadsThePreview() { }
```

Each must genuinely pin its claim. In particular `CapturingAStart_WritesItIntoStartText_WithSubSecondPrecision` must capture a position with a **fraction** (e.g. `83.45 s`) and assert `StartText` is `"1:23.45"` — **not** `"1:23"`. A fixture that captures a whole second proves nothing, because a truncating formatter would pass it.

- [ ] **Step 2: Run, verify FAIL, implement, run again**

Expected after implementation: PASS, and **`TooltipCoverageTests` still 3/3** (the preview's controls are inside the GIF Maker page now, so they must all carry tooltips — the test walks the real page and **does not filter on `IsVisible`**).

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(gif): the GIF Maker gets a preview — pause, then Set Start / Set End"
```

---

### Task 6: Prove the proxy against REAL ffmpeg

**Files:**
- Create: `src/FFMedia.Tests/Integration/PreviewProxyIntegrationTests.cs`

**Read first:** `src/FFMedia.Tests/Integration/GifIntegrationTests.cs` — it already synthesizes a clip with ffmpeg's `lavfi` `testsrc` and uses a real `FfprobeMediaAnalyzer`. **Reuse its helpers; adapt to their real signatures rather than assuming.**

- [ ] **Step 1: Write the test**

```csharp
[Fact]
[Trait("Category", "Integration")]
public async Task GetOrCreateAsync_TurnsAVp9WebmIntoAProxyOfTheSameLength()
{
    // THE FIXTURE MUST VARY ALONG THE AXIS THE INVARIANT IS ABOUT. A VP9/WebM source is the ONLY kind
    // that proves anything here: MediaElement genuinely cannot play it (verified), which is the entire
    // reason the proxy exists. An MP4 fixture would prove nothing, because the fast path would carry it.
    var source = await MakeVp9ClipAsync("src.webm", seconds: 4);

    var info = await AnalyzeAsync(source);
    Assert.True(info.IsSuccess, info.Error);

    var result = await NewProxyService().GetOrCreateAsync(source, info.Value!);
    Assert.True(result.IsSuccess, result.Error);

    // Probe the PROXY -- ffmpeg's exit code is exactly what cannot be trusted.
    var proxy = await AnalyzeAsync(result.Value!);
    Assert.True(proxy.IsSuccess, proxy.Error);
    Assert.Equal("h264", proxy.Value!.Video!.CodecName);          // a codec MediaElement can open
    Assert.Equal(0, proxy.Value.Video.Width % 2);                 // even -- libx264 requires it
    Assert.True(proxy.Value.Video.Width <= 640);                  // capped

    // THE HARD RULE: the proxy must NOT re-time. If its timeline drifted from the source's, every
    // captured timestamp would be a lie and the GIF would be cut somewhere other than where the user saw.
    Assert.Equal(info.Value!.Duration.TotalSeconds, proxy.Value.Duration.TotalSeconds, 1);
}
```

Synthesize the VP9 source with: `-f lavfi -i testsrc2=size=640x360:rate=25:duration=4 -c:v libvpx-vp9 -b:v 200k out.webm`.

- [ ] **Step 2: Run**

```
dotnet test FFMedia.sln -c Release --filter "Category=Integration"    # 11 pre-existing + your new one, ALL passing
```
The 11 existing integration tests must be **unaffected**.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "test(media): prove the preview proxy against real ffmpeg, from a real VP9 source"
```

---

### Task 7: Sync the docs, open the PR

- [ ] **Step 1: `SDD.md` (project Rule 1 — mandatory)**

Bump the version. Change the **M9** row in §17 from *"📐 designed"* to *"✅ complete"*. Add a Changelog row with the **real, honest** test counts. Record the new **`FFMedia.Ui`** layer in **§5** (the structure + the dependency rule: tools may reference `Core`, `Media` and `Ui`; **never another tool**).

- [ ] **Step 2: `CLAUDE.md` (Rule 2 — mandatory)**

Add a dated Progress Log entry at the **TOP**, in the established voice (written to teach the next session, not to congratulate this one). Cover: what shipped; **that `MediaElement` cannot play VP9/WebM — a format our own downloader produces — which is why the proxy exists**; **that the proxy rescales but never re-times, because a drifted timeline makes every captured timestamp a lie**; **that `TrimParsing` rejected `1:23.45` and the feature would have been broken on arrival**. Give the honest numbers, and **state plainly what is not verified: a human has not clicked through the preview.**

- [ ] **Step 3: Full verification**

```bash
dotnet build FFMedia.sln -c Release                                             # 0 warnings / 0 errors
dotnet test FFMedia.sln -c Release --no-build --filter "Category!=Integration"
dotnet test FFMedia.sln -c Release --no-build --filter "Category=Integration"
git status --short                                                              # no .exe, bin/, obj/, artifacts/
```

- [ ] **Step 4: Open the PR — do NOT merge**

```bash
git push -u origin feat/m9-video-preview
gh pr create --repo CharmHC/ff-media --base main --title "feat(m9): Video Preview & Frame Capture" --body "..."
```

The user reviews and merges (CLAUDE.md Rule 3).

---

## Self-Review

**Spec coverage:**

| Spec § | Requirement | Task |
|---|---|---|
| §2 | Play/pause, seek, frame-step, current-time readout | 3, 4 |
| §2 | ‹ Set Start / Set End › capture | 3, 4, 5 |
| §2 | Plays any format ffmpeg can read | 2, 3, 6 |
| §3.1 | `MediaElement` fails on VP9 → proxy fallback | 2, 3, 6 |
| §3.2 | `TrimParsing` rejects `1:23.45` | 1 |
| §4 | Fast path; proxy only on failure | 3 |
| §4.1 | The proxy **rescales only, never re-times** | 2 (asserted), 6 (proven vs real ffmpeg) |
| §4.2 | Proxy cached, cancellable, swept; failure is not a gate | 2, 3 |
| §5.1 | New `FFMedia.Ui` layer; proxy in `FFMedia.Media` | 2, 3 |
| §5.2 | Headless VM behind an `IMediaPlayer` seam | 3 |
| §5.3 | Capture refused when it would invert the range; **frozen while rendering** | 3, 5 |
| §6 | Fractional parse + round-trippable `Format` | 1 |
| §7 | Analyzer's own reason; proxy failure explained | 3 |
| §8 | Pure / VM / control / integration tests | 1–6 |
| §2 | SDD §17's stale M6 row | *(already fixed in the spec PR)* |

**Type consistency:** `TrimParsing.TryParse`/`.Format` · `PreviewProxyArgs.Build(sourcePath, info, outputPath)` · `PreviewProxyPath.For(sourcePath, proxyDirectory)` · `IPreviewProxyService.GetOrCreateAsync(sourcePath, info, progress, ct)` / `.SweepStale()` · `IMediaPlayer.Open/Play/Pause/Position/Duration/IsPlaying/MediaOpened/MediaFailed` · `VideoPreviewViewModel.LoadAsync/Play/Pause/StepForward/StepBack/CaptureStart/CaptureEnd/CanCapture/IsReady/IsPreparingProxy` + `StartCaptured`/`EndCaptured`. Used consistently in Tasks 1–6.

**The one thing the implementer must READ rather than assume:** `AudioStreamInfo`'s real constructor (Task 2's fixture), `GifIntegrationTests`' real helper names (Task 6), and `TrimParsingTests`' existing `using` alias (Task 1). All three are written here by *shape*, not by verified signature. **Adapt to the file; do not force the file to the plan.**
