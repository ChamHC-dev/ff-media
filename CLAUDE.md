# CLAUDE.md — Working Rules & Progress Log for FFMedia

## Project

FFMedia is an all-in-one **Windows media toolbox** (C# / .NET 9, WPF + WPF-UI).
The v1 tool is a **YouTube Downloader** orchestrating **yt-dlp** (download) +
**ffmpeg** (transcode/mux). Architecture is modular — an app shell hosts pluggable
`ITool` modules.

**[`SDD.md`](SDD.md) is the single source of truth** for architecture, scope, and
milestones. Read it before making design decisions.

---

## 🔴 Standing Rules (always follow)

1. **Keep [`SDD.md`](SDD.md) up to date.** Whenever a design decision, scope,
   convention, dependency, or milestone changes — update `SDD.md` in the same
   change, and bump its version + Changelog entry. The SDD must never lag reality.
2. **Record progress after every task** in the [Progress Log](#-progress-log)
   below. Append a dated entry describing what was done, what changed, and what's
   next. Newest entries go at the top.
3. **Branch per task; deliver via PR.** Never commit task work directly to `main`.
   For each task, branch off the latest `main` (e.g. `feat/…`, `fix/…`, `docs/…`),
   commit there, push, and open a **PR for the user to review**. Do not merge — the
   user reviews and merges.
4. When these rules conflict with anything else, these rules win unless the user
   says otherwise.

---

## 📓 Progress Log

_Newest first. One entry per completed task/session._

### 2026-07-11 — M7 PR 1: Video Merger engine (no UI)

- **Done:** realized `FFMedia.Media` — `IMediaAnalyzer`/`FfprobeMediaAnalyzer` over ffprobe,
  `IFfmpegRunner`/`FfmpegRunner` with `-progress` streaming + a stderr tail on failure, and the pure
  `FfprobeParsing` / `FfmpegProgressAccumulator` — and built the new `FFMedia.Tools.VideoMerger`
  engine: target derivation, `ConformanceCheck`, normalize/concat arg builders, seeded shuffle with
  locked indices, `MergeEstimator` + `SpeedProfile` (`encode-speed.json`), `DiskSpaceGuard`,
  `TempDirectorySweeper`, and `MergeService` (preflight → bounded-concurrency normalize →
  stream-copy concat, temp cleanup on **every** exit path). Added `ExternalBinary.Ffprobe` +
  `fetch-binaries.ps1` extraction from the same pinned, SHA-256-verified zip, and a non-generic
  `Result` in Core. `AddVideoMergerEngine` wires it all up.
- **The keystone invariant:** `MergeEstimator` and `MergeService` both **call** `ConformanceCheck`
  rather than re-implementing "does this clip need re-encoding". If they ever disagreed, the ETA,
  the fast-path promise and the disk reservation would describe a different plan than the one that
  runs. A false *conforming* is the dangerous direction — it stream-copies a mismatched clip into
  `concat` and corrupts the output.
- **The bug worth remembering:** cancelling a token does **not** synchronously dequeue a pending
  `SemaphoreSlim.WaitAsync` waiter — the cancellation's node-removal continuation is queued to the
  thread pool, so a `Release()` microseconds later still hands the permit to the cancelled waiter,
  which then launches a **full ffmpeg encode** for a merge the user already cancelled. It showed up
  as a 1-in-5 flake; a 200-iteration harness proved 197/200 runs launched the extra encode. Fixed by
  re-checking the token *after* acquiring the gate.
- **Verified:** Release build **0/0**; **414/414** unit tests pass (`Category!=Integration`). Each
  task was reviewed by an independent agent that mutation-tested the tests — which caught three
  suites that passed against a deliberately broken implementation (a reversed stderr tail, a biased
  Fisher–Yates, a per-progress-line speed sample). Argv was additionally validated end-to-end against
  a real ffmpeg 8.1 for both the normalize and concat phases.
- **Not verified:** a real end-to-end merge driven from the app — there is **no UI in this PR** (no
  ViewModels, no XAML, no `ITool`/nav registration, deliberately: the shell must not navigate to a
  page that does not exist) and no integration test. That lands with PR 2.
- **Next:** M7 PR 2 — `MergerViewModel`, `MergerPage`, `ITool`/nav registration, history +
  notifications wiring, the override UI (which must expose Opus — `Derive` never votes for it), and
  a trait-gated integration test merging three real `testsrc` clips. SDD → **v0.14**. Delivered via
  branch `feat/m7-merge-engine` → PR.

### 2026-07-10 — M7 Video Merger: design (spec only, no code)

- **Done:** brainstormed and specced FFMedia's **second tool module**,
  `FFMedia.Tools.VideoMerger` → `docs/superpowers/specs/2026-07-10-m7-video-merger-design.md`.
  Flow: ingest local clips → probe → **auto-derived, user-overridable** standardization
  target → normalize **only non-conforming** clips to temp intermediates (bounded
  concurrency, the §12 `SemaphoreSlim` pattern) → **stream-copy `concat`**. When every clip
  already conforms, normalization is skipped and the merge is a ~1 s copy (**fast path**).
- **Decisions (user-approved):** aspect mismatch → **letterbox/pillarbox** default with a
  per-merge `FitMode` (Fit/Fill+Crop/Stretch); clips with **no audio** get a synthesized
  `anullsrc` silent track (so `concat`'s identical-stream-layout requirement holds); merge-time
  estimate is a **calibrated heuristic shown as a range**, backed by a persisted `SpeedProfile`
  rolling average of the user's own measured throughput (new `encode-speed.json`), and
  **replaced by ffmpeg's real ETA** once merging starts; **disk-space guard** fails fast before
  any encoding; ordering is manual / random / **random-with-locks** (seeded Fisher–Yates ⇒
  deterministic tests); **one merge at a time** (no `IMergeManager` — the concurrency lives
  inside the normalize phase); reuse existing history/notifications/settings; drag-to-reorder in.
  **Deferred:** transitions/crossfades, per-clip trim, per-clip fit mode, background music.
- **Two consequential findings:** (1) **FFMpegCore dropped** — listed in SDD §3 since v0.1 but
  never referenced, and it manages its own child processes, which would bypass the
  `IProcessRunner` seam the codebase is tested through; `FFMedia.Media` (an empty shell until
  now) is instead realized as `IMediaAnalyzer`/`IFfmpegRunner` over `IProcessRunner` + pure
  `FfprobeParsing`/`FfmpegProgressParsing`. (2) **`ffprobe.exe` is not currently shipped** —
  it lives inside the *same* pinned, SHA-256-verified BtbN zip, so it's a second extraction,
  **no new download or hash**.
- **Verified:** `MergeDuplicate24` (my first icon pick) **does not exist** in `Wpf.Ui.dll`
  4.3.0 — checked the assembly; spec uses `VideoClipMultiple24`. No build/tests run: this
  change is documentation only, no code touched.
- **Docs updated:** SDD → **v0.13** (§3 stack, §4 diagram, §5 structure + dep rule, §7.1,
  §8 rewritten, §9 ffprobe, §10 `encode-speed.json`, §16 **GPL build is now load-bearing**
  since the merger re-encodes with x264/x265, §17 M7 row, §19 deferrals, Changelog);
  README (tech stack + roadmap); `THIRD-PARTY-NOTICES.md` (ffprobe under the same GPL build).
- **Next:** user reviews the spec → then `writing-plans` for the M7 PR 1 (engine) implementation
  plan. Delivered via branch `docs/m7-video-merger-design` → PR.

### 2026-07-10 — Post-v1 UI fixes round 2 (dark-mode page text, blank launch content)

- **Two bugs reported after installing v1:**
  1. **Dark-mode text still black** on page content ("YouTube Downloader" header, "Output:",
     "Container:", Settings labels, …). **Root cause:** the v0.12 fix set `FluentWindow.Foreground`,
     which themes only the chrome (title bar / nav pane) — WPF's `Frame` (which `NavigationView`
     hosts pages in) **isolates property-value inheritance**, so it never reaches page content.
     WPF-UI 4.3.0 ships **no implicit keyless `TextBlock` style** (only keyed ones like
     `BodyTextBlockStyle`), so plain `TextBlock`s fall back to WPF's default **black**. **Fix:**
     set `Foreground="{DynamicResource TextFillColorPrimaryBrush}"` on **each `Page` root**
     (`WelcomePage`, `DownloaderPage`, `HistoryPage`, `SettingsPage`) — page-local inheritance
     themes all plain text (a blanket implicit `TextBlock` style was rejected: it would also
     override button/combo template foregrounds, e.g. white-on-accent primary buttons).
  2. **Blank "main interface" on launch** until the user clicked a pane item. **Root cause:**
     `NavigationView` selects nothing by default and nothing navigated at startup; the
     purpose-built `WelcomePage` was never wired. **Fix:** registered `WelcomePage` in DI and
     navigate to it from `MainWindow` once `RootNavigation` is `Loaded`.
- **Verified:** Release build **0/0**, **189/189** unit tests pass. **Not verified (headless
  env):** the actual dark-mode text rendering and the WelcomePage landing — needs a user
  visual check. SDD → v0.12.2 (§13 + Changelog).
- **Next:** user confirms visually; delivered via branch `fix/dark-mode-text-and-default-page` → PR.

### 2026-07-08 — Docs: "personal project" scope note

- **Done:** added a note that FFMedia, though public, is developed primarily for the author's
  personal use and shipped as-is (no maintenance/support commitment). Placed a `> [!NOTE]`
  callout under the README intro + a bullet in the README Legal section, and a scope note in
  SDD §1. SDD → v0.12.1. No code change.
- **Next:** none pending for this; unchanged from prior (user's headed dry-run of the M6/UI work).

### 2026-07-08 — Post-v1 UI fixes (dark-mode text, footer icons, title bar)

- **Done:** three shell fixes reported after installing v1.0.0.
  1. **Dark-mode font was black** — page `TextBlock`s had no explicit `Foreground`, so they
     inherited WPF's default **black** (fine on light, invisible on dark). `MainWindow`
     (`FluentWindow`) now sets `Foreground="{DynamicResource TextFillColorPrimaryBrush}"`;
     inheritance themes all page text and buttons keep their own template foreground. This
     also makes the (previously black-on-dark) title text visible.
  2. **History/Settings icons missing** — swapped the raw-glyph `FontIcon`s for WPF-UI
     `SymbolIcon` (`SymbolRegular.History24`/`Settings24`), which use the bundled icon font
     (no dependency on an OS-installed Segoe icon font).
  3. **Title bar** — added the logo at top-left via `ui:TitleBar.Icon` + the "FFMedia"
     title; **removed the title-bar theme toggle** (theme already lives in Settings → Theme
     combo). Dropped `MainWindowViewModel`'s now-dead `ToggleThemeCommand` and its unused
     `ISettingsService`/`ThemeService` ctor deps.
- **Second round (same branch/PR):**
  4. **YouTube Downloader nav icon missing** — the tool icon was still a raw-glyph `FontIcon`
     (same unreliable path as the footer). Reinterpreted `ITool.IconGlyph` as a WPF-UI
     **`SymbolRegular` name** (`YouTubeDownloaderTool` → `"ArrowDownload24"`); the shell now
     `Enum.TryParse`s it into a `SymbolIcon` (fallback `Apps24`). Core stays UI-agnostic (still
     just a string).
  5. **Settings Save button removed** — settings now **auto-save** on change (`On<Property>Changed`
     → `Persist()`); theme applies live; **max concurrency** (read once at construction, §12)
     shows a **red "takes effect after you restart"** reminder when changed from the launch value.
     Folder box saves on focus-loss (dropped `UpdateSourceTrigger=PropertyChanged`).
  6. **History open-file/folder feedback** — `HistoryViewModel` gained `INotificationService`;
     a missing file/folder now raises a `Warning` notification (and, if only the file is gone,
     opens its parent folder), plus an `Error` notification if `Process.Start` throws.
- **Verified:** Release build **0/0**, **189/189** unit tests pass. **Not verified (headless
  env):** the actual dark-mode appearance, all icon rendering, title-bar layout, settings
  auto-save UX, and the History notifications — needs a user visual check. SDD → v0.12.
- **Next:** user confirms the fixes visually; delivered via branch
  `fix/ui-dark-theme-titlebar-icons` → PR #11.

### 2026-07-08 — Public-repo audit + licensing & disclaimers

- **Context:** repo was made public (to fix anonymous Velopack update checks), so audited
  for anything that shouldn't be exposed and for missing legal disclaimers.
- **Audit result (clean):** no secrets/credentials/keys in tracked files (only false
  positives like `CancellationToken`, built-in `secrets.GITHUB_TOKEN`); no machine paths
  (`C:\Users\…`) or PII in source; `.gitignore` correctly excludes binaries/logs/artifacts.
  Docs are professional (the "DRM/bypass" hits are appropriate non-goal disclaimers or the
  `-ExecutionPolicy Bypass` flag). Binaries are git-ignored, so the **repo** ships no GPL
  binary — only the **release installer** does.
- **Done:** added **`LICENSE`** (MIT, user-chosen) and **`THIRD-PARTY-NOTICES.md`** (yt-dlp
  Unlicense; bundled FFmpeg GPL-3.0 `win64-gpl` build with source links + trademark/
  non-affiliation notes; NuGet deps + licenses). Expanded README **License** + **Legal &
  disclaimer** sections (responsible use, no DRM circumvention, non-affiliation, no-warranty)
  and fixed the tech-stack (FFMpegCore is planned, not yet used). SDD §16 + Changelog → v0.11.
- **Advice pending user decision (asked, chose "advise me"):** keep the **GPL** ffmpeg build
  (current; supports x264/x265 re-encode incl. `PreciseCut`, GPL notice is easy to satisfy)
  vs switch to the **LGPL** build (lighter obligations, but loses GPL-only encoders). My
  recommendation: **keep GPL + comply via the notices file** unless minimizing GPL exposure
  matters more than re-encode support. `fetch-binaries.ps1` left unchanged.
- **Resolved:** user chose to **keep the GPL ffmpeg build** (comply via `THIRD-PARTY-NOTICES.md`);
  `fetch-binaries.ps1` unchanged. Note: the personal git-author email is in commit history
  (standard; only changeable going forward via a noreply address).

### 2026-07-08 — Fix: in-app "Update check failed" after first release

- **Symptom:** after installing v1.0.0, Settings → "Check for updates now" showed
  "Update check failed. See logs." **Root cause (from the Serilog file log at
  `%AppData%\FFMedia\logs`):** `Velopack.Sources.GithubSource.GetReleases` → **HTTP 404**.
  The repo `ChamHC-dev/ff-media` was **private**, but the update check runs **anonymously**
  (`VelopackUpdateService` uses `GithubSource(..., accessToken: null, ...)`); GitHub returns
  404 (not 403) to anonymous callers on a private repo. Not a code bug — the v1.0.0 release
  itself was complete (`RELEASES`, `releases.win.json`, full nupkg, Setup.exe all present).
- **Fix:** made the repo **public** (user-approved). Verified the exact chain the app walks
  is now anonymous-200: `GET /releases` → 200, and the `releases.win.json` asset → 200. A
  distributed desktop app can't ship a GitHub token safely (extractable from the `.exe`), so
  public is the correct distribution model. SDD §15 updated with this requirement.
- **Note:** the installed app is already on the latest stable (v1.0.0), so "Check for updates
  now" will now report **"You're up to date"** — to exercise the banner, publish a higher tag
  (e.g. `v1.0.1`). The `v0.9.0.0` **pre-release** is ignored by the stable channel.
- **Next:** unchanged — user's headed dry-run of M6 PR 2 (Binaries section, real `yt-dlp -U`,
  logo surfaces); publish `v1.0.1` when there's a change to ship to see the update loop.

### 2026-07-08 — M6 Ship v1 (PR 2: binary updates + app logo)

- **Done:** yt-dlp self-update + pinned binaries + app logo. Core gained `IProcessRunner`/
  `ProcessRunner` (the process seam, SDD §6) and `IBinaryUpdateService`/`BinaryUpdateService`
  (installed versions via `--version`/`-version`, `yt-dlp -U` self-update, and a GitHub
  latest-version check). A singleton `BinaryUpdateViewModel` drives a Settings **Binaries**
  section (yt-dlp + ffmpeg versions, "Update yt-dlp", "check yt-dlp on startup" toggle) and a
  fire-and-forget startup check that notifies (never auto-applies). `AppSettings` → schema
  **v3** (`CheckYtDlpForUpdatesOnStartup`, default true). `fetch-binaries.ps1` now pins
  yt-dlp **2026.07.04** and ffmpeg BtbN **autobuild-2026-07-07-13-44** and **verifies SHA-256**
  (throws on mismatch). `logo.png` moved to `assets/branding/`, converted to a committed
  multi-res `app.ico` (via `build/make-icon.ps1`), and wired as the exe/window/taskbar/
  installer icon + in-app branding (title bar, left of the theme toggle, + welcome page). **Verified:** Release build
  0/0, all **172/172** unit tests pass (`Category!=Integration`), pinned `fetch-binaries.ps1`
  runs and verifies clean. **Not verified (pending user dry-run):** headed GUI smoke of the
  Binaries section, the real `yt-dlp -U`, and the logo surfaces. SDD → v0.10.
- **Review fixes (whole-branch review before opening the PR):** the GitHub latest-version
  check now surfaces the remote tag only when **strictly newer** than installed — a new pure,
  unit-tested `YtDlpVersion.IsNewer` (component-wise compare of the dot date tags, tolerant of
  zero-padding skew) replaces the prior "any inequality" check that could nag forever on a
  locally-newer install; the Core `HttpClient` gained an explicit 10 s timeout; and the
  latest-check failure paths (HTTP error, malformed JSON, installed-is-newer) are now tested.
  Re-verified: Release build **0/0**, **189/189** unit tests pass. Reviewer's remaining notes
  (no `ProcessRunner` timeout, vestigial `AppSettings.Version`, `make-icon.ps1` path style)
  were triaged as out-of-scope Minors and left for later.
- **Decisions:** yt-dlp self-update via `yt-dlp -U`; ffmpeg has no self-update (rides app
  releases); startup check notifies only; both binaries pinned + hash-verified (ffmpeg hash
  computed once from the pinned zip); logo used everywhere. App-layer VMs verified by build +
  manual per the M5/M6 precedent.
- **Next:** user performs the headed dry-run; the public **v1.0.0** tag (machinery proven in
  PR 1) is user-initiated.

### 2026-07-07 — M6 Ship v1 (PR 1: packaging + app auto-update)

- **Done:** Velopack packaging + delta auto-update. Explicit `Program.Main` runs
  `VelopackApp.Build().Run()` before WPF (App.xaml switched to a `Page`, `<StartupObject>`
  set). Core `IUpdateService`/`AppUpdateInfo` realized in App by `VelopackUpdateService`
  (Velopack `UpdateManager` + GitHub `GithubSource`, stable channel; safe no-op when
  uninstalled/dev). Singleton `UpdateViewModel` drives a dismissible shell **update banner**
  (Update & restart / Later) and a Settings **"Check for updates now"** action + current-version
  display; a new `AppSettings.CheckForUpdatesOnStartup` (schema **v2**) gates a fire-and-forget
  startup check that never blocks/crashes launch. `build/pack.ps1` (publish self-contained +
  `vpk pack`, unsigned) + tag-gated `.github/workflows/release.yml` (`vpk upload github`).
  Velopack pinned at **1.2.0** (NuGet package + `vpk` CLI, matched versions).
  **Verified:** solution builds Release **0 warnings / 0 errors**, all **152/152** unit tests
  pass (`Category!=Integration`), and `build/pack.ps1` was run for real and produced an actual
  `FFMedia-win-Setup.exe` (~147 MB) + delta nupkg + `RELEASES` metadata locally — the pack
  machinery is proven end-to-end. `vpk`/`vpk upload github` flags were confirmed against the
  installed CLI's `--help` output. **Not verified (pending user dry-run):** the interactive
  install → pack 0.9.1 → banner appears → "Update & restart" → relaunch onto 0.9.1 loop, and a
  GUI smoke of the shell update banner and the Settings update section — this build environment
  is headless and can't drive a GUI, so these were reviewed by code/build inspection only, not
  exercised. SDD → v0.9.
- **Decisions:** update feed = GitHub Releases; UX = check-on-startup + manual (no silent
  installs); unsigned for v1 (SmartScreen accepted; `--signParams` seam left in `pack.ps1`);
  the real public **v1.0.0** tag is left to the user (machinery + local pack dry-run only, no
  tag pushed, no GitHub Actions release run performed). App-layer VMs
  (`UpdateViewModel`/`SettingsViewModel`) verified by build + manual per the M5 precedent
  (Tests doesn't reference the WinExe); only `AppSettings` migration is unit-tested.
- **Next:** M6 PR 2 — yt-dlp self-update (`IProcessRunner` + `IBinaryUpdateService`), binary
  version display in Settings, pinned `fetch-binaries.ps1` with hash checks. Before that: user
  performs the pending interactive dry-run (install → banner → update → relaunch) and GUI smoke
  of the banner/Settings controls; a whole-branch review runs before this PR is opened.

### 2026-07-06 — M5 Experience (PR 2: presets, history, notifications)

- **Done:** Presets — `IPresetService`/`PresetService` (Core, JSON-backed via
  `JsonStore<T>` at `presets.json`, `Changed` event) + module `PresetMapping`
  (serializes/deserializes `DownloadConfig` to an opaque payload string, tolerant of
  blank/malformed input) + `DownloaderViewModel` save/apply/delete preset commands +
  an inline Presets section (dropdown, Apply/Delete, "save as") on the Downloader page
  — no separate presets screen. History — `IHistoryService`/`HistoryService` (Core,
  JSON-backed at `history.json`, newest-first, `Changed` event) + a `DownloadManager`
  completion hook (two optional trailing ctor params, `IHistoryService?`/
  `INotificationService?`): `Completed` appends a `HistoryEntry` + notifies success,
  `Failed` notifies only (no history row), `Canceled` does neither; dispatched inside
  `RunAndTrackAsync` before the idle signal, wrapped in try/catch so a broken sink
  can't break the queue. A new **History** screen (footer nav, above Settings) shows a
  filterable list with per-row open file/open folder + a clear-history action.
  Notifications — `INotificationService` realized in the App layer as
  `SnackbarNotificationService`, wrapping WPF-UI's `ISnackbarService` via a
  `SnackbarPresenter` overlaying the shell (severity → Success/Caution/Danger/Info).
  SDD → v0.8, M5 marked complete.
- **Decisions:** re-download from history **deferred** — needs a cross-page seeding
  seam (`DownloaderViewModel` is DI-transient, so there's no existing channel for the
  History page to hand it a config) and a richer `HistoryEntry` that stores the
  serialized `DownloadConfig`, not just the `Format` label; failed jobs are notified
  but not written to history (only `Completed` rows persist); preset payload
  deserialization is tolerant (blank/malformed → `DownloadConfig.Default`); native
  Windows toast notifications stay deferred to M6 (in-app snackbar only, per PR 1's
  decision). Known follow-up (non-blocking): `HistoryViewModel` subscribes to
  `IHistoryService.Changed` with no unsubscribe and is DI-transient, so repeated page
  visits accumulate handlers — candidate fixes are a singleton VM or detaching on
  `Unloaded`.
- **Next:** M6 — Velopack installer + delta auto-update, yt-dlp/ffmpeg update flow,
  v1 release.

### 2026-07-06 — M5 Experience (PR 1: foundation)

- **Done:** Persistence foundation + settings + theming. `JsonStore<T>` (Core) does
  atomic temp-file writes and quarantines a corrupt file to `.bak` before returning a
  default. `AppSettings` (`Version`/`DefaultOutputFolder`/`MaxConcurrency`/`Theme`) +
  `ISettingsService`/`SettingsService` persist to `%AppData%\FFMedia\settings.json`;
  `AddFFMediaCore` gained a `dataDirectory` param and registers the service. App gained
  `ThemeService` (light/dark/system via WPF-UI `ApplicationThemeManager`), a **Settings**
  screen (footer nav) with folder/concurrency/theme, a title-bar theme toggle, and
  startup theme application. Wired into behavior: downloader output folder seeded from
  settings; `DownloadManager` concurrency cap read from settings at construction. SDD → v0.7.
- **Decisions:** history stored as JSON (resolves §19); notifications in-app only
  (Windows toast deferred to M6); concurrency applied at launch (live re-tuning deferred);
  App-layer VMs verified by build + manual run (Tests doesn't reference the WinExe; UI is
  thin per §14). Presets/history/notifications land in PR 2 (`feat/m5-presets-history`).
- **Next:** M5 PR 2 — presets (inline), history + screen, in-app notifications, and the
  `DownloadManager` completion hook.

### 2026-07-05 — M4 Processing

- **Done:** `ProcessingOptions` (`TrimRange?` Trim, `PreciseCut`, `EmbedSubtitles`,
  `SubtitleLanguage`, `EmbedMetadata`, `EmbedThumbnail`; default metadata+thumbnail ON,
  subs+trim off, language "en") added to `DownloadConfig.Processing`. Pure
  `OptionSetBuilder.ApplyProcessing` emits: trim → `--download-sections "*<start>-<end>"`
  (keyframe-fast), `PreciseCut` additionally emits `--force-keyframes-at-cuts`; subtitles
  **video-only** → `--write-subs --write-auto-subs --embed-subs --sub-langs <lang>`;
  `--embed-metadata`/`--embed-thumbnail` from the flags. Pure `TrimParsing` parses
  HH:MM:SS / MM:SS / seconds into a `TimeSpan`, producing a range only when both ends
  parse and End > Start. `DownloaderViewModel` gained processing selections (+ live
  `TrimHint` validation) that assemble `ProcessingOptions` per job; the page gained a
  "Processing" section (trim start/end + precise cut, embed subtitles + language, embed
  metadata/thumbnail). All processing flows per-job through the M3 queue. SDD synced to
  v0.6 (§7.3 processing flags, §8 trim-via-yt-dlp note, §17 M4 row).
- **Decisions:** precise-cut is a per-download toggle (not global); subtitles are
  video-only (ignored for audio downloads); metadata + thumbnail default ON; embedding a
  thumbnail is container-dependent — works for mp4/mkv/mp3/m4a, yt-dlp warns (but still
  proceeds) for webm/opus; trim uses yt-dlp's own `--download-sections` rather than a
  post-download `FFMedia.Media`/FFMpegCore pass — the FFMpegCore trim wrapper stays
  reserved for future tools that need frame-accurate cutting independent of yt-dlp.
- **Next:** M5 — settings, presets, history, notifications, dark/light theming.

### 2026-07-05 — M3 Queue

- **Done:** Download queue engine in `FFMedia.Tools.YouTubeDownloader`: `DownloadJob`
  (observable `Status`/`Progress`/`ProgressText`/`ErrorMessage`/`OutputPath` + per-job
  `CancellationTokenSource`), `JobStatus { Queued, Downloading, Processing, Completed,
  Canceled, Failed }`, `RetryPolicy` (transient-error classification + exponential
  backoff, default 3 attempts/1s), and `IDownloadManager`/`DownloadManager` (bounded
  concurrency via `SemaphoreSlim`, default cap 3; auto-start on enqueue; per-job cancel
  + cancel-all; clear-completed; failure isolation; `IdleAsync()` for deterministic
  tests). Added playlist/channel expansion (`IPlaylistProbe`/`YtDlpPlaylistProbe` +
  pure `PlaylistMapping`/`MediaEntry`) so a playlist URL becomes one job per entry at
  add-time. `DownloaderViewModel` restructured from single probe/download to
  "add to queue" with a bound `Jobs` list; the page shows the queue with per-job
  progress/cancel plus cancel-all/clear-completed. Trait-gated queue integration test
  added. SDD synced to v0.5 (§6 queue placement, §7.2 realized state machine, §12
  concurrency model, §17 M3 row, §19 resolutions).
- **Decisions:** auto-start on add (no separate "start" step); transient-only
  auto-retry with exponential backoff, permanent errors fail fast; probing/playlist
  expansion happens at add-time so `DownloadManager` stays a pure download engine (no
  `Fetching` state inside it); cancel-only for M3 (no pause/resume — stays a stretch
  goal per §19); concurrency cap = 3 is a constant this milestone (user-configurable
  deferred to M5); queue lives in the YouTube Downloader module, not `FFMedia.Core`
  (it orchestrates the module's own `IMediaProbe`/`IDownloadService`) — the generic
  bounded-concurrency shape can move to Core if a second tool needs it.
- **Next:** M4 — trim/clip, subtitles, and metadata + thumbnail embedding.

### 2026-07-05 — M2 Formats

- **Done:** Full format matrix — a pure, exhaustively-tested `OptionSetBuilder` maps a
  `DownloadConfig` to yt-dlp options: video MP4/MKV/WebM at a resolution cap, or audio-only
  MP3/WAV/M4A/Opus/FLAC with a bitrate (lossless ignores it). ViewModel exposes the
  selections; the page gained Video/Audio + format/quality dropdowns. SDD §7.3 finalized.
- **Changed:** dropped M1's `RecodeVideo` (re-encode) for `MergeOutputFormat` (mux); removed
  `DownloadOptions.Mp4`; `DownloadRequest` now carries a `DownloadConfig`. Audio bitrate uses
  `OptionSet.AddCustomOption("--audio-quality", …)` (typed `AudioQuality` is 0–10 VBR only).
- **Next:** M3 — download queue, bounded concurrency, playlist/channel support.

### 2026-07-05 — M1 fix: crash on Probe (missing binaries + no error isolation)

- **Bug:** Clicking **Probe** closed the app with no dialog/log. Root cause (found via
  systematic debugging + a reproduction test): (A) nothing copied `assets/binaries/*`
  into `FFMedia.App`'s output, so the resolved `yt-dlp.exe` path didn't exist, and
  (B) the resulting `Win32Exception` from `Process.Start` was unhandled — services
  didn't catch it, and no global exception handler existed (SDD §11 unimplemented).
- **Fix:** (A) App/Tests csproj now copy `assets/binaries/*.exe` to output; (B)
  `YtDlpMediaProbe`/`YtDlpDownloadService` catch exceptions → `Result.Failure`
  (friendly "run fetch-binaries" message for missing binaries; cancellation still
  propagates), and `App` wires `DispatcherUnhandledException` +
  `AppDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException` → Serilog
  `Fatal` + dialog. Added `YtDlpServiceErrorTests`. Verified: build clean, 20 unit
  tests pass, **both integration tests (real probe + real MP4 download) pass**, app
  boots with no Fatal.
- **Next:** M2 — full format matrix (unchanged).

### 2026-07-05 — M1 Vertical Slice

- **Done:** YouTube Downloader tool end-to-end — paste URL → probe (`IMediaProbe`) →
  download single MP4 with live progress + cancel (`IDownloadService`) via
  YoutubeDLSharp; `DownloaderViewModel` unit-tested with fakes; tool page + nav
  wiring so it appears in the shell's `NavigationView`; trait-gated yt-dlp
  integration test (excluded in CI); `Result<T>` + `IToolPage` added to Core.
  Build green, 18 unit tests pass. SDD synced to v0.3.
- **Changed:** `FFMedia.Tools.YouTubeDownloader` + `FFMedia.Tests` retargeted to
  `net9.0-windows` (UseWPF) so ViewModels are unit-testable headlessly; CI test
  step filters `Category!=Integration`.
- **Next:** M2 — full format matrix (video containers + audio-only
  wav/mp3/m4a/opus/flac + quality/resolution); `OptionSet` builder fully tested.

### 2026-07-04 — M0 Foundation

- **Done:** Solution skeleton (Core/Media/Tools/App/Tests); Core `ITool`/`IToolRegistry`,
  `IBinaryProvider`, `AddFFMediaCore` (all unit-tested); WPF-UI Fluent shell with Generic
  Host + Serilog + `NavigationView` seam; `build/fetch-binaries.ps1`; GitHub Actions CI.
- **Changed:** `ITool.Icon` → `string IconGlyph` (keeps Core UI-agnostic); assertions use
  plain xUnit `Assert` (FluentAssertions v8 is paid) — SDD updated to v0.2.
- **Next:** M1 — vertical slice: URL → probe → download single MP4 with progress + cancel.

### 2026-07-04 — Add branch-per-task / PR-review workflow rule

- **Done:** Added standing Rule 3 — always branch off `main` per task and deliver
  via a PR for user review (never commit task work to `main`, never self-merge).
  This change itself delivered on branch `docs/pr-workflow-rule` via PR.
- **Note:** `gh` CLI not yet installed, so PRs are teed up via a GitHub compare
  link rather than opened automatically. Install `gh` to let me open PRs directly.
- **Next:** M0 (Foundation) implementation plan — on its own branch + PR.

### 2026-07-04 — Project bootstrap & design

- **Done:**
  - Initialized git repo, wired `origin` → `github.com/ChamHC-dev/ff-media.git`,
    branch `main` (pushed).
  - Ran research (YoutubeDLSharp, FFMpegCore vs Xabe, WPF vs WinUI 3, Velopack).
  - Brainstormed & locked v1 decisions: WPF + WPF-UI / .NET 9, bundled binaries,
    full-featured downloader, modular shell.
  - Wrote **`SDD.md`** (single source of truth): architecture, stack, downloader
    design, testing strategy, milestones M0–M7.
  - Added brainstorming spec record (`docs/superpowers/specs/`), `README.md`,
    `.gitignore`.
  - Created this `CLAUDE.md` with standing rules + progress log.
- **State:** Design phase complete. No code/solution scaffolded yet.
- **Next:** Turn **Milestone M0 (Foundation)** into a detailed implementation plan,
  then scaffold the solution (shell + Core + DI + binary management).
