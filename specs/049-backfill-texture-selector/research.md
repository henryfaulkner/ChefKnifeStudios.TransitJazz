# Phase 0 Research: Selectable Backfill Texture

All Technical Context items resolved from the shipped codebase and the design
document (`docs/BACKING_TEXTURE_SELECTOR_DESIGN_DOCUMENT.md`). No open
`NEEDS CLARIFICATION` remain. The single spec clarification (persistence via the
existing settings mechanism) is reflected throughout.

---

## R1. How the persisted texture choice reaches storage and the engine

**Decision**: Add one enum property to the existing `Settings` model, persisted by
the existing `SettingsService` local-storage blob, and push it to the JS engine on
init beside the existing `SetAudioEnabledAsync` call.

**Rationale**: The 2026-07-25 clarification requires "the same way the app's existing
settings are persisted." Verified as-built:
- `SettingsService.GetSettings()` / `SetSettingValue<T>` (reflection-based) already
  persist any property on `Settings` — `SetSettingValue<T>` is generic and calls
  `prop.SetValue`, so an enum works with **no service change**
  (`SettingsService.cs:57`).
- `Settings.CurrentVersion` is currently **4** (`Settings.cs:10`); the version guard
  in `GetSettings` discards a stored blob whose `Version != CurrentVersion`
  (`SettingsService.cs:30`). Adding a field is a schema change → **bump to 5** so old
  blobs discard cleanly rather than deserializing without the new field.
- The init push site is `TransitMap.razor.cs:110`:
  `_ = TransitSynth.SetAudioEnabledAsync(_audioEnabled);` inside the same block that
  reads `SettingsService.GetSettings()`. The persisted texture is pushed there.

**Alternatives considered**:
- *A separate storage key for the texture* — rejected: contradicts the clarification
  (must reuse the settings mechanism) and fragments persistence.
- *Store in a new service* — rejected: `SettingsService` already handles this
  generically; a new service is pure overhead.

---

## R2. Enum vs. bool, and keeping the reflection-driven blade unaffected

**Decision**: Model the choice as `enum BackfillTexture { Noise, Percussion }` on
`Settings`, annotated `[HiddenSetting]`.

**Rationale**: The `SettingsBlade` renders **one checkbox per public bool** by
reflection; a non-bool would break that invariant. `[HiddenSetting]` already exists
(`Attributes/HiddenSettingAttribute.cs`) and is honored by both `SettingsBlade.razor`
and the `Version` property in `Settings.cs`. Marking the enum `[HiddenSetting]` keeps
it out of the blade — it lives on its own FAB instead. An enum (not two bools) is the
right shape because the states are mutually exclusive and the set is meant to grow
(third texture = one enum member + one menu item).

**Alternatives considered**:
- *A bool `IsPercussionBackfill`* — rejected: doesn't extend to a third texture
  without another orthogonal bool and an "which wins" rule; the design explicitly
  wants a single mutually-exclusive selector.
- *Render it in the blade with a custom control* — rejected: breaks the blade's
  pure-reflection boolean-only design for no benefit; the FAB is the chosen surface.

---

## R3. Generalizing the single noise node into a swappable backfill layer (JS)

**Decision**: Introduce module state `_backfillMode ('noise'|'percussion')` and lazy
`_percussion`, a single choke point `_applyBackfillLayer()`, and an export
`setBackfillTexture(mode)`. Route `getMasterBus`, `setAudioEnabled`, and
`setBackfillTexture` all through `_applyBackfillLayer()` so the `_audioEnabled ×
_backfillMode` reconciliation lives in exactly one place.

**Rationale**: Verified current shape:
- `getMasterBus` (`transit-synth.js:260`) ends with `if (_audioEnabled) noise.start();`
  — the exact line that becomes `_applyBackfillLayer()` after the noise node is wired,
  so first build honors the persisted mode.
- `setAudioEnabled` (`transit-synth.js:291`) currently starts/stops **only** the noise
  node. Routing it through `_applyBackfillLayer()` makes a mute stop **both** layers
  and an unmute restore **whichever** is selected — for free, no drift between the two
  gates.
- `_masterBus = { input: compressor, noise }` (`:272`) — percussion feeds
  `bus.input` (the compressor) so it inherits the master glue + 4000 Hz softening,
  exactly like the noise bed, and sits *under* the mix (not `Destination`).
- The defensive AudioContext resume already in `setAudioEnabled` (`:294–300`) is
  reused by `setBackfillTexture` so switching *to* a texture after a long idle/mute
  can still make sound.
- "Flag recorded even if bus not built yet" is an existing contract
  (`setAudioEnabled` returns early at `:293` when no bus); `setBackfillTexture`
  mirrors it (`if (!_masterBus) return;` after recording `_backfillMode`).

**Alternatives considered**:
- *Two independent start/stop code paths (one per gate)* — rejected: that is exactly
  how gates drift (a mute that forgets to stop percussion). One choke point is the
  design's core safety property (FR-008/010).

---

## R4. The percussion voice + the one genuinely new Tone.js surface (`Tone.Transport`)

**Decision**: Build `buildPercussion(T)` lazily once: a `Tone.MembraneSynth` kick +
`Tone.MetalSynth` rim/brush → per-voice filter/volume → a `Tone.Volume`
(`PERCUSSION_VOLUME_DB`) → `bus.input`, driven by a slow `Tone.Loop` (e.g. `'2n'`)
that triggers a soft kick every interval and an occasional (`~0.4` probability) rim,
humanized (velocity + small time jitter) like the existing note humanization. Starting
the loop starts `Tone.Transport`.

**Rationale**: The app has **never** used `Tone.Transport` — every existing sound
fires in real time off `_tone.now()` (`triggerNote`, `transit-synth.js:~470`). The
loop needs the Transport, so `buildPercussion` starts it. This is orthogonal to the
free-running note triggers (they don't schedule on the Transport), so starting it does
not affect them. The voice recipe (MembraneSynth kick + MetalSynth rim, per-voice
filter + volume, fixed pitch) is carried over from the now-superseded
`DRUMKIT_AND_DENSITY_DESIGN_DOCUMENT.md` §4 — a settled voice palette, not the rejected
event-driven trigger model.

**This is the one novel/risky surface**, so it is de-risked in the audition tool
(R5) before it touches the .NET solution. The params in the design doc
(`'2n'`, `C1` kick, `0.4` rim probability, volumes) are a **starting point**, not
final — the final values are the §9 audition output pinned as `PERCUSSION_*`
constants grouped with the existing `NOISE_*` constants (`transit-synth.js:204`).

**Alternatives considered**:
- *Sample-based lo-fi kit* — rejected: reintroduces the decoded-PCM RAM cost the app
  deliberately moved away from (memory notes: soundfont RAM regression). Synthesis is
  free.
- *Schedule hits off `_tone.now()` like notes (no Transport)* — rejected: a steady
  decorative loop is precisely what `Tone.Loop`/`Transport` is for; hand-rolling a
  self-rescheduling timer re-implements it worse.
- *Density-reactive / event-driven percussion* — rejected by the design (§3): revives
  the drumkit doc's blocking open question and needs live-vehicle telemetry piped into
  JS. Out of scope.

---

## R5. Where to audition the percussion sound

**Decision**: Iterate the existing `tools/instrument-compat/index.html` — add a
"Backfill" section (Noise/Percussion selector + live percussion knobs) using the
**same** `buildPercussion` recipe wired to the tool's existing `getMasterBus()`,
existing mute (fire-time re-check), and Enable-Audio unlock. Persist `backfill` +
`percussionParams` in the tool's existing localStorage session shape.

**Rationale**: The tool already reproduces the app's exact master bus byte-for-byte
(compressor `{threshold:-18, ratio:3, attack:0.02, release:0.25}` → 4000 Hz lowpass →
Destination, plus the −38 dB / 2000 Hz pink-noise bed). The percussion loop is a
sibling node on that identical bus, so auditioning there is **fidelity-accurate by
construction** — and it can be tuned *underneath a simulated soundscape* (the tool
loads real instruments + runs the density sim), which is the true test of a backfill.
A throwaway page would re-derive the bus and risk drift.

**Alternatives considered**:
- *A throwaway audition page* — rejected (drift risk; §9).
- *Tune by ear directly in the app* — rejected: no live knobs, slow iteration, and
  it couples sound-design iteration to .NET rebuilds.

**Output → app**: Once dialed in, the parameter values are **transcribed by hand**
into the `PERCUSSION_*` constants. The tool intentionally has no export (matches 047).

---

## R6. UI surface, mounting, and localization

**Decision**: New `BackfillTextureFab.razor` under `Components/FABs/`, structured like
`CityFab` (a `MatFAB` icon `graphic_eq` opening a `MatMenu` list of options, active
option `Disabled`) but wired like `AudioFab` (read persisted setting in
`OnInitialized`; on select → `SettingsService.SetSettingValue` + interop call). Mount
it in `MainLayout.razor` alongside the existing FABs. Labels via
`IStringLocalizer<RouteFilterResources>` (EN keys only).

**Rationale**: Both patterns are verified as-built:
- `CityFab.razor` = `MatFAB` + `MatMenu`/`MatList`/`MatListItem` with
  `Disabled="@(CurrentCity == …)"` on the active item — the exact menu shape reused.
- `AudioFab.razor` reads `SettingsService.GetSettings()` in `OnInitialized` and
  persists via `SetSettingValue(nameof(Settings.IsAudioEnabled), …)` — the exact
  read/persist shape reused (minus the event post, which this FAB omits).
- `MainLayout.razor` already mounts `<AudioFab/>`, `<MapStyleFab/>`, `<DarkModeFab/>`,
  `<InfoFab/>`, `<CityFab/>`, `<SettingsFab/>` — the new FAB slots in the same block.
- Constitution XII mandates the single canonical `RouteFilterResources.resx`; only
  the EN file exists today (no `.es`), so EN-only keys match the established 015/016/
  017 deferral.

**Alternatives considered**:
- *Put the selector in the SettingsBlade* — rejected: the blade is bool-only by
  reflection; see R2.
- *Post an event-args like AudioFab* — rejected: no second consumer (YAGNI); the FAB
  calls the interop directly.

---

## R7. Superseding the deferred drumkit document

**Decision**: At implementation time, add a one-line **SUPERSEDED by
specs/049-backfill-texture-selector / this feature** banner to the top of
`docs/DRUMKIT_AND_DENSITY_DESIGN_DOCUMENT.md`.

**Rationale**: The design doc (§3) rejects that doc's event-driven-percussion
direction and builds the continuous-loop sibling instead. Recording the rejection as
*closed* (banner) rather than leaving it *stale* keeps the docs honest. The only thing
carried over is that doc's settled synth-drum voice palette (§4), reused in R4.

**Alternatives considered**:
- *Delete the drumkit doc* — rejected: it holds the reasoning for why event-driven was
  rejected + the voice palette we reuse; a banner preserves that history.
