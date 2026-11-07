// transit-synth.js — Tone.js synthesis module for 009-transit-soundscape
// Route → instrument (6-voice sampled palette via Tone.Sampler; only 3 ship to prod — see
// PROD_INSTRUMENTS below)
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
//      PALETTE.length — this is what drove RAM to 1.7GB. Do not key this
//      cache by routeId. NOTE: resident cost scales with PALETTE.length (at
//      most 6 Samplers/Reverbs now, up from 3) — still flat regardless of
//      route/vehicle count, but roughly double the earlier fixed floor.
// Samples are FluidR3 GM (https://gleitz.github.io/midi-js-soundfonts,
// {instrument}-mp3.js per-note base64 MP3s) — same CDN as the legacy build.
// Palette is 6 voices (design doc §1/§3 plucked/struck picks + the legacy
// bowed-string trio brought back alongside them, not replacing them):
//   pizzicato_strings (pluck), acoustic_bass (sub bass), acoustic_grand_piano
//   (matches the Piano/Salamander-piano voice both sibling reference
//   projects, lofi-engine and lofi-station, use), contrabass, viola, cello.
// PROD SHIP LIST: only pizzicato_strings/acoustic_bass/acoustic_grand_piano play in prod —
// contrabass/viola/cello stay in PALETTE (this file has no dev/prod build split — it's a
// plain script, no bundler strip step) but are excluded by default via _enabledSlots
// (seeded from PROD_INSTRUMENTS, see below). Only the #if DEBUG-gated SettingsBlade dev
// panel calls setEnabledInstruments to opt them back in for development.
//
// SOFTNESS/TEXTURE PASS (post-anchor-notes tuning, informed by the sibling reference
// projects lofi-engine and lofi-station): the original chain was
// sampler → filter(3500Hz) → volume → reverb(wet 0.15) → Destination, per voice, dry and
// bright, with fully deterministic (unrandomized) note/velocity/timing. Now:
//   Per-voice: sampler → filter(1800Hz, darker) → StereoWidener(0.4) → volume →
//              reverb(decay 1.4/wet 0.35, wetter+longer) → shared master bus.
//   Master bus (built once, shared by all slots — see getMasterBus): Compressor → Filter
//              (4000Hz) → Destination. A continuous quiet pink-noise bed (lowshelf-filtered,
//              -38dB) feeds into the same compressor for constant ambient texture instead of
//              dead silence between notes. The noise generator only STARTS when audio is
//              enabled (see setAudioEnabled) — it must be silent whenever the app's mute
//              setting is on, same as every other voice, not just note triggers.
//   Per-note: triggerAttackRelease now passes a small randomized velocity (0.75-1.0) and a
//              +/-20ms start-time jitter. Note/duration selection stays djb2-deterministic
//              (durationSecondsFor must keep agreeing with what's actually played), so only
//              velocity/timing are humanized, not pitch/duration choice.
// ============================================================================

let _tone = null;
let _unlocked = false;
let _unlockGeneration = 0;
let _unlockPromise = null;
let _lastGestureAttemptPromise = null;
const _unlockGestureHandlers = new WeakMap();
const _audioFabGestureHandlers = new WeakMap();
let _lastAudioFabGestureAttemptPromise = null;
let _lastAudioFabGestureWasRunning = false;

// [TTFN] probe — window.TtfnProbe, mirrors the window.MemoryProbe idiom. Read-only
// instrumentation for feature 045 (time-to-first-note); see specs/045-time-to-first-note/
// contracts/ttfn-probe.md. `version` is replaced at WASM-publish time with the deploy
// commit short-SHA (see .github/workflows) so [TTFN] lines are comparable across deploys —
// left as the placeholder here is a build-pipeline bug, not expected behavior.
const _ttfn = {
    version: 'SET-PER-DEPLOY',
    unlockAt: null,
    firstTriggerAt: null,
    firstAudibleAt: null,
    droppedWhileLocked: 0,
    noiseBedAt: null,
};
window.TtfnProbe = _ttfn;

function _ttfnMarkUnlock() {
    if (_ttfn.unlockAt !== null) return;
    _ttfn.unlockAt = performance.now();
    performance.mark('ttfn:unlock');
}
// Mirrors the C# SettingsBlade "audio enabled" toggle (AudioSettingChangedEventArgs), kept
// current by setAudioEnabled on every toggle. This is the LIVE, fire-time mute gate for both
// the continuous noise bed AND triggerNote — the crossing dispatcher captures an audioEnabled
// flag once per batch and schedules notes up to a cycle out, so triggerNote must re-check this
// at fire time or a mid-batch mute would still sound every queued note. Defaults to true so
// audio behaves normally until told otherwise; setAudioEnabled(false) is called during init if
// the persisted setting is muted (see TransitMap.razor.cs).
let _audioEnabled = true;
// Selected background "backfill" texture (049-backfill-texture-selector). 'noise' | 'percussion',
// NEVER 'off' — there is always a backfill; total silence stays _audioEnabled's job. Mirrors the
// persisted Settings.BackfillTexture; pushed from C# on init and on every FAB selection via
// setBackfillTexture. Reconciled against _audioEnabled by the single _applyBackfillLayer choke
// point so the two gates (mute × texture) never drift.
let _backfillMode = 'noise';
// Lazily-built { loop, kick, rim, volume } once percussion is first needed, or null.
let _percussion = null;
// PROD SHIP LIST — only these instruments play unless a dev build opts more in via
// setEnabledInstruments (see docs/SYNTH_REFACTOR_DESIGN_DOCUMENT.md — SettingsBlade dev
// section, #if DEBUG-gated on the C# side). This file has no build-time dev/prod variants
// (plain script, no bundler strip step), so the split is runtime: prod never calls
// setEnabledInstruments and gets exactly this set; the dev-only SettingsBlade panel calls
// it on init with the FULL PALETTE instrument list to restore all 6 for development.
const PROD_INSTRUMENTS = ['pizzicato_strings', 'acoustic_bass', 'acoustic_grand_piano'];

// DEV-ONLY instrument toggle. Slot indices allowed to be selected by _slotIndexForRoute.
// Starts SCOPED to PROD_INSTRUMENTS (not "all enabled") so prod, which never calls
// setEnabledInstruments, only ever plays the 3 shipped voices — contrabass/viola/cello
// stay reachable only once a dev build explicitly re-enables them. Never persisted —
// in-memory only, reset on page reload.
let _enabledSlots = null; // populated lazily from PROD_INSTRUMENTS below, once PALETTE exists
// PALETTE index → { sampler, scale, durations } once loaded, or a pending Promise while
// loading. Keyed by SLOT, not routeId — every route mapping to the same slot (there are
// only PALETTE.length of them) shares one Sampler/Filter/Reverb. Keying by routeId instead
// built a distinct Sampler + Reverb (each with its own decoded-PCM anchors AND its own
// convolution impulse-response buffer) per ROUTE, which is what drove RAM past even the
// original ~1.2GB regression on a system with dozens of concurrent routes.
const _instrumentCache = new Map();
const _activeNoteEndsAt = [];
let _masterMuted = false;
let _audioCapabilityHealthReceiver = null;
let _audioCapabilityGeneration = 0;
let _audioCapabilityRequested = false;
let _audioCapabilityUnlocked = false;
const _audioCapabilityRequiredAt = new Map();
const _audioCapabilityReadySlots = new Set();
const _audioCapabilityFailedSlots = new Set();
let _audioCapabilityHealthTimer = null;

const SOUNDFONT_BASE = 'https://gleitz.github.io/midi-js-soundfonts/FluidR3_GM';

// C-minor pentatonic across the low octaves used for harmony warmth. These are the
// PLAYED notes; the Sampler interpolates them from the anchor samples below.
const SCALE = ['C2', 'Eb2', 'F2', 'G2', 'Bb2', 'C3', 'Eb3', 'F3', 'G3', 'Bb3'];

// Two anchor notes per instrument — the only MP3s actually fetched + decoded. One low,
// one high, so the worst-case pitch shift to any SCALE note is ~3 semitones.
const ANCHORS = { C2: 'C2', C3: 'C3' };

// Six sampled voices cycling across routes: pluck → bass → piano → contrabass → viola →
// cello → pluck … First 3 are the plucked/struck picks from design doc §1/§3. Last 3
// (contrabass/viola/cello) are the original bowed-string trio from the legacy Sampler
// build, brought back alongside — not in place of — the newer voices for extra variety.
// contrabass/viola/cello samples are themselves bowed (arco) recordings — Tone.Sampler
// only plays back + envelopes a fixed recording, it can't re-synthesize a plucked attack
// transient from a bowed one. The closest approximation within Sampler's own params:
// attack ~0 (no fade-in, as fast an onset as the sample allows) + a SHORT release (cuts
// the bow's natural sustain off quickly after note-off, so it reads as struck/plucked
// rather than sustained) + short duration tokens (note-off itself comes sooner). This is
// why the bowed trio's release/durations are deliberately SHORTER than their piano/bass/
// pluck pairing from the previous tuning pass, not matched to it.
// volumeDb (optional, defaults to 0 = sample's native recorded loudness) trims a single
// voice via a per-slot Tone.Volume in the chain — piano's -8dB softens it relative to the
// other 5 voices, which play at their native level.
const PALETTE = [
    { instrument: 'pizzicato_strings',    scale: SCALE, anchors: ANCHORS, attack: 0,    release: 1.2,  durations: ['4n', '4n.', '2n'] },
    { instrument: 'acoustic_bass',        scale: SCALE, anchors: ANCHORS, attack: 0,    release: 0.8,  durations: ['8n', '8n.', '4n'] },
    { instrument: 'acoustic_grand_piano', scale: SCALE, anchors: ANCHORS, attack: 0,    release: 1.0,  durations: ['8n', '8n.', '4n'], volumeDb: -8 },
    { instrument: 'contrabass',           scale: SCALE, anchors: ANCHORS, attack: 0.005, release: 0.3, durations: ['8n', '8n.', '4n'] },
    { instrument: 'viola',                scale: SCALE, anchors: ANCHORS, attack: 0.005, release: 0.2, durations: ['16n', '16n.', '8n'] },
    { instrument: 'cello',                scale: SCALE, anchors: ANCHORS, attack: 0.005, release: 0.25, durations: ['16n', '16n.', '8n'] },
];

// Maps PALETTE.instrument names → their slot INDICES (not the names themselves — see
// _slotIndexForRoute, which filters on index).
function _slotIndicesForNames(names) {
    const slots = new Set();
    PALETTE.forEach((slot, index) => {
        if (names.includes(slot.instrument)) slots.add(index);
    });
    return slots;
}

// Seeds _enabledSlots to the PROD_INSTRUMENTS ship list. Prod builds never call
// setEnabledInstruments, so this seed is the ENTIRE prod behavior: only
// pizzicato_strings/acoustic_bass/acoustic_grand_piano play.
_enabledSlots = _slotIndicesForNames(PROD_INSTRUMENTS);

// Default Tone Transport tempo is 120 BPM (unchanged in this app):
//   2n = 1.0s, 4n. = 0.75s, 4n = 0.5s, 8n. = 0.375s, 8n = 0.25s, 16n = 0.125s, 16n. = 0.1875s
const DURATION_SECONDS = {
    '16n': 0.125, '16n.': 0.1875,
    '8n': 0.25, '8n.': 0.375,
    '4n': 0.5, '4n.': 0.75,
    '2n': 1.0,
};

// Reverb parameters — softened from the v1 shipped values (decay 0.8/wet 0.15) toward the
// lofi-engine/lofi-station reference recipe (~1.5s decay / ~0.8 wet) for more tail/texture.
const REVERB_DECAY = 1.4;
const REVERB_PRE_DELAY = 0.02;
const REVERB_WET = 0.35;
const VOICE_LOAD_TIMEOUT_MS = 10_000;

// Per-voice lowpass cutoff — darkened from the original 3500Hz (barely filtered) toward the
// lofi reference range (1000-2400Hz) for the "muffled"/soft character. Kept above the bowed
// trio's playable range so contrabass/viola/cello aren't over-darkened.
const FILTER_CUTOFF_HZ = 1800;

// Per-voice stereo widener amount — 0 = mono/centered (the original had no spatial width at
// all), matching the ~0.3-0.7 range lofi-engine uses per drum/instrument voice.
const STEREO_WIDTH = 0.4;

// Master bus (see buildMasterBus) — glue compressor + a second, gentler lowpass over the
// whole mix, mirroring lofi-engine's Tone.Master.chain(compressor, lpf, volume).
const MASTER_COMPRESSOR = { threshold: -18, ratio: 3, attack: 0.02, release: 0.25 };
const MASTER_FILTER_HZ = 4000;

// Continuous pink-noise bed — a quiet, constantly-running texture layer (tape-hiss/vinyl-air
// surrogate) mixed under every voice via the master bus. Silent transport = no notes, but a
// faint continuous bed rather than dead air, matching lofi-engine's Noise.ts recipe.
const NOISE_VOLUME_DB = -38;
const NOISE_FILTER_HZ = 2000;

// Percussion backfill texture (049-backfill-texture-selector) — a sparse, humanized lo-fi
// kick+rim kit on a slow Tone.Loop, the alternative to the noise bed above. STARTING-POINT
// values pending the by-ear audition pass in tools/instrument-compat/
// (contracts/audition-tool.md) before being treated as final.
// REAL RECORDED HITS via Tone.Sampler (same FluidR3 GM CDN + URL shape as the melodic
// PALETTE voices) — the earlier MembraneSynth/MetalSynth synthesis read as drum-machine,
// not drums, in the 049 audition. Kick is a tom recording repitched to C2 (65Hz — C1's
// 33Hz fundamental is below what most laptop speakers reproduce); rim is a woodblock.
// Volumes stack (voice dB + layer dB) before the master compressor.
// Placement is DETERMINISTIC (audition feedback: probabilistic rim hits read as procedural,
// not human): the loop is one 4/4 measure ('1m', 2s at the default 120 BPM Transport) and
// each voice lands on fixed quarter-note beats (1-4) within it. Only ±20ms micro-jitter and
// velocity vary per hit — the pattern itself never changes.
const PERCUSSION_VOLUME_DB = -16;
const PERCUSSION_KICK_INSTRUMENT = 'melodic_tom';
const PERCUSSION_KICK_PITCH = 'C2';
const PERCUSSION_KICK_VOLUME_DB = -16;
const PERCUSSION_KICK_BEATS = [1, 2, 3, 4];
const PERCUSSION_RIM_INSTRUMENT = 'woodblock';
const PERCUSSION_RIM_PITCH = 'C4';
const PERCUSSION_RIM_VOLUME_DB = -16;
const PERCUSSION_RIM_BEATS = [];

// Small per-note humanization bounds. Timing offset only nudges the AUDIBLE trigger time —
// it must NOT change note/duration selection (djb2-driven, shared with durationSecondsFor
// for trail-length sync), so only velocity and a tiny start-time jitter are randomized here.
const HUMANIZE_TIME_JITTER_SEC = 0.02;
const HUMANIZE_VELOCITY_MIN = 0.75;
const HUMANIZE_VELOCITY_MAX = 1.0;

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
// _enabledSlots is ALWAYS populated (seeded to PROD_INSTRUMENTS above; dev builds widen it
// via setEnabledInstruments) — a route that hashes to a disabled slot deterministically
// falls forward to the next enabled slot (wrapping), keeping every route's voice stable as
// OTHER instruments are toggled rather than reshuffling the whole palette assignment.
function _slotIndexForRoute(routeId) {
    const base = djb2(String(routeId)) % PALETTE.length;
    if (!_enabledSlots || _enabledSlots.size === 0) return base; // defensive: never expected
    for (let i = 0; i < PALETTE.length; i++) {
        const candidate = (base + i) % PALETTE.length;
        if (_enabledSlots.has(candidate)) return candidate;
    }
    return base; // unreachable given the size===0 guard above, kept for safety
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

// Shared master bus: every slot's per-voice chain feeds into this instead of straight to
// T.Destination, plus a constantly-running quiet pink-noise bed for continuous texture.
// Built once, lazily, on first instrument load. Compressor gives the whole mix glue/cohesion;
// the second lowpass further softens the combined signal beyond each voice's own filter.
// The noise node is only STARTED if _audioEnabled is true at build time — see
// setAudioEnabled, which starts/stops it on later mute toggles.
let _masterBus = null;
function getMasterBus(T) {
    if (_masterBus) return _masterBus;
    const compressor = new T.Compressor(MASTER_COMPRESSOR);
    const filter = new T.Filter(MASTER_FILTER_HZ, 'lowpass');
    const output = new T.Gain(_masterMuted ? 0 : 1);
    compressor.chain(filter, output, T.Destination);

    const noise = new T.Noise('pink');
    const noiseFilter = new T.Filter(NOISE_FILTER_HZ, 'lowshelf');
    const noiseVolume = new T.Volume(NOISE_VOLUME_DB);
    noise.chain(noiseFilter, noiseVolume, compressor);

    _masterBus = { input: compressor, noise, output };
    _applyBackfillLayer();
    return _masterBus;
}

function _humanVelocity() {
    return HUMANIZE_VELOCITY_MIN + Math.random() * (HUMANIZE_VELOCITY_MAX - HUMANIZE_VELOCITY_MIN);
}

// Builds the percussion backfill kit lazily, once: a soft sampled tom-thump kick + an
// occasional sampled woodblock rim, humanized like the note triggers, on a slow Tone.Loop.
// Feeds bus.input (the master compressor) — NOT Destination — so it inherits the same master
// glue/softening as the noise bed and every sampled voice. Starting the loop starts
// Tone.Transport — the app's FIRST use of it; orthogonal to note triggers, which fire off
// _tone.now(), unscheduled on the Transport (contracts/synth-engine.md INV-5).
function buildPercussion(T) {
    const bus = getMasterBus(T);
    const volume = new T.Volume(PERCUSSION_VOLUME_DB);
    volume.connect(bus.input);

    // Real recorded hits via Tone.Sampler — the Sampler repitches the two anchor recordings,
    // exactly like the melodic PALETTE voices (and reuses their buildSampleUrls URL shape).
    // Four extra small one-shot buffers total; negligible next to the melodic anchors.
    const kick = new T.Sampler({
        urls: buildSampleUrls(PERCUSSION_KICK_INSTRUMENT, { C2: 'C2', C3: 'C3' }),
        volume: PERCUSSION_KICK_VOLUME_DB,
        release: 1,
    });
    kick.connect(volume);

    const rim = new T.Sampler({
        urls: buildSampleUrls(PERCUSSION_RIM_INSTRUMENT, { C4: 'C4', C5: 'C5' }),
        volume: PERCUSSION_RIM_VOLUME_DB,
        release: 0.5,
    });
    rim.connect(volume);

    // One callback per 4/4 measure; each voice's hits are scheduled at its fixed
    // PERCUSSION_*_BEATS quarter-note offsets within the measure. Deterministic placement —
    // only ±20ms micro-jitter + velocity humanize per hit (Loop callbacks fire a lookAhead
    // early, so time minus 20ms is still schedulable). loaded guards: the sample fetch/decode
    // is async while buildPercussion is sync — ticks that fire before the buffers land are
    // skipped rather than throwing inside the Transport callback.
    const loop = new T.Loop((time) => {
        const beatSec = T.Time('4n').toSeconds();
        const jitter = () => (Math.random() * 2 - 1) * HUMANIZE_TIME_JITTER_SEC;
        if (kick.loaded) {
            for (const beat of PERCUSSION_KICK_BEATS) {
                kick.triggerAttackRelease(PERCUSSION_KICK_PITCH, '2n', time + (beat - 1) * beatSec + jitter(), _humanVelocity());
            }
        }
        if (rim.loaded) {
            for (const beat of PERCUSSION_RIM_BEATS) {
                rim.triggerAttackRelease(PERCUSSION_RIM_PITCH, '4n', time + (beat - 1) * beatSec + jitter(), _humanVelocity());
            }
        }
    }, '1m');

    return { loop, kick, rim, volume };
}

// The single choke point reconciling _audioEnabled x _backfillMode into exactly which of
// {noise, percussion} runs. BOTH setAudioEnabled and setBackfillTexture call this so the two
// gates never drift (contracts/synth-engine.md): muted -> both stopped; unmuted -> exactly the
// selected texture running, the other stopped. Percussion is built lazily on first need.
function _applyBackfillLayer() {
    if (!_masterBus) return;

    if (!_audioEnabled) {
        if (_masterBus.noise.state === 'started') _masterBus.noise.stop();
        if (_percussion && _percussion.loop.state === 'started') _percussion.loop.stop();
        return;
    }

    if (_backfillMode === 'percussion') {
        if (_masterBus.noise.state === 'started') _masterBus.noise.stop();
        if (!_percussion) _percussion = buildPercussion(_tone);
        if (_percussion.loop.state !== 'started') {
            _tone.getTransport().start();
            _percussion.loop.start(0);
        }
    } else {
        if (_percussion && _percussion.loop.state === 'started') _percussion.loop.stop();
        if (_masterBus.noise.state !== 'started') _masterBus.noise.start();
    }
}

function _pruneActiveNotes() {
    const now = performance.now();
    for (let i = _activeNoteEndsAt.length - 1; i >= 0; i--) {
        if (_activeNoteEndsAt[i] <= now) _activeNoteEndsAt.splice(i, 1);
    }
}

function _releaseActiveVoices() {
    let succeeded = true;
    for (const entry of _instrumentCache.values()) {
        if (!entry?.sampler) continue; // unresolved warm/load promises cannot be sounding
        try { entry.sampler.releaseAll(); }
        catch (_) { succeeded = false; }
    }
    if (_percussion) {
        try { _percussion.kick.releaseAll(); }
        catch (_) { succeeded = false; }
        try { _percussion.rim.releaseAll(); }
        catch (_) { succeeded = false; }
    }
    _pruneActiveNotes();
    return succeeded;
}

// Mirrors the app's mute/unmute setting into the JS module. Called from C# on init (with the
// persisted setting) and whenever AudioSettingChangedEventArgs fires. Updates _audioEnabled —
// the live gate triggerNote re-reads at fire time — and starts/stops the continuous noise bed
// immediately. If the master bus hasn't been built yet (no instrument loaded so far), the flag
// is still recorded so both getMasterBus (noise bed) and triggerNote honor it once audio runs.
//
// Return true only when normal stop leaves no tracked source active. The caller attempts the
// output-bus mute and verifies it if an already-triggered note or percussion hit can still ring.
export function setAudioEnabled(enabled) {
    const previous = _audioEnabled;
    const percussionWasRunning = !enabled && _percussion?.loop.state === 'started';
    _audioEnabled = !!enabled;
    try {
        _applyBackfillLayer();
        if (_audioEnabled) return true;

        const released = _releaseActiveVoices();
        const noiseStopped = _masterBus?.noise.state !== 'started';
        const percussionStopped = _percussion?.loop.state !== 'started';
        return released
            && !percussionWasRunning
            && noiseStopped
            && percussionStopped
            && _activeNoteEndsAt.length === 0;
    } catch (error) {
        _audioEnabled = previous;
        console.warn('[TransitSynth] audio setting effect failed', error);
        return false;
    }
}

export function setMasterMuted(muted) {
    const next = !!muted;
    try {
        if (_masterBus) {
            _masterBus.output.gain.value = next ? 0 : 1;
            if (_masterBus.output.gain.value !== (next ? 0 : 1)) return false;
        }
        _masterMuted = next;
        return true;
    } catch (error) {
        console.warn('[TransitSynth] master mute effect failed', error);
        return false;
    }
}

// The .NET owner receives only fixed palette masks, never note/sample/URL details.
export function setAudioCapabilityHealthReceiver(receiver) {
    _audioCapabilityHealthReceiver = receiver || null;
}

export function beginAudioCapabilityHealth(generation, audioRequested, audioUnlocked) {
    _clearAudioCapabilityHealthTimer();
    _audioCapabilityGeneration = Number.isSafeInteger(generation) ? generation : _audioCapabilityGeneration + 1;
    _audioCapabilityRequested = !!audioRequested;
    _audioCapabilityUnlocked = !!audioUnlocked && isUnlocked();
    _audioCapabilityRequiredAt.clear();
    _audioCapabilityReadySlots.clear();
    _audioCapabilityFailedSlots.clear();
}

export function stopAudioCapabilityHealth() {
    _audioCapabilityRequested = false;
    _audioCapabilityUnlocked = false;
    _audioCapabilityRequiredAt.clear();
    _audioCapabilityReadySlots.clear();
    _audioCapabilityFailedSlots.clear();
    _clearAudioCapabilityHealthTimer();
}

function _audioCapabilityMasks() {
    let requiredSlotMask = 0;
    let readySlotMask = 0;
    let failedSlotMask = 0;
    for (const [slotIndex] of _audioCapabilityRequiredAt) requiredSlotMask |= 1 << slotIndex;
    for (const slotIndex of _audioCapabilityReadySlots) readySlotMask |= 1 << slotIndex;
    for (const slotIndex of _audioCapabilityFailedSlots) failedSlotMask |= 1 << slotIndex;
    return { requiredSlotMask, readySlotMask, failedSlotMask };
}

function _reportAudioCapabilityHealth(terminalFailure = false) {
    const receiver = _audioCapabilityHealthReceiver;
    if (!receiver || !_audioCapabilityRequested || !_audioCapabilityUnlocked) return;
    const masks = _audioCapabilityMasks();
    if (masks.requiredSlotMask === 0) return;
    receiver.invokeMethodAsync(
        'OnAudioCapabilityHealthChanged',
        _audioCapabilityGeneration,
        masks.requiredSlotMask,
        masks.readySlotMask,
        masks.failedSlotMask,
        !!terminalFailure,
        isUnlocked()
    ).catch(error => console.warn('[TransitSynth] palette health report failed', error));
}

function _clearAudioCapabilityHealthTimer() {
    if (_audioCapabilityHealthTimer !== null) {
        clearTimeout(_audioCapabilityHealthTimer);
        _audioCapabilityHealthTimer = null;
    }
}

function _scheduleAudioCapabilityEvaluation() {
    _clearAudioCapabilityHealthTimer();
    if (!_audioCapabilityHealthReceiver || !_audioCapabilityRequested || !_audioCapabilityUnlocked) return;

    const unresolvedSince = [..._audioCapabilityRequiredAt.entries()]
        .filter(([slotIndex]) => !_audioCapabilityReadySlots.has(slotIndex) && !_audioCapabilityFailedSlots.has(slotIndex))
        .map(([, since]) => since);
    if (unresolvedSince.length === 0) return;

    const nextAt = Math.min(...unresolvedSince) + 10_000;
    const delay = Math.max(0, nextAt - performance.now());
    const generation = _audioCapabilityGeneration;
    _audioCapabilityHealthTimer = setTimeout(() => {
        _audioCapabilityHealthTimer = null;
        const receiver = _audioCapabilityHealthReceiver;
        if (!receiver || generation !== _audioCapabilityGeneration) return;
        receiver.invokeMethodAsync('EvaluateAudioCapabilityHealth', generation, isUnlocked())
            .catch(error => console.warn('[TransitSynth] palette health evaluation failed', error))
            .finally(_scheduleAudioCapabilityEvaluation);
    }, delay);
}

function _requireAudioCapabilitySlot(slotIndex) {
    if (!_audioCapabilityRequested || !_audioCapabilityUnlocked) return;

    if (!_audioCapabilityRequiredAt.has(slotIndex)) {
        _audioCapabilityRequiredAt.set(slotIndex, performance.now());
        const cached = _instrumentCache.get(slotIndex);
        if (cached && !(cached instanceof Promise))
            _audioCapabilityReadySlots.add(slotIndex);
        else
            _audioCapabilityFailedSlots.delete(slotIndex);
        _reportAudioCapabilityHealth();
    }
    _scheduleAudioCapabilityEvaluation();
}

function _markAudioCapabilitySlotReady(slotIndex) {
    if (!_audioCapabilityRequiredAt.has(slotIndex)) return;
    _audioCapabilityReadySlots.add(slotIndex);
    _audioCapabilityFailedSlots.delete(slotIndex);
    _reportAudioCapabilityHealth();
    _scheduleAudioCapabilityEvaluation();
}

function _markAudioCapabilitySlotFailed(slotIndex) {
    if (!_audioCapabilityRequiredAt.has(slotIndex)) return;
    _audioCapabilityFailedSlots.add(slotIndex);
    _audioCapabilityReadySlots.delete(slotIndex);
    _reportAudioCapabilityHealth(true);
    _scheduleAudioCapabilityEvaluation();
}

export function verifyAudioSilenced() {
    try {
        if (_masterBus?.output?.gain?.value === 0) return 'stopped';
        const context = _tone?.getContext().rawContext;
        _pruneActiveNotes();
        const noiseRunning = _masterBus?.noise?.state === 'started';
        const percussionRunning = _percussion?.loop.state === 'started';
        const hasActiveSources = noiseRunning || percussionRunning || _activeNoteEndsAt.length > 0;
        if (!hasActiveSources) return 'stopped';
        if (context?.state === 'running') return 'still-playing';
        return 'unknown';
    } catch (error) {
        console.warn('[TransitSynth] audio stop verification failed', error);
        return 'unknown';
    }
}

// Selects which continuous background "backfill" texture plays under the note triggers
// ('noise' | 'percussion'). Mirrors setAudioEnabled's established live-toggle shape. Safe to
// call before the master bus exists — the mode is recorded and honored once getMasterBus
// builds it (contracts/synth-engine.md INV-4).
export function setBackfillTexture(mode) {
    const previous = _backfillMode;
    _backfillMode = (mode === 'percussion') ? 'percussion' : 'noise';
    if (!_masterBus) return true;
    try {
        if (_tone) {
            const ctx = _tone.getContext().rawContext;
            if (ctx && ctx.state !== 'running' && typeof ctx.resume === 'function') {
                ctx.resume().catch(() => { });
            }
        }
        _applyBackfillLayer();
        return true;
    } catch (error) {
        _backfillMode = previous;
        try { _applyBackfillLayer(); } catch (_) { /* retain the prior mode; caller shows local failure */ }
        console.warn('[TransitSynth] backfill texture change failed', error);
        return false;
    }
}

// Kicks off instrumentForSlot for exactly the PROD_INSTRUMENTS slots (never the full
// PALETTE — the 1.2-1.7GB RAM regression lever, see file header) so the MP3 fetch/decode/
// reverb.generate() IR-build for the 3 shipped voices happen before the first crossing
// arrives, not on its critical path (contracts/unlock-warming.md D2). Fire-and-forget —
// callers must not await this, so it never delays overlay dismissal (Principle XI).
//
// EVICTED-BEFORE-FIRST-NOTE EDGE (FR-003, contracts/unlock-warming.md D2): a warmed slot
// can be disposed by EvictInactiveRouteAudioAsync (TransitMap.razor.cs, after 3 absent
// batches) before its route's first note plays. This is accepted, not pinned:
// instrumentForSlot rebuilds transparently on the next triggerNote call (its cache lookup
// misses and it re-fetches), so the first note for that route is still correct — merely
// without the warm-time saving, i.e. no worse than pre-fix behavior.
export function warmProdSamplers() {
    const prodSlots = _slotIndicesForNames(PROD_INSTRUMENTS);
    for (const slotIndex of prodSlots) {
        // Optional prewarming is best-effort. A required crossing retries through its own
        // awaited path and reports only that current palette slot's coarse outcome.
        instrumentForSlot(slotIndex).catch(() => { });
    }
}

// Shared by unlock() and the attachUnlockGesture handler (contracts/unlock-warming.md D1/D2):
// builds the master bus (starting the ambient noise bed iff _audioEnabled) and fires off
// prod-sampler warming, immediately after Tone.start() resolves. Idempotent via
// getMasterBus's own `if (_masterBus) return` guard, so calling this from both unlock paths
// (or a repeat call) never double-builds.
function _warmAtUnlock(T) {
    getMasterBus(T);
    if (_ttfn.noiseBedAt === null) _ttfn.noiseBedAt = performance.now();
    warmProdSamplers();
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

    let loading;
    loading = getTone().then(T => new Promise((resolve, reject) => {
        const bus = getMasterBus(T);
        const filter = new T.Filter(FILTER_CUTOFF_HZ, 'lowpass');
        const widener = new T.StereoWidener(STEREO_WIDTH);
        const reverb = new T.Reverb({ decay: REVERB_DECAY, preDelay: REVERB_PRE_DELAY, wet: REVERB_WET });
        const volume = new T.Volume(slot.volumeDb ?? 0);
        let settled = false;
        let timeoutId = null;
        let sampler = null;
        const cleanup = () => {
            for (const node of [sampler, filter, widener, volume, reverb]) {
                try { node?.dispose(); } catch (_) { /* ignore */ }
            }
        };
        const fail = error => {
            if (settled) return;
            settled = true;
            if (timeoutId !== null) clearTimeout(timeoutId);
            cleanup();
            reject(error);
        };
        try {
            sampler = new T.Sampler(
                buildSampleUrls(slot.instrument, slot.anchors),
                {
                    attack: slot.attack ?? 0,
                    release: slot.release ?? 1.2,
                    onload: () => {
                        if (settled) return;
                        Promise.resolve().then(() => reverb.generate()).then(() => {
                            if (settled) return;
                            settled = true;
                            if (timeoutId !== null) clearTimeout(timeoutId);
                            sampler.chain(filter, widener, volume, reverb, bus.input);
                            const entry = { sampler, scale: slot.scale, durations: slot.durations };
                            _instrumentCache.set(slotIndex, entry); // replace the pending Promise with the resolved entry
                            _markAudioCapabilitySlotReady(slotIndex);
                            resolve(entry);
                        }, fail);
                    },
                    onerror: (err) => {
                        if (settled) return;
                        console.error('[TransitSynth] sampler load failed for slot=' + slotIndex + ' instrument=' + slot.instrument, err);
                        fail(err);
                    },
                }
            );
            timeoutId = setTimeout(() => fail(new Error('audio voice load timed out')), VOICE_LOAD_TIMEOUT_MS);
        } catch (error) {
            fail(error);
        }
    })).catch(error => {
        if (_instrumentCache.get(slotIndex) === loading)
            _instrumentCache.delete(slotIndex); // failed loads are retryable and cannot stay pending
        _markAudioCapabilitySlotFailed(slotIndex);
        throw error;
    });

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
//
// D6 (contracts/unlock-warming.md, FR-010/FR-011): the listener MUST be registered
// BEFORE getTone() is awaited — a slow esm.sh load must not push the addEventListener
// call past the click. The handler itself still awaits getTone()/T.start(), but that
// promise chain resolves inside the browser's trusted-gesture window because it was
// KICKED OFF synchronously from within the click handler, not from a prior await. Do not
// re-add an await before addEventListener here (that reintroduces the iOS
// permanent-silence bug this fixes — see discovery §2.5).
export async function attachUnlockGesture(elementId) {
    const el = document.getElementById(elementId);
    if (!el || _unlockGestureHandlers.has(el)) return;
    function handler() {
        el.removeEventListener('click', handler);
        _unlockGestureHandlers.delete(el);
        _lastGestureAttemptPromise = startUnlockAttempt();
    }
    _unlockGestureHandlers.set(el, handler);
    el.addEventListener('click', handler);
    // Warm the Tone.js import itself during overlay display so the handler's getTone()
    // call above usually resolves instantly; harmless if the click races ahead of this.
    getTone().catch(() => { /* the gesture attempt reports the bounded failure */ });
}

// The audio FAB doubles as a resume action after a previously-running context is
// suspended. Capture context state synchronously in the native event before the
// Blazor callback crosses the JS interop boundary.
export async function attachAudioFabGesture(elementId) {
    const el = document.getElementById(elementId);
    if (!el || _audioFabGestureHandlers.has(el)) return;
    function handler() {
        el.removeEventListener('click', handler);
        _audioFabGestureHandlers.delete(el);
        const rawContext = _tone?.getContext().rawContext;
        _lastAudioFabGestureWasRunning = !!rawContext && rawContext.state === 'running';
        _lastAudioFabGestureAttemptPromise = startUnlockAttempt();
        _lastGestureAttemptPromise = _lastAudioFabGestureAttemptPromise;
    }
    _audioFabGestureHandlers.set(el, handler);
    el.addEventListener('click', handler);
    getTone().catch(() => { /* a native click reports the bounded failure */ });
}

export async function consumeAudioFabGesture() {
    const attempt = _lastAudioFabGestureAttemptPromise;
    if (!attempt) return null;
    const wasRunning = _lastAudioFabGestureWasRunning;
    _lastAudioFabGestureAttemptPromise = null;
    _lastAudioFabGestureWasRunning = false;
    if (_lastGestureAttemptPromise === attempt) _lastGestureAttemptPromise = null;
    return { wasRunning, status: await attempt };
}

function startUnlockAttempt() {
    if (_unlockPromise) return _unlockPromise;
    if (_unlocked && _tone?.getContext().state === 'running') return Promise.resolve('running');

    const generation = _unlockGeneration;
    const attempt = (async () => {
        try {
            const T = await getTone();
            await T.start();
            if (generation !== _unlockGeneration) return 'failed';
            if (T.getContext().state !== 'running') {
                _unlocked = false;
                return 'suspended';
            }

            _warmAtUnlock(T);
            if (generation !== _unlockGeneration) return 'failed';
            _unlocked = true;
            _ttfnMarkUnlock();
            console.log('[TransitSynth] unlocked');
            return 'running';
        } catch (error) {
            if (generation === _unlockGeneration) _unlocked = false;
            console.warn('[TransitSynth] audio unlock attempt failed', error);
            return 'failed';
        }
    })();

    let tracked;
    tracked = attempt.finally(() => {
        if (_unlockPromise === tracked) _unlockPromise = null;
    });
    _unlockPromise = tracked;
    return tracked;
}

export function unlock() {
    if (_lastGestureAttemptPromise) {
        const attempt = _lastGestureAttemptPromise;
        _lastGestureAttemptPromise = null;
        return attempt;
    }
    return startUnlockAttempt();
}

export function isUnlocked() {
    return _unlocked && _tone?.getContext().state === 'running';
}

// Required browser playback capability after the visitor explicitly requests audio.
export function isPlaybackSupported() {
    if (!isUnlocked() || typeof _tone.now !== 'function') return false;
    const context = _tone.getContext();
    const rawContext = context.rawContext;
    return !!rawContext
        && rawContext.state === 'running'
        && !!rawContext.destination
        && typeof rawContext.resume === 'function';
}

// triggerIndex and totalTriggers come from checkpoint-tracker; both default to
// 0/1 so the C# interop path (which omits them) still plays without error.
export async function triggerNote(routeId, vehicleId, triggerIndex = 0, totalTriggers = 1) {
    if (!_unlocked) {
        // [TTFN] probe: counts notes that arrived before unlock. >0 by the time the first
        // audible note plays means this was a dwell/steady-state unlock (notes were already
        // queued); 0 means cold-start (first note ever seen was already past the gate).
        _ttfn.droppedWhileLocked++;
        return;
    }
    // Live mute gate — read at FIRE time, not batch-capture time. The crossing dispatcher
    // schedules each note on a setTimeout up to a full cycle (~10s) out and closes over the
    // audioEnabled flag captured when the batch arrived; a mute that lands after scheduling
    // would otherwise still sound every already-queued note (the AudioFAB "not toggling"
    // symptom). _audioEnabled mirrors the live setting via setAudioEnabled on every toggle.
    if (!_audioEnabled) return;
    if (_ttfn.firstTriggerAt === null) _ttfn.firstTriggerAt = performance.now();
    // Self-heal: if the context slipped back to suspended/interrupted while idle (see
    // setAudioEnabled), resume it before scheduling so this note isn't lost to a frozen clock.
    const ctx = _tone && _tone.getContext().rawContext;
    if (ctx && ctx.state !== 'running' && typeof ctx.resume === 'function') {
        try { await ctx.resume(); } catch (_) { }
    }
    try {
        const slotIndex = _slotIndexForRoute(routeId);
        _requireAudioCapabilitySlot(slotIndex);
        const { sampler, scale, durations } = await instrumentForSlot(slotIndex);
        // The sample/reverb load is asynchronous. A mute may have landed while it was loading;
        // honor the current gate again immediately before scheduling the actual note.
        if (!_audioEnabled || !_unlocked) return;
        const note = noteForPosition(scale, triggerIndex, totalTriggers);
        const duration = durations[djb2(String(vehicleId)) % durations.length];
        // Humanize velocity + start time only — note/duration stay hash-deterministic so
        // durationSecondsFor (trail length) always agrees with what's actually played.
        const velocity = HUMANIZE_VELOCITY_MIN + Math.random() * (HUMANIZE_VELOCITY_MAX - HUMANIZE_VELOCITY_MIN);
        const now = _tone.now();
        const startTime = now + (Math.random() * 2 - 1) * HUMANIZE_TIME_JITTER_SEC;
        sampler.triggerAttackRelease(note, duration, startTime, velocity);
        const durationSec = DURATION_SECONDS[duration] ?? 0.25;
        const releaseSec = PALETTE[slotIndex].release;
        _activeNoteEndsAt.push(performance.now()
            + Math.max(0, (startTime - now) * 1000)
            + (durationSec + releaseSec) * 1000
            + 50);
        // [TTFN] probe: fires once, on the first audible note of the session.
        if (_ttfn.firstAudibleAt === null && _ttfn.unlockAt !== null) {
            _ttfn.firstAudibleAt = performance.now();
            performance.measure('ttfn:unlock->audible', 'ttfn:unlock');
            const unlockToTrigger = _ttfn.firstTriggerAt - _ttfn.unlockAt;
            const triggerToAudible = _ttfn.firstAudibleAt - _ttfn.firstTriggerAt;
            const total = _ttfn.firstAudibleAt - _ttfn.unlockAt;
            console.log(`[TTFN] v=${_ttfn.version} unlock→trigger=${unlockToTrigger.toFixed(0)}ms trigger→audible=${triggerToAudible.toFixed(0)}ms total=${total.toFixed(0)}ms droppedWhileLocked=${_ttfn.droppedWhileLocked}`);
        }
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

// DEV-ONLY: the full list of PALETTE.instrument names, in slot order, for the dev
// checkbox UI to render without hardcoding instrument names on the C# side.
export function getInstrumentNames() {
    return PALETTE.map(slot => slot.instrument);
}

// DEV-ONLY: restricts which PALETTE instruments _slotIndexForRoute may select. `names` is
// an array of PALETTE[].instrument strings (e.g. "pizzicato_strings", "cello"); an empty
// array or omitted/null argument RESETS to PROD_INSTRUMENTS (the ship default), it does
// NOT mean "unfiltered" — prod builds never call this at all, and dev builds that want
// "everything" must pass getInstrumentNames() explicitly (the SettingsBlade dev panel does
// this on init so contrabass/viola/cello are available to toggle back on in dev, while
// staying unreachable in prod, which never calls this function). Does NOT dispose already-
// built Samplers for now-disabled slots — a route reassigned away from a slot simply stops
// picking it; disposeInactiveRoutes still owns eviction. Not persisted across reload by
// design (see docs/SYNTH_REFACTOR_DESIGN_DOCUMENT.md — dev-session-only debug aid, gated
// #if DEBUG on the C# side).
export function setEnabledInstruments(names) {
    const slots = _slotIndicesForNames(Array.isArray(names) ? names : []);
    // Nothing matched (empty/omitted names, or names that don't exist) — reset to prod default.
    _enabledSlots = slots.size > 0 ? slots : _slotIndicesForNames(PROD_INSTRUMENTS);
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
    stopAudioCapabilityHealth();
    _audioCapabilityHealthReceiver = null;
    _unlockGeneration++;
    _unlockPromise = null;
    _lastGestureAttemptPromise = null;
    _lastAudioFabGestureAttemptPromise = null;
    _lastAudioFabGestureWasRunning = false;
    for (const entry of _instrumentCache.values()) {
        if (entry && entry.sampler) {
            try { entry.sampler.dispose(); } catch (_) { /* ignore */ }
        }
    }
    _instrumentCache.clear();
    if (_percussion) {
        try {
            _percussion.loop.dispose();
            _percussion.kick.dispose();
            _percussion.rim.dispose();
            _percussion.volume.dispose();
        } catch (_) { /* ignore */ }
        _percussion = null;
    }
    _masterBus = null;
    _masterMuted = false;
    _activeNoteEndsAt.length = 0;
    _unlocked = false;
    _tone = null;
}

window.TransitSynth = { unlock, isUnlocked, preload, triggerNote, dispose, durationSecondsFor, disposeInactiveRoutes, getInstrumentNames, setEnabledInstruments, setAudioEnabled, setMasterMuted, verifyAudioSilenced, setBackfillTexture, setAudioCapabilityHealthReceiver, beginAudioCapabilityHealth, stopAudioCapabilityHealth };
