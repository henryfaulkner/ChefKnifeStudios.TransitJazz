# Selectable Backfill Texture — Design Document

**Status:** DESIGN — ready to implement, pending a percussion-audition pass (§9).
**Component:** `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/transit-synth.js`
(engine) + a new `BackfillTextureFab.razor` (UI) + `Settings.cs`/interop plumbing +
an **iteration on `tools/instrument-compat/`** (audition surface, §9).
**Depends on:** the shipped synth build (Tone.js v15 Sampler chain + master bus already in
`transit-synth.js`).
**Supersedes:** `docs/DRUMKIT_AND_DENSITY_DESIGN_DOCUMENT.md` — see §3.

---

## Problem

TransitJazz plays a continuous, quiet **pink-noise bed** under the procedural transit
notes — a tape-hiss/vinyl-air surrogate so the space between crossings is textured
rather than dead silent (`transit-synth.js` `getMasterBus`, the `Tone.Noise('pink')`
node at ~-38 dB into the master compressor). It is fixed: there is exactly one bed, it is
not user-configurable, and it is coupled to the global audio mute (`_audioEnabled`).

We want to **expose the backfill filler as a user-selectable choice** — first adding
**lo-fi percussion** alongside the existing noise. The selection is surfaced via a **new
FAB with a menu** (the same shape as the existing city selector), persists across
reloads, and defaults to today's behavior so nothing changes until the user opts in.

**There is always a backfill.** The gaps between procedural notes are never left
completely dry — the only question is *which* texture fills them. Silencing everything is
the job of the separate, broad audio mute (§below), not of this selector.

---

## Concept

One mutually-exclusive selector, **always** on one of these (no "off" — see above):

| Mode | Behavior |
|---|---|
| **Noise** (default) | Today's continuous pink-noise bed. Byte-for-byte the current behavior. |
| **Percussion** | A sparse lo-fi kit (soft kick + rim/brush) on a slow, tempo-synced loop, humanized, feeding the same master bus. Synth-based — no samples, no RAM cost. |

The FAB lets the user swap between them live; the choice is persisted to local storage
and re-applied on the next unlock. Adding a third texture later (e.g. vinyl crackle,
rain) is a one-line-per-option change to the enum + menu.

---

## Mute is a separate, broad audio gate — decoupled from the backfill selector

Two orthogonal controls:

- **`IsAudioEnabled` (the AudioFAB mute)** is the **broad audio config**: the master
  gate over *everything* — procedural notes AND whatever backfill is selected. Muted →
  total silence. This is unchanged in scope; it is NOT one of the backfill options.
- **The backfill selector (new FAB)** only chooses *which* filler plays *while audio is
  enabled*. It never means "silence" — that's what mute is for.

The two compose: a backfill layer runs iff `_audioEnabled AND _backfillMode selects it`.
Because there is always a selected backfill, unmuting always brings back both the notes
and the chosen texture. Defaulting the selector to `Noise` preserves today's sound
exactly until the user changes it.

---

## 3. This SUPERSEDES the deferred Drumkit doc

`docs/DRUMKIT_AND_DENSITY_DESIGN_DOCUMENT.md` (Status: DEFERRED) proposed an
**event-driven-off-transit** percussion voice: each drum hit would represent a real
transit event (a vehicle crossing a checkpoint / entering-leaving a route), explicitly
rejecting a "bolted-on decorative loop" and parking on an unresolved question — *what
real signal should a drum hit represent*.

**That direction is rejected. Backfill percussion is the future.** This feature
deliberately builds the very thing that doc set aside: a **continuous decorative loop**, a
sibling of the pink-noise bed, NOT tied to transit events. Reasons:

- It's a **filler**, framed honestly as such — its whole job is to texture the gaps, not
  to be emergent-music-from-transit. The melodic notes already carry the "real transit
  events → sound" premise; the backfill's job is atmosphere underneath them.
- It sidesteps the drumkit doc's blocking open question entirely (no "what does a hit
  mean" to answer) and needs **no new telemetry plumbing** into the JS layer (the
  drumkit's density option (c) required piping a live vehicle count into
  `transit-synth.js`; this doesn't).
- It is immediately **user-selectable and shippable**, which the event-driven design
  never was.

**Action at implementation time:** mark
`docs/DRUMKIT_AND_DENSITY_DESIGN_DOCUMENT.md` as **SUPERSEDED by this doc** (a one-line
status banner at its top pointing here) so the rejected direction is recorded as closed,
not merely stale.

The one thing worth **carrying over** from that doc is its settled synthesized-drum voice
palette (§4 there): `Tone.MembraneSynth` kick, `Tone.MetalSynth` hat/rim, per-voice
filter + volume, single fixed pitch per drum. We take the kick + a rim/brush for the
sparse kit. That's a voice recipe, not the rejected trigger model.

---

## 4. Layer 1 — the engine (`transit-synth.js`)

The master bus already lazy-builds the noise node exactly once and gates it on
`_audioEnabled`. We generalize that single node into a **swappable backfill layer** with
two states, mirroring the existing `setAudioEnabled` structure so the new code reads like
the code already there.

### New module state

```js
// 'noise' | 'percussion' — the LIVE backfill selection. There is ALWAYS a backfill;
// this never holds an "off" value (silence is the mute's job, not this selector's).
// Defaults to 'noise' so a plain load reproduces today's behavior exactly.
let _backfillMode = 'noise';
// Built lazily, once, like the master bus. { loop, kick, rim, volume } or null.
let _percussion = null;
```

### New export `setBackfillTexture(mode)`

A near-copy of `setAudioEnabled`'s shape (the file's established pattern for a
live-toggled audio flag):

- Normalizes + records `_backfillMode` (anything other than `'percussion'` → `'noise'`;
  there is no off).
- Resumes the AudioContext if it slipped to `suspended`/`interrupted` (the same
  defensive resume `setAudioEnabled` already does), because switching *to* a texture
  after a long idle mute must be able to make sound.
- Ensures **exactly one** backfill layer runs, gated by `_audioEnabled`: starts the
  selected layer, stops the other. Building the percussion layer lazily on first
  selection.
- Safe to call before the master bus exists: it records the flag and returns; both the
  master-bus build path and the unlock-warm path honor `_backfillMode` when they later
  run. This matches `setAudioEnabled`'s "flag recorded even if bus not built yet"
  contract.

```js
export function setBackfillTexture(mode) {
    _backfillMode = (mode === 'percussion') ? 'percussion' : 'noise';
    if (!_masterBus) return;                 // recorded; honored when the bus builds
    _resumeContextIfNeeded();                // same helper setAudioEnabled uses
    _applyBackfillLayer();                   // start/stop noise vs percussion per _audioEnabled & _backfillMode
}
```

`_applyBackfillLayer()` is the single choke point that reconciles `_audioEnabled` ×
`_backfillMode` → which of `{noise, percussion}` is running. Both `setAudioEnabled` and
`setBackfillTexture` call it, so the two gates never drift. While muted, it stops both;
while enabled, it runs exactly the selected one.

### Changes to existing functions

- **`getMasterBus`** currently ends with `if (_audioEnabled) noise.start()`. That becomes
  a call to `_applyBackfillLayer()` after the noise node is wired, so first build honors
  the persisted mode (noise or percussion) instead of always starting noise.
- **`setAudioEnabled`** currently unconditionally restarts the noise bed on unmute. It
  must instead restart **whichever** layer `_backfillMode` selects, and on mute must stop
  **both**. Routing both through `_applyBackfillLayer()` gets this for free and ensures a
  mute silences percussion too.
- **`dispose`** tears down `_percussion` (loop + synth voices) alongside the samplers and
  clears `_masterBus`.
- The `window.TransitSynth = { ... }` export map gains `setBackfillTexture`.

### The percussion layer

Built lazily once (like the master bus), feeding the **same** master compressor so it
inherits the master glue + 4000 Hz softening every voice already gets:

```js
// Sparse lo-fi kit: soft low-tuned kick + a quiet rim/brush, on a slow Tone.Loop.
// Feeds bus.input (the master compressor), NOT Destination, so it sits under the mix
// like the noise bed. Humanized (velocity + small time jitter) to match the note
// humanization already in this file. Voice recipe carried over from the (now superseded)
// drumkit doc §4.
function buildPercussion(T) {
    const bus = getMasterBus(T);
    const volume = new T.Volume(PERCUSSION_VOLUME_DB);
    volume.connect(bus.input);

    const kick = new T.MembraneSynth({ /* low, short, soft */ });
    const rim  = new T.MetalSynth({ /* short, quiet, brushy */ });
    kick.connect(volume);
    rim.connect(/* its own filter → */ volume);

    // No app-wide clock exists today (notes fire in real time via _tone.now()). The loop
    // needs Tone.Transport, so buildPercussion starts it. Starting the Transport does NOT
    // affect the free-running note triggers — they don't schedule on it.
    const loop = new T.Loop((time) => {
        kick.triggerAttackRelease('C1', '8n', time, _humanVelocity());
        if (Math.random() < 0.4) rim.triggerAttackRelease('16n', time + 0.02, _humanVelocity());
    }, '2n');   // slow, sparse — one soft kick every half note, occasional rim

    return { loop, kick, rim, volume };
}
```

**This is the one genuinely new Tone.js surface in the feature** — starting the
`Tone.Transport` (the app has never used it; every existing sound fires in real time off
`_tone.now()`). It is safe and orthogonal to the note triggers, but because the *sound*
is the whole point, the exact voices/tempo/sparsity are best dialed in by ear first, in
the audition tool (§9). The params above are a starting point, not final values.

New constants (grouped with the existing `NOISE_*` constants):
`PERCUSSION_VOLUME_DB`, the loop interval, the kick/rim synth params, and the rim
probability. **These are the values the §9 audition produces and pins here.**

---

## 5. Layer 2 — interop + settings (C#)

- **`ITransitSynthJsInterop` / `TransitSynthJsInterop`**: add
  `SetBackfillTextureAsync(string mode)` — a copy of `SetAudioEnabledAsync` that invokes
  `setBackfillTexture` with the lowercase mode string.
- **`Settings.cs`**: add a new enum and a persisted property:
  ```csharp
  public enum BackfillTexture { Noise, Percussion }   // no "off" — there is always a backfill

  [ObservableProperty]
  [HiddenSetting]                        // the reflection-driven SettingsBlade only renders bools; skip this
  BackfillTexture _backfillTexture = BackfillTexture.Noise;
  ```
  and **bump `Settings.CurrentVersion` 4 → 5** so older serialized settings discard
  cleanly (the existing version-guard pattern in `SettingsService.GetSettings`).
- `ISettingsService.SetSettingValue<T>` is already generic and reflection-based — it
  persists an enum with **no change**.
- `[HiddenSetting]` keeps the enum out of the pure-reflection `SettingsBlade` (which
  renders one checkbox per public bool). This setting lives on its own FAB, not the
  blade, so the blade's boolean-only invariant is preserved.

---

## 6. Layer 3 — the UI (`BackfillTextureFab.razor`)

A new component under `Components/FABs/`, structured like **`CityFab`** (a `MatFAB` that
opens a `MatMenu` list) but wired like **`AudioFab`** (read persisted setting → on select,
persist + apply):

```razor
<div class="backfill-texture-fab-container">
    <MatFAB Icon="graphic_eq" Mini="true" OnClick="OpenMenu" @ref="_button" />
    <MatMenu @ref="_menu">
        <MatList>
            <MatListItem><MatButton Label="Ambient noise"    Mini="true"
                @onclick="() => Select(BackfillTexture.Noise)"      Disabled="@(_current == BackfillTexture.Noise)" /></MatListItem>
            <MatListItem><MatButton Label="Lo-fi percussion" Mini="true"
                @onclick="() => Select(BackfillTexture.Percussion)" Disabled="@(_current == BackfillTexture.Percussion)" /></MatListItem>
        </MatList>
    </MatMenu>
</div>
```

```csharp
BackfillTexture _current;
protected override void OnInitialized() =>
    _current = SettingsService.GetSettings().BackfillTexture;

async Task Select(BackfillTexture mode) {
    SettingsService.SetSettingValue(nameof(Settings.BackfillTexture), mode);
    _current = mode;
    await TransitSynth.SetBackfillTextureAsync(mode.ToString().ToLowerInvariant());
}
```

**No event bus.** Unlike `AudioFab` (whose mute state has multiple consumers, so it posts
`AudioSettingChangedEventArgs`), nothing else in the app reacts to the backfill texture —
the FAB calls the interop directly. If a second consumer ever appears, add an event args
type then, not now (YAGNI).

- **Mount** in `MainLayout.razor` alongside `<AudioFab />` / `<CityFab />` /
  `<SettingsFab />`.
- Labels should route through `IStringLocalizer<RouteFilterResources>` for consistency
  with the other chrome (EN keys only; `.es` deferred, matching 015/016 precedent).

---

## 7. Initial-load / persistence honoring

The persisted texture must reach the JS engine on startup so the saved choice is heard
from the first unlock — the same way the persisted mute setting is pushed via
`SetAudioEnabledAsync` during init. Locate that init call site (in `TransitMap.razor.cs`,
where `SetAudioEnabledAsync(persistedSetting)` runs) and add a
`SetBackfillTextureAsync(persistedMode)` next to it. Because `setBackfillTexture` is safe
to call before the master bus exists (§4), ordering relative to unlock/warm is not
fragile — the flag is recorded and honored when the bus builds.

Persistence is of the **enum only** — never live Tone.js nodes — rebuilt on reload, the
same discipline the rest of `transit-synth.js` follows.

---

## 8. Scope — files touched

| File | Change |
|---|---|
| `wwwroot/js/transit-synth.js` | `_backfillMode`/`_percussion` state; `setBackfillTexture` export; `_applyBackfillLayer` choke point; `buildPercussion`; `getMasterBus`/`setAudioEnabled`/`dispose` updates; export-map entry; new `PERCUSSION_*` constants |
| `Services/JsInterop/ITransitSynthJsInterop.cs` + `TransitSynthJsInterop.cs` | `SetBackfillTextureAsync(string)` |
| `Models/Settings.cs` | `BackfillTexture` enum + `[HiddenSetting]` property; `CurrentVersion` 4 → 5 |
| `Components/FABs/BackfillTextureFab.razor` (+ `.razor.css`) | new component |
| `Layout/MainLayout.razor` | mount the FAB |
| `TransitMap.razor.cs` (init) | push persisted texture on startup, beside `SetAudioEnabledAsync` |
| `Resources/RouteFilterResources.*.resx` | FAB menu label keys (EN) |
| **`tools/instrument-compat/index.html`** | **backfill-percussion audition mode (§9)** |
| `tools/instrument-compat/DESIGN_DOCUMENT.md` | document the new audition mode |
| `docs/DRUMKIT_AND_DENSITY_DESIGN_DOCUMENT.md` | SUPERSEDED banner pointing here (§3) |

Frontend-only. No server / worker / shared changes.

---

## 9. Audition surface — iterate `tools/instrument-compat/`, do NOT build a throwaway

The percussion *sound* is the actual experiment; everything else is plumbing. The
existing `tools/instrument-compat/index.html` is already the right home for it: it
**already reproduces the exact master bus** this feature's percussion feeds into —
compressor (`{threshold:-18, ratio:3, attack:0.02, release:0.25}`) → 4000 Hz lowpass →
Destination, plus the -38 dB / 2000 Hz pink-noise bed (`getMasterBus`, index.html
~L289–311) — with the same Enable-Audio gesture unlock, the same fire-time-rechecked
mute, the same density scheduler, and localStorage persistence. The percussion loop is a
sibling node on that identical bus, so auditioning it there is **fidelity-accurate by
construction**, exactly as the tool auditions melodic instruments today. A throwaway page
would re-derive that bus and risk drift; this reuses it.

**This feature therefore includes an iteration on the tool** (its 048 pass, analogous to
how 047 built it):

- **New "Backfill" section** (a sibling of the existing Transport/Density and Instruments
  sections) with:
  - a **Noise / Percussion** selector mirroring the app's FAB (so the tool exercises the
    same two-mode model, not a bespoke one);
  - when Percussion is selected, **live controls** for the parameters that will be pinned
    into `transit-synth.js`: loop interval (`1n`/`2n`/`4n`), kick tuning + decay +
    volume, rim volume + probability, and overall `PERCUSSION_VOLUME_DB`. These are the
    knobs the sound designer turns by ear.
- The percussion builder in the tool is the **same recipe** as §4's `buildPercussion`,
  wired to the tool's existing `getMasterBus()` and gated by the tool's existing `muted`
  flag (fire-time re-check) and Enable-Audio unlock — no new bus, no new unlock path.
- It reuses the tool's existing **localStorage session** shape (add `backfill` +
  `percussionParams` alongside `instruments`/`activityLevel`/`muted`) so a dialed-in kit
  survives reload while tuning.
- **Auditioned against real voices:** because the tool can load the app's actual
  instruments and run the density sim, the percussion is tuned *underneath a simulated
  soundscape*, not in isolation — the true test of a backfill.

**Output of the audition → the pinned constants.** Once the kit sounds right, its
parameter values become the `PERCUSSION_*` constants in §4. Auditioning first is what
de-risks the one novel surface (the `Tone.Transport` loop) before it touches the .NET
solution. The tool intentionally exposes these as live knobs; the app hardcodes the
chosen result (the tool has no PALETTE-snippet export today, and this feature does not add
one — transcribing the final values is a deliberate manual step, matching 047's stance).

---

## 10. Deliberately out of scope / open items

- **Not** density-reactive and **not** a full groove — just the sparse lo-fi kit, to keep
  the first experiment small. (The rejected drumkit doc's density-reactive idea is not
  revived here.)
- **No** third+ texture yet — the enum makes adding more (vinyl crackle, rain, …) a
  one-line-per-option change, but ship Noise + Percussion first.
- **No** settings-blade entry (FAB-based by decision; enum is `[HiddenSetting]`).
- **No** "off" backfill — there is always a texture; total silence is the mute's job.
- **No** PALETTE/percussion-snippet export from the audition tool — final params are
  transcribed by hand into `transit-synth.js` (matches 047's no-export stance).
- **Open:** final percussion voice params — produced by the §9 audition.
