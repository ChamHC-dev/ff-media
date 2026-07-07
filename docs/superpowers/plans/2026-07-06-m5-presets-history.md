# M5 Presets / History / Notifications Implementation Plan (PR 2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete M5 by adding download **presets** (inline on the Downloader page), a persisted **history** with its own screen, in-app **notifications** (WPF-UI Snackbar), and the `DownloadManager` completion hook that feeds history + notifications.

**Architecture:** New UI-agnostic Core services (`IHistoryService`, `IPresetService`, `INotificationService` + `Notification`) persisted via the existing `JsonStore<T>` under `%AppData%\FFMedia`. The YouTube Downloader module owns `DownloadConfig`↔payload serialization (`PresetMapping`) and its `DownloadManager` fires history/notification side effects at the single terminal chokepoint (spec "Approach A"). `FFMedia.App` provides the WPF-UI `SnackbarNotificationService`, the History page/VM, and the inline preset UI. Dependencies point inward to Core; Core references no UI framework.

**Tech Stack:** C# / .NET 9, WPF + WPF-UI 4.3.0, CommunityToolkit.Mvvm 8.4.2 (`[ObservableProperty]`/`[RelayCommand]` source generators), Microsoft.Extensions.DependencyInjection/Hosting, System.Text.Json, xUnit (plain `Assert`).

## Global Constraints

- **Branch/PR:** All work on `feat/m5-presets-history` (already created off the merged `main`). Deliver via a PR for user review; never self-merge (CLAUDE.md Rule 3).
- **Core is UI-agnostic** (`FFMedia.Core`, `net9.0`, `TreatWarningsAsErrors=true`): no WPF/WPF-UI references; code must be **warning-clean** or the build fails.
- **Solution builds with 0 warnings, 0 errors** (PR 1 gate). Module + App are `Nullable=enable`, `ImplicitUsings=enable`; avoid nullable warnings (use `null!` where a WPF-UI API takes a non-nullable reference we intend to pass null to).
- **Tests use plain xUnit `Assert`** (no FluentAssertions — it is paid).
- **App-layer ViewModels are not unit-tested** — `FFMedia.Tests` does not reference the `FFMedia.App` WinExe (per SDD §14 / PR 1). `SettingsViewModel`/`HistoryViewModel`/`SnackbarNotificationService` are verified by **build + manual smoke**. Module ViewModels (`DownloaderViewModel`) and Core/module services **are** unit-tested.
- **Persistence files** live under the data directory passed to `AddFFMediaCore` (App uses `%AppData%\FFMedia`): `presets.json`, `history.json` (alongside PR 1's `settings.json`).
- **Commit trailer:** end every commit message with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- **Scope note (deliberate deviation from spec §6.1):** History row-actions ship as **Open file / Open folder / Clear**. **Re-download is deferred** — it needs a cross-page seeding seam (the Downloader VM is transient) and a `HistoryEntry` stores only a format *label*, not a reconstructable `DownloadConfig`. Recorded as an SDD open item + called out in the PR body for the user to decide.

---

### Task 1: Core notifications contract

**Files:**
- Create: `src/FFMedia.Core/Notifications/NotificationSeverity.cs`
- Create: `src/FFMedia.Core/Notifications/Notification.cs`
- Create: `src/FFMedia.Core/Notifications/INotificationService.cs`
- Test: `src/FFMedia.Tests/Notifications/NotificationTests.cs`

**Interfaces:**
- Produces:
  - `enum FFMedia.Core.Notifications.NotificationSeverity { Info, Success, Warning, Error }`
  - `sealed record Notification(string Title, string Message, NotificationSeverity Severity)`
  - `interface INotificationService { void Notify(Notification notification); }`

- [ ] **Step 1: Write the failing test**

`src/FFMedia.Tests/Notifications/NotificationTests.cs`:

```csharp
using FFMedia.Core.Notifications;
using Xunit;

namespace FFMedia.Tests.Notifications;

public class NotificationTests
{
    [Fact]
    public void Notification_CarriesTitleMessageSeverity()
    {
        var n = new Notification("Done", "\"Clip\" finished.", NotificationSeverity.Success);

        Assert.Equal("Done", n.Title);
        Assert.Equal("\"Clip\" finished.", n.Message);
        Assert.Equal(NotificationSeverity.Success, n.Severity);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/FFMedia.Tests --filter FullyQualifiedName~NotificationTests`
Expected: FAIL — compile error, `Notification`/`NotificationSeverity` not defined.

- [ ] **Step 3: Write minimal implementation**

`src/FFMedia.Core/Notifications/NotificationSeverity.cs`:

```csharp
namespace FFMedia.Core.Notifications;

/// <summary>How prominently a notification should be surfaced.</summary>
public enum NotificationSeverity
{
    Info,
    Success,
    Warning,
    Error,
}
```

`src/FFMedia.Core/Notifications/Notification.cs`:

```csharp
namespace FFMedia.Core.Notifications;

/// <summary>A user-facing message raised by the app (e.g. a download finished or failed).</summary>
public sealed record Notification(string Title, string Message, NotificationSeverity Severity);
```

`src/FFMedia.Core/Notifications/INotificationService.cs`:

```csharp
namespace FFMedia.Core.Notifications;

/// <summary>Surfaces <see cref="Notification"/>s to the user. UI implementation lives in the app layer.</summary>
public interface INotificationService
{
    /// <summary>Show a notification. Implementations must be safe to call from any thread.</summary>
    void Notify(Notification notification);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/FFMedia.Tests --filter FullyQualifiedName~NotificationTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/FFMedia.Core/Notifications src/FFMedia.Tests/Notifications
git commit -m "feat(core): add notification contract (Notification + INotificationService)"
```

---

### Task 2: Core history service

**Files:**
- Create: `src/FFMedia.Core/History/HistoryEntry.cs`
- Create: `src/FFMedia.Core/History/HistoryDocument.cs`
- Create: `src/FFMedia.Core/History/IHistoryService.cs`
- Create: `src/FFMedia.Core/History/HistoryService.cs`
- Modify: `src/FFMedia.Core/CoreServiceCollectionExtensions.cs`
- Test: `src/FFMedia.Tests/History/HistoryServiceTests.cs`
- Test: `src/FFMedia.Tests/CoreServiceCollectionExtensionsTests.cs` (add one test)

**Interfaces:**
- Consumes: `JsonStore<T>` (`src/FFMedia.Core/Persistence/JsonStore.cs`, ctor `(string filePath, ILogger logger)`, `T Load(Func<T>)`, `void Save(T)`).
- Produces:
  - `sealed record HistoryEntry(string Title, string Url, string? OutputPath, string Format, DateTimeOffset Timestamp, string Status)`
  - `sealed record HistoryDocument(int Version, IReadOnlyList<HistoryEntry> Entries)` with `static HistoryDocument Empty`
  - `interface IHistoryService { IReadOnlyList<HistoryEntry> Query(); void Append(HistoryEntry entry); void Clear(); event EventHandler? Changed; }`
  - `sealed class HistoryService(string dataDirectory, ILogger<HistoryService> logger) : IHistoryService` — writes `history.json`, newest entry first.
  - `AddFFMediaCore` now also registers `IHistoryService`.

- [ ] **Step 1: Write the failing test**

`src/FFMedia.Tests/History/HistoryServiceTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using FFMedia.Core.History;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FFMedia.Tests.History;

public class HistoryServiceTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ffmedia-hist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static HistoryEntry Entry(string title) =>
        new(title, "https://u/" + title, @"C:\out\" + title + ".mp4", "Mp4 P1080", DateTimeOffset.Now, "Completed");

    [Fact]
    public void Append_ThenQuery_ReturnsEntryNewestFirst()
    {
        var svc = new HistoryService(TempDir(), NullLogger<HistoryService>.Instance);

        svc.Append(Entry("A"));
        svc.Append(Entry("B"));

        Assert.Equal(new[] { "B", "A" }, svc.Query().Select(e => e.Title));
    }

    [Fact]
    public void Append_RaisesChanged()
    {
        var svc = new HistoryService(TempDir(), NullLogger<HistoryService>.Instance);
        var raised = 0;
        svc.Changed += (_, _) => raised++;

        svc.Append(Entry("A"));

        Assert.Equal(1, raised);
    }

    [Fact]
    public void History_PersistsAcrossReload()
    {
        var dir = TempDir();
        new HistoryService(dir, NullLogger<HistoryService>.Instance).Append(Entry("A"));

        var reloaded = new HistoryService(dir, NullLogger<HistoryService>.Instance);

        Assert.Equal("A", Assert.Single(reloaded.Query()).Title);
    }

    [Fact]
    public void Clear_EmptiesAndPersists()
    {
        var dir = TempDir();
        var svc = new HistoryService(dir, NullLogger<HistoryService>.Instance);
        svc.Append(Entry("A"));

        svc.Clear();

        Assert.Empty(svc.Query());
        Assert.Empty(new HistoryService(dir, NullLogger<HistoryService>.Instance).Query());
    }

    [Fact]
    public void MissingFile_QueryIsEmpty()
    {
        var svc = new HistoryService(TempDir(), NullLogger<HistoryService>.Instance);

        Assert.Empty(svc.Query());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/FFMedia.Tests --filter FullyQualifiedName~HistoryServiceTests`
Expected: FAIL — `HistoryService`/`HistoryEntry` not defined.

- [ ] **Step 3: Write minimal implementation**

`src/FFMedia.Core/History/HistoryEntry.cs`:

```csharp
namespace FFMedia.Core.History;

/// <summary>One finished download recorded for the history screen.</summary>
public sealed record HistoryEntry(
    string Title,
    string Url,
    string? OutputPath,
    string Format,
    DateTimeOffset Timestamp,
    string Status);
```

`src/FFMedia.Core/History/HistoryDocument.cs`:

```csharp
namespace FFMedia.Core.History;

/// <summary>Versioned on-disk shape for history (the <c>Version</c> keeps a future SQLite migration clean).</summary>
public sealed record HistoryDocument(int Version, IReadOnlyList<HistoryEntry> Entries)
{
    public static HistoryDocument Empty { get; } = new(1, Array.Empty<HistoryEntry>());
}
```

`src/FFMedia.Core/History/IHistoryService.cs`:

```csharp
namespace FFMedia.Core.History;

/// <summary>Persisted record of finished downloads. Newest entry first.</summary>
public interface IHistoryService
{
    /// <summary>All recorded entries, newest first.</summary>
    IReadOnlyList<HistoryEntry> Query();

    /// <summary>Append an entry and persist.</summary>
    void Append(HistoryEntry entry);

    /// <summary>Remove all entries and persist.</summary>
    void Clear();

    /// <summary>Raised after the history changes (append or clear).</summary>
    event EventHandler? Changed;
}
```

`src/FFMedia.Core/History/HistoryService.cs`:

```csharp
using System.IO;
using FFMedia.Core.Persistence;
using Microsoft.Extensions.Logging;

namespace FFMedia.Core.History;

/// <summary>JSON-file-backed <see cref="IHistoryService"/> (history.json under the data directory).</summary>
public sealed class HistoryService : IHistoryService
{
    private readonly JsonStore<HistoryDocument> _store;
    private HistoryDocument _document;

    public HistoryService(string dataDirectory, ILogger<HistoryService> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(logger);
        _store = new JsonStore<HistoryDocument>(Path.Combine(dataDirectory, "history.json"), logger);
        _document = _store.Load(() => HistoryDocument.Empty);
    }

    public IReadOnlyList<HistoryEntry> Query() => _document.Entries;

    public void Append(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var entries = new List<HistoryEntry>(_document.Entries.Count + 1) { entry };
        entries.AddRange(_document.Entries); // newest first
        _document = _document with { Entries = entries };
        _store.Save(_document);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _document = _document with { Entries = Array.Empty<HistoryEntry>() };
        _store.Save(_document);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;
}
```

Modify `src/FFMedia.Core/CoreServiceCollectionExtensions.cs` — add `using FFMedia.Core.History;` at the top and register the service inside `AddFFMediaCore`, right after the `ISettingsService` registration:

```csharp
        services.AddSingleton<IHistoryService>(sp => new HistoryService(
            dataDirectory,
            sp.GetService<ILogger<HistoryService>>() ?? NullLogger<HistoryService>.Instance));
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/FFMedia.Tests --filter FullyQualifiedName~HistoryServiceTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Add + run the DI resolution test**

Append to `src/FFMedia.Tests/CoreServiceCollectionExtensionsTests.cs` (inside the class):

```csharp
    [Fact]
    public void AddFFMediaCore_ResolvesHistoryService()
    {
        var provider = new ServiceCollection()
            .AddFFMediaCore(binariesDirectory: Path.GetTempPath(), dataDirectory: Path.GetTempPath())
            .BuildServiceProvider();

        var history = provider.GetRequiredService<FFMedia.Core.History.IHistoryService>();

        Assert.Empty(history.Query());
    }
```

Run: `dotnet test src/FFMedia.Tests --filter FullyQualifiedName~CoreServiceCollectionExtensionsTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/FFMedia.Core/History src/FFMedia.Core/CoreServiceCollectionExtensions.cs src/FFMedia.Tests/History src/FFMedia.Tests/CoreServiceCollectionExtensionsTests.cs
git commit -m "feat(core): add JSON-backed download history service"
```

---

### Task 3: Core preset service

**Files:**
- Create: `src/FFMedia.Core/Presets/Preset.cs`
- Create: `src/FFMedia.Core/Presets/PresetDocument.cs`
- Create: `src/FFMedia.Core/Presets/IPresetService.cs`
- Create: `src/FFMedia.Core/Presets/PresetService.cs`
- Modify: `src/FFMedia.Core/CoreServiceCollectionExtensions.cs`
- Test: `src/FFMedia.Tests/Presets/PresetServiceTests.cs`
- Test: `src/FFMedia.Tests/CoreServiceCollectionExtensionsTests.cs` (add one test)

**Interfaces:**
- Consumes: `JsonStore<T>`.
- Produces:
  - `sealed record Preset(string Name, string Payload)` — `Payload` is an opaque serialized config string (Core stays config-agnostic).
  - `sealed record PresetDocument(int Version, IReadOnlyList<Preset> Presets)` with `static PresetDocument Empty`
  - `interface IPresetService { IReadOnlyList<Preset> List(); void Save(Preset preset); void Delete(string name); event EventHandler? Changed; }` — `Save` upserts by `Name` (ordinal, case-insensitive).
  - `sealed class PresetService(string dataDirectory, ILogger<PresetService> logger) : IPresetService` — writes `presets.json`.
  - `AddFFMediaCore` now also registers `IPresetService`.

- [ ] **Step 1: Write the failing test**

`src/FFMedia.Tests/Presets/PresetServiceTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using FFMedia.Core.Presets;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FFMedia.Tests.Presets;

public class PresetServiceTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ffmedia-preset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Save_ThenList_ReturnsPreset()
    {
        var svc = new PresetService(TempDir(), NullLogger<PresetService>.Instance);

        svc.Save(new Preset("1080p MP4", "{payload}"));

        var p = Assert.Single(svc.List());
        Assert.Equal("1080p MP4", p.Name);
        Assert.Equal("{payload}", p.Payload);
    }

    [Fact]
    public void Save_SameName_UpsertsInPlace()
    {
        var svc = new PresetService(TempDir(), NullLogger<PresetService>.Instance);
        svc.Save(new Preset("Fast", "old"));

        svc.Save(new Preset("Fast", "new"));

        var p = Assert.Single(svc.List());
        Assert.Equal("new", p.Payload);
    }

    [Fact]
    public void Delete_RemovesByName()
    {
        var svc = new PresetService(TempDir(), NullLogger<PresetService>.Instance);
        svc.Save(new Preset("Keep", "k"));
        svc.Save(new Preset("Drop", "d"));

        svc.Delete("Drop");

        Assert.Equal("Keep", Assert.Single(svc.List()).Name);
    }

    [Fact]
    public void Presets_PersistAcrossReload()
    {
        var dir = TempDir();
        new PresetService(dir, NullLogger<PresetService>.Instance).Save(new Preset("P", "x"));

        var reloaded = new PresetService(dir, NullLogger<PresetService>.Instance);

        Assert.Equal("P", Assert.Single(reloaded.List()).Name);
    }

    [Fact]
    public void Save_RaisesChanged()
    {
        var svc = new PresetService(TempDir(), NullLogger<PresetService>.Instance);
        var raised = 0;
        svc.Changed += (_, _) => raised++;

        svc.Save(new Preset("P", "x"));

        Assert.Equal(1, raised);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/FFMedia.Tests --filter FullyQualifiedName~PresetServiceTests`
Expected: FAIL — types not defined.

- [ ] **Step 3: Write minimal implementation**

`src/FFMedia.Core/Presets/Preset.cs`:

```csharp
namespace FFMedia.Core.Presets;

/// <summary>A named, reusable download configuration. <see cref="Payload"/> is an opaque
/// serialized config owned by the tool module — Core stays config-agnostic.</summary>
public sealed record Preset(string Name, string Payload);
```

`src/FFMedia.Core/Presets/PresetDocument.cs`:

```csharp
namespace FFMedia.Core.Presets;

/// <summary>Versioned on-disk shape for presets.</summary>
public sealed record PresetDocument(int Version, IReadOnlyList<Preset> Presets)
{
    public static PresetDocument Empty { get; } = new(1, Array.Empty<Preset>());
}
```

`src/FFMedia.Core/Presets/IPresetService.cs`:

```csharp
namespace FFMedia.Core.Presets;

/// <summary>Persisted, named download presets. Config-agnostic — payloads are opaque strings.</summary>
public interface IPresetService
{
    /// <summary>All saved presets.</summary>
    IReadOnlyList<Preset> List();

    /// <summary>Add or replace a preset (matched by <see cref="Preset.Name"/>) and persist.</summary>
    void Save(Preset preset);

    /// <summary>Remove the preset with the given name (no-op if absent) and persist.</summary>
    void Delete(string name);

    /// <summary>Raised after the preset set changes (save or delete).</summary>
    event EventHandler? Changed;
}
```

`src/FFMedia.Core/Presets/PresetService.cs`:

```csharp
using System.IO;
using FFMedia.Core.Persistence;
using Microsoft.Extensions.Logging;

namespace FFMedia.Core.Presets;

/// <summary>JSON-file-backed <see cref="IPresetService"/> (presets.json under the data directory).</summary>
public sealed class PresetService : IPresetService
{
    private readonly JsonStore<PresetDocument> _store;
    private PresetDocument _document;

    public PresetService(string dataDirectory, ILogger<PresetService> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(logger);
        _store = new JsonStore<PresetDocument>(Path.Combine(dataDirectory, "presets.json"), logger);
        _document = _store.Load(() => PresetDocument.Empty);
    }

    public IReadOnlyList<Preset> List() => _document.Presets;

    public void Save(Preset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var presets = _document.Presets
            .Where(p => !string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase))
            .Append(preset)
            .ToList();
        Commit(presets);
    }

    public void Delete(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var presets = _document.Presets
            .Where(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Commit(presets);
    }

    private void Commit(IReadOnlyList<Preset> presets)
    {
        _document = _document with { Presets = presets };
        _store.Save(_document);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;
}
```

> Note: `Where`/`Append`/`ToList` require `System.Linq`, which is covered by ImplicitUsings.

Modify `src/FFMedia.Core/CoreServiceCollectionExtensions.cs` — add `using FFMedia.Core.Presets;` and register after the `IHistoryService` registration:

```csharp
        services.AddSingleton<IPresetService>(sp => new PresetService(
            dataDirectory,
            sp.GetService<ILogger<PresetService>>() ?? NullLogger<PresetService>.Instance));
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/FFMedia.Tests --filter FullyQualifiedName~PresetServiceTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Add + run the DI resolution test**

Append to `src/FFMedia.Tests/CoreServiceCollectionExtensionsTests.cs`:

```csharp
    [Fact]
    public void AddFFMediaCore_ResolvesPresetService()
    {
        var provider = new ServiceCollection()
            .AddFFMediaCore(binariesDirectory: Path.GetTempPath(), dataDirectory: Path.GetTempPath())
            .BuildServiceProvider();

        var presets = provider.GetRequiredService<FFMedia.Core.Presets.IPresetService>();

        Assert.Empty(presets.List());
    }
```

Run: `dotnet test src/FFMedia.Tests --filter FullyQualifiedName~CoreServiceCollectionExtensionsTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/FFMedia.Core/Presets src/FFMedia.Core/CoreServiceCollectionExtensions.cs src/FFMedia.Tests/Presets src/FFMedia.Tests/CoreServiceCollectionExtensionsTests.cs
git commit -m "feat(core): add JSON-backed preset service"
```

---

### Task 4: Module `PresetMapping` (DownloadConfig ↔ payload)

**Files:**
- Create: `src/FFMedia.Tools.YouTubeDownloader/Services/PresetMapping.cs`
- Test: `src/FFMedia.Tests/YouTubeDownloader/PresetMappingTests.cs`

**Interfaces:**
- Consumes: `DownloadConfig` (`src/FFMedia.Tools.YouTubeDownloader/Models/DownloadConfig.cs`), `DownloadConfig.Default`.
- Produces:
  - `static class PresetMapping` with `static string Serialize(DownloadConfig config)` and `static DownloadConfig Deserialize(string payload)` (tolerant: blank or malformed payload → `DownloadConfig.Default`).

- [ ] **Step 1: Write the failing test**

`src/FFMedia.Tests/YouTubeDownloader/PresetMappingTests.cs`:

```csharp
using System;
using FFMedia.Tools.YouTubeDownloader.Models;
using FFMedia.Tools.YouTubeDownloader.Services;
using Xunit;

namespace FFMedia.Tests.YouTubeDownloader;

public class PresetMappingTests
{
    [Fact]
    public void RoundTrip_PreservesConfig()
    {
        var config = new DownloadConfig(
            OutputKind.Audio, VideoContainer.Mkv, VideoResolution.P720,
            AudioFormat.Opus, AudioBitrate.K256,
            new ProcessingOptions(
                new TrimRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)),
                PreciseCut: true, EmbedSubtitles: true, SubtitleLanguage: "es",
                EmbedMetadata: false, EmbedThumbnail: false));

        var back = PresetMapping.Deserialize(PresetMapping.Serialize(config));

        Assert.Equal(config, back);
    }

    [Fact]
    public void Deserialize_Blank_ReturnsDefault()
    {
        Assert.Equal(DownloadConfig.Default, PresetMapping.Deserialize("   "));
    }

    [Fact]
    public void Deserialize_Malformed_ReturnsDefault()
    {
        Assert.Equal(DownloadConfig.Default, PresetMapping.Deserialize("{ not valid json "));
    }

    [Fact]
    public void Deserialize_PartialPayload_DoesNotThrow()
    {
        // Missing fields fall back to defaults rather than throwing (tolerant to older shapes).
        var result = PresetMapping.Deserialize("{\"Kind\":\"Audio\"}");

        Assert.Equal(OutputKind.Audio, result.Kind);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/FFMedia.Tests --filter FullyQualifiedName~PresetMappingTests`
Expected: FAIL — `PresetMapping` not defined.

- [ ] **Step 3: Write minimal implementation**

`src/FFMedia.Tools.YouTubeDownloader/Services/PresetMapping.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using FFMedia.Tools.YouTubeDownloader.Models;

namespace FFMedia.Tools.YouTubeDownloader.Services;

/// <summary>Serializes a <see cref="DownloadConfig"/> to/from a preset payload string.
/// Deserialization is tolerant: blank or malformed input yields <see cref="DownloadConfig.Default"/>.</summary>
public static class PresetMapping
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(DownloadConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return JsonSerializer.Serialize(config, Options);
    }

    public static DownloadConfig Deserialize(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return DownloadConfig.Default;
        }

        try
        {
            return JsonSerializer.Deserialize<DownloadConfig>(payload, Options) ?? DownloadConfig.Default;
        }
        catch (JsonException)
        {
            return DownloadConfig.Default;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/FFMedia.Tests --filter FullyQualifiedName~PresetMappingTests`
Expected: PASS (4 tests).

> If `Deserialize_PartialPayload_DoesNotThrow` fails because System.Text.Json throws on the missing
> non-nullable `Processing` sub-object, wrap the body's `catch (JsonException)` to also cover it (it
> already does) — the test asserts no throw, which the try/catch guarantees. Keep the assertion on
> `Kind` only.

- [ ] **Step 5: Commit**

```bash
git add src/FFMedia.Tools.YouTubeDownloader/Services/PresetMapping.cs src/FFMedia.Tests/YouTubeDownloader/PresetMappingTests.cs
git commit -m "feat(youtube): add PresetMapping for DownloadConfig payloads"
```

---

### Task 5: DownloadManager completion hook

**Files:**
- Modify: `src/FFMedia.Tools.YouTubeDownloader/Services/DownloadManager.cs`
- Modify: `src/FFMedia.Tools.YouTubeDownloader/ServiceCollectionExtensions.cs`
- Test: `src/FFMedia.Tests/YouTubeDownloader/DownloadManagerCompletionTests.cs`

**Interfaces:**
- Consumes: `IHistoryService` + `HistoryEntry` (Core, Task 2), `INotificationService` + `Notification` + `NotificationSeverity` (Core, Task 1), existing `DownloadJob`, `DownloadConfig`, `JobStatus`.
- Produces:
  - `DownloadManager` ctor gains two **optional trailing** params:
    `DownloadManager(IDownloadService download, RetryPolicy policy, int maxConcurrency = 3, IHistoryService? history = null, INotificationService? notifications = null)`.
  - On a job reaching a terminal status: `Completed` → append a `HistoryEntry` + success `Notification`; `Failed` → error `Notification` (no history row); `Canceled` → neither.
- Note: existing `DownloadManagerTests` construct the manager without the new params — they must keep compiling and passing (defaults are null).

- [ ] **Step 1: Write the failing test**

`src/FFMedia.Tests/YouTubeDownloader/DownloadManagerCompletionTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FFMedia.Core.History;
using FFMedia.Core.Notifications;
using FFMedia.Core.Results;
using FFMedia.Tools.YouTubeDownloader.Models;
using FFMedia.Tools.YouTubeDownloader.Services;
using Xunit;

namespace FFMedia.Tests.YouTubeDownloader;

public class DownloadManagerCompletionTests
{
    private static RetryPolicy FastPolicy(int attempts = 3) => new(attempts, TimeSpan.Zero);
    private static DownloadJob Job() => new("https://x", "Clip", DownloadConfig.Default, @"C:\out");

    private sealed class FakeHistory : IHistoryService
    {
        public List<HistoryEntry> Appended { get; } = new();
        public IReadOnlyList<HistoryEntry> Query() => Appended;
        public void Append(HistoryEntry entry) => Appended.Add(entry);
        public void Clear() => Appended.Clear();
        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed class FakeNotifications : INotificationService
    {
        public List<Notification> Sent { get; } = new();
        public void Notify(Notification notification) => Sent.Add(notification);
    }

    private sealed class ImmediateDownload : IDownloadService
    {
        public Result<string> Result = Result<string>.Success(@"C:\out\Clip.mp4");
        public Task<Result<string>> DownloadAsync(DownloadRequest r, IProgress<DownloadUpdate> p, CancellationToken ct)
            => Task.FromResult(Result);
    }

    private sealed class GatedDownload : IDownloadService
    {
        private readonly Task _gate;
        private readonly Action _onEnter;
        public GatedDownload(Task gate, Action onEnter) { _gate = gate; _onEnter = onEnter; }
        public async Task<Result<string>> DownloadAsync(DownloadRequest r, IProgress<DownloadUpdate> p, CancellationToken ct)
        {
            _onEnter();
            await _gate.WaitAsync(ct);
            return Result<string>.Success(@"C:\out\Clip.mp4");
        }
    }

    [Fact]
    public async Task Completed_AppendsHistoryAndNotifiesSuccess()
    {
        var history = new FakeHistory();
        var notifications = new FakeNotifications();
        var mgr = new DownloadManager(new ImmediateDownload(), FastPolicy(), 3, history, notifications);

        var job = mgr.Enqueue(Job());
        await mgr.IdleAsync();

        var entry = Assert.Single(history.Appended);
        Assert.Equal("Clip", entry.Title);
        Assert.Equal("https://x", entry.Url);
        Assert.Equal(@"C:\out\Clip.mp4", entry.OutputPath);
        Assert.Equal("Completed", entry.Status);
        Assert.Contains(notifications.Sent, n => n.Severity == NotificationSeverity.Success);
    }

    [Fact]
    public async Task Failed_NotifiesErrorAndDoesNotAppendHistory()
    {
        var history = new FakeHistory();
        var notifications = new FakeNotifications();
        var dl = new ImmediateDownload { Result = Result<string>.Failure("Video unavailable") };
        var mgr = new DownloadManager(dl, FastPolicy(attempts: 1), 3, history, notifications);

        var job = mgr.Enqueue(Job());
        await mgr.IdleAsync();

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Empty(history.Appended);
        Assert.Contains(notifications.Sent, n => n.Severity == NotificationSeverity.Error);
    }

    [Fact]
    public async Task Canceled_AppendsNothingAndNotifiesNothing()
    {
        var history = new FakeHistory();
        var notifications = new FakeNotifications();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dl = new GatedDownload(gate.Task, () => entered.TrySetResult());
        var mgr = new DownloadManager(dl, FastPolicy(), 3, history, notifications);

        var job = mgr.Enqueue(Job());
        await entered.Task;   // job is running inside DownloadAsync
        mgr.Cancel(job);      // cancel unblocks the gate.WaitAsync(ct)
        await mgr.IdleAsync();

        Assert.Equal(JobStatus.Canceled, job.Status);
        Assert.Empty(history.Appended);
        Assert.Empty(notifications.Sent);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/FFMedia.Tests --filter FullyQualifiedName~DownloadManagerCompletionTests`
Expected: FAIL — the 5-arg `DownloadManager` constructor does not exist.

- [ ] **Step 3: Write minimal implementation**

In `src/FFMedia.Tools.YouTubeDownloader/Services/DownloadManager.cs`:

Add usings at the top (after the existing usings):

```csharp
using FFMedia.Core.History;
using FFMedia.Core.Notifications;
```

Add two fields next to the existing `_download`/`_policy` fields:

```csharp
    private readonly IHistoryService? _history;
    private readonly INotificationService? _notifications;
```

Replace the constructor with the extended signature (keep the existing body, assign the new fields):

```csharp
    public DownloadManager(
        IDownloadService download,
        RetryPolicy policy,
        int maxConcurrency = 3,
        IHistoryService? history = null,
        INotificationService? notifications = null)
    {
        ArgumentNullException.ThrowIfNull(download);
        ArgumentNullException.ThrowIfNull(policy);
        if (maxConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
        _download = download;
        _policy = policy;
        _history = history;
        _notifications = notifications;
        _slots = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        Jobs = new ReadOnlyObservableCollection<DownloadJob>(_jobs);
    }
```

Replace `RunAndTrackAsync` so the terminal side effects fire exactly once, after the job is terminal and before the idle signal:

```csharp
    private async Task RunAndTrackAsync(DownloadJob job)
    {
        try
        {
            await RunAsync(job);
            RaiseTerminalSideEffects(job);
        }
        finally
        {
            TaskCompletionSource? toComplete = null;
            lock (_gate)
            {
                if (--_activeCount == 0) { toComplete = _idleTcs; _idleTcs = null; }
            }
            toComplete?.TrySetResult();
        }
    }

    /// <summary>Records history and raises a notification for a terminal job. Best-effort:
    /// side effects must never break the queue, so failures here are swallowed.</summary>
    private void RaiseTerminalSideEffects(DownloadJob job)
    {
        try
        {
            switch (job.Status)
            {
                case JobStatus.Completed:
                    _history?.Append(new HistoryEntry(
                        job.Title, job.Url, job.OutputPath, DescribeFormat(job.Config),
                        DateTimeOffset.Now, job.Status.ToString()));
                    _notifications?.Notify(new Notification(
                        "Download complete", $"\"{job.Title}\" finished.", NotificationSeverity.Success));
                    break;
                case JobStatus.Failed:
                    _notifications?.Notify(new Notification(
                        "Download failed", $"\"{job.Title}\": {job.ErrorMessage}", NotificationSeverity.Error));
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Terminal side-effect failed for {job.Title}: {ex}");
        }
    }

    private static string DescribeFormat(DownloadConfig config) =>
        config.Kind == OutputKind.Video
            ? $"{config.Container} {config.Resolution}"
            : $"{config.AudioFormat} {config.Bitrate}";
```

In `src/FFMedia.Tools.YouTubeDownloader/ServiceCollectionExtensions.cs`, add `using FFMedia.Core.History;` and `using FFMedia.Core.Notifications;`, then extend the `IDownloadManager` factory to pass the optional deps (resolved with `GetService` so unit tests / Core-only hosts still work):

```csharp
        services.AddSingleton<IDownloadManager>(sp => new DownloadManager(
            sp.GetRequiredService<IDownloadService>(),
            sp.GetRequiredService<RetryPolicy>(),
            Math.Max(1, sp.GetRequiredService<ISettingsService>().Current.MaxConcurrency),
            sp.GetService<IHistoryService>(),
            sp.GetService<INotificationService>()));
```

- [ ] **Step 4: Run the new tests + the existing manager tests**

Run: `dotnet test src/FFMedia.Tests --filter "FullyQualifiedName~DownloadManagerCompletionTests|FullyQualifiedName~DownloadManagerTests"`
Expected: PASS — new completion tests plus all pre-existing `DownloadManagerTests`.

- [ ] **Step 5: Commit**

```bash
git add src/FFMedia.Tools.YouTubeDownloader/Services/DownloadManager.cs src/FFMedia.Tools.YouTubeDownloader/ServiceCollectionExtensions.cs src/FFMedia.Tests/YouTubeDownloader/DownloadManagerCompletionTests.cs
git commit -m "feat(youtube): record history + notify on job completion (Approach A hook)"
```

---

### Task 6: DownloaderViewModel presets (save / apply / delete)

**Files:**
- Modify: `src/FFMedia.Tools.YouTubeDownloader/ViewModels/DownloaderViewModel.cs`
- Modify: `src/FFMedia.Tests/YouTubeDownloader/DownloaderViewModelTests.cs`

**Interfaces:**
- Consumes: `IPresetService` + `Preset` (Core, Task 3), `PresetMapping` (Task 4), existing selection properties + `BuildProcessing()`.
- Produces: `DownloaderViewModel` ctor gains a trailing `IPresetService presets` param; new members:
  - `ObservableCollection<Preset> Presets { get; }`
  - `Preset? SelectedPreset` (observable), `string NewPresetName` (observable)
  - commands `SaveAsPresetCommand`, `ApplyPresetCommand`, `DeletePresetCommand`
  - private `DownloadConfig BuildConfig()` (also used by `AddToQueueAsync`).

- [ ] **Step 1: Write the failing tests**

In `src/FFMedia.Tests/YouTubeDownloader/DownloaderViewModelTests.cs`:

Add a fake preset service inside the class (next to `FakeSettings`):

```csharp
    private sealed class FakePresets : FFMedia.Core.Presets.IPresetService
    {
        public List<FFMedia.Core.Presets.Preset> Items { get; } = new();
        public IReadOnlyList<FFMedia.Core.Presets.Preset> List() => Items;
        public void Save(FFMedia.Core.Presets.Preset preset)
        {
            Items.RemoveAll(p => p.Name == preset.Name);
            Items.Add(preset);
        }
        public void Delete(string name) => Items.RemoveAll(p => p.Name == name);
        public event EventHandler? Changed { add { } remove { } }
    }
```

Replace the `Vm` helper with one that also injects presets (default a fresh fake):

```csharp
    private static DownloaderViewModel Vm(FakePlaylistProbe probe, FakeManager mgr, FakePresets? presets = null) =>
        new(probe, mgr, new FakeSettings(), presets ?? new FakePresets());
```

Add these tests to the class:

```csharp
    [Fact]
    public void SaveAsPreset_SerializesCurrentConfig_UnderGivenName()
    {
        var presets = new FakePresets();
        var vm = Vm(new FakePlaylistProbe(), new FakeManager(), presets);
        vm.SelectedKind = OutputKind.Audio;
        vm.SelectedAudioFormat = AudioFormat.Mp3;
        vm.SelectedBitrate = AudioBitrate.K192;
        vm.NewPresetName = "Podcast";

        vm.SaveAsPresetCommand.Execute(null);

        var saved = Assert.Single(presets.Items);
        Assert.Equal("Podcast", saved.Name);
        var config = FFMedia.Tools.YouTubeDownloader.Services.PresetMapping.Deserialize(saved.Payload);
        Assert.Equal(OutputKind.Audio, config.Kind);
        Assert.Equal(AudioFormat.Mp3, config.AudioFormat);
        Assert.Equal(AudioBitrate.K192, config.Bitrate);
        Assert.Contains(vm.Presets, p => p.Name == "Podcast");
    }

    [Fact]
    public void ApplyPreset_SeedsSelectionsFromPayload()
    {
        var presets = new FakePresets();
        var config = new DownloadConfig(
            OutputKind.Video, VideoContainer.Mkv, VideoResolution.P720,
            AudioFormat.Mp3, AudioBitrate.Best,
            new ProcessingOptions(null, PreciseCut: true, EmbedSubtitles: true, SubtitleLanguage: "fr",
                EmbedMetadata: false, EmbedThumbnail: false));
        presets.Items.Add(new FFMedia.Core.Presets.Preset(
            "HD", FFMedia.Tools.YouTubeDownloader.Services.PresetMapping.Serialize(config)));
        var vm = Vm(new FakePlaylistProbe(), new FakeManager(), presets);
        vm.SelectedPreset = vm.Presets[0];

        vm.ApplyPresetCommand.Execute(null);

        Assert.Equal(VideoContainer.Mkv, vm.SelectedContainer);
        Assert.Equal(VideoResolution.P720, vm.SelectedResolution);
        Assert.True(vm.PreciseCut);
        Assert.True(vm.EmbedSubtitles);
        Assert.Equal("fr", vm.SubtitleLanguage);
        Assert.False(vm.EmbedMetadata);
        Assert.False(vm.EmbedThumbnail);
    }

    [Fact]
    public void DeletePreset_RemovesSelected()
    {
        var presets = new FakePresets();
        presets.Items.Add(new FFMedia.Core.Presets.Preset("Gone", "{}"));
        var vm = Vm(new FakePlaylistProbe(), new FakeManager(), presets);
        vm.SelectedPreset = vm.Presets[0];

        vm.DeletePresetCommand.Execute(null);

        Assert.Empty(presets.Items);
        Assert.Empty(vm.Presets);
    }

    [Fact]
    public void Presets_PopulatedFromServiceAtConstruction()
    {
        var presets = new FakePresets();
        presets.Items.Add(new FFMedia.Core.Presets.Preset("Seed", "{}"));

        var vm = Vm(new FakePlaylistProbe(), new FakeManager(), presets);

        Assert.Contains(vm.Presets, p => p.Name == "Seed");
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/FFMedia.Tests --filter FullyQualifiedName~DownloaderViewModelTests`
Expected: FAIL — the 4-arg constructor and preset members do not exist.

- [ ] **Step 3: Write the implementation**

In `src/FFMedia.Tools.YouTubeDownloader/ViewModels/DownloaderViewModel.cs`:

Add usings:

```csharp
using FFMedia.Core.Presets;
```

Add a field and extend the constructor:

```csharp
    private readonly IPresetService _presets;

    public DownloaderViewModel(
        IPlaylistProbe playlistProbe, IDownloadManager manager, ISettingsService settings, IPresetService presets)
    {
        ArgumentNullException.ThrowIfNull(playlistProbe);
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(presets);
        _playlistProbe = playlistProbe;
        _manager = manager;
        _presets = presets;
        OutputFolder = settings.Current.DefaultOutputFolder;
        ReloadPresets();
    }
```

Add the observable members (place near the other `[ObservableProperty]` fields):

```csharp
    public ObservableCollection<Preset> Presets { get; } = new();
    [ObservableProperty] private Preset? _selectedPreset;
    [ObservableProperty] private string _newPresetName = string.Empty;
```

Add a `BuildConfig()` helper and use it in `AddToQueueAsync` (replace the inline `new DownloadConfig(...)`):

```csharp
    private DownloadConfig BuildConfig() => new(
        SelectedKind, SelectedContainer, SelectedResolution, SelectedAudioFormat, SelectedBitrate,
        BuildProcessing());
```

Change the assignment inside `AddToQueueAsync` from the inline constructor to:

```csharp
            var config = BuildConfig();
```

Add the preset commands and helpers at the end of the class:

```csharp
    [RelayCommand]
    private void SaveAsPreset()
    {
        if (string.IsNullOrWhiteSpace(NewPresetName)) return;
        _presets.Save(new Preset(NewPresetName.Trim(), PresetMapping.Serialize(BuildConfig())));
        ReloadPresets();
        NewPresetName = string.Empty;
    }

    [RelayCommand]
    private void ApplyPreset()
    {
        if (SelectedPreset is null) return;
        ApplyConfig(PresetMapping.Deserialize(SelectedPreset.Payload));
    }

    [RelayCommand]
    private void DeletePreset()
    {
        if (SelectedPreset is null) return;
        _presets.Delete(SelectedPreset.Name);
        ReloadPresets();
    }

    private void ReloadPresets()
    {
        Presets.Clear();
        foreach (var preset in _presets.List())
            Presets.Add(preset);
    }

    private void ApplyConfig(DownloadConfig config)
    {
        SelectedKind = config.Kind;
        SelectedContainer = config.Container;
        SelectedResolution = config.Resolution;
        SelectedAudioFormat = config.AudioFormat;
        SelectedBitrate = config.Bitrate;
        PreciseCut = config.Processing.PreciseCut;
        EmbedSubtitles = config.Processing.EmbedSubtitles;
        SubtitleLanguage = config.Processing.SubtitleLanguage;
        EmbedMetadata = config.Processing.EmbedMetadata;
        EmbedThumbnail = config.Processing.EmbedThumbnail;
        if (config.Processing.Trim is { } trim)
        {
            TrimStart = trim.Start.ToString(@"hh\:mm\:ss");
            TrimEnd = trim.End.ToString(@"hh\:mm\:ss");
        }
        else
        {
            TrimStart = string.Empty;
            TrimEnd = string.Empty;
        }
    }
```

> `PresetMapping` is in the same `...Services` namespace already imported by the ViewModel's usings
> (`using FFMedia.Tools.YouTubeDownloader.Services;`). If it is not imported, add that using.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/FFMedia.Tests --filter FullyQualifiedName~DownloaderViewModelTests`
Expected: PASS — the new preset tests plus all pre-existing DownloaderViewModel tests.

- [ ] **Step 5: Commit**

```bash
git add src/FFMedia.Tools.YouTubeDownloader/ViewModels/DownloaderViewModel.cs src/FFMedia.Tests/YouTubeDownloader/DownloaderViewModelTests.cs
git commit -m "feat(youtube): preset save/apply/delete on the downloader view model"
```

---

### Task 7: App SnackbarNotificationService + shell wiring

**Files:**
- Create: `src/FFMedia.App/Services/SnackbarNotificationService.cs`
- Modify: `src/FFMedia.App/MainWindow.xaml`
- Modify: `src/FFMedia.App/MainWindow.xaml.cs`
- Modify: `src/FFMedia.App/App.xaml.cs`

**Interfaces:**
- Consumes: `INotificationService`/`Notification`/`NotificationSeverity` (Core, Task 1); WPF-UI `ISnackbarService` (`SetSnackbarPresenter(SnackbarPresenter)`, `Show(string title, string message, ControlAppearance appearance, IconElement icon, TimeSpan timeout)`), `ControlAppearance` (`Info`/`Success`/`Caution`/`Danger`).
- Produces: `sealed class SnackbarNotificationService : INotificationService` registered as the app's `INotificationService`; `RootSnackbar` presenter in the shell; DI registrations for `ISnackbarService` + `INotificationService`.
- Verification: **build + manual smoke** (no unit test — App layer is not referenced by Tests).

- [ ] **Step 1: Implement the service**

`src/FFMedia.App/Services/SnackbarNotificationService.cs`:

```csharp
using System.Windows;
using FFMedia.Core.Notifications;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace FFMedia.App.Services;

/// <summary>Shows <see cref="Notification"/>s as WPF-UI snackbars. Safe to call from any thread —
/// marshals onto the UI dispatcher before touching the presenter.</summary>
public sealed class SnackbarNotificationService : INotificationService
{
    private readonly ISnackbarService _snackbar;

    public SnackbarNotificationService(ISnackbarService snackbar)
    {
        ArgumentNullException.ThrowIfNull(snackbar);
        _snackbar = snackbar;
    }

    public void Notify(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var app = Application.Current;
        if (app?.Dispatcher is null) return;

        app.Dispatcher.Invoke(() => _snackbar.Show(
            notification.Title,
            notification.Message,
            Map(notification.Severity),
            null!,
            TimeSpan.FromSeconds(4)));
    }

    private static ControlAppearance Map(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Success => ControlAppearance.Success,
        NotificationSeverity.Warning => ControlAppearance.Caution,
        NotificationSeverity.Error => ControlAppearance.Danger,
        _ => ControlAppearance.Info,
    };
}
```

> `null!` for the `IconElement icon` parameter: WPF-UI renders no icon when it is null; `null!`
> avoids the CS8625 nullable warning under the app's `Nullable=enable`.

- [ ] **Step 2: Add the snackbar presenter to the shell**

In `src/FFMedia.App/MainWindow.xaml`, add a `SnackbarPresenter` in the same grid cell as the
`NavigationView` (declared after it so it overlays), just before the closing `</Grid>`:

```xml
        <ui:SnackbarPresenter Grid.Row="1" x:Name="RootSnackbar"
                              VerticalAlignment="Bottom" Margin="0,0,0,16" />
```

- [ ] **Step 3: Wire the presenter in code-behind**

Replace `src/FFMedia.App/MainWindow.xaml.cs` with:

```csharp
using FFMedia.App.ViewModels;
using Wpf.Ui;                 // INavigationService, ISnackbarService
using Wpf.Ui.Controls;        // FluentWindow

namespace FFMedia.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow(
        MainWindowViewModel viewModel,
        INavigationService navigationService,
        ISnackbarService snackbarService)
    {
        DataContext = viewModel;
        InitializeComponent();

        // NavigationService.SetNavigationControl also propagates the DI-backed
        // INavigationViewPageProvider (registered via AddNavigationViewPageProvider())
        // onto RootNavigation, so selecting a MenuItemsSource entry resolves its
        // TargetPageType through the app's service provider.
        navigationService.SetNavigationControl(RootNavigation);

        // Point the snackbar service at the shell-owned presenter so notifications
        // raised anywhere (including off the UI thread) render here.
        snackbarService.SetSnackbarPresenter(RootSnackbar);
    }
}
```

- [ ] **Step 4: Register the services**

In `src/FFMedia.App/App.xaml.cs`, inside `ConfigureServices`, after the `AddYouTubeDownloader()` / `ThemeService` registrations, add:

```csharp
                services.AddSingleton<Wpf.Ui.ISnackbarService, Wpf.Ui.SnackbarService>();
                services.AddSingleton<FFMedia.Core.Notifications.INotificationService,
                    FFMedia.App.Services.SnackbarNotificationService>();
```

- [ ] **Step 5: Build**

Run: `dotnet build FFMedia.sln -c Debug`
Expected: Build succeeded, **0 warnings, 0 errors**.

- [ ] **Step 6: Commit**

```bash
git add src/FFMedia.App/Services/SnackbarNotificationService.cs src/FFMedia.App/MainWindow.xaml src/FFMedia.App/MainWindow.xaml.cs src/FFMedia.App/App.xaml.cs
git commit -m "feat(app): in-app snackbar notifications via WPF-UI SnackbarPresenter"
```

---

### Task 8: Inline preset UI on the Downloader page

**Files:**
- Modify: `src/FFMedia.Tools.YouTubeDownloader/Views/DownloaderPage.xaml`

**Interfaces:**
- Consumes: `Presets`, `SelectedPreset`, `NewPresetName`, `ApplyPresetCommand`, `SaveAsPresetCommand`, `DeletePresetCommand` (Task 6).
- Produces: a "Presets" row on the page. Verification: **build + manual smoke**.

- [ ] **Step 1: Add the preset UI**

In `src/FFMedia.Tools.YouTubeDownloader/Views/DownloaderPage.xaml`, insert a Presets block
immediately after the closing `</StackPanel>` of the "Processing" section and **before** the
"Add to queue" button row (i.e. between the Processing `StackPanel` and the action-buttons
`StackPanel`):

```xml
            <StackPanel Margin="0,16,0,0">
                <TextBlock Text="Presets" FontWeight="SemiBold" />
                <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
                    <ComboBox Width="200" ItemsSource="{Binding Presets}"
                              SelectedItem="{Binding SelectedPreset}"
                              DisplayMemberPath="Name"
                              ToolTip="Choose a saved preset, then Apply" />
                    <ui:Button Content="Apply" Margin="8,0,0,0" Command="{Binding ApplyPresetCommand}" />
                    <ui:Button Content="Delete" Margin="8,0,0,0" Command="{Binding DeletePresetCommand}" />
                </StackPanel>
                <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
                    <ui:TextBox Width="200" PlaceholderText="New preset name"
                                Text="{Binding NewPresetName, UpdateSourceTrigger=PropertyChanged}" />
                    <ui:Button Content="Save current as preset" Margin="8,0,0,0"
                               Command="{Binding SaveAsPresetCommand}" />
                </StackPanel>
            </StackPanel>
```

- [ ] **Step 2: Build**

Run: `dotnet build FFMedia.sln -c Debug`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/FFMedia.Tools.YouTubeDownloader/Views/DownloaderPage.xaml
git commit -m "feat(youtube): inline preset dropdown + save/apply/delete on downloader page"
```

---

### Task 9: History page + navigation

**Files:**
- Create: `src/FFMedia.App/ViewModels/HistoryViewModel.cs`
- Create: `src/FFMedia.App/Views/HistoryPage.xaml`
- Create: `src/FFMedia.App/Views/HistoryPage.xaml.cs`
- Modify: `src/FFMedia.App/ViewModels/MainWindowViewModel.cs`
- Modify: `src/FFMedia.App/App.xaml.cs`

**Interfaces:**
- Consumes: `IHistoryService` + `HistoryEntry` (Core, Task 2); existing footer-nav pattern in `MainWindowViewModel`.
- Produces: `HistoryViewModel` (Entries + FilterText filter + refresh-on-`Changed`, Clear/OpenFile/OpenFolder commands); `HistoryPage`; a **History** footer nav item; DI registrations. Verification: **build + manual smoke**.

- [ ] **Step 1: Implement the view model**

`src/FFMedia.App/ViewModels/HistoryViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFMedia.Core.History;

namespace FFMedia.App.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly IHistoryService _history;

    public HistoryViewModel(IHistoryService history)
    {
        ArgumentNullException.ThrowIfNull(history);
        _history = history;
        _history.Changed += (_, _) => Application.Current?.Dispatcher.Invoke(Refresh);
        Refresh();
    }

    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    [ObservableProperty] private string _filterText = string.Empty;

    partial void OnFilterTextChanged(string value) => Refresh();

    private void Refresh()
    {
        var filter = FilterText?.Trim() ?? string.Empty;
        var matches = _history.Query().Where(e =>
            filter.Length == 0
            || (e.Title?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
            || (e.Url?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
            || (e.Format?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false));

        Entries.Clear();
        foreach (var entry in matches)
            Entries.Add(entry);
    }

    [RelayCommand]
    private void Clear() => _history.Clear();

    [RelayCommand]
    private void OpenFile(HistoryEntry? entry)
    {
        if (entry?.OutputPath is not { } path || !File.Exists(path)) return;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenFolder(HistoryEntry? entry)
    {
        if (entry?.OutputPath is not { } path || !File.Exists(path)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }
}
```

- [ ] **Step 2: Implement the page**

`src/FFMedia.App/Views/HistoryPage.xaml`:

```xml
<Page x:Class="FFMedia.App.Views.HistoryPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
    <Grid Margin="24">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <StackPanel Grid.Row="0">
            <TextBlock Text="History" FontSize="24" FontWeight="SemiBold" />
            <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
                <ui:TextBox Width="280" PlaceholderText="Filter by title, URL, or format…"
                            Text="{Binding FilterText, UpdateSourceTrigger=PropertyChanged}" />
                <ui:Button Content="Clear history" Margin="12,0,0,0" Command="{Binding ClearCommand}" />
            </StackPanel>
        </StackPanel>

        <ItemsControl Grid.Row="1" Margin="0,16,0,0" ItemsSource="{Binding Entries}"
                      ScrollViewer.VerticalScrollBarVisibility="Auto">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border Padding="12" Margin="0,0,0,8" CornerRadius="6"
                            Background="{DynamicResource ControlFillColorDefaultBrush}">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <StackPanel Grid.Column="0">
                                <TextBlock Text="{Binding Title}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis" />
                                <TextBlock Text="{Binding Url}" Opacity="0.7" TextTrimming="CharacterEllipsis" Margin="0,2,0,0" />
                                <StackPanel Orientation="Horizontal" Margin="0,2,0,0">
                                    <TextBlock Text="{Binding Format}" Opacity="0.6" />
                                    <TextBlock Text="{Binding Timestamp, StringFormat='  ·  {0:g}'}" Opacity="0.6" />
                                    <TextBlock Text="{Binding Status, StringFormat='  ·  {0}'}" Opacity="0.6" />
                                </StackPanel>
                            </StackPanel>
                            <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Top" Margin="12,0,0,0">
                                <ui:Button Content="Open file"
                                           Command="{Binding DataContext.OpenFileCommand, RelativeSource={RelativeSource AncestorType=Page}}"
                                           CommandParameter="{Binding}" />
                                <ui:Button Content="Open folder" Margin="8,0,0,0"
                                           Command="{Binding DataContext.OpenFolderCommand, RelativeSource={RelativeSource AncestorType=Page}}"
                                           CommandParameter="{Binding}" />
                            </StackPanel>
                        </Grid>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </Grid>
</Page>
```

`src/FFMedia.App/Views/HistoryPage.xaml.cs`:

```csharp
using System.Windows.Controls;
using FFMedia.App.ViewModels;

namespace FFMedia.App.Views;

public partial class HistoryPage : Page
{
    public HistoryPage(HistoryViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Add the History footer nav item**

In `src/FFMedia.App/ViewModels/MainWindowViewModel.cs`, extend the `FooterMenuItems`
initializer to include History (before Settings, so History sits above the gear):

```csharp
        FooterMenuItems = new ObservableCollection<object>
        {
            new NavigationViewItem
            {
                Content = "History",
                Icon = new FontIcon { Glyph = "\uE81C" }, // Segoe Fluent "History"
                TargetPageType = typeof(HistoryPage),
            },
            new NavigationViewItem
            {
                Content = "Settings",
                Icon = new FontIcon { Glyph = "" }, // Segoe Fluent settings gear
                TargetPageType = typeof(SettingsPage),
            },
        };
```

> **Keep the existing Settings NavigationViewItem exactly as it is** (raw Segoe Fluent gear
> glyph, ``TargetPageType = typeof(SettingsPage)``); only add the History item above it. The
> History glyph is written as the escape `"\uE81C"` so the codepoint is unambiguous.

- [ ] **Step 4: Register the page + view model**

In `src/FFMedia.App/App.xaml.cs` `ConfigureServices`, next to the Settings registrations, add:

```csharp
                services.AddTransient<FFMedia.App.ViewModels.HistoryViewModel>();
                services.AddTransient<FFMedia.App.Views.HistoryPage>();
```

- [ ] **Step 5: Build**

Run: `dotnet build FFMedia.sln -c Debug`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/FFMedia.App/ViewModels/HistoryViewModel.cs src/FFMedia.App/Views/HistoryPage.xaml src/FFMedia.App/Views/HistoryPage.xaml.cs src/FFMedia.App/ViewModels/MainWindowViewModel.cs src/FFMedia.App/App.xaml.cs
git commit -m "feat(app): add History screen with filter, open, and clear"
```

---

### Task 10: Docs — SDD v0.8 + progress log

**Files:**
- Modify: `SDD.md`
- Modify: `CLAUDE.md`

**Interfaces:** none (documentation).

- [ ] **Step 1: Update the SDD**

Edit `SDD.md`:
- Bump the version header to **v0.8**.
- §6 (services): mark `IPresetService`, `IHistoryService`, `INotificationService` realized.
- §7.2: add the honest amendment that `DownloadManager` performs terminal-transition side effects
  (history append + notification) through Core abstractions — still no direct UI dependency.
- §13: note the History screen and inline presets are delivered; in-app (snackbar) notifications
  delivered, native Windows toast still deferred to M6.
- §17: mark **M5 complete**.
- §19 (open items): record the **Re-download deferral** (History re-download needs a cross-page
  seeding seam + a richer `HistoryEntry` that stores the serialized config; deferred as a follow-up).
- Add a Changelog row for v0.8 summarizing PR 2.

- [ ] **Step 2: Verify the whole solution is green**

Run: `dotnet build FFMedia.sln -c Debug` then `dotnet test src/FFMedia.Tests --filter Category!=Integration`
Expected: Build 0 warnings / 0 errors; all unit tests pass.

- [ ] **Step 3: Append the CLAUDE.md progress-log entry (newest at top)**

Add an entry dated 2026-07-06 under `## 📓 Progress Log` describing M5 PR 2: presets (Core service +
module `PresetMapping` + inline UI), history (Core service + `DownloadManager` completion hook +
History screen), in-app snackbar notifications, and the Re-download deferral. Note SDD → v0.8.

- [ ] **Step 4: Commit**

```bash
git add SDD.md CLAUDE.md
git commit -m "docs: sync SDD to v0.8 and log M5 PR2 progress"
```

---

## Self-Review

**Spec coverage (spec §2–§10):**
- §2.1 Core `Notifications` → Task 1; `History` → Task 2; `Presets` → Task 3. ✅
- §2.2 App `SnackbarNotificationService` → Task 7; `HistoryPage`/`HistoryViewModel` → Task 9. ✅
- §2.3 preset-payload boundary (opaque string in Core; module owns (de)serialization) → `Preset.Payload` (Task 3) + `PresetMapping` (Task 4). ✅
- §3 footer nav for History → Task 9; theme toggle already delivered in PR 1. ✅
- §4 settings seams → delivered in PR 1 (unchanged). ✅
- §5 presets inline on Downloader (dropdown + save/delete, apply seeds selections, output folder excluded) → Tasks 6 + 8. ✅
- §6 completion hook Approach A (nullable deps, Completed→history+notify, Failed→notify, Canceled→neither) → Task 5. ✅
- §6.1 History page: filter, Clear, Open file/folder → Task 9. **Re-download deferred** (documented in Global Constraints + Task 10 SDD §19). ⚠️ intentional deviation.
- §7 testing: JsonStore (PR 1) + Settings/Preset/History services + completion hook + PresetMapping + DownloaderVM preset commands unit-tested; App VMs build+manual per SDD §14. ✅
- §8.1 failed-job history → **only `Completed`** rows (Task 5). §8.2 tolerant preset payload → Task 4. §8.3 `System` theme resolved at startup (PR 1); live OS-theme reaction deferred. ✅
- §9 PR 2 content + SDD → v0.8 → Task 10. ✅

**Placeholder scan:** No TBD/TODO; every code step shows complete code; commands have expected output. ✅

**Type consistency:** `IHistoryService` (`Query`/`Append`/`Clear`/`Changed`) used identically in Tasks 2, 5, 9. `IPresetService` (`List`/`Save`/`Delete`/`Changed`) identical in Tasks 3, 6. `Notification(Title, Message, Severity)` + `NotificationSeverity` identical in Tasks 1, 5, 7. `PresetMapping.Serialize`/`Deserialize` identical in Tasks 4, 6. `DownloadManager` 5-arg ctor identical in Tasks 5 (impl) and 5 (DI). `HistoryEntry(Title, Url, OutputPath, Format, Timestamp, Status)` identical in Tasks 2, 5, 9. ✅
