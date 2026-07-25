# Research: Instrument Compatibility Audition Tool

All technical unknowns for this feature are pre-resolved by `tools/instrument-compat/DESIGN_DOCUMENT.md`, which was written as an exhaustive, ready-to-build spec distilled from the live app's `transit-synth.js` and `crossing-dispatcher.js`. There is no open technology selection to research — the task is faithful reproduction, not evaluation of alternatives. This document records the decisions and their provenance so Phase 1 design artifacts can reference them without re-deriving them.

## Decision: Tone.js version & load mechanism

- **Decision**: Load Tone.js v15 via `import('https://esm.sh/tone@15')` inside a `<script type="module">`, exactly matching the app's import site and version pin.
- **Rationale**: Different Tone.js major versions can change default DSP behavior (reverb algorithm, compressor curve, filter slope). Using a different version would silently break the tool's entire purpose — acoustic fidelity to the live app. Pinning the same CDN + major version is the cheapest way to guarantee identical behavior without vendoring the library.
- **Alternatives considered**: Bundling Tone.js locally (rejected — the design doc mandates zero build step, and a local copy risks drifting from the app's pinned CDN version over time); using a different audio library entirely (rejected — the entire premise is chain-for-chain reproduction of the app's existing Tone.js chain).

## Decision: Audio unlock gesture handling

- **Decision**: A single "Enable Audio" button whose click handler calls `await Tone.start()` synchronously as the first async boundary, with no `await` of anything else beforehand. No autoplay attempt on load/`DOMContentLoaded`.
- **Rationale**: Browsers (particularly iOS Safari) only honor `AudioContext.resume()`/creation inside the call stack of a genuine user gesture; the "trusted gesture window" closes the moment any other `await` runs first. The app already learned this the hard way (documented in design doc §3.2). Reproducing the gesture-first pattern exactly avoids re-discovering the same iOS silent-audio bug.
- **Alternatives considered**: Attempting a best-effort auto-unlock with a fallback button (rejected — adds complexity and risks the exact bug the app already fixed; explicit-only is simpler and matches design doc mandate).

## Decision: Note vocabulary & position→pitch mapping

- **Decision**: Reproduce `SCALE = ['C2','Eb2','F2','G2','Bb2','C3','Eb3','F3','G3','Bb3']` and `noteForPosition(scale, triggerIndex, totalTriggers)` verbatim (design doc §3.3).
- **Rationale**: This is the app's actual pitch vocabulary (C-minor pentatonic across low octaves for consonant overlap per constitution Principle VIII). Any deviation — different scale, different octave range, different interpolation — makes the tool's pitch judgments meaningless for predicting how a candidate instrument will sound in the real app.
- **Alternatives considered**: None — this is a direct verbatim-reproduction requirement, not a design choice with tradeoffs.

## Decision: Per-voice and master-bus signal chain

- **Decision**: Per instrument: `Sampler → Filter(lowpass, 1800Hz) → StereoWidener(0.4) → Volume(voiceDb) → Reverb(decay 1.4, preDelay 0.02, wet 0.35) → masterBus.input`. Shared master bus (built once, lazily, cached): `Compressor(threshold -18, ratio 3, attack 0.02, release 0.25) → Filter(lowpass, 4000Hz) → Destination`, plus a continuous `Noise('pink')` bed at `-38dB` through a `Filter(lowshelf, 2000Hz)` mixed into the same compressor.
- **Rationale**: This is the app's actual production chain (design doc §3.5–3.6, marked "reproduce exactly" / "fixed"). The pink-noise bed is called out explicitly as intentional ambient texture, not incidental — omitting it would make the tool sound emptier/drier than the real app and mislead the compatibility judgment.
- **Alternatives considered**: A simplified chain (dry sampler only, or fewer effects) was explicitly rejected in the design doc's Fidelity Notes (§7) as breaking the tool's core purpose.

## Decision: Reverb build timing (async IR generation)

- **Decision**: Build each instrument's full chain — filter, widener, volume, reverb — inside the Sampler's `onload` callback, `await reverb.generate()` before wiring the chain, and only then mark the instrument Ready.
- **Rationale**: `Tone.Reverb` computes a convolution impulse response asynchronously; playing through an unwired/ungenerated reverb either errors or produces a dry, tail-less sound that doesn't match the app. Building once per instrument (not per note) avoids redundant IR generation cost on every play.
- **Alternatives considered**: Lazily building the reverb on first play (rejected — adds latency/inconsistency to the very first note, and complicates the Ready-state contract in FR-008, which requires the full chain built before Ready).

## Decision: Duration-token selection without a visual trail

- **Decision**: For each synthetic note, pick one duration token uniformly at random from the instrument's configured duration set, rather than deterministically hashing a vehicle/route id as the app does.
- **Rationale**: The app's deterministic hash exists solely to keep a note's duration in sync with its visual trail animation. This tool has no map, no trail, and no vehicle identity — there's nothing to stay in sync with, so determinism buys nothing here. Design doc §3.7/§7 explicitly sanctions randomization as an acceptable, sound-equivalent difference.
- **Alternatives considered**: Reproducing the deterministic hash anyway for hollow "fidelity" (rejected — pointless complexity with no perceptual benefit; the spec's Assumptions section explicitly allows this simplification).

## Decision: Density simulation approach (rate + scheduling shape)

- **Decision**: A self-rescheduling timer with randomized inter-arrival gaps (Poisson-ish), targeting rates of Low ≈0.5–1/sec, Medium ≈4–5/sec, High ≈7–9/sec. These are no longer pure ear-tuned guesses: they're grounded in real single-city telemetry queried via `mj-data-explorer`/`telemetry-query-bridge` against the `telemetry` dataset (MARTA, 2026-07-25, 103 `PerCityCycle` worker ticks, average tick-to-tick cadence ~11.7s, confirming the prior "~10s batch cycle" documentation). `tones_emitted` per tick was strongly bimodal, not a smooth ramp: p25 ≈5 tones/tick (quiet), median ≈8, p75 ≈49 (busy), p90 ≈63, max 103 — i.e. quiet ticks cluster near 5–8 tones and busy ticks cluster near 49–103, with little occupied in between. Dividing by the ~11.7s cadence gives Low ≈0.4–0.7/sec (p25/median tick), Medium ≈4.2/sec (p75 "typical busy tick"), High ≈5.4–8.8/sec (p90-to-peak tick) — rounded to the ranges above. Medium was deliberately anchored to the *busy-tick floor* rather than an interpolated Low–High midpoint, because real activity rarely sits at an intermediate rate (see spec.md Clarifications, 2026-07-25).
- **Rationale**: The app's real scheduling (10-second SignalR batch cycles with jittered `setTimeout` spread and a density cap) is server/network-driven and not meaningful to replicate exactly client-side; design doc §3.9 explicitly says the exact motion-timing math is "not needed here" — only the *feel* (sparse → busy, non-metronomic) matters, which is what spec FR-012/SC-003 test for (subjective distinguishability by ear). But "tuned by ear" alone risked drifting arbitrarily far from what the live app actually produces for one city; querying real `tones_emitted` telemetry keeps the three levels anchored to observed reality while still leaving room for by-ear fine-tuning within those ranges.
- **Alternatives considered**: A fixed-interval `setInterval` firing N notes per tick (simpler, explicitly allowed as an alternative in design doc §5.3) — viable fallback if the self-rescheduling approach proves harder to tune; both are acceptable per the design doc, self-rescheduling is preferred for a more organic feel. Modeling the combined 5-city rate instead of single-city (rejected — the all-city `FullCycle` rate ran ~3.7–88.6 crossings/sec across ticks, which reflects the whole deployed app's aggregate load across every city at once, not what a single candidate instrument realistically competes against in a plausible one-city mix).

## Decision: Instrument→note-event assignment (no routes to hash)

- **Decision**: Each synthetic crossing picks uniformly at random among the currently-Ready instruments (not a deterministic route-id hash, since there are no routes), and a pitch via a random `totalTriggers` (8–24) + random `triggerIndex` fed through the real `noteForPosition`.
- **Rationale**: The app derives instrument choice from a route-id hash into a fixed palette slot; this tool has a dynamically-grown, unordered instrument list instead of a fixed palette, so uniform-random selection is the closest fair analogue (every added candidate gets airtime) — directly specified in design doc §5.4 and spec FR-014.
- **Alternatives considered**: Round-robin cycling through instruments (rejected in design doc as unnecessary — uniform random already satisfies "fair airtime" and is simpler); letting the user pick which instrument plays each event (rejected — out of scope per design doc §2, no dedicated per-instrument solo/mute-in-mix toggle for v1).

## Decision: Persistence mechanism & shape

- **Decision**: `localStorage`, single key (e.g. `instrument-compat:instruments` for the instrument spec array; density/mute alongside or in a sibling key), storing instrument **specs** (name, anchor note/url rows, attack, release, volumeDb, durations) — never the live Tone.js node graph. On load, re-run `buildInstrument` per restored spec (re-fetching samples, so link rot surfaces as Failed rather than a false Ready).
- **Rationale**: Tone.js audio nodes are not serializable and are meaningless across a page reload (the AudioContext itself is destroyed); persisting only the declarative spec and rebuilding is the only sound approach. Design doc §5.7 and spec FR-019–FR-021 mandate this shape directly.
- **Alternatives considered**: No persistence (rejected — explicit spec requirement, User Story 4); server-side/account persistence (rejected — no backend exists or should exist per design doc §2/FR-023; localStorage is sufixed as sufficient in spec Assumptions).

## Decision: Failure surfacing for bad sample URLs

- **Decision**: Any Sampler `onerror` (404, CORS block, non-audio content, decode failure) sets that instrument's card to a distinct Failed state with the underlying error message, without throwing an unhandled exception or affecting other instruments' state.
- **Rationale**: CORS/host failures are expected and common when auditioning arbitrary third-party sample URLs (design doc §4 notes CORS is "a property of the sample host, not something the tool can fix"); the tool's job is to surface this clearly (spec FR-009, SC-004), not to work around it.
- **Alternatives considered**: Silent failure / no visible state change (explicitly rejected — spec edge cases and acceptance scenario US1-4 require a clear failed state with a reason).

## Open questions

None remaining. The design document, combined with the spec's Assumptions section, resolves every technical decision needed to proceed to Phase 1.
