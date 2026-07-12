# Video Merger — bounded output options (`TargetBounds`)

> **Status:** approved design · **Date:** 2026-07-12 · **Milestone:** M7 follow-up
> **Module:** `FFMedia.Tools.VideoMerger`

---

## 1. The problem

The merger's output target is **auto-derived but fully overridable**. The override UI lets the user
pick *any* value — including values that are strictly worse than doing nothing:

- Clips are all 30 fps → the user can select **60 fps**. ffmpeg duplicates every frame. Bigger file,
  longer encode, **not one extra frame of information**.
- Clips are all 1080p → the user can select **4K**. Upscaled pixels, invented by an interpolator.
- Clips are stereo → the user can select **5.1**. Four silent channels.
- Width/Height are free text, so **1920 × 102** is reachable: valid, even, and absurd.
- CRF is unvalidated, so **CRF 99** is reachable — ffmpeg rejects it outright and the merge fails.

`MergeTargetDerivation` already computes the *maximum* across the clips (largest dimensions, fastest
frame rate, highest sample rate, most channels) — a deliberate "never degrade a source" rule. The
override UI simply ignores that ceiling.

**The goal: make the pointless states unrepresentable, not merely rejected.** A value that cannot add
information should not be offered.

---

## 2. What is *not* in scope (and why)

**Codec × container combinations are NOT restricted.** The obvious candidate — "block Opus in MP4" —
was tested against the real bundled ffmpeg 8.1, all eight combinations (2 containers × 2 video codecs
× 2 audio codecs), and **every one muxes cleanly**:

```
mp4  x264  aac    OK   h264 aac      mkv  x264  aac    OK   h264 aac
mp4  x264  opus   OK   h264 opus     mkv  x264  opus   OK   h264 opus
mp4  x265  aac    OK   hevc aac      mkv  x265  aac    OK   hevc aac
mp4  x265  opus   OK   hevc opus     mkv  x265  opus   OK   hevc opus
```

There is no invalid combination to block. MP4 + Opus is a **playability** problem (VLC and Chrome
play it; QuickTime and most TVs do not), not a validity one, and blocking it would invent a
restriction ffmpeg does not have. It gets a **warning**, not a block.

This keeps the promise sharp: **a blocked option means "this is provably pointless", never "we would
rather you didn't".**

---

## 3. Design

### 3.1 `TargetBounds` — one pure type

A new pure type describing *what the user may choose*, derived from the same clips the target is
derived from:

```csharp
public sealed record TargetBounds(
    IReadOnlyList<Resolution> Resolutions,   // source resolution first, then standard steps below
    IReadOnlyList<FrameRate>  FrameRates,    // standard ladder, capped at the fastest clip
    IReadOnlyList<int>        SampleRates,   // capped at the highest clip
    IReadOnlyList<int>        ChannelCounts) // capped at the widest clip
{
    public static TargetBounds From(IReadOnlyList<MediaInfo> clips);
}
```

**The keystone invariant: the derived target is always the first entry of each list.** `TargetBounds`
and `MergeTargetDerivation` must not compute the ceiling independently — the bounds are built *from
the derivation's own maxima*, so the two cannot drift. (This is the discipline the codebase already
applies to `ConformanceCheck`, which `MergeEstimator` and `MergeService` both *call* rather than
re-implement.)

The lists:

| Field | Contents |
|---|---|
| `Resolutions` | The source resolution (largest clip, even-rounded), then standard heights below it — 1440, 1080, 900, 720, 540, 480, 360 — each **scaled to the source's aspect ratio** and rounded even. Entries ≥ the source are dropped. |
| `FrameRates` | `MergeTargetDerivation.StandardRates` (24/1.001, 24, 25, 30/1.001, 30, 50, 60/1.001, 60) filtered to ≤ the fastest clip. A **non-standard** source rate (a 12 fps clip) is itself the ceiling and is included, or the list would be empty. |
| `SampleRates` | 22050, 44100, 48000, 96000 — filtered to ≤ the highest clip. |
| `ChannelCounts` | 1, 2, 6 — filtered to ≤ the widest clip. |
| CRF | Not source-bounded. Clamped to ffmpeg's real range, **0–51**. |

Codec, container and `FitMode` remain free choices — nothing about the sources makes H.265 or CRF 23
invalid.

### 3.2 Snap-down: the one rule for a moving ceiling

The ceiling **moves** as clips are added and removed. Adding a 4K clip raises it; deleting the only
1080p clip lowers it. An override that was legal can become illegal through no action of the user's.

**One rule handles every case: snap down to the largest allowed value ≤ the current one; if none
exists, take the smallest.** Applied per field:

```csharp
public MergeTarget ClampTo(TargetBounds bounds);   // pure, on MergeTarget
```

- Sources 1080p+720p, override **720p**, delete the 1080p clip → 720p is still on the list → **untouched**.
- Sources 1080p+720p, override **1080p**, delete the 1080p clip → 1080p is gone → **snaps to 720p**.
- The source aspect ratio changes, so the old resolution is not on the new ladder → **snaps to the
  largest entry not exceeding it**.

This happens **silently** (user-approved): no dialog, no snackbar. The invariant *"the target never
exceeds the sources"* holds at all times, which is the entire point of the feature. The override
remains an override — its *other* intent (the user deliberately chose to go smaller) is preserved.

### 3.3 The UI: lists, not free text

| Control | Today | After |
|---|---|---|
| Width, Height | two `ui:TextBox` | **one** `ComboBox` bound to `Resolutions` |
| Frame rate | `ComboBox` (all standard rates) | `ComboBox` bound to `FrameRates` (filtered) |
| Audio sample rate | `ui:TextBox` | `ComboBox` bound to `SampleRates` |
| Audio channels | `ui:TextBox` | `ComboBox` bound to `ChannelCounts` |
| CRF | `ui:TextBox`, unvalidated | `ui:TextBox`, clamped 0–51 on commit |
| Container / codecs / FitMode | `ComboBox` | unchanged |

Replacing the text boxes with lists is what makes the bad state **unrepresentable rather than
validated**: no clamp-on-commit for dimensions, no odd dimensions (the libx264 `ToEven` bug cannot
recur through this path), no `1920 × 102`.

Selecting **MP4 + Opus** reveals a compatibility note beneath the audio codec. It does **not** block,
and the merge proceeds normally.

**Empty clip list:** there is no source to bound against, so the Output section is **disabled**. A
merge needs ≥ 2 clips anyway (`CanMerge`).

### 3.4 Where it lives

- `Resolution` — **new** pure record (`int Width, int Height`) in `Models/`. `MergeTarget` keeps its
  flat `Width`/`Height` (the engine and every existing test read them); `Resolution` exists so the
  ladder is a list of *pairs* and the ComboBox can bind to one item rather than two coupled boxes.
- `TargetBounds` — `FFMedia.Tools.VideoMerger/Models/` (pure; a record, like `MergeTarget`).
- **`MergeTargetDerivation.StandardRates` is currently `private`** and must be exposed (`internal` +
  `InternalsVisibleTo`, or `public static IReadOnlyList<FrameRate>`) so `TargetBounds` consumes the
  *same* list. Copying it into a second array would let the offered rates and the derived rate drift —
  the exact failure mode §3.1's keystone invariant exists to prevent.
- `TargetBounds.From` + `MergeTarget.ClampTo` — pure, no I/O, no WPF.
- `MergerViewModel` gains observable `Resolutions` / `FrameRates` / `SampleRates` / `ChannelCounts`
  lists plus the corresponding `Selected*` properties, all recomputed in the existing `Recompute()`
  whenever `Clips` changes. `ClampTo` runs there.
- `MergerPage.xaml` swaps four inputs for `ComboBox`es and adds the MP4+Opus note.

Nothing in the engine (`MergeService`, `NormalizeArgsBuilder`, `ConformanceCheck`) changes: a target
smaller than a clip already means "non-conforming → re-encode", and `NormalizeArgsBuilder` already
scales and applies `FitMode`.

---

## 4. Testing

**Pure (`TargetBounds`, `ClampTo`) — exhaustive, no WPF:**
- The derived target is the first entry of every list (the keystone invariant — assert it directly,
  or the bounds and the derivation can drift).
- The resolution ladder preserves source aspect, rounds even, and never lists ≥ the source.
- A non-standard source frame rate (12 fps) is offered, and the list is never empty.
- Snap-down: unchanged when still valid; snaps to the new ceiling when not; snaps to the largest
  entry ≤ the old value when the ladder shifts; takes the smallest when nothing qualifies.

**ViewModel — the part that actually broke things before:**
- The option lists recompute on add *and* remove.
- An override that goes out of range **snaps down** rather than producing an impossible target.
- An override that is still valid **survives** a clip-list change untouched.
- The MP4+Opus warning flag turns on for that pair only.

**Page-load (`MergerPageLoadTests`)** already guards the XAML: four new `ComboBox`es and a new
warning `TextBlock` must still parse and bind against the real resource dictionaries.

**Integration (trait-gated, real ffmpeg):** merge 1080p sources to a **720p** target and **probe the
output dimensions**. A resolution ladder that produces something ffmpeg rejects — an odd height, a
broken aspect — is precisely the failure this feature would be embarrassed by, and the exit code is
exactly what cannot be trusted (see the concat-truncation bug, SDD Changelog 0.15).

---

## 5. Decisions (user-approved)

1. **Hard cap**, not warn-and-allow: values above the source ceiling are **not offered**.
2. **Resolution is a dropdown** of standard steps at-or-below the source, always at source aspect —
   which kills upscaling, odd dimensions and absurd aspect ratios in one move.
3. **Silent snap-down** when the ceiling moves; the override is kept, not discarded.
4. **Codec × container is not restricted** — all 8 combinations mux cleanly in ffmpeg 8.1 (verified).
   MP4 + Opus gets a **playability warning**, not a block.

## 6. Deferred

- Per-clip target overrides (the target is per-merge, per the M7 design).
- Offering resolutions *above* the source behind an "allow upscaling" escape hatch — no use case yet.
- Bitrate/two-pass encoding controls (CRF only, per the M7 design).
