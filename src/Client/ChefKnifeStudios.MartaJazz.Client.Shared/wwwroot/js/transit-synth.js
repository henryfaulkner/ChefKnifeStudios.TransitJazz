// transit-synth.js — Tone.js synthesis module for 009-transit-soundscape
// Route → instrument (3-voice acoustic jazz trio via Sampler)
// Pitch maps trigger position along route → C-minor pentatonic scale degree

let _tone = null;
let _unlocked = false;
const _instrumentCache = new Map();

const SOUNDFONT_BASE = 'https://gleitz.github.io/midi-js-soundfonts/FluidR3_GM';

// C-minor pentatonic: C Eb F G Bb — kept in low octaves for warmth
const SCALE_BASS = ['C2','Eb2','F2','G2','Bb2','C3','Eb3','F3','G3','Bb3'];
const SCALE_LOW  = ['C2','Eb2','F2','G2','Bb2','C3','Eb3','F3','G3','Bb3'];

// Three sampled voices cycling across routes: bass → viola → cello → bass …
const PALETTE = [
    {
        instrument: 'contrabass',
        scale: SCALE_BASS,
        notes: { C2:'C2',Eb2:'Eb2',F2:'F2',G2:'G2',Bb2:'Bb2',C3:'C3',Eb3:'Eb3',F3:'F3',G3:'G3',Bb3:'Bb3' },
        release: 1.2,
        durations: ['4n', '4n.', '2n'],
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
    const mod = await import('https://esm.sh/tone@15');
    if (!_tone) _tone = mod;
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

async function instrumentFor(routeId) {
    if (_instrumentCache.has(routeId)) return _instrumentCache.get(routeId);
    const T = await getTone();
    if (_instrumentCache.has(routeId)) return _instrumentCache.get(routeId);

    const h = djb2(String(routeId));
    const slot = PALETTE[h % PALETTE.length];

    return new Promise((resolve, reject) => {
        const sampler = new T.Sampler(
            buildSampleUrls(slot.instrument, slot.notes),
            {
                release: slot.release ?? 1.2,
                onload: () => {
                    const vol = new T.Volume(slot.volume ?? 0).toDestination();
                    sampler.connect(vol);
                    _instrumentCache.set(routeId, { sampler, scale: slot.scale, durations: slot.durations });
                    resolve({ sampler, scale: slot.scale, durations: slot.durations });
                },
                onerror: (err) => {
                    console.error('[TransitSynth] sampler load failed for routeId=' + routeId + ' instrument=' + slot.instrument, err);
                    reject(err);
                },
            }
        );
    });
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
    await Promise.allSettled(routeIds.map(id => instrumentFor(id)));
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
    try {
        const T = await getTone();
        if (T.context.state !== 'running') {
            await T.start();
        }
        const { sampler, scale, durations } = await instrumentFor(routeId);
        const note = noteForPosition(scale, triggerIndex, totalTriggers);
        const duration = durations[djb2(String(vehicleId)) % durations.length];
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
    _unlocked = false;
    _tone = null;
}

window.TransitSynth = { unlock, isUnlocked, preload, triggerNote, dispose };
