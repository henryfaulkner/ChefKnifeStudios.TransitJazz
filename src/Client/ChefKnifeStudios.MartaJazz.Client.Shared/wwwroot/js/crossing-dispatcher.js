// Crossing dispatcher — schedules a whole batch of checkpoint crossings from ONE interop
// call, replacing the old per-crossing C# Task.Delay + 4 sequential interop hops.
//
// Why this exists: at NYMTA scale a batch carries thousands of crossings. The previous design
// fired one fire-and-forget C# task PER crossing, each awaiting Task.Delay(OffsetMs) then FOUR
// sequential JS-interop calls (pulse, duration, trail, note). Thousands of timers + tens of
// thousands of marshaled calls all queued on WASM's single thread, so pulse/note/trail for a
// given crossing drifted apart from each other and from OffsetMs — worse the larger the fleet.
//
// Here: C# hands the entire batch (each item carrying offsetMs) plus the three gating flags in
// ONE call. We own the timers in JS, and each crossing's pulse + trail + note fire together off
// a single setTimeout — so a crossing's effects can never desync from each other, and there is
// exactly one interop crossing per batch regardless of fleet size. OffsetMs accuracy is kept.

let _synthModule = null;

// TEMP DIAGNOSTIC (feature 040) — crossing-timing bug. Every fired crossing is buffered into
// window.__crossingDiag (see _fireOne). Call window.__crossingDiagDump() in the console after a
// run to download it as CSV (correlate with server-crossing-diag.csv by veh+tpIndex). Remove
// with the bug fix.
if (typeof window !== 'undefined' && !window.__crossingDiagDump) {
    window.__crossingDiagDump = function () {
        var rows = window.__crossingDiag || [];
        if (rows.length === 0) { console.warn('[CrossingDiag] nothing buffered yet'); return; }
        var cols = ['utc', 'veh', 'route', 'tpIndex', 'tpAlongM', 'dotDistM',
            'gapM', 'phase', 'elapsedMs', 'durationMs', 'empSpeed'];
        var csv = cols.join(',') + '\n' + rows.map(function (r) {
            return cols.map(function (k) {
                var v = r[k];
                return (v == null) ? '' : String(v);
            }).join(',');
        }).join('\n') + '\n';
        var blob = new Blob([csv], { type: 'text/csv' });
        var a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = 'client-crossing-diag.csv';
        a.click();
        URL.revokeObjectURL(a.href);
        console.log('[CrossingDiag] downloaded ' + rows.length + ' rows');
    };
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
    // TEMP DIAGNOSTIC (feature 040) — the CLIENT side of the tone-leads-dot bug. At the exact
    // moment this crossing's tone/pulse fires, measure where the animated dot actually is along
    // the route (dotDistM) and compare to where the checkpoint is (tpAlongM, from the trigger
    // feature). If dotDistM < tpAlongM the tone LEADS the dot; the gap quantifies it. Pair this
    // with the [CrossingDiag/SERVER] line for the same veh+tpIndex. Remove with the bug fix.
    try {
        // Checkpoint distance is the server-sent alongDistanceM (the stable identity) — do NOT
        // look it up by triggerIndex, which collides on sparse polylines and gave frozen/wrong
        // values in earlier runs.
        var tpAlongM = (typeof c.alongDistanceM === 'number') ? c.alongDistanceM : null;
        var dot = window.ChefMapAnimator
            && window.ChefMapAnimator.diagDotDistanceAlongRoute(c.vehicleId, c.routeJoinKey);
        var gapM = (dot && tpAlongM != null) ? (dot.dotDistM - tpAlongM) : null;
        var row = {
            utc: new Date().toISOString(),
            veh: c.vehicleId, route: c.routeJoinKey, tpIndex: c.triggerIndex,
            tpAlongM: tpAlongM != null ? Math.round(tpAlongM) : null,
            dotDistM: dot ? Math.round(dot.dotDistM) : null,
            gapM: gapM != null ? Math.round(gapM) : null, // <0 = tone LEADS dot (dot hasn't arrived)
            phase: dot ? dot.phase : null,
            elapsedMs: dot ? dot.elapsedMs : null,
            durationMs: dot ? dot.durationMs : null,
            empSpeed: dot ? Number(dot.empiricalSpeed).toFixed(2) : null
        };
        console.log('[CrossingDiag/CLIENT] fire', row);
        (window.__crossingDiag || (window.__crossingDiag = [])).push(row);
    } catch (e) { console.warn('[CrossingDiag/CLIENT] diag failed', e); }

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
    // TEMP DIAGNOSTIC (feature 040 desync fix) — remove once confirmed working.
    console.log('[CrossingDispatcher] dispatchCrossings called', {
        elementId,
        count: crossings ? crossings.length : 'null',
        firstCrossing: crossings && crossings[0],
        flags,
        hasChefMap: !!window.ChefMap,
        chefMapHasElement: !!(window.ChefMap && window.ChefMap.maps && window.ChefMap.maps[elementId])
    });

    if (!crossings || crossings.length === 0) return;

    // Import the synth module once up front so per-crossing dispatch never awaits it under a timer.
    let synth;
    try { synth = await _getSynth(); }
    catch (e) { console.error('[CrossingDispatcher] synth import failed', e); return; }
    console.log('[CrossingDispatcher] synth loaded, keys:', Object.keys(synth));

    for (let i = 0; i < crossings.length; i++) {
        const c = crossings[i];
        const delay = _delayForCrossing(c);
        // One timer per crossing, but all in JS off the browser's timer queue — not thousands of
        // marshaled C# continuations. Each timer fires all three effects together.
        setTimeout(() => { _fireOne(elementId, c, flags, synth); }, delay);
    }
}
