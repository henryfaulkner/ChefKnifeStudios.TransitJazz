# Contract: Internal Synthesis Engine

This tool has no network API and no external service boundary — its only "contract" is the internal JS module surface that the UI (add-instrument form, instrument cards, density/mute controls) calls into, and the fidelity contract against the live app's `transit-synth.js`. Documented here because Phase 1 design normally captures interface contracts, and getting this shape right is what makes the acceptance checklist (spec §6) testable function-by-function rather than only end-to-end.

## Module surface (recommended shape, from design doc §5.2)

### `getTone(): Promise<ToneModule>`
- Memoized. First call performs `await import('https://esm.sh/tone@15')`; subsequent calls return the cached module without re-importing.
- MUST NOT be called before a user gesture has occurred (see `enableAudio` below) — importing the module itself is fine pre-gesture (no audio starts from importing), but nothing in this contract should call `Tone.start()` except from the gesture handler.

### `enableAudio(): Promise<void>`
- Bound directly to the Enable Audio button's `click` handler, called synchronously (no `await` of anything else before this call within the handler).
- Body: `await Tone.start()`. On success, sets `audioUnlocked = true` and, if `muted === false`, starts the master bus's noise bed.
- Idempotent: calling again after already unlocked is a harmless no-op (or a resume-if-suspended safety check).

### `getMasterBus(): Promise<MasterBus>`
- Lazily builds the shared master bus (Compressor → Filter → Destination + pink-noise bed) **exactly once** per page life, per the constants in `data-model.md`, and caches it.
- The noise bed's `start()`/`stop()` state MUST track current mute state at build time and on every subsequent mute toggle (see `setMuted` below) — it is not "fire and forget."

### `buildInstrument(spec: InstrumentSpec): Promise<InstrumentVoice>`
- Input: an `InstrumentSpec` (see data-model.md). Output: resolves to an `InstrumentVoice` in `"ready"` state, or resolves/settles to one in `"failed"` state with `errorMessage` populated — **implementer's choice whether this rejects or resolves-with-failed-state**, but the caller (card renderer) must be able to distinguish ready vs. failed without an uncaught exception reaching the page.
- MUST: construct the Sampler from `spec.anchors` (map of `noteName → url`), and only inside its `onload` callback build `Filter/StereoWidener/Volume/Reverb`, `await reverb.generate()`, then `.chain(...)` into `(await getMasterBus()).input`.
- MUST NOT mark the resulting voice `"ready"` until reverb generation and chaining are complete (this is the FR-008 gate: not playable before Ready).
- On `onerror`: MUST set state to `"failed"` with a human-readable `errorMessage`, and MUST NOT throw an unhandled exception that could affect any other instrument's build in flight.
- Disposal: the caller (Remove button, or edit-triggered rebuild) is responsible for calling `.dispose()` on every node in `chainNodes` plus the `sampler` itself before discarding an `InstrumentVoice`. This contract does not auto-dispose on its own — no garbage-collection-based cleanup assumption.

### `triggerNote(voice: InstrumentVoice, triggerIndex: number, totalTriggers: number): void`
- Precondition: `voice.state === "ready"`. Callers (solo Play-note button, density scheduler) MUST check this before calling; the function itself MAY additionally no-op defensively if called on a non-ready voice.
- MUST re-check current mute state at call time (not just at schedule time) and no-op entirely if muted — this is the fire-time mute gate required by FR-015/US3, since density events are scheduled slightly ahead via `setTimeout`.
- Behavior when not muted: compute `note = noteForPosition(SCALE, triggerIndex, totalTriggers)`, pick a random `durationToken` from `voice`'s instrument spec's `durations`, compute humanized `velocity` and `startTime` per the constants in data-model.md, and call `sampler.triggerAttackRelease(note, durationToken, startTime, velocity)`.

### `setMuted(muted: boolean): void`
- Updates the shared mute flag read by `triggerNote`'s fire-time check.
- On mute → true: stop the noise bed immediately (`noise.stop()`).
- On mute → false: if `audioUnlocked`, resume the AudioContext if suspended, then restart the noise bed if not already running.
- Persisted immediately to the `SessionState` envelope (data-model.md).

### `setActivityLevel(level: ActivityLevel): void`
- Updates the density scheduler's target rate immediately (no restart-the-page or restart-the-timer-chain-from-scratch requirement, though implementation MAY choose to reset the reschedule timer using the new rate on next tick).
- `"off"` MUST stop scheduling of further `SyntheticNoteEvent`s; any already-`setTimeout`-scheduled ones may still fire (acceptable per FR-013).
- Persisted immediately.

### Density scheduler (internal, no fixed function signature mandated)
- On each generated `SyntheticNoteEvent`: pick uniformly at random among currently `"ready"` `InstrumentVoice`s; if none are ready, skip (no-op) this tick rather than erroring.
- MUST NOT fire anything while `activityLevel === "off"`.
- MUST re-check mute at each individual note's actual fire time via `triggerNote`'s own gate (scheduler does not need its own separate mute check, since `triggerNote` already gates).

## Fidelity contract (against the live app)

Any implementation of the above module surface is only correct if, for equivalent inputs, it is **acoustically indistinguishable** from `transit-synth.js` in the live app. Concretely, this means:

| Aspect | Must match live app |
|---|---|
| Tone.js version | v15, same `esm.sh` import site |
| `SCALE` array | Verbatim, same order |
| `noteForPosition` | Verbatim algorithm (clamp + linear round-to-nearest-scale-index) |
| Per-voice chain | Same node types, same order, same constants (1800Hz, 0.4, 1.4/0.02/0.35) |
| Master bus | Same node types, same order, same constants (compressor, 4000Hz filter, -38dB pink noise, 2000Hz lowshelf) |
| Humanization | Same jitter ranges (±20ms time, 0.75–1.0 velocity) |
| Mute semantics | Fire-time re-check, not just schedule-time |

Anything **not** in this table (duration-token selection method, instrument-selection-for-a-crossing method, density arrival-rate shape) is explicitly permitted to differ from the live app's real implementation, per research.md's documented rationale — those differences don't affect the acoustic fidelity a listener judges.
