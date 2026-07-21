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

// Delay (ms) at which this crossing's tone/pulse should fire. The server sends the checkpoint's
// absolute along-route distance (alongDistanceM); the animator computes how long until the DOT
// actually reaches it, against the dot's own motion model (extrapolation at empirical speed, or
// interpolation along its subPath). This is the tone-leads-dot fix (feature 040): timing tracks
// the animated dot exactly, eliminating both the ~8s-spread mismatch and the speed-scaled
// residual lead that a server-baked frac/offset left behind. Returns null (not 0) when the
// animator has no usable motion state for the vehicle yet (idle phase, unknown vehicle/route) —
// the caller distinguishes "no delay available" from a genuine zero so it can stagger fallback
// crossings instead of firing them all in the same tick (see dispatchCrossings).
function _delayForCrossing(c) {
    var anim = window.ChefMapAnimator;
    if (anim && typeof anim.crossingDelayMsFor === 'function') {
        var d = anim.crossingDelayMsFor(c.vehicleId, c.routeJoinKey, c.alongDistanceM);
        if (typeof d === 'number' && d >= 0) return d;
    }
    return null;
}

// Window crossings that fall back to no motion-based delay are jittered across (e.g. join-replay's
// age-capped backlog landing on freshly-seeded, still-idle vehicles — feature 045). Without this,
// every such crossing resolves to "fire now" and setTimeout(fn, 0) for all of them collapses into
// the same task-queue tick: a burst of simultaneous pulses/tones (the bug feature 045
// reintroduced; see specs/045-time-to-first-note/BURST-FIX-DESIGN.md). Random rather than a fixed
// step so some genuinely land together (coincident crossings are real, not the bug) while most
// spread out — a metronomic ramp would sound mechanical instead of like ordinary traffic.
// Crossings with a real motion-based delay are untouched.
const FALLBACK_JITTER_WINDOW_MS = 250;

// C# entry point. crossings: [{ routeJoinKey, vehicleId, triggerIndex, totalTriggers, frac }].
// flags: { checkpointsVisible, crossingTrailVisible } captured at batch-receipt time. Audio is
// NOT in flags — it's re-checked live at fire time via _audioEnabled (see setAudioEnabled).
export async function dispatchCrossings(elementId, crossings, flags) {
    if (!crossings || crossings.length === 0) return;

    // Import the synth module once up front so per-crossing dispatch never awaits it under a timer.
    let synth;
    try { synth = await _getSynth(); }
    catch (e) { console.error('[CrossingDispatcher] synth import failed', e); return; }

    for (let i = 0; i < crossings.length; i++) {
        const c = crossings[i];
        const computed = _delayForCrossing(c);
        const delay = computed !== null ? computed : Math.random() * FALLBACK_JITTER_WINDOW_MS;
        // One timer per crossing, but all in JS off the browser's timer queue — not thousands of
        // marshaled C# continuations. Each timer fires all three effects together.
        setTimeout(() => { _fireOne(elementId, c, flags, synth); }, delay);
    }
}
