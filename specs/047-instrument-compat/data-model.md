# Data Model: Instrument Compatibility Audition Tool

This tool has no database and no server; "data model" here means the in-memory/localStorage shapes that flow through the page's single JS module. All shapes are plain JS objects (no classes required). Field names below are descriptive; the implementing file may choose exact key names, but MUST keep the semantics and constraints listed.

## InstrumentSpec

The user-editable, persistable description of a candidate instrument. This is what's saved to `localStorage` and what `buildInstrument` consumes.

| Field | Type | Constraints / Notes |
|---|---|---|
| `id` | string | Stable unique id (e.g. generated at add-time), used as the React-less DOM/card key and as the density scheduler's instrument reference. |
| `name` | string | Free text, display-only label. Empty allowed but a placeholder default (e.g. "Instrument N") SHOULD be filled in if left blank. |
| `anchors` | array of `{ noteName: string, url: string }` | Minimum length 1. Default seed: two rows, `noteName` = `"C2"` and `"C3"`, `url` = `""`. `noteName` is the Sampler URL-map key (the true pitch of that recording — see Fidelity Notes); `url` is the full hosted sample URL. Both required (non-empty) before the instrument can be built. |
| `attack` | number (seconds) | Default `0`. |
| `release` | number (seconds) | Default `1.0`. |
| `volumeDb` | number (dB) | Default `0`. Optional per-instrument trim. |
| `durations` | array of duration-token strings | Subset of `['16n','16n.','8n','8n.','4n','4n.','2n']`. Default `['8n','8n.','4n']`. Minimum length 1. |

**Validation rules**:
- At least one anchor row with both a non-empty `noteName` and a non-empty `url` is required to attempt a build; the add-instrument form MAY block submission or MAY allow submission and immediately show a per-row validation error / Failed state — implementer's choice, but it must not silently no-op.
- `noteName` values should be recognizable pitch names (e.g. `C2`, `Eb3`) since they become Sampler map keys directly; the tool does not need to validate they're "real" notes beyond non-empty — an invalid note name is the user's own compatibility-testing mistake and will surface as a Sampler-level error if truly malformed.

## InstrumentVoice (runtime-only, never persisted)

The live, built representation of an `InstrumentSpec` once `buildInstrument` succeeds — the actual Tone.js node graph plus load-state bookkeeping. This is transient: destroyed and rebuilt on every page load, on Remove, and (simplest correct approach per design doc §5.6) on any edit to its underlying spec.

| Field | Type | Notes |
|---|---|---|
| `specId` | string | Back-reference to the `InstrumentSpec.id` it was built from. |
| `state` | enum: `"loading"` \| `"ready"` \| `"failed"` | Drives the card's visible state (FR-008/FR-009). Starts `"loading"` the instant `buildInstrument` is called. |
| `errorMessage` | string \| null | Populated only when `state === "failed"`; human-readable (surfaces the underlying `onerror`/fetch/decode failure). |
| `sampler` | Tone.Sampler instance \| null | Null until `onload` fires. |
| `chainNodes` | `{ filter, widener, volume, reverb }` | Built and wired together inside `onload`, after `await reverb.generate()`, per the fixed chain in Fidelity Notes below. |

**State transitions**: `loading → ready` (on successful `onload` + reverb generation + chain wiring) or `loading → failed` (on `onerror`, or on any thrown error during chain construction). There is no `ready → failed` or `failed → ready` transition in place — an edit to the spec disposes the old voice entirely and starts a fresh `loading` voice for the new spec (per design doc §5.6, "rebuilding is simplest and always correct — dispose the old Sampler first").

## ActivityLevel

A single global value, not a per-instrument setting.

| Value | Meaning | Approx. target rate (grounded in real MARTA telemetry, see research.md) |
|---|---|---|
| `"off"` | No new synthetic note events are scheduled. | 0/sec |
| `"low"` | Sparse, occasional single notes. | ~0.5–1/sec (quiet-tick floor, p25) |
| `"medium"` | Typical busy-tick rate — deliberately NOT an interpolated Low–High midpoint, since real activity is bimodal and rarely sits between quiet and busy. | ~4–5/sec (busy-tick floor, p75) |
| `"high"` | Busy, overlapping texture. | ~7–9/sec (p90-to-peak tick) |

Persisted verbatim in `localStorage`. Changing the value takes effect immediately (the scheduler reads current value on each tick/reschedule; no restart-the-page needed).

## MuteState / AudioUnlockState

Two independent booleans, not a single combined enum (per FR-017, "the audio-unlock control and the mute control MUST behave independently").

| Field | Type | Persisted? | Notes |
|---|---|---|---|
| `audioUnlocked` | boolean | No (always false on fresh load — a stale "true" from a prior session would be meaningless since `Tone.start()` must happen from a real gesture in *this* page life) | Set true only inside the Enable Audio click handler. |
| `muted` | boolean | Yes | Default `false` (unmuted) on first-ever run; restored from `localStorage` afterward. Gates the noise bed's `start()`/`stop()` and is re-checked at every note's fire time (see Fidelity Notes). |

## SyntheticNoteEvent (ephemeral, never persisted, not a stored entity)

Represents one "crossing" the density scheduler generates. Exists only for the duration of scheduling → firing.

| Field | Type | Notes |
|---|---|---|
| `instrumentSpecId` | string | Chosen uniformly at random among currently `"ready"` InstrumentVoices at schedule time. |
| `triggerIndex` | integer | Random, `0 ≤ triggerIndex < totalTriggers`. |
| `totalTriggers` | integer | Random, realistic range 8–24 (representing "checkpoints along a route"). |
| `note` | string | Derived: `noteForPosition(SCALE, triggerIndex, totalTriggers)` — never stored independently of the above two, always recomputed. |
| `durationToken` | string | Chosen uniformly at random from the target instrument's `durations` set at fire time. |
| `velocity` | number | `HUMANIZE_VELOCITY_MIN + Math.random() * (HUMANIZE_VELOCITY_MAX - HUMANIZE_VELOCITY_MIN)` = range `[0.75, 1.0]`. |
| `startTimeOffsetSec` | number | `(Math.random() * 2 - 1) * HUMANIZE_TIME_JITTER_SEC` = range `[-0.02, +0.02]` seconds, added to `Tone.now()`. |

## SessionState (the localStorage persistence envelope)

The single top-level shape written to `localStorage` (design doc §5.7 suggests a dedicated key such as `instrument-compat:instruments`; density/mute may share the same envelope or a sibling key — implementer's choice, single source of truth either way).

| Field | Type | Notes |
|---|---|---|
| `instruments` | array of `InstrumentSpec` | The full add-instrument history for this browser. |
| `activityLevel` | `ActivityLevel` | Last-selected density. |
| `muted` | boolean | Last-selected mute state. |

**Lifecycle**: Written on every mutating action (add/edit/remove instrument, change density, toggle mute) — simplest correct approach is "write the whole envelope on any change" given the expected scale (a handful to a few dozen instruments). Read once on page load to reconstruct `instruments` (each re-built via `buildInstrument`, independently reaching `ready`/`failed`) and to restore `activityLevel`/`muted`. "Clear all" (FR-022) removes the key entirely and disposes all live `InstrumentVoice`s, returning the page to first-run defaults (`instruments: []`, `activityLevel: "off"`, `muted: false`).

## Fidelity constants (verbatim — reproduced from `DESIGN_DOCUMENT.md`, not chosen by this plan)

These are not "data" the user manages — they are fixed constants the engine must reproduce exactly, listed here because `InstrumentVoice.chainNodes` and `SyntheticNoteEvent` construction depend on them directly, and Phase 1 design review needs them enumerated once, in one place, to check for drift.

```
SCALE = ['C2', 'Eb2', 'F2', 'G2', 'Bb2', 'C3', 'Eb3', 'F3', 'G3', 'Bb3']

FILTER_CUTOFF_HZ = 1800        (per-voice lowpass)
STEREO_WIDTH      = 0.4
REVERB_DECAY      = 1.4
REVERB_PRE_DELAY  = 0.02
REVERB_WET        = 0.35

MASTER_COMPRESSOR = { threshold: -18, ratio: 3, attack: 0.02, release: 0.25 }
MASTER_FILTER_HZ  = 4000
NOISE_VOLUME_DB   = -38
NOISE_FILTER_HZ   = 2000       (lowshelf)

DURATION_SECONDS (at 120 BPM, informational — Tone.js resolves tokens itself):
  16n=0.125  16n.=0.1875  8n=0.25  8n.=0.375  4n=0.5  4n.=0.75  2n=1.0

HUMANIZE_TIME_JITTER_SEC = 0.02
HUMANIZE_VELOCITY_MIN    = 0.75
HUMANIZE_VELOCITY_MAX    = 1.0
```

Chain order per voice (fixed): `Sampler → Filter(1800, lowpass) → StereoWidener(0.4) → Volume(voiceDb) → Reverb({1.4, 0.02, 0.35}) → masterBus.input`.

Master bus (built once, cached): `Compressor(MASTER_COMPRESSOR) → Filter(4000, lowpass) → Destination`, with `Noise('pink') → Filter(2000, lowshelf) → Volume(-38) → compressor` running continuously whenever unmuted.
