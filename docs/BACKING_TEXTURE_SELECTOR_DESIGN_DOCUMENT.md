# Selectable Backing Texture — Design Document

**Status:** DESIGN — ready to implement, pending a percussion-audition pass (§9).
**Component:** `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/transit-synth.js`
(engine) + a new `BackingTextureFab.razor` (UI) + `Settings.cs`/interop plumbing.
**Depends on:** the shipped synth build (Tone.js v15 Sampler chain + master bus already in
`transit-synth.js`). No dependency on the deferred drumkit — see §3 for why they don't
collide.

---

## Problem

TransitJazz plays a continuous, quiet **pink-noise bed** under the procedural transit
notes — a tape-hiss/vinyl-air surrogate so the space between crossings is textured
rather than dead silent (`transit-synth.js` `getMasterBus`, the `Tone.Noise('pink')`
node at ~-38 dB into the master compressor). It is fixed: there is exactly one bed, it is
not user-configurable, and it is coupled to the global audio mute (`_audioEnabled`).

We want to **expose the backing filler as a user-selectable choice** — first with a
second option, **lo-fi percussion** — so a listener can pick the texture that fills the
gaps between notes. The selection is surfaced via a **new FAB with a menu** (the same
shape as the existing city selector), persists across reloads, and defaults to today's
behavior so nothing changes until the user opts in.

---

## Concept

One mutually-exclusive selector with three states:

| Mode | Behavior |
|---|---|
| **Off** | No backing texture. Procedural transit notes only, silence between them. |
| **Noise** (default) | Today's continuous pink-noise bed. Byte-for-byte the current behavior. |
| **Percussion** | A sparse lo-fi kit (soft kick + rim/brush) on a slow, tempo-synced loop, humanized, feeding the same master bus. Synth-based — no samples, no RAM cost. |

The FAB lets the user swap between them live; the choice is persisted to local storage
and re-applied on the next unlock.

---

## The one semantic decision worth flagging

Today the pink-noise bed **is** "audio on" — it starts iff `_audioEnabled` is true, and
the AudioFAB mute silences it along with everything else. This feature **decouples
"which backing texture" from "is audio muted":**

- **`IsAudioEnabled` (AudioFAB)** stays the master gate. Muted → *everything* silent,
  including whatever backing texture is selected. Unchanged.
- **Backing-texture selector (new FAB)** chooses *which* filler plays *when audio is
  enabled*. `Off` = no filler; `Noise`/`Percussion` = that layer.

The two gates compose: a layer runs iff `_audioEnabled AND mode selects it`. Defaulting
the new setting to `Noise` preserves the current sound exactly until the user changes it.

---

## 3. Relationship to the deferred Drumkit doc (important — they do NOT collide)

`docs/DRUMKIT_AND_DENSITY_DESIGN_DOCUMENT.md` (Status: DEFERRED) also proposes
percussion. It must not be confused with this feature; they are **different things by
design**, and this doc does not resolve or supersede that one.

| | This doc (Backing Texture) | Drumkit doc (deferred) |
|---|---|---|
| **What a hit represents** | Nothing — it's a **continuous filler loop**, a sibling of the pink-noise bed | A **real transit event** (a vehicle crossing a checkpoint / entering-leaving a route) |
| **Trigger model** | Fixed-tempo `Tone.Loop` on `Tone.Transport` — a decorative bed | Event-driven off `triggerNote`, no sequencer — the drumkit doc explicitly *rejects* a "bolted-on decorative loop" |
| **Open question** | None — it's deliberately just a bed | Unresolved: "what real signal should a drum hit represent" (§2 there) |
| **User-facing** | Yes — a selectable config | Not specified as user-selectable |

The drumkit doc's core objection — *"becoming a bolted-on decorative loop"* — is
precisely what this feature **is**, and that's fine, because it's framed honestly as
*filler*, not as emergent-music-from-transit. The two can coexist later: this backing
loop is the ambient bed; a future event-driven drumkit (if built) would be a separate
voice layered on top, driven by real crossings. **If the event-driven drumkit is ever
built, revisit whether the two percussion sources should be mutually exclusive** — that
is out of scope here and left as an open item (§10).

The one thing this doc reuses from the drumkit doc is its **settled voice palette** (§4
there): synthesized one-shot drums (`Tone.MembraneSynth` kick, `Tone.MetalSynth`
hat/rim), per-voice filter + volume. We take the kick + a rim/brush for the sparse kit.

---

## 4. Layer 1 — the engine (`transit-synth.js`)

The master bus already lazy-builds the noise node exactly once and gates it on
`_audioEnabled`. We generalize that single node into a **swappable backing layer** with
three states, mirroring the existing `setAudioEnabled` structure so the new code reads
like the code already there.

### New module state

```js
// 'off' | 'noise' | 'percussion' — the LIVE backing selection, re-checked like _audioEnabled.
// Defaults to 'noise' so a plain load reproduces today's behavior exactly.
let _backingMode = 'noise';
// Built lazily, once, like the master bus. { loop, kick, rim, volume } or null.
let _percussion = null;
```

### New export `setBackingTexture(mode)`

A near-copy of `setAudioEnabled`'s shape (the file's established pattern for a
live-toggled audio flag):

- Normalizes + records `_backingMode`.
- Resumes the AudioContext if it slipped to `suspended`/`interrupted` (the same
  defensive resume `setAudioEnabled` already does), because switching *to* a texture
  after a long idle mute must be able to make sound.
- Ensures **exactly one** backing layer runs (or none), gated by `_audioEnabled`:
  starts the selected layer, stops the other. Building the percussion layer lazily on
  first selection.
- Safe to call before the master bus exists: it records the flag and returns; both the
  master-bus build path and the unlock-warm path honor `_backingMode` when they later
  run. This matches `setAudioEnabled`'s "flag recorded even if bus not built yet"
  contract.

```js
export function setBackingTexture(mode) {
    _backingMode = (mode === 'off' || mode === 'percussion') ? mode : 'noise';
    if (!_masterBus) return;                 // recorded; honored when the bus builds
    _resumeContextIfNeeded();                // same helper setAudioEnabled uses
    _applyBackingLayer();                    // start/stop noise vs percussion per _audioEnabled & _backingMode
}
```

`_applyBackingLayer()` is the single choke point that reconciles `_audioEnabled` ×
`_backingMode` → which of `{noise, percussion}` is running. Both `setAudioEnabled` and
`setBackingTexture` call it, so the two gates never drift.

### Changes to existing functions

- **`getMasterBus`** currently ends with `if (_audioEnabled) noise.start()`. That becomes
  a call to `_applyBackingLayer()` after the noise node is wired, so first build honors
  the persisted mode (noise, percussion, or neither) instead of always starting noise.
- **`setAudioEnabled`** currently unconditionally restarts the noise bed on unmute. It
  must instead restart **whichever** layer `_backingMode` selects (and neither if `off`),
  and on mute must stop **both**. This is the one spot that genuinely needs care — a mute
  must silence percussion too. Routing both through `_applyBackingLayer()` gets this for
  free.
- **`dispose`** tears down `_percussion` (loop + synth voices) alongside the samplers and
  clears `_masterBus`.
- The `window.TransitSynth = { ... }` export map gains `setBackingTexture`.

### The percussion layer

Built lazily once (like the master bus), feeding the **same** master compressor so it
inherits the master glue + 4000 Hz softening every voice already gets:

```js
// Sparse lo-fi kit: soft low-tuned kick + a quiet rim/brush, on a slow Tone.Loop.
// Feeds bus.input (the master compressor), NOT Destination, so it sits under the mix
// like the noise bed. Humanized (velocity + small time jitter) to match the note
// humanization already in this file.
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
is the whole point, the exact voices/tempo/sparsity are best dialed in by ear first
(§9). The params above are a starting point, not final values.

New constants (grouped with the existing `NOISE_*` constants):
`PERCUSSION_VOLUME_DB`, the loop interval, the kick/rim synth params, and the rim
probability.

---

## 5. Layer 2 — interop + settings (C#)

- **`ITransitSynthJsInterop` / `TransitSynthJsInterop`**: add
  `SetBackingTextureAsync(string mode)` — a copy of `SetAudioEnabledAsync` that invokes
  `setBackingTexture` with the lowercase mode string.
- **`Settings.cs`**: add a new enum and a persisted property:
  ```csharp
  public enum BackingTexture { Off, Noise, Percussion }

  [ObservableProperty]
  [HiddenSetting]                        // the reflection-driven SettingsBlade only renders bools; skip this
  BackingTexture _backingTexture = BackingTexture.Noise;
  ```
  and **bump `Settings.CurrentVersion` 4 → 5** so older serialized settings discard
  cleanly (the existing version-guard pattern in `SettingsService.GetSettings`).
- `ISettingsService.SetSettingValue<T>` is already generic and reflection-based — it
  persists an enum with **no change**.
- `[HiddenSetting]` keeps the enum out of the pure-reflection `SettingsBlade` (which
  renders one checkbox per public bool). This setting lives on its own FAB, not the
  blade, so the blade's boolean-only invariant is preserved.

---

## 6. Layer 3 — the UI (`BackingTextureFab.razor`)

A new component under `Components/FABs/`, structured like **`CityFab`** (a `MatFAB` that
opens a `MatMenu` list) but wired like **`AudioFab`** (read persisted setting → on select,
persist + apply):

```razor
<div class="backing-texture-fab-container">
    <MatFAB Icon="graphic_eq" Mini="true" OnClick="OpenMenu" @ref="_button" />
    <MatMenu @ref="_menu">
        <MatList>
            <MatListItem><MatButton Label="No backing"       Mini="true"
                @onclick="() => Select(BackingTexture.Off)"        Disabled="@(_current == BackingTexture.Off)" /></MatListItem>
            <MatListItem><MatButton Label="Ambient noise"    Mini="true"
                @onclick="() => Select(BackingTexture.Noise)"      Disabled="@(_current == BackingTexture.Noise)" /></MatListItem>
            <MatListItem><MatButton Label="Lo-fi percussion" Mini="true"
                @onclick="() => Select(BackingTexture.Percussion)" Disabled="@(_current == BackingTexture.Percussion)" /></MatListItem>
        </MatList>
    </MatMenu>
</div>
```

```csharp
BackingTexture _current;
protected override void OnInitialized() =>
    _current = SettingsService.GetSettings().BackingTexture;

async Task Select(BackingTexture mode) {
    SettingsService.SetSettingValue(nameof(Settings.BackingTexture), mode);
    _current = mode;
    await TransitSynth.SetBackingTextureAsync(mode.ToString().ToLowerInvariant());
}
```

**No event bus.** Unlike `AudioFab` (whose mute state has multiple consumers, so it posts
`AudioSettingChangedEventArgs`), nothing else in the app reacts to the backing texture —
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
`SetBackingTextureAsync(persistedMode)` next to it. Because `setBackingTexture` is safe
to call before the master bus exists (§4), ordering relative to unlock/warm is not
fragile — the flag is recorded and honored when the bus builds.

Persistence is of the **enum only** — never live Tone.js nodes — rebuilt on reload, the
same discipline the rest of `transit-synth.js` follows.

---

## 8. Scope — files touched

| File | Change |
|---|---|
| `wwwroot/js/transit-synth.js` | `_backingMode`/`_percussion` state; `setBackingTexture` export; `_applyBackingLayer` choke point; `buildPercussion`; `getMasterBus`/`setAudioEnabled`/`dispose` updates; export-map entry; new `PERCUSSION_*` constants |
| `Services/JsInterop/ITransitSynthJsInterop.cs` + `TransitSynthJsInterop.cs` | `SetBackingTextureAsync(string)` |
| `Models/Settings.cs` | `BackingTexture` enum + `[HiddenSetting]` property; `CurrentVersion` 4 → 5 |
| `Components/FABs/BackingTextureFab.razor` (+ `.razor.css`) | new component |
| `Layout/MainLayout.razor` | mount the FAB |
| `TransitMap.razor.cs` (init) | push persisted texture on startup, beside `SetAudioEnabledAsync` |
| `Resources/RouteFilterResources.*.resx` | FAB menu label keys (EN) |

Frontend-only. No server / worker / shared changes.

---

## 9. Recommended first step — audition the percussion out-of-app

The percussion *sound* is the actual experiment; everything else is plumbing. Following
the `tools/instrument-compat/` precedent (a self-contained static HTML audition tool with
no build step, no app dependency), build a **throwaway HTML page** that reproduces the
master-bus chain (compressor → 4000 Hz filter → destination + the -38 dB pink bed) and
lets the designer tweak the kick/rim voices, loop interval, and sparsity live before any
of the params are pinned into `transit-synth.js`. This de-risks the one novel surface
(the `Tone.Transport` loop) without touching the .NET solution.

---

## 10. Deliberately out of scope / open items

- **Not** density-reactive and **not** a full groove — just the sparse lo-fi kit, to keep
  the first experiment small. (Density-reactive percussion is the drumkit doc's option
  (c), a separate, larger design.)
- **No** third+ texture yet — the enum makes adding more (e.g. vinyl crackle, rain) a
  one-line-per-option change, but ship two beyond Off first.
- **No** settings-blade entry (FAB-based by decision; enum is `[HiddenSetting]`).
- **Open:** if the event-driven drumkit (`DRUMKIT_AND_DENSITY_DESIGN_DOCUMENT.md`) is
  ever built, decide whether it and this backing loop should be mutually exclusive or
  layerable. Not decided here.
- **Open:** final percussion voice params — deferred to the §9 audition.
