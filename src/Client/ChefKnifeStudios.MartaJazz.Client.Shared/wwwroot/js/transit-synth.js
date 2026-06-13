// transit-synth.js — Tone.js synthesis module for 009-transit-soundscape
// Route → instrument (deterministic palette), vehicle → pitch (C-minor pentatonic)

let _tone = null;
let _unlocked = false;
const _instrumentCache = new Map();
const _pitchCache = new Map();

async function getTone() {
    if (_tone) return _tone;
    const mod = await import('https://esm.sh/tone@15');
    if (!_tone) _tone = mod;  // only assign if still null
    return _tone;
}

// djb2 hash — deterministic, no crypto needed
function djb2(s) {
    let h = 5381;
    for (let i = 0; i < s.length; i++) {
        h = ((h << 5) + h + s.charCodeAt(i)) | 0;
    }
    return h >>> 0;
}

// C-minor pentatonic across two octaves (MIDI note numbers)
const SCALE = [48, 51, 53, 55, 58, 60, 63, 65, 67, 70];

// Six Tone.js voices — audibly distinct, pleasant in combination.
// PolySynth only accepts Monophonic subclasses: Synth, AMSynth, FMSynth, DuoSynth.
// PluckSynth, MembraneSynth, MetalSynth are polyphonic already — use them directly.
const PALETTE = [
    { build: (T) => new T.PolySynth(T.Synth, { oscillator: { type: 'triangle' }, envelope: { attack: 0.2, release: 0.6 } }).toDestination() },
    { build: (T) => new T.PolySynth(T.AMSynth).toDestination() },
    { build: (T) => new T.PolySynth(T.FMSynth, { modulationIndex: 2 }).toDestination() },
    { build: (T) => new T.PolySynth(T.DuoSynth, { vibratoAmount: 0.1, harmonicity: 1.5 }).toDestination() },
    { build: (T) => new T.PluckSynth().toDestination() },
    { build: (T) => new T.MembraneSynth({ volume: -12 }).toDestination() },
];

async function instrumentFor(routeId) {
    if (_instrumentCache.has(routeId)) return _instrumentCache.get(routeId);
    const T = await getTone();
    // Check again after the await — another concurrent call may have populated it
    if (_instrumentCache.has(routeId)) return _instrumentCache.get(routeId);
    const h = djb2(String(routeId));
    const inst = PALETTE[h % PALETTE.length].build(T);
    _instrumentCache.set(routeId, inst);
    return inst;
}

function pitchFor(vehicleId) {
    if (_pitchCache.has(vehicleId)) return _pitchCache.get(vehicleId);
    const h = djb2(String(vehicleId));
    const midiNote = SCALE[h % SCALE.length];
    _pitchCache.set(vehicleId, midiNote);
    return midiNote;
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

export async function triggerNote(routeId, vehicleId) {
    try {
        const T = await getTone();
        if (T.context.state !== 'running') {
            await T.start();
        }
        const inst = await instrumentFor(routeId);
        const midiNote = pitchFor(vehicleId);
        const freq = T.Frequency(midiNote, 'midi').toFrequency();
        inst.triggerAttackRelease(freq, '8n');
    } catch (err) {
        console.warn('[TransitSynth] triggerNote error:', err);
    }
}

export async function dispose() {
    for (const inst of _instrumentCache.values()) {
        try { inst.dispose(); } catch (_) { /* ignore */ }
    }
    _instrumentCache.clear();
    _pitchCache.clear();
    _unlocked = false;
    _tone = null;
}

window.TransitSynth = { unlock, isUnlocked, triggerNote, dispose };
