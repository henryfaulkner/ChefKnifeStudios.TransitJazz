// transit-synth.js — Tone.js synthesis module for 009-transit-soundscape
// Route → instrument (3-voice acoustic jazz trio via Sampler)
// Pitch maps trigger position along route → C-minor pentatonic scale degree
//
// ============================================================================
// BROWSER-MEMORY FINDINGS — READ BEFORE CHANGING THE AUDIO LOADING STRATEGY
// ============================================================================
// This file is THE dominant lever on the client's browser RAM. History:
//
//   * Good baseline (commit e07ae70, oscillators): client used sub-500 MB.
//   * After commit 187e784 ("sampler-based strings trio"): client jumped to
//     ~1.2 GB resident, flat over time, prod == dev.
//   * Reverting to oscillators dropped it back to sub-600 MB — proving the
//     soundfonts, not the .NET WASM heap, were the regression.
//
// Why a heap snapshot MISSED this: a Chrome heap snapshot is post-GC JS-heap
// only (~170 MB here) and does NOT show decoded-audio cost or the WASM
// high-water the early decode burst pins. An earlier investigation therefore
// wrongly blamed "unavoidable WASM linear memory." It is NOT — it is decoded
// audio PCM. Don't chase the WASM heap or MapLibre cache for this number.
//
// WHY SAMPLERS ARE SO HEAVY: Tone.Sampler fetches each note's MP3 and the Web
// Audio API decodes it to a raw 32-bit-float PCM AudioBuffer held resident for
// the session. The MP3s are tiny; the DECODED buffers are not (a multi-second
// FluidR3 note ≈ 0.5–1 MB each). The original loaded ~25 notes eagerly for
// every route → that's the ~600 MB.
//
// THE THREE LEVERS THIS BUILD USES (keep all three; each is load-bearing):
//   1. TWO anchor notes per instrument instead of 5–10. Tone.Sampler pitch-
//      shifts between supplied notes, so 2 anchors cover the whole scale.
//      This is the biggest cut (~75% less decoded PCM). Anchors C2 + C3 keep
//      every C2–Bb3 target within ~3 semitones of a real sample — below the
//      ~5–7 semitone threshold where pitch-shifting sounds artificial. Do NOT
//      go back to a dense note map "for quality"; it re-creates the 600 MB.
//   2. LAZY load — build a route's Sampler on first trigger, never eagerly for
//      all routes. preload() only warms the Tone.js import. This also kills the
//      early concurrent decode burst that pinned the WASM high-water mark.
//   3. dispose-on-inactive — disposeInactiveRoutes() frees Samplers for routes
//      whose vehicles have left the feed (driven from TransitMap, with a few-
//      batch hysteresis so a brief gap doesn't force a re-fetch+decode). Keeps
//      the resident set proportional to what's actually sounding.
//
// Net result: sampled timbre retained at ~200–300 MB instead of ~1.2 GB.
// If you must enrich the sound, prefer SHORTER / mono / self-hosted samples
// (less PCM per note) over adding more notes per instrument.
// ============================================================================

let _tone = null;
let _unlocked = false;
// routeId → { sampler, scale, durations } once loaded, or a pending Promise while loading.
const _instrumentCache = new Map();

const SOUNDFONT_BASE = 'https://gleitz.github.io/midi-js-soundfonts/FluidR3_GM';

// C-minor pentatonic across the low octaves used for harmony warmth. These are the
// PLAYED notes; the Sampler interpolates them from the two anchor samples below.
const SCALE = ['C2', 'Eb2', 'F2', 'G2', 'Bb2', 'C3', 'Eb3', 'F3', 'G3', 'Bb3'];

// Two anchor notes per instrument — the only MP3s actually fetched + decoded. One low,
// one high, so the worst-case pitch shift to any SCALE note is ~3 semitones.
const ANCHORS = { C2: 'C2', C3: 'C3' };

// Three sampled voices cycling across routes: bass → viola → cello → bass …
const PALETTE = [
    { instrument: 'contrabass', scale: SCALE, anchors: ANCHORS, release: 1.2, durations: ['4n', '4n.', '2n'] },
    { instrument: 'viola',      scale: SCALE, anchors: ANCHORS, release: 0.5, durations: ['8n', '8n.', '4n'] },
    { instrument: 'cello',      scale: SCALE, anchors: ANCHORS, release: 0.5, durations: ['8n', '8n.', '4n'] },
];

// Default Tone Transport tempo is 120 BPM (unchanged in this app):
//   2n = 1.0s, 4n. = 0.75s, 4n = 0.5s, 8n. = 0.375s, 8n = 0.25s
const DURATION_SECONDS = { '8n': 0.25, '8n.': 0.375, '4n': 0.5, '4n.': 0.75, '2n': 1.0 };

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

function buildSampleUrls(instrument, anchors) {
    const urls = {};
    for (const note of Object.keys(anchors)) {
        urls[note] = `${SOUNDFONT_BASE}/${instrument}-mp3/${note}.mp3`;
    }
    return urls;
}

// Lazily builds (and caches) the Sampler for a route. Concurrent callers share the same
// in-flight Promise so a note never double-loads.
function instrumentFor(routeId) {
    const cached = _instrumentCache.get(routeId);
    if (cached) return cached;

    const slot = PALETTE[djb2(String(routeId)) % PALETTE.length];

    const loading = getTone().then(T => new Promise((resolve, reject) => {
        const sampler = new T.Sampler(
            buildSampleUrls(slot.instrument, slot.anchors),
            {
                release: slot.release ?? 1.2,
                onload: () => {
                    const entry = { sampler: sampler.toDestination(), scale: slot.scale, durations: slot.durations };
                    _instrumentCache.set(routeId, entry); // replace the pending Promise with the resolved entry
                    resolve(entry);
                },
                onerror: (err) => {
                    console.error('[TransitSynth] sampler load failed for routeId=' + routeId + ' instrument=' + slot.instrument, err);
                    _instrumentCache.delete(routeId); // allow a later retry
                    reject(err);
                },
            }
        );
    }));

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

// LAZY build replaces the old eager preload of every route. Kept as an export (the C#
// PreloadAsync call) but now only warms the Tone.js import so the first real trigger
// doesn't pay the module-load latency. No samples are fetched until a route first sounds.
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
        const { sampler, scale, durations } = await instrumentFor(routeId);
        const note = noteForPosition(scale, triggerIndex, totalTriggers);
        const duration = durations[djb2(String(vehicleId)) % durations.length];
        sampler.triggerAttackRelease(note, duration);
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

// Frees Samplers for routes no longer active so decoded PCM doesn't accumulate across a
// long session. Callable from the C# side with the current set of routes that have live
// vehicles; any cached route NOT in that set is disposed.
export function disposeInactiveRoutes(activeRouteIds) {
    const active = new Set(Array.isArray(activeRouteIds) ? activeRouteIds.map(String) : []);
    for (const routeId of [..._instrumentCache.keys()]) {
        if (active.has(String(routeId))) continue;
        const entry = _instrumentCache.get(routeId);
        // Skip entries still loading (Promises) — don't dispose mid-fetch.
        if (entry && entry.sampler) {
            try { entry.sampler.dispose(); } catch (_) { /* ignore */ }
            _instrumentCache.delete(routeId);
        }
    }
}

export async function dispose() {
    for (const entry of _instrumentCache.values()) {
        if (entry && entry.sampler) {
            try { entry.sampler.dispose(); } catch (_) { /* ignore */ }
        }
    }
    _instrumentCache.clear();
    _unlocked = false;
    _tone = null;
}

window.TransitSynth = { unlock, isUnlocked, preload, triggerNote, dispose, durationSecondsFor, disposeInactiveRoutes };
