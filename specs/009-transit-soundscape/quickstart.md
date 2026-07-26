# Quickstart: Manual Verification — Transit Soundscape v1

**Feature**: 009-transit-soundscape
**Date**: 2026-05-22

Manual verification protocol mapped 1:1 to the spec's Success Criteria. Run all seven tests in sequence in a single session unless a test isolation requirement says otherwise. Use headphones or a quiet room — several tests depend on subjective listening judgments.

## Prerequisites

- AppHost running locally (`dotnet run --project src/Orchestration/ChefKnifeStudios.TransitJazz.AppHost`) OR pointed at a deployed build.
- MARTA service hours: tests assume at least one active bus is in motion. If running off-hours (late night / pre-dawn), skip the listening-based tests and verify only the silent-system behavior (edge case + SC-007).
- Browser: Chrome or Edge latest. Open the **devtools console** before navigating to `/transit-map` so the pre-interaction phase is captured.

---

## Test 1 — First-note latency (SC-001)

**Steps**:
1. Cold-load `/transit-map` (hard refresh, cache disabled).
2. Wait for the map to render and at least one vehicle marker to appear.
3. Click anywhere on the page once. Start a stopwatch.
4. Listen.

**Pass criteria**:
- An audible musical note plays within **30 seconds** of the click, assuming a vehicle was already in motion.
- The "click to enable audio" hint overlay disappears on the click.
- Browser console shows a single `[TransitSynth] unlocked` log line.

**Fail diagnostics**:
- No note in 30 s with visible moving vehicles → check `[CheckpointTracker]` logs for crossing events. If crossings are firing but no audio, the synth unlock failed.
- No note in 30 s with no moving vehicles → invalid test run; retry when buses are active.

---

## Test 2 — Distinct route timbres (SC-002)

**Steps**:
1. (Continuing from Test 1.) Confirm at least 3 different routes have at least one active vehicle each (count distinct route-line colors with active markers).
2. Listen for 2 minutes.

**Pass criteria**:
- Subjectively, the listener perceives **at least 2 distinct instrument timbres** (target: 3 or more). One instrument family per route — different routes sound like different things, not pitches of the same thing.

**Fail diagnostics**:
- All notes sound similar → the palette diversity is insufficient; revisit research § R4 palette choices.
- Cannot identify the source route of any note → expected for a passive listener; this test passes if instruments are distinct, not if the listener can map each instrument back to a specific route on the map.

---

## Test 3 — Harmonic compatibility (SC-003)

**Steps**:
1. Identify a route with at least 2 active vehicles (look for two markers of the same color in motion).
2. Listen for 2 minutes, attending specifically to that route's instrument timbre.

**Pass criteria**:
- When two notes from the same route play within ~1 second of each other, the combined sound is **harmonically compatible** — no audible dissonance.
- Subjective judgement test: "this sounds like music, not noise."

**Fail diagnostics**:
- Dissonant intervals heard → the scale is being violated; check that `pitchFor` always indexes into the pentatonic scale.
- Audio glitches / clipping → polyphony cap is too low; check `polyphony: 4` is set on the affected instrument's `PolySynth`.

---

## Test 4 — Stopped-bus suppression (SC-004)

**Steps**:
1. Locate a vehicle that is stationary (at a stop, traffic light, or end-of-line layover). Easiest to find: route-end terminals or downtown rush-hour intersections.
2. Confirm in console that this vehicle's tracker state exists: filter logs for the vehicle's ID.
3. Observe for **60 seconds**.

**Pass criteria**:
- The stopped vehicle produces **at most 1 note** in 60 seconds.
- When the vehicle resumes motion, notes from it resume at the expected cadence (Test 5).

**Fail diagnostics**:
- Repeating notes from a stationary vehicle → either the index-jitter is crossing a trigger repeatedly (check `lastTriggeredIndex` log line; if it increments then decrements then increments, the monotonicity rule needs scrutiny) or the cooldown is too short.

---

## Test 5 — Cadence band (SC-005)

**Steps**:
1. Pick a route with **one** active vehicle (isolation makes this test easier; otherwise focus on the loudest/clearest instrument).
2. Confirm the vehicle is moving steadily (not stopped, not crawling).
3. Time the interval between successive notes attributable to that vehicle for 5 successive notes.

**Pass criteria**:
- Median interval falls within **[5 s, 30 s]**.
- No interval exceeds 30 s during continuous motion.

**Fail diagnostics**:
- Intervals too long (> 30 s) → either the bus is in a low-speed regime (acceptable), or `triggerSpacingMeters` is too large; try 150 m and re-test.
- Intervals too short (< 5 s) → spacing is too small; try 250 m and re-test.
- Intervals erratic → the bus is alternating fast and slow segments (expected real-world behavior, not a bug).

---

## Test 6 — No regression in time-to-first-vehicle (SC-006)

**Steps**:
1. Open browser devtools → Performance tab.
2. Hard refresh `/transit-map` with throttling off, recording from before navigation.
3. Stop recording when the first vehicle marker is visibly on the map.
4. Note `DOMContentLoaded` time and time-to-first-vehicle-marker.
5. Compare against the same measurement on the prior production build (pre-009 baseline).

**Pass criteria**:
- Time-to-first-vehicle increases by **≤ 10%** vs. baseline (within measurement noise).
- No new long-tasks in the Performance flame chart attributable to `transit-synth.js`, `checkpoint-tracker.js`, or trigger-point generation.

**Fail diagnostics**:
- Significant regression → check that Tone.js is NOT loaded before the unlock click (network tab should show no `tone` request until the gesture).
- Trigger-point generation appears in flame chart → confirm it runs once per route, not per tick.

---

## Test 7 — Zero console errors (SC-007)

**Steps**:
1. Hard refresh `/transit-map` with devtools console open and "Preserve log" enabled.
2. **Do NOT click** for the first 60 seconds. Let the pre-interaction phase run.
3. After 60 s, click anywhere. Continue for 4 more minutes (5 min total).
4. Inspect console.

**Pass criteria**:
- **Zero red error log lines** for the entire 5 minutes.
- Warnings about autoplay restrictions (yellow) are acceptable *before* the click; they should not appear after.
- The transition from pre-interaction to post-interaction is clean: `[TransitSynth] unlocked` log appears, then audio begins on the next crossing.

**Fail diagnostics**:
- Errors during pre-interaction → `TriggerNoteAsync` is not silently no-op'ing; check the `_unlocked` guard in `transit-synth.js`.
- Errors after first click → check the Tone.js version pinning; a surprise major-version bump can break the API.
- Errors during page unload → check that `CheckpointTrackerJsInterop` and `TransitSynthJsInterop` both implement `IAsyncDisposable` and the page disposes them.

---

## Edge-case spot checks (run if time permits)

- **No active vehicles**: Wait for a low-traffic period (early morning weekend). Confirm the page is silent for ≥ 60 s and the console shows no errors.
- **Vehicle teleport**: If you observe a vehicle marker jump multiple kilometers on the map (rare but happens — driver swapped trips, GPS glitch), confirm the audio does not produce a burst of rapid notes for that vehicle. The jump should be followed by a single re-baselined silent period before the next legitimate crossing.
- **Route geometry not yet loaded**: Refresh the page and observe the brief window where vehicle markers appear before route lines. During that window, no audio should play for any vehicle whose route has not yet loaded.

---

## Sign-off

When all 7 tests pass:

- Update `MEMORY.md` with the actual measured cadence and palette feedback from listening sessions.
- Remove the stale `project_008_checkpoint_audio.md` memory note (the 008 implementation never landed).
- Annotate the spec's Status from "Draft" to "Verified".
