# Research: Emergent Transit Soundscape v1

**Feature**: 009-transit-soundscape
**Date**: 2026-05-22
**Status**: Complete (no NEEDS CLARIFICATION remaining)

This document resolves the four research items identified in `plan.md` § "Phase 0 Research".

---

## R1. Tone.js loading pattern from Blazor WASM

**Decision**: Load Tone.js as a lazy ES-module import inside `transit-synth.js`, using a CDN URL. Specifically:

```js
let _tone = null;
async function getTone() {
    if (!_tone) {
        _tone = await import('https://esm.sh/tone@15');
    }
    return _tone;
}
```

The `TransitSynthJsInterop` C# class then loads `transit-synth.js` itself via the established lazy-module pattern (mirroring `AudioPlayerJsInterop`), and the JS module loads Tone.js the first time `unlock()` is called from a user gesture.

**Rationale**:
- Matches the existing codebase pattern for browser-side libraries — `audioPlayerJsInterop.js` uses the same `_content/...`-relative lazy import. No new build step, no bundler change, no `package.json`.
- Tone.js does not need to be present during the silent pre-interaction phase. Deferring its load until the first user gesture also defers the ~150 KB network cost from the critical path, so SC-006 (no time-to-first-vehicle regression) is satisfied by construction.
- ESM-on-CDN (`esm.sh` or `jsdelivr.net`) gives us a tree-shaken Tone.js without us running a bundler. `esm.sh` is the lower-latency choice in the US; jsDelivr is the fallback if esm.sh has an outage. The CDN URL lives in one place (`transit-synth.js` constant) so swapping is a one-line change.
- The autoplay-unlock contract is built into Tone.js: calling `await Tone.start()` from a user-gesture handler is the documented and only correct unlock path. The `unlock()` exported function on `transit-synth.js` is a thin wrapper.

**Alternatives considered**:
- **Bundle Tone.js into the WASM `wwwroot`**: would add it to the initial download, regressing SC-006 with no upside. The CDN's caching is also better than ours for a stable library version.
- **Use `<script src>` in `index.html`**: would force eager loading and pollute `window.*`. Inconsistent with the rest of the codebase, which has fully migrated to ES-module imports.
- **Use a different synthesis library (Howler.js, raw Web Audio, the Web Audio AudioWorklet API)**: Howler is sample-oriented and we explicitly rejected samples; raw Web Audio puts the burden of scale/note/scheduling primitives on us (this is what 008's `checkpoint-audio.js` would have been); AudioWorklets are overkill for a few oscillator-per-route synths.

**Notes for the implementer**:
- Pin a major-minor: `tone@15` not `tone@latest`. A surprise major-version bump can break Tone's API.
- Tone's `AudioContext` is shared globally per-page. Multiple `Tone.Synth` instances are fine; they all route to the same destination by default.
- If a synth has zero polyphony budget set, the second concurrent note steals the first — set `polyphony: 4` or use `PolySynth` so simultaneous vehicle notes on the same route coexist (FR-012).

---

## R2. Crossing-detection algorithm

**Decision**: Per-vehicle state `{ routeId, lastTriggeredIndex, lastTriggerTimeMs }`. On each position event for a vehicle:

1. If `routeId` changed (vehicle transferred routes): reset state with `lastTriggeredIndex = currentIndex` and fire nothing. (Treat as fresh appearance.)
2. If no prior state (first observation): record `lastTriggeredIndex = currentIndex` and fire nothing. (Spec edge case: no retroactive triggers.)
3. Compute `delta = currentIndex - lastTriggeredIndex`.
4. If `|delta| > teleportThreshold` (e.g., > 50 polyline vertices in one tick): treat as teleport. Reset to current index, fire nothing. (Spec edge case: GPS glitch.)
5. If `delta <= 0`: vehicle did not move forward along the polyline this tick. Fire nothing. (Direction reversal or jitter is silently ignored by the index-monotonicity rule.)
6. If `delta > 0`: find all trigger points with `lastTriggeredIndex < triggerIndex <= currentIndex`. For each, check the cooldown: if `(now - lastTriggerTimeMs) < cooldownMs`, suppress; otherwise emit a `CrossingEvent` and update `lastTriggerTimeMs = now`. Update `lastTriggeredIndex = currentIndex`.

`cooldownMs` defaults to 2000 ms — long enough to suppress the most pathological oscillation jitter, short enough not to throttle musical rate. The spacing distance (R3) is what actually controls cadence.

**Rationale**:
- The "monotonic forward-index" rule cleanly handles every edge case in the spec:
  - **Stopped bus oscillating around a trigger** (FR-007): `currentIndex` jitters by ±1. Forward jitters fire once (the first crossing), then `lastTriggeredIndex` catches up; backward jitters are silently ignored. Net: ≤ 1 fire per oscillation window, satisfied by either the monotonicity rule alone or — for vehicles whose index actually does advance and retreat past the same trigger — the 2 s cooldown.
  - **Vehicle teleport** (FR-010): the `|delta| > teleportThreshold` branch prevents firing every trigger between the two positions.
  - **Vehicle appears mid-route** (FR-009): the "no prior state" branch sets the baseline at current position and fires nothing.
  - **Direction reversal**: handled by `delta <= 0` returning early.
- The algorithm uses information the animator already computes. `ChefMapAnimator.findNearestIndex(coords, point)` is exactly what we need to find `currentIndex`; we just call it from inside the tracker rather than re-implementing.
- Per-tick cost: for each vehicle that moved, one `findNearestIndex` call (O(polylineLength) — already the animator's cost profile) + iteration over the at-most-few trigger points in the index delta. Total well under one frame at 60 fps for ≤ 50 vehicles.

**Alternatives considered**:
- **Distance-from-trigger threshold** ("vehicle within 20 m of a trigger fires it"): simple to implement but produces double-fires (entering and exiting the threshold), worse jitter behavior, and an extra tuning knob.
- **Line-segment intersection** ("vehicle's segment from prev to curr crosses a perpendicular gate line at the trigger"): mathematically clean but vastly more expensive per tick and doesn't survive the snap-to-polyline geometry — the bus doesn't move in straight lines.
- **Cooldown-only, no monotonicity rule**: would require a much larger cooldown (~10 s like the 008 POC) to suppress oscillation, which directly fights the cadence requirement (SC-005).
- **C#-side detection** (sending currentPos per vehicle per tick to C# via SignalR or invokeMethodAsync): per-frame round-trip cost would dominate; rejected in the plan's complexity-tracking note.

**Notes for the implementer**:
- `teleportThreshold` should be tuned in absolute distance, not vertex-count, since polyline vertex density varies. Use `cumDist[currentIndex] - cumDist[lastTriggeredIndex] > 2000` (2 km in one tick = teleport).
- When the route geometry isn't loaded yet (FR-011), the tracker silently drops the vehicle's events. The vehicle's tracker state is created on the *first event for which routeGeometry exists*, satisfying the "no retroactive triggers" rule cleanly.

---

## R3. Spacing tuning (`triggerSpacingMeters`)

**Decision**: `triggerSpacingMeters = 200` as the initial default. Exposed as a `const` at the top of `TriggerPointGenerator.cs` for easy adjustment during manual verification.

**Rationale**:
- SC-005 requires cadence in `[5 s, 30 s]` per vehicle during continuous motion. Inverting that: a bus traveling at speed `v` m/s must cross trigger points at intervals between `5v` and `30v` meters.
- MARTA bus typical-speed band, in m/s: `5–15` (≈ 11–34 mph). City driving spends most of its time at the lower end; expressways briefly hit the upper end.
- Trying `spacing = 200 m` against this:
  - At 5 m/s (≈ 11 mph, slow city): 200 / 5 = **40 s per trigger** → slightly above the 30 s upper bound. Acceptable: very slow city driving will sound sparse, which is *truthful to the experience*.
  - At 10 m/s (≈ 22 mph, typical): 200 / 10 = **20 s per trigger** → comfortably inside the band.
  - At 15 m/s (≈ 34 mph, fast): 200 / 15 = **13 s per trigger** → comfortably inside the band.
- Going smaller (100 m): at 15 m/s gives **6.6 s per trigger** → near the 5 s lower bound, and concurrent vehicles on the same route stack to a near-continuous note stream. Rejected as the default.
- Going larger (400 m): at 5 m/s gives **80 s per trigger** → well outside the band on slow vehicles. Rejected.

**Alternatives considered**:
- **Per-route spacing tied to expected speed**: would push slow-route triggers closer together and fast-route triggers farther apart, equalizing perceived cadence. Tempting but adds complexity and a data source (per-route speed estimates) we don't have. Punt to a future tuning pass.
- **Stop-based triggers** (one per GTFS stop): would inherit MARTA's stop density, which is correlated with passenger density and arguably more "meaningful" musically. Rejected because it's a different feature (different data source, different generation pipeline) and the user explicitly chose uniform spacing.
- **Beat-grid spacing** (set spacing to make notes hit a tempo at assumed speed): elegant but only works at the assumed speed; buses that speed up or slow down go off-beat, which would sound worse than uniform.

**Notes for the implementer**:
- The constant should be in one place. `TriggerPointGenerator.cs` is the right home — it's the only consumer.
- Document the SC-005 derivation as a comment on the constant so future tuning passes know what band to stay in.
- During manual verification (quickstart step 5), if cadence feels wrong, try 150 m or 250 m before deciding the algorithm needs deeper revision.

---

## R4. Instrument palette and deterministic assignment

**Decision**: A palette of **6 Tone.js voice presets** chosen for audible distinction and chordal compatibility:

| Slot | Tone.js voice | Character | Rationale |
|------|---------------|-----------|-----------|
| 0 | `PolySynth(Synth)` with triangle wave + slow attack | Soft pad | Sustained, foundational |
| 1 | `PolySynth(AMSynth)` | Bell-like | Distinctive plucked-bell timbre |
| 2 | `PluckSynth` | String pluck | Clearly percussive, decays fast |
| 3 | `PolySynth(FMSynth)` with low modulation index | Soft mallet | Wooden, warm |
| 4 | `MembraneSynth` | Pitched kick / tom | Low-register punch |
| 5 | `MetalSynth` (tuned, short envelope) | Metallic bell | Bright, cuts through mix |

Route-to-slot mapping: `paletteIndex = stringHash(routeShortName) % 6`. The hash is a simple djb2-style accumulator — deterministic, no crypto needed.

Pitch derivation: a single shared **C-minor pentatonic scale** across two octaves — `[C3, Eb3, F3, G3, Bb3, C4, Eb4, F4, G4, Bb4]` (10 pitches). Per-vehicle pitch: `midiIndex = stringHash(vehicleId) % 10`.

**Rationale**:
- **Six voices, not eight**: keeps the palette small enough that each is memorable; with MARTA's ≈ 40 routes, the average bucket holds ~7 routes sharing an instrument — acceptable per the spec's assumption that more routes than palette entries is OK.
- **All Tone.js built-ins**: no custom DSP. Each voice has documented good defaults; we override only `polyphony: 4` and an envelope tweak where useful.
- **Pentatonic scale (no semitones)**: any combination of pitches is consonant. This is the cheapest possible solution to FR-005 (concurrent notes harmonize) and SC-003 (no audibly-dissonant concurrent pitches). C minor is conventionally somber/contemplative, which fits a city-soundscape vibe better than C major would.
- **Two octaves, 10 pitches**: enough variety that two vehicles on the same route rarely play the same pitch; small enough that a listener can perceive the route as a coherent voice rather than a chromatic scatter.
- **`stringHash` deterministic**: same vehicle ID → same pitch across sessions and reloads (FR-004). Same route ID → same instrument across sessions and reloads (FR-003).
- **Palette assignment by short name**: aligns with Principle VI (GTFS-RT routes are keyed by `routeShortName`). Avoids the risk that two builds of the same MARTA route end up with different instruments because the *internal* `routeId` was reassigned.

**Alternatives considered**:
- **Sample-based instruments (SoundFont / SFZ / SF2 via smplr or Tonejs-Instruments)**: would sound dramatically more "real," but ships 5–20 MB of audio samples per instrument. Rejected per user choice in the spec phase. Worth revisiting in a v2.
- **Per-route key signatures** (each route uses a different scale): more harmonic variety but breaks the simple "any combination is consonant" guarantee. Rejected for v1 per spec assumption.
- **Modulating key over time** (the soundscape modulates through a chord progression): genuinely interesting, would make the city feel like it's composing in real time. Out of scope for v1; logged as a future direction.
- **Larger palette (12+)**: more variety but harder to remember individual routes by sound. Rejected.

**Notes for the implementer**:
- Place the palette table and the hash function in `transit-synth.js` so all the musical decisions live in one file.
- `MembraneSynth` and `MetalSynth` are loud by default — set `volume: -12` (dB) on those two slots to balance the mix.
- Tone.js instruments are constructed lazily *per route* (not per vehicle), so a route with zero active vehicles costs nothing. Construct on first call to `triggerNote(routeId, …)`.

---

## Resolved unknowns

All four research items closed. No remaining `NEEDS CLARIFICATION` markers. Ready for Phase 1.
