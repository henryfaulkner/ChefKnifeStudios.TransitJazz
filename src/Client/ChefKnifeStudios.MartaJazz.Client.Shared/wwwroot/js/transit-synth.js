// transit-synth.js — Tone.js synthesis module for 009-transit-soundscape
// Route → instrument (3-voice pure-synthesis palette)
// Pitch maps trigger position along route → C-minor pentatonic scale degree
//
// ============================================================================
// PURE SYNTHESIS — see docs/SYNTH_REFACTOR_DESIGN_DOCUMENT.md for the full
// rationale. Summary: the previous build used Tone.Sampler over FluidR3
// soundfont MP3s (bowed strings — contrabass/viola/cello), which decodes each
// note to a resident AudioBuffer and was the dominant lever on client RAM
// (~1.2 GB regression, see docs/BROWSER_MEMORY_INVESTIGATION_DESIGN_DOCUMENT.md
// §3.5). This build uses zero decoded audio — Tone.PluckSynth (Karplus-Strong
// physical model), Tone.MonoSynth (subtractive sub bass), and Tone.FMSynth
// (bell/mallet) — so the marginal cost of a voice is a config object, not a
// sample fetch+decode. Sustained bowed strings are dropped from the palette
// entirely (design doc §1): synthesis can't fake bow noise / pressure
// micro-variation, but it's genuinely good at plucked/struck/FM timbres.
//
// The old file is preserved, unwired, at transit-synth.legacy.js for
// reference/rollback (design doc §6) — do not delete it.
// ============================================================================

let _tone = null;
let _unlocked = false;
// routeId → { synth, scale, durations } once loaded, or a pending Promise while loading.
const _instrumentCache = new Map();

// C-minor pentatonic across the low octaves used for harmony warmth. Synthesis has no
// sample-count constraint, so the full scale is voiced directly (design doc §3).
const SCALE = ['C2', 'Eb2', 'F2', 'G2', 'Bb2', 'C3', 'Eb3', 'F3', 'G3', 'Bb3'];

// Three synthesized voices cycling across routes: pluck → sub bass → FM bell → pluck …
// filterHz tames raw-oscillator harshness on the foreground/melodic signal (design doc §4).
const PALETTE = [
    {
        type: 'pluck',
        release: 1.2,
        durations: ['4n', '4n.', '2n'],
        filterHz: 3500,
        volumeDb: -6,
    },
    {
        type: 'subbass',
        release: 0.5,
        durations: ['8n', '8n.', '4n'],
        filterHz: 3000,
        volumeDb: -8,
    },
    {
        type: 'fmbell',
        release: 0.5,
        durations: ['8n', '8n.', '4n'],
        filterHz: 4000,
        volumeDb: -10,
    },
];

// Default Tone Transport tempo is 120 BPM (unchanged in this app):
//   2n = 1.0s, 4n. = 0.75s, 4n = 0.5s, 8n. = 0.375s, 8n = 0.25s
const DURATION_SECONDS = { '8n': 0.25, '8n.': 0.375, '4n': 0.5, '4n.': 0.75, '2n': 1.0 };

// Reverb parameters — shipped in v1, uniform across all voices (design doc §4/§7).
const REVERB_DECAY = 0.8;
const REVERB_PRE_DELAY = 0.01;
const REVERB_WET = 0.15;

// djb2 hash — deterministic, no crypto needed
function djb2(s) {
    let h = 5381;
    for (let i = 0; i < s.length; i++) {
        h = ((h << 5) + h + s.charCodeAt(i)) | 0;
    }
    return h >>> 0;
}

// Deterministic, audio-independent duration token for a vehicle. Shared by triggerNote
// (audible note) and durationSecondsFor (trail growth) so they always agree. Resolves the
// route's palette slot so the token comes from that instrument's own duration set.
function _slotForRoute(routeId) {
    return PALETTE[djb2(String(routeId)) % PALETTE.length];
}

async function getTone() {
    if (_tone) return _tone;
    const mod = await import('https://esm.sh/tone@15');
    if (!_tone) _tone = mod;
    return _tone;
}

// Builds the Tone.js synth instance for a palette slot's `type`. Each voice type maps to
// the instrument family design doc §1/§3 identified as a good fit for pure synthesis.
function buildSynth(T, type) {
    switch (type) {
        case 'pluck':
            // Karplus-Strong physical model — plucked-string stand-in for the old "contrabass" slot.
            return new T.PluckSynth({ attackNoise: 1, dampening: 4000, resonance: 0.9 });
        case 'subbass':
            // Subtractive: sine-ish osc + lowpass filter + short envelope, close to real bass.
            return new T.MonoSynth({
                oscillator: { type: 'sine' },
                envelope: { attack: 0.01, decay: 0.2, sustain: 0.3, release: 0.8 },
                filterEnvelope: { attack: 0.01, decay: 0.1, sustain: 0.5, release: 0.5, baseFrequency: 200, octaves: 2 },
            });
        case 'fmbell':
            // FM synthesis — brighter, mallet/kalimba-like character; fills the third-voice role.
            return new T.FMSynth({
                harmonicity: 3,
                modulationIndex: 10,
                envelope: { attack: 0.005, decay: 0.3, sustain: 0.1, release: 0.8 },
                modulationEnvelope: { attack: 0.005, decay: 0.2, sustain: 0, release: 0.3 },
            });
        default:
            throw new Error(`[TransitSynth] unknown voice type: ${type}`);
    }
}

// Lazily builds (and caches) the synth + per-voice effects chain for a route. Concurrent
// callers share the same in-flight Promise so a voice never double-builds. Tone.Reverb has
// its own async IR-generation cost (`generate()`), so it's built once here, not per note
// (design doc §4).
function instrumentFor(routeId) {
    const cached = _instrumentCache.get(routeId);
    if (cached) return cached;

    const slot = PALETTE[djb2(String(routeId)) % PALETTE.length];

    const loading = getTone().then(async T => {
        const synth = buildSynth(T, slot.type);
        const filter = new T.Filter(slot.filterHz, 'lowpass');
        const reverb = new T.Reverb({ decay: REVERB_DECAY, preDelay: REVERB_PRE_DELAY, wet: REVERB_WET });
        const volume = new T.Volume(slot.volumeDb);
        await reverb.generate();
        synth.chain(filter, reverb, volume, T.Destination);

        const entry = { synth, scale: SCALE, durations: slot.durations };
        _instrumentCache.set(routeId, entry); // replace the pending Promise with the resolved entry
        return entry;
    }).catch(err => {
        console.error('[TransitSynth] synth build failed for routeId=' + routeId + ' type=' + slot.type, err);
        _instrumentCache.delete(routeId); // allow a later retry
        throw err;
    });

    _instrumentCache.set(routeId, loading);
    return loading;
}

// Maps a bus's progress along its route to a scale degree.
// triggerIndex 0 → root note, triggerIndex totalTriggers-1 → top note.
function noteForPosition(scale, triggerIndex, totalTriggers) {
    const clampedTotal = Math.max(totalTriggers, 1);
    const clampedIndex = Math.max(0, Math.min(triggerIndex, clampedTotal - 1));
    const scaleIndex = Math.round((clampedIndex / (clampedTotal - 1 || 1)) * (scale.length - 1));
    return scale[scaleIndex];
}

// LAZY build — a route's synth + effects chain is built on first trigger, never eagerly for
// all routes. Kept as an export (the C# PreloadAsync call) but only warms the Tone.js import
// so the first real trigger doesn't pay the module-load latency.
export async function preload(routeIds) {
    await getTone();
}

// Attaches a one-shot native click listener to the unlock button so that
// Tone.start() fires synchronously inside the gesture event, before Blazor's
// async interop chain breaks the browser's autoplay trust window (iOS Safari).
export async function attachUnlockGesture(elementId) {
    const T = await getTone();
    const el = document.getElementById(elementId);
    if (!el) return;
    function handler() {
        el.removeEventListener('click', handler);
        T.start().then(() => {
            _unlocked = true;
            console.log('[TransitSynth] unlocked via gesture');
        });
    }
    el.addEventListener('click', handler);
}

export async function unlock() {
    if (_unlocked) return;
    const T = await getTone();
    await T.start();
    _unlocked = true;
    console.log('[TransitSynth] unlocked');
}

export function isUnlocked() {
    return _unlocked;
}

// triggerIndex and totalTriggers come from checkpoint-tracker; both default to
// 0/1 so the C# interop path (which omits them) still plays without error.
export async function triggerNote(routeId, vehicleId, triggerIndex = 0, totalTriggers = 1) {
    if (!_unlocked) return;
    try {
        const { synth, scale, durations } = await instrumentFor(routeId);
        const note = noteForPosition(scale, triggerIndex, totalTriggers);
        const duration = durations[djb2(String(vehicleId)) % durations.length];
        synth.triggerAttackRelease(note, duration);
    } catch (err) {
        console.warn('[TransitSynth] triggerNote error:', err);
    }
}

// Audio-independent note duration in seconds for a vehicle's crossing. No _unlocked
// guard, no AudioContext, no Tone import — callable while muted/locked (FR-001). Uses
// the SAME deterministic selection as triggerNote (route slot → duration set → djb2)
// so the trail length matches the note.
export function durationSecondsFor(vehicleId, routeId) {
    const durations = routeId ? _slotForRoute(routeId).durations : PALETTE[0].durations;
    const tok = durations[djb2(String(vehicleId)) % durations.length];
    return DURATION_SECONDS[tok] ?? 0.25;
}

// Frees synths + effects chains for routes no longer active so the node graph doesn't
// accumulate across a long session. Callable from the C# side with the current set of
// routes that have live vehicles; any cached route NOT in that set is disposed.
export function disposeInactiveRoutes(activeRouteIds) {
    const active = new Set(Array.isArray(activeRouteIds) ? activeRouteIds.map(String) : []);
    for (const routeId of [..._instrumentCache.keys()]) {
        if (active.has(String(routeId))) continue;
        const entry = _instrumentCache.get(routeId);
        // Skip entries still loading (Promises) — don't dispose mid-build.
        if (entry && entry.synth) {
            try { entry.synth.dispose(); } catch (_) { /* ignore */ }
            _instrumentCache.delete(routeId);
        }
    }
}

export async function dispose() {
    for (const entry of _instrumentCache.values()) {
        if (entry && entry.synth) {
            try { entry.synth.dispose(); } catch (_) { /* ignore */ }
        }
    }
    _instrumentCache.clear();
    _unlocked = false;
    _tone = null;
}

window.TransitSynth = { unlock, isUnlocked, preload, triggerNote, dispose, durationSecondsFor, disposeInactiveRoutes };
