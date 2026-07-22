// Crossing dispatcher — schedules a whole batch of checkpoint crossings from ONE interop
// call, replacing the old per-crossing C# Task.Delay + 4 sequential interop hops.
//
// Why this exists: at NYMTA scale a batch carries thousands of crossings. The previous design
// fired one fire-and-forget C# task PER crossing, each awaiting Task.Delay(OffsetMs) then FOUR
// sequential JS-interop calls (pulse, duration, trail, note). Thousands of timers + tens of
// thousands of marshaled calls all queued on WASM's single thread, so pulse/note/trail for a
// given crossing drifted apart from each other and from OffsetMs — worse the larger the fleet.
//
// Here: C# hands the entire batch (each item carrying alongDistanceM) plus the three gating flags
// in ONE call. We own the timers in JS, and each crossing's pulse + trail + note fire together off
// a single setTimeout — so a crossing's effects can never desync from each other, and there is
// exactly one interop crossing per batch regardless of fleet size. Each timer's delay is the time
// for the animated dot to actually reach the checkpoint (see _delayForCrossing).

let _synthModule = null;

// Live route-filter snapshot, pushed from C# whenever the selection/hover changes. null means
// "no filter — everything passes". A non-null Set holds the effective route keys (selection ∪
// hover). Timers re-check THIS at fire time, so deselecting a route silences its already-scheduled
// crossings within the frame instead of after the ~10s scheduling horizon drains.
let _activeFilter = null;

// C# pushes the current effective filter here on every selection/hover change (and at dispatch).
// Pass null/empty to clear the filter (all routes pass).
export function setActiveFilter(keys) {
    _activeFilter = (keys && keys.length > 0) ? new Set(keys) : null;
}

// Live audio-enabled flag, pushed from C# on every mute/unmute toggle. Re-checked at FIRE time
// (NOT captured per-batch in `flags`) for the SAME reason as _activeFilter: crossings are scheduled
// up to a full cycle (~10s) out, plus jittered idle-fallback ones. If this were the per-batch
// captured flags.audioEnabled, every crossing scheduled during a muted window would stay silent
// after unmute until that backlog drained — you'd hear the (live) noise bed return instantly but no
// checkpoint tones for several seconds (the "extended mute" symptom). Defaults true so audio behaves
// normally until C# says otherwise; TransitMap pushes the persisted setting on init.
let _audioEnabled = true;

// C# pushes the current mute/unmute setting here on every AudioSettingChanged toggle (and init).
export function setAudioEnabled(enabled) {
    _audioEnabled = !!enabled;
}

// A crossing is allowed to fire iff there is no active filter or its route is in the filter.
// Evaluated at FIRE time against the live _activeFilter, not at schedule time.
function _passesFilter(routeJoinKey) {
    return _activeFilter === null || _activeFilter.has(routeJoinKey);
}

// Lazily import the transit-synth ES module once; reused across batches. ChefMap is a global
// (window.ChefMap), so no import needed for pulse/trail.
async function _getSynth() {
    if (_synthModule) return _synthModule;
    // Sibling module in the same directory. This import() resolves RELATIVE TO this file's URL
    // (/_content/.../js/), so it must be a bare sibling path — a full /_content/... path would
    // resolve against this dir and DOUBLE it. Bare-name (no ?g= guid) so it's the SAME module
    // instance TransitSynthJsInterop imports → shared _unlocked state (feature 040).
    _synthModule = await import('./transit-synth.js');
    return _synthModule;
}

// Fire one crossing's three effects together. Duration is resolved in-JS (no interop round-trip)
// only when the trail is actually going to be drawn.
async function _fireOne(elementId, c, flags, synth) {
    // Re-check the live route filter at fire time. A crossing scheduled up to a cycle ago must
    // NOT fire if its route has since been deselected — this is what makes filter changes take
    // effect immediately instead of after the scheduling horizon drains (~10s).
    if (!_passesFilter(c.routeJoinKey)) return;

    if (flags.checkpointsVisible && window.ChefMap) {
        try { window.ChefMap.pulseCheckpoint(elementId, c.routeJoinKey, c.triggerIndex, c.alongDistanceM); } catch (_) { }
    }

    if (flags.crossingTrailVisible && window.ChefMap) {
        try {
            const durationSec = await synth.durationSecondsFor(c.vehicleId, c.routeJoinKey);
            window.ChefMap.startCrossingTrail(elementId, c.routeJoinKey, c.vehicleId, c.triggerIndex, durationSec, c.alongDistanceM);
        } catch (_) { }
    }

    // Re-check the LIVE mute state at fire time, not the batch-captured flags.audioEnabled — a
    // crossing scheduled while muted must sound if the user has unmuted by the time it fires
    // (and vice versa). See _audioEnabled/setAudioEnabled above.
    if (_audioEnabled) {
        try { synth.triggerNote(c.routeJoinKey, c.vehicleId, c.triggerIndex, c.totalTriggers); } catch (_) { }
    }
}

// Delay (ms) at which this crossing's tone/pulse should fire, per the animator's motion model
// (extrapolation at empirical speed, or interpolation along its subPath) — the tone-leads-dot fix
// (feature 040): timing tracks the animated dot exactly. Returns null when the animator has no
// usable motion state for the vehicle at all (idle phase, unknown vehicle/route), and returns a
// number <= 0 whenever the model says the dot has already reached/passed the checkpoint (e.g.
// vehicle-animator.js's `remainingM <= 0` / `distIntoSub <= 0` branches). Both cases are "no
// positive delay to wait out," and the caller (dispatchCrossings) jitters them the same way.
function _delayForCrossing(c) {
    var anim = window.ChefMapAnimator;
    if (anim && typeof anim.crossingDelayMsFor === 'function') {
        var d = anim.crossingDelayMsFor(c.vehicleId, c.routeJoinKey, c.alongDistanceM);
        if (typeof d === 'number') return d;
    }
    return null;
}

// Crossings with no positive delay (null from the animator, or a computed <= 0 — the dot has
// already reached/passed the checkpoint by the model) are jittered across a window SCALED to how
// many of them the batch contains. A single batch can legitimately bundle an entire tick's
// crossings across the whole fleet (join-replay's age-capped backlog, or a busy live tick — NYMTA
// peak observed ~85 crossings/tick, see specs/045-time-to-first-note/BURST-FIX-DESIGN.md). The
// previous FIXED 250ms window stopped the same-task-tick collapse but not the audible burst:
// dozens of pulses inside a quarter second still read as one blast. Scaling the window by the
// fallback count (~FALLBACK_SPACING_MS of room per crossing) keeps small batches snappy while a
// big backlog trickles out like ordinary traffic. Random placement within the window (not a
// fixed index*step ramp) so some crossings still genuinely coincide — coincident crossings are
// real, not the bug — and nothing sounds metronomic. Capped safely under the ~10s batch cadence
// so one batch's spread always drains before the next batch arrives. Crossings with a real
// positive motion-based delay are untouched.
const FALLBACK_SPACING_MS = 150;
const FALLBACK_WINDOW_MIN_MS = 250;
const FALLBACK_WINDOW_MAX_MS = 8000;

// Density cap for the fallback (backlog) stream. Even coalesced to one crossing per vehicle, a
// big backlog is too many events for a few seconds of audio — but thinning ONLY the tone left
// silent "ghost pulses" on the map, which broke the app's core pairing (a pulse IS a note made
// visible). So the budget gates the WHOLE crossing: a density-capped, RANDOMLY-chosen subset of
// survivors fires pulse+trail+tone together, and the rest of the backlog doesn't fire at all —
// at most ~one fire per FALLBACK_FIRE_SPACING_MS of jitter window (≈3/sec), never fewer than
// FALLBACK_FIRE_MIN_COUNT (so small backlogs are never thinned). Random selection so no
// route/vehicle is systematically dropped — which vehicles fire is a fresh draw every batch.
// Live crossings with a real positive motion delay ALWAYS fire — they're the current music;
// only the replayed backlog is capped.
const FALLBACK_FIRE_SPACING_MS = 350;
const FALLBACK_FIRE_MIN_COUNT = 3;

// C# entry point. crossings: [{ routeJoinKey, vehicleId, triggerIndex, totalTriggers, frac }].
// flags: { checkpointsVisible, crossingTrailVisible } captured at batch-receipt time. Audio is
// NOT in flags — it's re-checked live at fire time via _audioEnabled (see setAudioEnabled).
export async function dispatchCrossings(elementId, crossings, flags) {
    if (!crossings || crossings.length === 0) return;

    // Import the synth module once up front so per-crossing dispatch never awaits it under a timer.
    let synth;
    try { synth = await _getSynth(); }
    catch (e) { console.error('[CrossingDispatcher] synth import failed', e); return; }

    // First pass: compute each crossing's motion-model delay, and COALESCE the fallback
    // crossings (no positive delay to wait out) per vehicle. A backlog batch — join-replay,
    // or a dot that lagged several checkpoints behind — can carry SEVERAL already-passed
    // checkpoints for the SAME vehicle; firing all of them is a replay of that vehicle's
    // recent path, which is what sounded unnatural even once jittered. Keeping only the most
    // recent fallback crossing per vehicle (last in batch order — the server emits crossings
    // in traversal order) lets each vehicle announce itself exactly once without re-performing
    // its history. Live crossings with a real positive motion delay are NEVER coalesced —
    // they're the current music, not backlog.
    const computedDelays = new Array(crossings.length);
    const lastFallbackIdxByVehicle = new Map();
    for (let i = 0; i < crossings.length; i++) {
        const d = _delayForCrossing(crossings[i]);
        computedDelays[i] = d;
        if (d === null || d <= 0) lastFallbackIdxByVehicle.set(crossings[i].vehicleId, i);
    }

    // Size the fallback window to the burst it has to absorb — the COALESCED count (one
    // survivor per vehicle), since that's how many fallback timers actually get scheduled.
    const fallbackCount = lastFallbackIdxByVehicle.size;
    const jitterWindowMs = Math.min(FALLBACK_WINDOW_MAX_MS,
        Math.max(FALLBACK_WINDOW_MIN_MS, fallbackCount * FALLBACK_SPACING_MS));

    // Density cap: when the coalesced backlog still exceeds the fire budget for the window,
    // pick WHICH survivors fire uniformly at random (partial Fisher-Yates over the survivor
    // indices); the rest are dropped whole — pulse and tone together, never a silent "ghost
    // pulse". null → no capping needed, every survivor fires.
    const maxFires = Math.max(FALLBACK_FIRE_MIN_COUNT,
        Math.floor(jitterWindowMs / FALLBACK_FIRE_SPACING_MS));
    let firingIdxs = null;
    if (fallbackCount > maxFires) {
        const survivorIdxs = Array.from(lastFallbackIdxByVehicle.values());
        for (let i = 0; i < maxFires; i++) {
            const j = i + Math.floor(Math.random() * (survivorIdxs.length - i));
            const tmp = survivorIdxs[i]; survivorIdxs[i] = survivorIdxs[j]; survivorIdxs[j] = tmp;
        }
        firingIdxs = new Set(survivorIdxs.slice(0, maxFires));
    }

    for (let i = 0; i < crossings.length; i++) {
        const c = crossings[i];
        const computed = computedDelays[i];
        // One timer per crossing, but all in JS off the browser's timer queue — not thousands of
        // marshaled C# continuations. Each timer fires all three effects together.
        if (computed !== null && computed > 0) {
            // Live crossing: always fires, never coalesced or capped.
            setTimeout(() => { _fireOne(elementId, c, flags, synth); }, computed);
            continue;
        }
        // Fallback crossing: skip it if a later crossing in this batch superseded it for the
        // same vehicle (per-vehicle coalescing above) or it didn't make the density-capped
        // random draw; otherwise jitter it across the window.
        if (lastFallbackIdxByVehicle.get(c.vehicleId) !== i) continue;
        if (firingIdxs !== null && !firingIdxs.has(i)) continue;
        setTimeout(() => { _fireOne(elementId, c, flags, synth); }, Math.random() * jitterWindowMs);
    }
}
