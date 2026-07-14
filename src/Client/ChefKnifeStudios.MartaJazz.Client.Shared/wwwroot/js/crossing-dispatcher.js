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
    if (flags.checkpointsVisible && window.ChefMap) {
        try { window.ChefMap.pulseCheckpoint(elementId, c.routeJoinKey, c.triggerIndex, c.alongDistanceM); } catch (_) { }
    }

    if (flags.crossingTrailVisible && window.ChefMap) {
        try {
            const durationSec = await synth.durationSecondsFor(c.vehicleId, c.routeJoinKey);
            window.ChefMap.startCrossingTrail(elementId, c.routeJoinKey, c.vehicleId, c.triggerIndex, durationSec, c.alongDistanceM);
        } catch (_) { }
    }

    if (flags.audioEnabled) {
        try { synth.triggerNote(c.routeJoinKey, c.vehicleId, c.triggerIndex, c.totalTriggers); } catch (_) { }
    }
}

// Delay (ms) at which this crossing's tone/pulse should fire. The server sends the checkpoint's
// absolute along-route distance (alongDistanceM); the animator computes how long until the DOT
// actually reaches it, against the dot's own motion model (extrapolation at empirical speed, or
// interpolation along its subPath). This is the tone-leads-dot fix (feature 040): timing tracks
// the animated dot exactly, eliminating both the ~8s-spread mismatch and the speed-scaled
// residual lead that a server-baked frac/offset left behind. Falls back to 0 (fire immediately)
// when the animator has no usable state for the vehicle yet.
function _delayForCrossing(c) {
    var anim = window.ChefMapAnimator;
    if (anim && typeof anim.crossingDelayMsFor === 'function') {
        var d = anim.crossingDelayMsFor(c.vehicleId, c.routeJoinKey, c.alongDistanceM);
        if (typeof d === 'number' && d >= 0) return d;
    }
    return 0;
}

// C# entry point. crossings: [{ routeJoinKey, vehicleId, triggerIndex, totalTriggers, frac }].
// flags: { checkpointsVisible, crossingTrailVisible, audioEnabled } captured at batch-receipt time.
export async function dispatchCrossings(elementId, crossings, flags) {
    if (!crossings || crossings.length === 0) return;

    // Import the synth module once up front so per-crossing dispatch never awaits it under a timer.
    let synth;
    try { synth = await _getSynth(); }
    catch (e) { console.error('[CrossingDispatcher] synth import failed', e); return; }

    for (let i = 0; i < crossings.length; i++) {
        const c = crossings[i];
        const delay = _delayForCrossing(c);
        // One timer per crossing, but all in JS off the browser's timer queue — not thousands of
        // marshaled C# continuations. Each timer fires all three effects together.
        setTimeout(() => { _fireOne(elementId, c, flags, synth); }, delay);
    }
}
