# FFMedia — Design Spec (brainstorming record)

> **Date:** 2026-07-04
>
> **Canonical design lives in [`/SDD.md`](../../../SDD.md).** This file is the
> brainstorming record only; the SDD is the single source of truth. If the two
> ever diverge, the SDD wins.

## Summary of decisions

- **What:** Windows desktop **all-in-one media toolbox**. v1 tool = **YouTube
  Downloader**; more tools (video standardize/merge, …) come later — architecture
  is modular from day one.
- **Key reality:** FFmpeg can't download from YouTube; **yt-dlp** extracts/downloads,
  **ffmpeg** transcodes/muxes. FFMedia orchestrates both.
- **Stack:** C# / **.NET 9**, **WPF + WPF-UI**, CommunityToolkit.Mvvm,
  Microsoft.Extensions.Hosting (DI), **YoutubeDLSharp**, **FFMpegCore** (MIT),
  Serilog, **Velopack** (installer + auto-update), xUnit.
- **Binaries:** `yt-dlp.exe` + `ffmpeg.exe` **bundled** in the installer
  (`assets/binaries/`); yt-dlp self-updates in-app.
- **v1 scope:** full-featured downloader — queue, concurrency, playlists, formats
  (mp4/mkv + wav/mp3/m4a/opus/flac), trim/clip, subtitles, metadata+thumbnail embed,
  settings, presets, history, notifications, theming.
- **Architecture:** app shell (`FFMedia.App`) hosts `ITool` modules discovered via
  DI; UI-agnostic `FFMedia.Core` holds all testable orchestration.
- **Milestones:** M0 Foundation → M1 Vertical slice → M2 Formats → M3 Queue →
  M4 Processing → M5 Experience → M6 Ship v1 → M7 (future) second module.

See [`/SDD.md`](../../../SDD.md) for the full design.
