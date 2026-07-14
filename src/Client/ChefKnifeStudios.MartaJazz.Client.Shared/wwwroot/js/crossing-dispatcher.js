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
        var cols = ['utc', 'veh', 'route', 'tpIndex', 'frac', 'delayMs', 'tpAlongM', 'dotDistM',
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
        var tpAlongM = null;
        var feats = window.ChefMap && window.ChefMap._triggerPointFeatures
            && window.ChefMap._triggerPointFeatures[c.routeJoinKey];
        if (feats) {
            var f = feats.find(function (x) { return x.properties.triggerIndex === c.triggerIndex; });
            if (f) tpAlongM = f.properties.alongDistanceM;
        }
        var dot = window.ChefMapAnimator
            && window.ChefMapAnimator.diagDotDistanceAlongRoute(c.vehicleId, c.routeJoinKey);
        var gapM = (dot && tpAlongM != null) ? (dot.dotDistM - tpAlongM) : null;
        var row = {
            utc: new Date().toISOString(),
            veh: c.vehicleId, route: c.routeJoinKey, tpIndex: c.triggerIndex,
            frac: (typeof c.frac === 'number') ? Number(c.frac).toFixed(4) : null,
            delayMs: Math.round(_delayForCrossing(c)), // frac × durationMs — the NEW timing
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
        try { window.ChefMap.pulseCheckpoint(elementId, c.routeJoinKey, c.triggerIndex); } catch (_) { }
    }

    if (flags.crossingTrailVisible && window.ChefMap) {
        try {
            const durationSec = await synth.durationSecondsFor(c.vehicleId, c.routeJoinKey);
            window.ChefMap.startCrossingTrail(elementId, c.routeJoinKey, c.vehicleId, c.triggerIndex, durationSec);
        } catch (_) { }
    }

    if (flags.audioEnabled) {
        try { synth.triggerNote(c.routeJoinKey, c.vehicleId, c.triggerIndex, c.totalTriggers); } catch (_) { }
    }
}

// Default animation duration (ms) when the animator has no state yet for a vehicle — mirrors
// vehicle-animator's own `rec.durationMs || 10000` fallback.
const DEFAULT_DURATION_MS = 10000;

// Delay (ms) at which this crossing's tone/pulse should fire: the fraction of the travel span
// where the checkpoint sits (frac, from the server), rescaled onto the DOT'S ACTUAL animation
// duration. This is the fix for the tone-leads-dot bug (feature 040): the dot animates over
// `duration` (typically ~30s of subway interpolation, sometimes minutes), while the server's
// old pre-baked OffsetMs was keyed to an ~8s spread window — a different clock — so tones fired
// far ahead of the dot. frac × duration puts the tone exactly where the dot is at that fraction
// of its animation. frac is in [0,1], so delay never exceeds the animation duration.
function _delayForCrossing(c) {
    var anim = window.ChefMapAnimator;
    var state = anim && anim.vehicles && anim.vehicles[c.vehicleId];
    var duration = (state && state.duration > 0) ? state.duration : DEFAULT_DURATION_MS;
    var frac = (typeof c.frac === 'number' && c.frac > 0) ? Math.min(c.frac, 1) : 0;
    return frac * duration;
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
