// transit-synth.js — Tone.js synthesis module for 009-transit-soundscape
// Route → instrument (3-voice sampled palette via Tone.Sampler)
// Pitch maps trigger position along route → C-minor pentatonic scale degree
//
// ============================================================================
// SAMPLER, NOT PURE SYNTHESIS — see docs/SYNTH_REFACTOR_DESIGN_DOCUMENT.md for
// the pure-synthesis attempt this build replaces. Two problems with the pure
// synthesis build (PluckSynth/MonoSynth/FMSynth):
//   1. Those instruments are each a SINGLE monophonic voice. Every route
//      shares one synth instance (instrumentFor(routeId)), so when two
//      vehicles on the same route cross near-simultaneously, the second
//      triggerAttackRelease() steals/retriggers the first note before it
//      finishes — audibly dropped notes.
//   2. Tone.Sampler fixes this for free: each triggerAttackRelease() call
//      gets its own internally-managed voice, so overlapping notes on the
//      same route no longer cut each other off.
// Going back to Sampler reopens the RAM regression documented in
// docs/BROWSER_MEMORY_INVESTIGATION_DESIGN_DOCUMENT.md §3.5 — mitigated with
// the SAME three levers the legacy build used, PLUS a fourth that turned out to
// be load-bearing here (an earlier version of this file omitted it and hit
// ~1.7GB, WORSE than the original ~1.2GB regression):
//   1. Sparse anchor notes per instrument (not a dense chromatic map) —
//      Tone.Sampler pitch-shifts between supplied anchors.
//   2. LAZY load — a slot's Sampler is built on first trigger, never eagerly.
//   3. dispose-on-inactive — disposeInactiveRoutes() frees a slot's Sampler
//      once no active route hashes to it anymore.
//   4. *** SHARE BY PALETTE SLOT, NOT PER ROUTE. *** _instrumentCache is keyed
//      by slot index (0..PALETTE.length-1), NOT routeId. Every route that
//      hashes to the same slot reuses ONE Sampler/Filter/Reverb. Keying by
//      routeId instead builds a DISTINCT Sampler (decoded anchor PCM) and a
//      DISTINCT Tone.Reverb (its own convolution impulse-response buffer —
//      also decoded audio) per ROUTE. On a system with dozens of concurrent
//      routes that's dozens of duplicate instrument+reverb builds instead of
//      3 — this is what drove RAM to 1.7GB. Do not key this cache by routeId.
// Samples are FluidR3 GM (https://gleitz.github.io/midi-js-soundfonts,
// {instrument}-mp3.js per-note base64 MP3s) — same CDN as the legacy build,
// new instrument picks per docs/SYNTH_REFACTOR_DESIGN_DOCUMENT.md §1/§3
// (plucked/struck families, NOT sustained bowed strings):
//   pizzicato_strings (pluck), acoustic_bass (sub bass), acoustic_grand_piano
//   (third voice — matches the Piano/Salamander-piano voice both sibling
//   reference projects, lofi-engine and lofi-station, use; replaces an
//   earlier tubular_bells pick).
// ============================================================================

let _tone = null;
let _unlocked = false;
// PALETTE index → { sampler, scale, durations } once loaded, or a pending Promise while
// loading. Keyed by SLOT, not routeId — every route mapping to the same slot (there are
// only PALETTE.length of them) shares one Sampler/Filter/Reverb. Keying by routeId instead
// built a distinct Sampler + Reverb (each with its own decoded-PCM anchors AND its own
// convolution impulse-response buffer) per ROUTE, which is what drove RAM past even the
// original ~1.2GB regression on a system with dozens of concurrent routes.
const _instrumentCache = new Map();

const SOUNDFONT_BASE = 'https://gleitz.github.io/midi-js-soundfonts/FluidR3_GM';

// C-minor pentatonic across the low octaves used for harmony warmth. These are the
// PLAYED notes; the Sampler interpolates them from the anchor samples below.
const SCALE = ['C2', 'Eb2', 'F2', 'G2', 'Bb2', 'C3', 'Eb3', 'F3', 'G3', 'Bb3'];

// Two anchor notes per instrument — the only MP3s actually fetched + decoded. One low,
// one high, so the worst-case pitch shift to any SCALE note is ~3 semitones.
const ANCHORS = { C2: 'C2', C3: 'C3' };

// Three sampled voices cycling across routes: pluck → bass → piano → pluck …
// Plucked/struck families (design doc §1) — NOT the old sustained-bowed-string palette.
// Piano replaces an earlier tubular_bells pick, matching the Piano/Salamander-piano voice
// both sibling reference projects (lofi-engine, lofi-station) use as their melodic voice.
const PALETTE = [
    { instrument: 'pizzicato_strings',    scale: SCALE, anchors: ANCHORS, release: 1.2, durations: ['4n', '4n.', '2n'] },
    { instrument: 'acoustic_bass',        scale: SCALE, anchors: ANCHORS, release: 0.8, durations: ['8n', '8n.', '4n'] },
    { instrument: 'acoustic_grand_piano', scale: SCALE, anchors: ANCHORS, release: 1.0, durations: ['8n', '8n.', '4n'] },
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

// Deterministic route → palette slot INDEX. Cheap (no audio nodes) — used both to pick the
// duration set (_slotForRoute) and to key the shared Sampler cache (instrumentForSlot).
function _slotIndexForRoute(routeId) {
    return djb2(String(routeId)) % PALETTE.length;
}

// Deterministic, audio-independent duration token for a vehicle. Shared by triggerNote
// (audible note) and durationSecondsFor (trail growth) so they always agree. Resolves the
// route's palette slot so the token comes from that instrument's own duration set.
function _slotForRoute(routeId) {
    return PALETTE[_slotIndexForRoute(routeId)];
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

// Lazily builds (and caches) the Sampler + effects chain for a PALETTE SLOT — shared by
// every route that hashes to that slot, so there are at most PALETTE.length Samplers/
// Reverbs resident, never one per route. Concurrent callers (different routes hashing to
// the same slot, or the same route) share the in-flight Promise so a voice never double-
// loads. Tone.Reverb has its own async IR-generation cost (`generate()`), built once here,
// not per note trigger (mirrors the lofi-station/lofi-engine reference recipe — see
// docs/SYNTH_REFACTOR_DESIGN_DOCUMENT.md §4).
function instrumentForSlot(slotIndex) {
    const cached = _instrumentCache.get(slotIndex);
    if (cached) return cached;

    const slot = PALETTE[slotIndex];

    const loading = getTone().then(T => new Promise((resolve, reject) => {
        const filter = new T.Filter(3500, 'lowpass');
        const reverb = new T.Reverb({ decay: REVERB_DECAY, preDelay: REVERB_PRE_DELAY, wet: REVERB_WET });
        const sampler = new T.Sampler(
            buildSampleUrls(slot.instrument, slot.anchors),
            {
                release: slot.release ?? 1.2,
                onload: () => {
                    reverb.generate().then(() => {
                        sampler.chain(filter, reverb, T.Destination);
                        const entry = { sampler, scale: slot.scale, durations: slot.durations };
                        _instrumentCache.set(slotIndex, entry); // replace the pending Promise with the resolved entry
                        resolve(entry);
                    });
                },
                onerror: (err) => {
                    console.error('[TransitSynth] sampler load failed for slot=' + slotIndex + ' instrument=' + slot.instrument, err);
                    _instrumentCache.delete(slotIndex); // allow a later retry
                    reject(err);
                },
            }
        );
    }));

    _instrumentCache.set(slotIndex, loading);
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

// LAZY build replaces eager preload of every route. Kept as an export (the C#
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
        const { sampler, scale, durations } = await instrumentForSlot(_slotIndexForRoute(routeId));
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

// Frees a slot's Sampler if NO active route hashes to it anymore, so decoded PCM doesn't
// stay resident once its timbre has gone fully silent. Callable from the C# side with the
// current set of routes that have live vehicles. Cache is keyed by PALETTE slot (at most
// PALETTE.length entries, shared across all routes on that slot — see instrumentForSlot),
// so this translates the route-id set to the slot-index set still in use before evicting.
export function disposeInactiveRoutes(activeRouteIds) {
    const activeSlots = new Set(
        (Array.isArray(activeRouteIds) ? activeRouteIds : []).map(id => _slotIndexForRoute(id))
    );
    for (const slotIndex of [..._instrumentCache.keys()]) {
        if (activeSlots.has(slotIndex)) continue;
        const entry = _instrumentCache.get(slotIndex);
        // Skip entries still loading (Promises) — don't dispose mid-fetch.
        if (entry && entry.sampler) {
            try { entry.sampler.dispose(); } catch (_) { /* ignore */ }
            _instrumentCache.delete(slotIndex);
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
