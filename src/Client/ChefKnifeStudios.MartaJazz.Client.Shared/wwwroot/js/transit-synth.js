// transit-synth.js — Tone.js synthesis module for 009-transit-soundscape
// Route → instrument (3-voice acoustic jazz trio via Sampler)
// Pitch maps trigger position along route → C-minor pentatonic scale degree

let _tone = null;
let _unlocked = false;
const _instrumentCache = new Map();
const _pendingInstruments = new Map();

// Start Tone.js import immediately when module loads, before any function is called.
// This gives the import a head start on slow mobile connections.
const _tonePromise = import('https://esm.sh/tone@15');

// Create a bare AudioContext at module level so it exists when the gesture fires.
// It starts "suspended"; the native click handler resumes it synchronously within
// the gesture stack — no dependency on Tone.js being loaded yet.
let _audioCtx = null;
try {
    _audioCtx = new (window.AudioContext || window.webkitAudioContext)();
} catch (_) { /* older browser or non-browser environment */ }

const SOUNDFONT_BASE = 'https://gleitz.github.io/midi-js-soundfonts/FluidR3_GM';

// C-minor pentatonic: C Eb F G Bb — kept in low octaves for warmth
const SCALE_BASS = ['C2','Eb2','F2','G2','Bb2','C3','Eb3','F3','G3','Bb3'];
const SCALE_LOW  = ['C2','Eb2','F2','G2','Bb2','C3','Eb3','F3','G3','Bb3'];

// Three sampled voices cycling across routes: bass → viola → cello → bass …
const PALETTE = [
    {
        instrument: 'bassoon',
        scale: ['C1','Eb1','F1','G1','Bb1'],
        notes: { C1:'C1',Eb1:'Eb1',F1:'F1',G1:'G1',Bb1:'Bb1' },
        release: 0.3,
        durations: ['8n', '8n.', '4n'],
    },
    {
        instrument: 'viola',
        scale: SCALE_LOW,
        notes: { C2:'C2',Eb2:'Eb2',F2:'F2',G2:'G2',Bb2:'Bb2',C3:'C3',Eb3:'Eb3',F3:'F3',G3:'G3',Bb3:'Bb3' },
        release: 0.5,
        durations: ['8n', '8n.', '4n'],
    },
    {
        instrument: 'cello',
        scale: SCALE_BASS,
        notes: { C2:'C2',Eb2:'Eb2',F2:'F2',G2:'G2',Bb2:'Bb2',C3:'C3',Eb3:'Eb3',F3:'F3',G3:'G3',Bb3:'Bb3' },
        release: 0.5,
        durations: ['8n', '8n.', '4n'],
    },
];

// djb2 hash — deterministic, no crypto needed
function djb2(s) {
    let h = 5381;
    for (let i = 0; i < s.length; i++) {
        h = ((h << 5) + h + s.charCodeAt(i)) | 0;
    }
    return h >>> 0;
}

async function getTone() {
    if (_tone) return _tone;
    console.log('[TransitSynth] getTone: awaiting Tone.js import');
    const mod = await _tonePromise;
    if (!_tone) _tone = mod;
    console.log('[TransitSynth] getTone: Tone.js imported, context state=' + _tone.getContext().rawContext.state);
    return _tone;
}

function buildSampleUrls(instrument, notes) {
    const urls = {};
    for (const note of Object.keys(notes)) {
        // MIDI.js filenames use unicode flat sign: Eb4 → Eb4.mp3
        urls[note] = `${SOUNDFONT_BASE}/${instrument}-mp3/${note}.mp3`;
    }
    return urls;
}

// Per-route dedup: concurrent calls for the same route share one promise,
// preventing duplicate Sampler instances.
async function instrumentFor(routeId) {
    if (_instrumentCache.has(routeId)) return _instrumentCache.get(routeId);
    if (_pendingInstruments.has(routeId)) return _pendingInstruments.get(routeId);

    const promise = (async () => {
        const T = await getTone();
        if (_instrumentCache.has(routeId)) return _instrumentCache.get(routeId);

        const h = djb2(String(routeId));
        const slotIndex = h % PALETTE.length;
        const slot = PALETTE[slotIndex];

        console.log('[TransitSynth] route=' + routeId + ' → slot=' + slotIndex + ' instrument=' + slot.instrument);

        return new Promise((resolve, reject) => {
            const sampler = new T.Sampler(
                buildSampleUrls(slot.instrument, slot.notes),
                {
                    release: slot.release ?? 1.2,
                    onload: () => {
                        console.log('[TransitSynth] loaded route=' + routeId + ' instrument=' + slot.instrument);
                        const vol = new T.Volume(slot.volume ?? 0).toDestination();
                        sampler.connect(vol);
                        const result = { sampler, scale: slot.scale, durations: slot.durations };
                        _instrumentCache.set(routeId, result);
                        _pendingInstruments.delete(routeId);
                        resolve(result);
                    },
                    onerror: (err) => {
                        console.error('[TransitSynth] sampler load failed for routeId=' + routeId + ' instrument=' + slot.instrument, err);
                        _pendingInstruments.delete(routeId);
                        reject(err);
                    },
                }
            );
        });
    })();

    _pendingInstruments.set(routeId, promise);
    return promise;
}

// Maps a bus's progress along its route to a scale degree.
// triggerIndex 0 → root note, triggerIndex totalTriggers-1 → top note.
function noteForPosition(scale, triggerIndex, totalTriggers) {
    const clampedTotal = Math.max(totalTriggers, 1);
    const clampedIndex = Math.max(0, Math.min(triggerIndex, clampedTotal - 1));
    const scaleIndex = Math.round((clampedIndex / (clampedTotal - 1 || 1)) * (scale.length - 1));
    return scale[scaleIndex];
}

// Kicks off Sampler HTTP fetches for the given route IDs without waiting for
// AudioContext to be running — files arrive before the first gesture.
export async function preload(routeIds) {
    if (!Array.isArray(routeIds) || routeIds.length === 0) return;
    console.log('[TransitSynth] preload: starting for ' + routeIds.length + ' routes');
    const results = await Promise.allSettled(routeIds.map(id => instrumentFor(id)));
    const failed = results.filter(r => r.status === 'rejected').length;
    console.log('[TransitSynth] preload: complete — ' + (routeIds.length - failed) + ' loaded, ' + failed + ' failed');
}

// Attaches a one-shot native click listener to the unlock button so that
// AudioContext.resume() fires synchronously inside the gesture event, before
// Blazor's async interop chain breaks the browser's autoplay trust window
// (iOS Safari). Uses a bare AudioContext created at module load so there is
// zero dependency on the Tone.js import — the handler is attached immediately.
//
// When Tone.js finishes loading, its context is replaced with our already-running
// AudioContext via setContext, so every later triggerNote runs without issue.
export function attachUnlockGesture(elementId) {
    console.log('[TransitSynth] attachUnlockGesture: elementId=' + elementId);
    const el = document.getElementById(elementId);
    if (!el) {
        console.warn('[TransitSynth] attachUnlockGesture: element not found, id=' + elementId);
        return;
    }

    function handler() {
        el.removeEventListener('click', handler);
        // Resume bare AudioContext synchronously within the gesture stack.
        // iOS Safari requires the resume() call to originate here, not in a
        // downstream Promise chain.
        if (_audioCtx && _audioCtx.state !== 'running') {
            _audioCtx.resume();
        }
        _unlocked = true;
        console.log('[TransitSynth] _unlocked set true via native gesture');
    }
    el.addEventListener('click', handler);

    // When Tone.js finishes loading, swap in our bare AudioContext so Tone
    // uses the exact same context that the native gesture handler resumes.
    // Always adopt — even while suspended — so that when the gesture fires,
    // ctx.resume() on the bare AudioContext wakes Tone up too.
    _tonePromise.then(T => {
        _tone = T;
        if (_audioCtx) {
            try {
                T.setContext(new T.Context(_audioCtx));
                console.log('[TransitSynth] Tone context adopted from bare AudioContext, state=' + _audioCtx.state);
            } catch (err) {
                console.warn('[TransitSynth] could not adopt bare AudioContext into Tone, falling back', err);
            }
        }
    });
}

export async function unlock() {
    console.log('[TransitSynth] unlock: called, _unlocked=' + _unlocked);
    if (_unlocked) return;
    const T = await getTone();
    console.log('[TransitSynth] unlock: calling T.start(), context state=' + T.getContext().rawContext.state);
    await T.start();
    _unlocked = true;
    console.log('[TransitSynth] unlock: complete, context state=' + T.getContext().rawContext.state);
}

export function isUnlocked() {
    return _unlocked;
}

// triggerIndex and totalTriggers come from checkpoint-tracker; both default to
// 0/1 so the C# interop path (which omits them) still plays without error.
export async function triggerNote(routeId, vehicleId, triggerIndex = 0, totalTriggers = 1) {
    if (!_unlocked) {
        console.log('[TransitSynth] triggerNote: skipped, not unlocked (route=' + routeId + ')');
        return;
    }
    try {
        const T = await getTone();
        const ctx = T.getContext().rawContext;
        if (ctx.state !== 'running') {
            console.warn('[TransitSynth] triggerNote: context not running, resuming, state=' + ctx.state + ' route=' + routeId);
            ctx.resume();
        }
        const { sampler, scale, durations } = await instrumentFor(routeId);
        const note = noteForPosition(scale, triggerIndex, totalTriggers);
        const duration = durations[djb2(String(vehicleId)) % durations.length];
        console.log('[TransitSynth] play route=' + routeId + ' note=' + note + ' duration=' + duration + ' contextState=' + ctx.state);
        sampler.triggerAttackRelease(note, duration);
    } catch (err) {
        console.warn('[TransitSynth] triggerNote error:', err);
    }
}

export async function dispose() {
    for (const { sampler } of _instrumentCache.values()) {
        try { sampler.dispose(); } catch (_) { /* ignore */ }
    }
    _instrumentCache.clear();
    _pendingInstruments.clear();
    _unlocked = false;
    _tone = null;
}

window.TransitSynth = { unlock, isUnlocked, preload, attachUnlockGesture, triggerNote, dispose };
