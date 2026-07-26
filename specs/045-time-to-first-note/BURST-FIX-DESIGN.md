# Design: Fix reintroduced checkpoint-pulse burst

Status: implemented in `crossing-dispatcher.js`. First pass (v1, jitter only
the `null`/idle fallback) was insufficient — see "v1 was incomplete" below.
Second pass (v2) jittered both `null` and any computed delay `<= 0`, but
across a FIXED 250ms window, which still read as a burst at batch scale — see
"v2 was incomplete" below. Third pass (v3) scaled the jitter window with the
number of no-positive-delay crossings in the batch. Fourth pass (v4) added
per-vehicle coalescing of fallback crossings on top of v3. Fifth pass (v5)
thinned only the TONES of large backlogs, keeping every survivor's visual
pulse — which produced silent "ghost pulses" and was revised. Current version
(v6) density-caps WHOLE crossings: a capped random subset of survivors fires
pulse+trail+tone together; the rest don't fire at all — see "v4"–"v6" below.

## Symptom

Many checkpoint pulses (pulse + trail + tone) fire at the exact same instant,
audibly and visually, for every city. Previously fixed; now back.

## Root cause

This feature (045) intentionally reversed a prior fix. Commit `16de12d`
(2026-06-29) made SignalR join-replay carry **zero** crossings specifically to
kill a "rapid pulsing on load" burst. `contracts/join-replay.md` in this spec
put crossings back into join-replay (age-capped at `CrossingAgeCap` = 10s, see
`ILastBatchCache.cs`) to shorten time-to-first-note, and argued this was safe
because the client already drops/delays stale crossings via
`crossingDelayMsFor` (`vehicle-animator.js:184`).

`crossingDelayMsFor` (`vehicle-animator.js:184-219`) returns one of three
things for a given crossing:

- `null` — no usable motion state at all (unknown vehicle/route, or
  `phase === 'idle'`, line 218).
- a **positive** number — a real motion-model delay, ms until the dot reaches
  the checkpoint.
- a **non-positive number (`0`)** — the model says the dot has already
  reached/passed the checkpoint: `remainingM <= 0 → 0` (line 200, extrapolating
  phase), `distIntoSub <= 0 → 0` (line 211, interpolating phase), or
  `delay > 0 ? delay : 0` (line 215, interpolating phase, arrival already
  elapsed).

The join-replay contract's safety argument only accounted for the third case
("stale crossings produce a non-positive delay and are dropped/immediate") and
treated that as fine. It is not fine at batch scale: a single batch —
join-replay's age-capped backlog, or just a busy live tick (NYMTA observed
~85 crossings/tick at peak) — can contain dozens of crossings across
different vehicles that *all* resolve to `0` at once, whether because they're
freshly-seeded/idle (`null`) or because the model genuinely says "already
arrived" for many of them simultaneously (a real, not-even-rare coincidence
at fleet scale). Every one of those fires via `setTimeout(fn, 0)` in the same
task-queue tick — the burst.

## v1 was incomplete

The first version of this fix only changed the `null` case: `_delayForCrossing`
returned `null` instead of `0` for unknown/idle vehicles, and
`dispatchCrossings` jittered `null` results across a random window. This did
not fix the reported bug, because most of a large batch's simultaneous
pulses were going through the **second** path — `crossingDelayMsFor`
returning a literal `0` from the extrapolating/interpolating "already
arrived" branches (lines 200, 211, 215) — which is a real, valid `number`,
not `null`, so v1's `computed !== null` check let it straight through
unstaggered. At batch sizes like NYMTA's ~85 crossings/tick, "already
arrived" is common enough across many vehicles at once that this path,
not the idle/null path, was the dominant source of the burst.

## v2 was incomplete

v2 correctly routed every no-positive-delay crossing (null/0/negative) into
the random jitter path, so nothing collapsed onto the same task-queue tick
anymore — but it jittered them across a FIXED `FALLBACK_JITTER_WINDOW_MS =
250`. That window does not scale with the batch: on join-replay (route
geometry often not yet loaded → `crossingDelayMsFor` returns `null` for
everything; stale-seeded vehicles are `idle` → also `null`) or a busy NYMTA
live tick (~85 crossings, "already arrived" common at fleet scale), dozens of
pulses/tones still landed inside one quarter-second. Technically staggered,
perceptually the exact same burst. The doc's own "250 is a starting point —
tune by ear" caveat was the tell: the window needed to be a function of how
many crossings it has to absorb, not a constant.

## Fix (v3): scale the jitter window with the fallback count

`dispatchCrossings` now does two passes. The first computes every crossing's
motion-model delay and counts the fallback crossings (null/0/negative). The
window is then sized to give each fallback crossing ~`FALLBACK_SPACING_MS`
(150ms) of room, clamped to `[FALLBACK_WINDOW_MIN_MS, FALLBACK_WINDOW_MAX_MS]`
= [250ms, 8000ms]:

```js
const jitterWindowMs = Math.min(FALLBACK_WINDOW_MAX_MS,
    Math.max(FALLBACK_WINDOW_MIN_MS, fallbackCount * FALLBACK_SPACING_MS));
```

The second pass schedules exactly as v2 did — positive computed delays used
verbatim, everything else `Math.random() * jitterWindowMs`.

- 2 fallback crossings → 250ms window (small batches stay snappy; min clamp).
- 20 → 3s window: a short, natural flurry.
- 85 (NYMTA peak / join backlog) → 8s (max clamp): the backlog trickles out
  like ordinary traffic instead of detonating.

The 8s cap sits safely under the ~10s batch cadence, so one batch's spread
always drains before the next batch arrives; overlapping tails from
consecutive batches are fine (that's just continuous traffic). Random
placement within the window is kept from v2, and for the same reason: a
metronomic `index * step` ramp sounds mechanical; random lets some crossings
genuinely coincide while most spread out.

## v4: coalesce fallback crossings per vehicle

v3 fixed the *timing* collapse but exposed a *density* problem: even spread
across 8s, a large backlog is still one full pulse+trail+tone per crossing,
and a backlog batch is a replay of the past — each fallback crossing is a
checkpoint its vehicle already passed. A single vehicle can contribute
SEVERAL of them in one batch (join-replay backlog, or a dot that lagged
multiple checkpoints behind), so the batch re-performs each vehicle's recent
path. That's what still sounded unnatural.

v4 keeps only the MOST RECENT fallback crossing per vehicle — last in batch
order, since the server emits crossings in traversal order — and drops that
vehicle's earlier ones. Each recently-active vehicle announces itself exactly
once; its existence is respected, its history isn't re-performed. Properties:

- No tuning constant; the reduction is structural, not sampled.
- Live crossings (positive motion-model delay) are NEVER coalesced — they are
  current events, not backlog, and remain one-to-one with real crossings.
- The jitter window is sized by the coalesced count (`Map.size`), so the
  spread matches what actually fires.
- Coalescing is per-batch only; it cannot suppress crossings across batches.

Alternatives considered for the density problem and not (yet) taken: demoting
backlog notes to a quieter velocity register (needs a velocity param through
transit-synth's triggerNote); a hard probabilistic cap (rejected — silently
deletes crossings with no structural justification). Tone thinning was
initially deferred and then adopted as v5, below.

## v5 (revised away): thin fallback tones, keep every visual pulse

Even coalesced to one crossing per vehicle, a big backlog (NYMTA join: dozens
of vehicles) is still one full-salience TONE per vehicle. v5's theory was
that the two channels have different density tolerances — dozens of small map
pulses read as liveliness, dozens of tones as cacophony — so it fired
pulse+trail for every survivor but gave tones only to a density-capped random
subset (a `toneEligible` flag on `_fireOne`).

In practice the desync was worse than the density: silent "ghost pulses" on
the map broke the app's core pairing — a checkpoint pulse IS a note made
visible, so a pulse with no sound reads as a glitch, not as liveliness.
Superseded by v6.

## v6: density-cap whole crossings — no ghost pulses

Same budget and selection as v5, but the cap now gates the ENTIRE crossing:
survivors that make the random draw fire pulse+trail+tone together; the rest
of the backlog doesn't fire at all. Everything that fires is a complete
audio-visual event.

- Budget: `maxFires = max(FALLBACK_FIRE_MIN_COUNT, floor(jitterWindowMs /
  FALLBACK_FIRE_SPACING_MS))` = max(3, window/350ms) — ≈3 fires/sec, small
  backlogs (≤3) never capped. At the 8s max window that's ~22 fires
  regardless of how many dozens of vehicles survived coalescing.
- Selection is uniformly random per batch (partial Fisher-Yates over the
  survivor indices), so no route or vehicle is systematically dropped —
  which vehicles fire is a fresh draw every batch.
- Live crossings (positive motion-model delay) ALWAYS fire — only the
  replayed backlog is capped; the current music stays one-to-one with real
  crossings.
- `_fireOne` is back to its pre-v5 signature; unselected crossings simply
  never get a timer.

This IS the "hard probabilistic cap" the v4 notes rejected — but layered
AFTER structural coalescing (so it only ever drops backlog that coalescing
couldn't reduce), sized by a density budget rather than an arbitrary N, and
forced by an aesthetic constraint the audio-only thinning violated: pulse and
tone must arrive together or not at all.

Net effect on a large join: a natural ~3/sec scatter of complete pulse+tone
events across the window — fewer vehicles acknowledged than v5 showed
visually, but every acknowledgment is whole.

## Why the fix belongs in the dispatcher, not in `crossingDelayMsFor`

`crossingDelayMsFor`'s contract is "how long until the animated dot reaches
this checkpoint" for one crossing, computed from that vehicle's own motion
model. Reporting `0` (or less) when the dot has already arrived is the
*correct* answer for that one crossing in isolation — it's not a bug in the
function. The bug only exists at the batch level: many correct "already
arrived" answers collapsing onto the same instant. `dispatchCrossings`
already sees the whole batch at once and is the only place that knows how
many other crossings are firing in the same tick, so it's the right layer to
prevent simultaneous same-tick fires, independent of *why* a given crossing
had no positive delay to wait out.

## Fix (v2)

`_delayForCrossing` now returns the raw result of `crossingDelayMsFor`
whenever it's a `number` at all (positive, zero, or negative), and still
returns `null` only when the animator has no usable motion state whatsoever:

```js
function _delayForCrossing(c) {
    var anim = window.ChefMapAnimator;
    if (anim && typeof anim.crossingDelayMsFor === 'function') {
        var d = anim.crossingDelayMsFor(c.vehicleId, c.routeJoinKey, c.alongDistanceM);
        if (typeof d === 'number') return d;
    }
    return null;
}
```

`dispatchCrossings` jitters a crossing whenever it has **no positive delay to
wait out** — i.e. `_delayForCrossing` returned `null`, `0`, or negative —
using a random delay within a small window, and only uses the computed value
directly when it's a genuine positive wait:

```js
const FALLBACK_JITTER_WINDOW_MS = 250;

export async function dispatchCrossings(elementId, crossings, flags) {
    if (!crossings || crossings.length === 0) return;

    let synth;
    try { synth = await _getSynth(); }
    catch (e) { console.error('[CrossingDispatcher] synth import failed', e); return; }

    for (let i = 0; i < crossings.length; i++) {
        const c = crossings[i];
        const computed = _delayForCrossing(c);
        const delay = (computed !== null && computed > 0) ? computed : Math.random() * FALLBACK_JITTER_WINDOW_MS;
        setTimeout(() => { _fireOne(elementId, c, flags, synth); }, delay);
    }
}
```

This covers all three "no positive delay" branches in `vehicle-animator.js`
(200, 211, 215) as well as the `null`/idle case (218) — every crossing that
would otherwise fire at `t=0` now gets jittered, regardless of which branch
produced the non-positive result.

Random jitter (not a linear `index * step` ramp) is deliberate: some
crossings genuinely coincide — two different vehicles crossing checkpoints at
the same real moment is normal traffic, not the bug — and a metronomic ramp
would force perfectly even spacing that sounds mechanical rather than
"natural." With `Math.random()` some notes still land close together or
overlap by chance, most spread out, and nothing ever collapses onto a single
`t=0` instant. `250`ms is a starting point — wide enough to break up a burst
of dozens of crossings, narrow enough that the whole backlog still resolves
as roughly one perceptual "moment" rather than a drawn-out roll. Tune by ear.

## Why jitter (not dropping, not a fixed step)

Dropping "already arrived" / idle-vehicle crossings on join would silently
lose real crossings (defeats the point of 045 — those crossings are what
shortens time-to-first-note). Jittering preserves every crossing's
audio/visual event and spreads most of their fire times apart, while still
letting some land together by chance — which is what real, uncoordinated
vehicle crossings sound/look like. A fixed `index * step` ramp was
considered and rejected: it forces perfectly even spacing regardless of how
many crossings are in the batch, which reads as an artificial arpeggio
instead of ordinary traffic.

## What NOT to change

- `crossingDelayMsFor`'s motion-model math is correct and unrelated; leave it.
  Its `0`/negative returns are the *correct* per-crossing answer — the fix is
  about batch-level collision, not about that function's math.
- `ILastBatchCache`'s age cap / ordering is correct and is not the bug;
  leave it. (Its own comment in `join-replay.md:22-23` had the wrong theory
  for why replay was safe — this fix makes that theory hold in practice.)
- Do not touch `CrossingDetector.cs` reverse-direction detection; it's a
  separate, intentional 045 feature that only makes collisions more likely,
  not the cause.

## Test gap (not closed)

`LastBatchCacheCrossingExclusionTests.cs` only covers single-vehicle,
single-crossing replay (server-side). Ideally there'd be a client-side test
asserting: given N crossings across N vehicles (mix of idle and
"already-arrived") dispatched in one `dispatchCrossings` call, their fire
times are spread across `[0, FALLBACK_JITTER_WINDOW_MS)` rather than all
landing on `0`. No JS test harness exists in this repo (no `package.json`, no
jest/vitest, nothing in CI) — every other file in `wwwroot/js/` is untested
the same way, so standing one up is out of scope for this fix. Verified
manually instead.

## Files touched

- `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/crossing-dispatcher.js`
  — `_delayForCrossing` returns the raw computed number (incl. `0`/negative)
  instead of clamping to `0`, still `null` when there's no motion state at
  all; `dispatchCrossings` coalesces fallback crossings to the most recent
  one per vehicle (v4), jitters each survivor across a window scaled to the
  coalesced fallback count (v3):
  `clamp(fallbackCount * FALLBACK_SPACING_MS, FALLBACK_WINDOW_MIN_MS,
  FALLBACK_WINDOW_MAX_MS)` = clamp(count × 150ms, 250ms, 8000ms), and caps
  backlog fires at `max(3, floor(window / 350ms))` randomly-chosen survivors,
  each firing pulse+trail+tone whole — unselected backlog crossings don't
  fire at all (v6; live crossings always fire).
