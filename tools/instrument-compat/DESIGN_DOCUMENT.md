# Instrument Compatibility Page — Design Document

**Status:** Ready to build
**Audience:** An engineer/agent building this tool from scratch, with NO other context loaded.
**Deliverable:** A single self-contained static HTML file (`tools/instrument-compat/index.html`) that runs by opening it in a browser (or serving it with any static file server). No build step, no framework, no backend, no dependency on the TransitJazz .NET app.

---

## 1. Purpose & Motivation

TransitJazz (a.k.a. MartaJazz) turns live transit movement into an ambient generative soundscape. When a transit vehicle crosses a checkpoint along its route, the app plays one note. **Route → instrument, position-along-route → pitch.** The instruments are sampled voices (MP3 anchor notes) played through a fixed Tone.js effects chain (lowpass filter → stereo widener → reverb → shared master bus with a compressor and a continuous pink-noise bed).

Onboarding a **new instrument** into the app today means editing `transit-synth.js` (adding a `PALETTE` entry), rebuilding the Blazor WASM client, and spinning up the full stack (map + SignalR worker + backend) to hear it in a realistic multi-voice context. That is slow and heavyweight for what is really an **audio taste/QA decision**: *does this sampled voice sound good in the TransitJazz soundscape, at low / medium / high transit density?*

This tool removes all of that. It is a **client-side-only audition bench** that:

1. Reproduces the **exact** app synthesis chain and note vocabulary, so what you hear here is what the app produces.
2. Lets you **add instruments at runtime** by pasting hosted sample URLs — **no code changes** required to test a new instrument.
3. Simulates **low / medium / high transit density** so you hear the instrument in a realistic, multi-voice soundscape without running the map/worker/backend.

The output of a session with this tool is a **judgment**: "this instrument is compatible / sounds good" (and, informally, good attack/release/duration values to later hand-enter into the app's `PALETTE`). This tool does **not** auto-generate a `PALETTE` entry and does **not** modify the app.

---

## 2. Scope

### In scope
- Single self-contained `index.html` (inline CSS + inline JS; only external dependency is the Tone.js ES module from a CDN, exactly as the app loads it).
- **Add instrument via explicit per-note URLs**: for each instrument you paste one full URL per anchor note (typically two: a low anchor and a high anchor).
- **"Loads & plays" check**: confirm the anchor MP3s fetch, decode, and sound through the real app synthesis chain.
- **Density audition**: Low / Medium / High density simulation that fires synthetic checkpoint "crossings" across the instruments you've added, so you hear them together in a realistic soundscape.
- Per-instrument controls to audition it solo (play a single note now).
- Persist the instrument list to `localStorage` so a page reload doesn't lose your setup.

### Out of scope (explicitly do NOT build)
- **Base-URL / note-list convenience mode.** The user chose explicit per-note URLs. Do not build a "give a base folder and auto-derive `{base}/{Note}.mp3`" mode.
- **Scale-sweep / pitch-shift-range audition.** Do not add a "play every scale note in sequence" button as a dedicated compatibility check. (The density sim and solo-play will naturally exercise multiple scale degrees; that is sufficient.)
- **PALETTE snippet export.** Do not emit a copy-paste `PALETTE` entry. (Onboarding into the app stays a manual step the user does later by hand.)
- Any map, vehicle animation, trail rendering, checkpoint pulse visuals, SignalR, or worker simulation. This tool is **audio only**.
- Any C#/Blazor/.NET code. This is a standalone HTML file.

---

## 3. Background: How the app's audio actually works

This section is the ground truth the tool must faithfully reproduce. It is distilled from the app's `transit-synth.js` and `crossing-dispatcher.js`. **A builder should not need to open those files** — everything needed is here. (Paths given only for provenance: `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/transit-synth.js` and `.../crossing-dispatcher.js`.)

### 3.1 Tone.js version & load

The app imports Tone.js as an ES module from esm.sh, pinned to major version 15:

```js
const mod = await import('https://esm.sh/tone@15');
```

Use the **same import and the same major version** so the DSP behaves identically. All node names below (`Tone.Sampler`, `Tone.Filter`, `Tone.StereoWidener`, `Tone.Reverb`, `Tone.Volume`, `Tone.Compressor`, `Tone.Noise`, `Tone.Destination`, `Tone.now()`, `Tone.start()`, `Tone.getContext()`) are Tone v15 APIs.

### 3.2 The audio unlock gesture (REQUIRED — browsers block autoplay)

Browsers will not let audio start without a user gesture. `Tone.start()` **must** be called synchronously inside a real click/tap handler, or the AudioContext stays suspended and nothing is ever audible. The app learned this the hard way on iOS Safari: if you `await` anything *before* wiring the click handler, the browser's "trusted gesture" window closes and audio is permanently silent for the session.

**Requirement for the tool:** There must be an explicit **"Enable Audio"** button. Its click handler must call `await Tone.start()` (and may then kick off warming). Do not attempt to start audio on page load, on `DOMContentLoaded`, or from any non-gesture path. Until audio is unlocked, disable the density/play controls (or have them no-op with a visible "enable audio first" hint).

### 3.3 The note vocabulary (pitch)

The app plays notes from a **C-minor pentatonic** scale spread across low octaves (chosen for warm, consonant harmony when many voices overlap). This is the exact array — reproduce it verbatim:

```js
const SCALE = ['C2', 'Eb2', 'F2', 'G2', 'Bb2', 'C3', 'Eb3', 'F3', 'G3', 'Bb3'];
```

**Position → pitch mapping.** A vehicle's progress along its route (an integer `triggerIndex` out of `totalTriggers`) maps linearly onto this scale: `triggerIndex 0` → lowest note (`C2`), `triggerIndex totalTriggers-1` → highest note (`Bb3`). Exact function to reproduce:

```js
function noteForPosition(scale, triggerIndex, totalTriggers) {
    const clampedTotal = Math.max(totalTriggers, 1);
    const clampedIndex = Math.max(0, Math.min(triggerIndex, clampedTotal - 1));
    const scaleIndex = Math.round((clampedIndex / (clampedTotal - 1 || 1)) * (scale.length - 1));
    return scale[scaleIndex];
}
```

For the density simulation, the tool synthesizes `triggerIndex` / `totalTriggers` values itself (see §5.4).

### 3.4 Anchor samples & pitch-shifting (`Tone.Sampler`)

Each instrument is a `Tone.Sampler` built from a **sparse** set of anchor notes — not one MP3 per playable note. The Sampler pitch-shifts (resamples) between the anchors you give it to reach any requested note. The app ships **two anchors per instrument** — one low, one high — so the worst-case shift to any `SCALE` note is only ~3 semitones (keeps artifacts low):

```js
const ANCHORS = { C2: 'C2', C3: 'C3' };  // note name -> which MP3 file
```

The app builds the sample URL map from a base + instrument name + note:

```js
const SOUNDFONT_BASE = 'https://gleitz.github.io/midi-js-soundfonts/FluidR3_GM';
// url for a note = `${SOUNDFONT_BASE}/${instrument}-mp3/${note}.mp3`
// e.g. https://gleitz.github.io/midi-js-soundfonts/FluidR3_GM/cello-mp3/C2.mp3
```

**In THIS tool, the user supplies those final URLs directly** (explicit per-note mode). So a `Tone.Sampler` is constructed from a `{ noteName: fullUrl }` map, e.g.:

```js
new Tone.Sampler(
  { "C2": "https://.../cello-mp3/C2.mp3", "C3": "https://.../cello-mp3/C3.mp3" },
  { attack, release, onload, onerror }
);
```

**Anchor notes matter.** The KEY of each URL entry (e.g. `"C2"`) tells the Sampler the true pitch of that recording, which is what makes pitch-shifting correct. The tool's add-instrument form must therefore capture, per URL, **which note that MP3 actually is**. Default the anchor set to `C2` and `C3` (matching the app), but let the user change note names and add/remove anchor rows (some soundfonts are hosted at other pitches).

### 3.5 The per-voice effects chain (fixed — reproduce exactly)

Each instrument's Sampler feeds this chain, in this order, into the shared master bus:

```
Sampler → Filter(lowpass, 1800 Hz) → StereoWidener(0.4) → Volume(voice dB) → Reverb(decay 1.4, preDelay 0.02, wet 0.35) → masterBus.input
```

Constants (verbatim from the app):

```js
const FILTER_CUTOFF_HZ = 1800;        // per-voice lowpass, dark/soft character
const STEREO_WIDTH      = 0.4;        // Tone.StereoWidener amount
const REVERB_DECAY      = 1.4;
const REVERB_PRE_DELAY  = 0.02;
const REVERB_WET        = 0.35;
```

`Volume(voice dB)` is an optional per-instrument trim (the app uses `-8` dB on piano to sit it under the other voices; default `0` = the sample's native recorded loudness). Expose this as a per-instrument field, defaulting to `0`.

**Reverb is async to build.** `Tone.Reverb` generates a convolution impulse response; you must `await reverb.generate()` (or `.then(...)`) before the voice is ready. Build the reverb **once per instrument**, not per note. Concretely, the app builds the whole chain inside the Sampler's `onload` callback:

```js
const filter  = new T.Filter(FILTER_CUTOFF_HZ, 'lowpass');
const widener = new T.StereoWidener(STEREO_WIDTH);
const reverb  = new T.Reverb({ decay: REVERB_DECAY, preDelay: REVERB_PRE_DELAY, wet: REVERB_WET });
const volume  = new T.Volume(volumeDb ?? 0);
const sampler = new T.Sampler(urls, {
  attack, release,
  onload: () => {
    reverb.generate().then(() => {
      sampler.chain(filter, widener, volume, reverb, masterBus.input);
      // NOW this instrument is ready to play
    });
  },
  onerror: (err) => { /* mark this instrument as failed-to-load, surface in UI */ },
});
```

### 3.6 The shared master bus (built ONCE — reproduce exactly)

All instruments feed one shared master bus (not their own path to `Destination`). The master bus is: a glue **compressor** → a gentle **lowpass filter** → `Destination`. Plus a **continuous quiet pink-noise bed** (a tape-hiss / vinyl-air texture layer) that runs constantly under everything, mixed into the same compressor. Constants and construction (verbatim):

```js
const MASTER_COMPRESSOR = { threshold: -18, ratio: 3, attack: 0.02, release: 0.25 };
const MASTER_FILTER_HZ  = 4000;
const NOISE_VOLUME_DB   = -38;   // pink-noise bed level (quiet)
const NOISE_FILTER_HZ   = 2000;  // lowshelf on the noise

function buildMasterBus(T, audioEnabled) {
  const compressor = new T.Compressor(MASTER_COMPRESSOR);
  const filter     = new T.Filter(MASTER_FILTER_HZ, 'lowpass');
  compressor.chain(filter, T.Destination);

  const noise       = new T.Noise('pink');
  const noiseFilter = new T.Filter(NOISE_FILTER_HZ, 'lowshelf');
  const noiseVolume = new T.Volume(NOISE_VOLUME_DB);
  noise.chain(noiseFilter, noiseVolume, compressor);
  if (audioEnabled) noise.start();   // only start if not muted

  return { input: compressor, noise };
}
```

The **noise bed is important to the character** of the soundscape — the app deliberately avoids dead silence between notes. Include it. It must only `start()` when audio is enabled/unmuted, and `stop()` when muted (see §5.5 mute behavior). Build the master bus lazily on first use and cache it (build it exactly once per session).

### 3.7 Note durations & per-note humanization

Each instrument has a small set of **duration tokens** (musical note lengths). Which token a given note uses is picked deterministically in the app (hashing the vehicle id) so the note and its visual trail agree — but **this tool has no trail**, so it may pick a duration token at random from the instrument's set; that is acceptable and sounds equivalent. The app's default token→seconds table (at Tone's default 120 BPM transport) is:

```js
const DURATION_SECONDS = {
  '16n': 0.125, '16n.': 0.1875,
  '8n': 0.25,  '8n.': 0.375,
  '4n': 0.5,   '4n.': 0.75,
  '2n': 1.0,
};
```

Duration sets the app ships per voice (use these as **defaults** for the corresponding instrument type; let the user edit the set per instrument):

| Instrument (app example) | attack | release | durations       |
|--------------------------|--------|---------|-----------------|
| pizzicato_strings (pluck)| 0      | 1.2     | `4n, 4n., 2n`   |
| acoustic_bass (sub bass) | 0      | 0.8     | `8n, 8n., 4n`   |
| acoustic_grand_piano     | 0      | 1.0     | `8n, 8n., 4n` (volume −8 dB) |
| contrabass (bowed)       | 0.005  | 0.3     | `8n, 8n., 4n`   |
| viola (bowed)            | 0.005  | 0.2     | `16n, 16n., 8n` |
| cello (bowed)            | 0.005  | 0.25    | `16n, 16n., 8n` |

Notes on why (informs sensible form defaults): plucked/struck voices use `attack: 0` (instant onset). Bowed voices get a tiny `attack` (~0.005) and a **short** `release` + short durations to read as struck/plucked rather than sustained. New-instrument default suggestion: `attack: 0`, `release: 1.0`, durations `['8n', '8n.', '4n']`, `volumeDb: 0` — the user tunes from there by ear.

**Per-note humanization** (reproduce — it's what keeps the soundscape from sounding robotic):

```js
const HUMANIZE_TIME_JITTER_SEC = 0.02;   // ±20 ms start-time jitter
const HUMANIZE_VELOCITY_MIN    = 0.75;
const HUMANIZE_VELOCITY_MAX    = 1.0;
// per note:
const velocity  = HUMANIZE_VELOCITY_MIN + Math.random() * (HUMANIZE_VELOCITY_MAX - HUMANIZE_VELOCITY_MIN);
const startTime = Tone.now() + (Math.random() * 2 - 1) * HUMANIZE_TIME_JITTER_SEC;
sampler.triggerAttackRelease(note, duration, startTime, velocity);
```

### 3.8 What a single "note trigger" is, end to end

Putting §3.3–3.7 together, playing one crossing =

1. Pick the **instrument** (in the app: hash route id → a palette slot; in this tool: see the density model §5.4 for how a synthetic "route" maps to one of the added instruments).
2. `note = noteForPosition(scale, triggerIndex, totalTriggers)` — pitch from position.
3. `duration = ` one token from that instrument's duration set (random token is fine here).
4. `velocity`, `startTime` = humanized per §3.7.
5. `sampler.triggerAttackRelease(note, duration, startTime, velocity)`.

### 3.9 Density model — how the app schedules many crossings

In the app, the server sends batches of "crossings"; a JS dispatcher schedules each one on a `setTimeout` so effects fire spread out over time rather than all at once. The exact motion-timing math is map-specific and **not needed here**. What matters for reproducing the *feel* of density:

- Crossings arrive in **batches roughly every ~10 seconds** (the "cycle").
- Within a batch, crossings are **jittered randomly across a window** so they don't all hit at once; the window scales with how many crossings there are (more crossings → wider spread), and there's a **density cap** so a huge backlog trickles out rather than blasting. The app's spacing intuition: on the order of **~one crossing every 150 ms** of spread, capped so bursts stay musical (roughly a few fires per second at most in the fallback path).

For this tool we don't replay real batches; we **generate** a steady stream of synthetic crossings at a chosen rate (see §5.3). The design goal is that **High** density should feel like a busy multi-voice texture (many overlapping notes), **Low** should feel sparse (occasional single notes), **Medium** in between — matching the subjective experience of the live app at quiet vs. rush-hour transit levels.

---

## 4. File layout & tech constraints

```
tools/instrument-compat/
├── DESIGN_DOCUMENT.md   ← this file
└── index.html           ← the entire tool (inline CSS + inline JS module)
```

Constraints:

- **Single file.** All HTML, CSS, and JS in `index.html`. The only network fetches at runtime are (a) the Tone.js module from esm.sh and (b) the user-supplied instrument MP3 URLs. No other assets.
- **ES module.** Put the app logic in a `<script type="module">` so `import(...)` and top-level `await` work. Tone.js is loaded via dynamic `import('https://esm.sh/tone@15')` exactly as the app does (do not use a `<script src>` global build — match the app's module import for identical behavior).
- **No framework.** Plain DOM APIs. No React/Vue/Blazor/bundler. "Simple is good."
- **Runs by opening the file** (or via any static server). Note: some browsers restrict `import()` from `file://`. If that bites, the fallback is to serve the folder with any static server (e.g. `python -m http.server` from `tools/instrument-compat/`). Document this in a short comment at the top of the file and in §8.
- **CORS:** the instrument MP3 URLs must be fetchable cross-origin (the gleitz FluidR3 GitHub-Pages host sends permissive CORS, which is why the app uses it). If a user's URL fails CORS, surface the load error clearly (see §5.6). This is a property of the sample host, not something the tool can fix.

---

## 5. Functional design

### 5.1 Overall layout (single screen)

A simple vertical page, mobile-friendly-ish but desktop-first. Suggested regions top to bottom:

1. **Header** — title ("TransitJazz Instrument Compatibility"), one-line explainer, and the **Enable Audio** button (see §3.2). Show current audio state (locked / enabled / muted).
2. **Transport / density controls** — a Density selector (Off / Low / Medium / High) and a global **Mute** toggle. When density is not Off and audio is enabled, the synthetic crossing stream runs.
3. **Instruments list** — one card per added instrument (see §5.6), plus an **"Add instrument"** form/button.
4. **Status / log area** (optional but recommended) — a small rolling log of load successes/failures and, optionally, a live "notes/sec" or "active voices" readout so the user can correlate what they hear with what's firing.

Keep styling minimal and legible (system font, adequate spacing, clear disabled states). Dark background suits an audio tool but is not required.

### 5.2 The synthesis engine (shared, reused verbatim)

Implement a small internal module inside the page that mirrors the app's engine. Recommended shape:

- `getTone()` — memoized `import('https://esm.sh/tone@15')`.
- `getMasterBus()` — lazily builds+caches the master bus per §3.6, honoring the current mute state for the noise bed.
- `buildInstrument(spec)` — given `{ id, name, urls: {note:url}, attack, release, volumeDb, durations }`, returns a promise that resolves once the Sampler is loaded and the reverb IR is generated and the chain is wired to the master bus (per §3.5). Rejects / flags on `onerror`. Cache the built voice on the spec so it's built once.
- `triggerNote(instrumentVoice, triggerIndex, totalTriggers)` — does §3.8 steps 2–5 against a **specific** instrument's Sampler (this tool triggers a chosen instrument directly rather than hashing a route id, because instruments are added dynamically and auditioned individually and together).
- Mute plumbing per §5.5.

Keep the constants (§3.3, §3.5, §3.6, §3.7) as named consts at the top so they read as the same recipe as the app.

### 5.3 Density simulation

A single interval-driven scheduler. When density ∈ {Low, Medium, High} and audio is enabled and not muted:

- Maintain a target **crossing rate** (crossings per second). Suggested starting values (tune by ear against the real app):
  - **Low** ≈ 0.5–1 crossings/sec (sparse; frequent gaps).
  - **Medium** ≈ 2–3 crossings/sec.
  - **High** ≈ 5–8 crossings/sec (busy, overlapping texture).
- Implement as either (a) a `setInterval` that, each tick, decides how many crossings to fire and schedules each on a short random `setTimeout` within the tick window (mirrors the app's jitter-across-a-window behavior, §3.9), or (b) a self-rescheduling timer with randomized inter-arrival gaps (Poisson-ish) for a more organic feel. Either is fine; (b) tends to sound more natural. Randomize timing so it never sounds metronomic.
- Each synthetic crossing:
  - **Picks an instrument** among the currently-added, successfully-loaded instruments (see §5.4).
  - **Picks a `triggerIndex`/`totalTriggers`** to get a pitch from the scale (see §5.4).
  - Calls `triggerNote(...)`.
- If **no instruments are loaded yet**, density does nothing audible (the noise bed still runs if enabled) — optionally surface a hint "add an instrument to hear the density sim."
- Changing density takes effect immediately. Setting density to **Off** stops scheduling new crossings (already-scheduled `setTimeout`s within the last tick may still fire — that's fine, it's <1s of tail).

### 5.4 Mapping synthetic crossings → instrument & pitch

The app derives instrument from route and pitch from along-route position. This tool has no routes, so synthesize plausibly:

- **Instrument choice:** pick **uniformly at random** among loaded instruments each crossing. (Rationale: the user wants to hear each candidate instrument *in the mix*; uniform random gives every added voice fair airtime. Optionally add a per-instrument "solo/mute in mix" toggle later, but not required for v1.) When only one instrument is loaded, the density sim auditions it solo at the chosen rate.
- **Pitch choice:** assign each synthetic crossing a random `totalTriggers` in a realistic range (e.g. 8–24, representing "number of checkpoints along a route") and a random `triggerIndex` in `[0, totalTriggers)`. Feed both into `noteForPosition`. This exercises the full scale over time exactly as varied route positions do in the app. (Simpler acceptable alternative: pick a random index directly into `SCALE`. Prefer the `triggerIndex/totalTriggers` route because it's the app's real signature and keeps the code honest to §3.3.)

### 5.5 Mute behavior (match the app's semantics)

The app has a single audio mute setting that (a) gates note triggers **at fire time** and (b) starts/stops the continuous noise bed. Reproduce:

- A live `audioEnabled` boolean (default `true` once unlocked).
- **Mute toggle** flips it. On mute: `stop()` the noise bed. On unmute: resume the AudioContext if it slipped to suspended (`Tone.getContext().rawContext.resume()`), then `start()` the noise bed if not already started.
- `triggerNote` must **re-check** `audioEnabled` at fire time and no-op if muted (because crossings are scheduled slightly ahead via `setTimeout`, a mute mid-flight must silence already-queued notes). This mirrors the app's fire-time mute gate.
- The **Enable Audio** unlock (§3.2) and **Mute** are distinct: unlock is the one-time gesture that starts the AudioContext; mute is the ongoing on/off. Before unlock, everything is silent regardless of mute.

### 5.6 Instrument card & add-instrument form

**Add-instrument form** (explicit per-note URL mode) captures:

- `name` — free text label (for display only; e.g. "cello", "my-custom-marimba").
- **Anchor rows** — a repeatable row of `{ noteName, url }`. Seed with two rows defaulting to `C2` and `C3` note names with empty URLs. Allow add/remove rows (min 1). The `noteName` is the true pitch of that MP3 and becomes the Sampler URL-map key (§3.4); the `url` is the full hosted MP3 URL.
- `attack` (number, default `0`), `release` (number, default `1.0`), `volumeDb` (number, default `0`).
- `durations` — the token set, as a small multi-select or comma field over the allowed tokens `16n, 16n., 8n, 8n., 4n, 4n., 2n` (default `8n, 8n., 4n`).
- A **Add / Load** button that constructs the instrument (`buildInstrument`) and, on success, adds a card; on failure, shows the error inline.

**Instrument card** (per added instrument) shows:

- Name and its anchor notes/URLs (compact).
- **Load state**: loading… / ready / **failed** (with the error message — e.g. 404, CORS, decode failure).
- **"Play note" (solo)** button — triggers a single note *now* through the full chain (respects mute/unlock). Optionally a small pitch control (or just play a mid-scale note; a random scale degree each press is fine and mirrors real variety).
- **Edit** (attack / release / volumeDb / durations) applied to the built voice where possible, or rebuild on change (rebuilding is simplest and always correct — dispose the old Sampler first).
- **Remove** — disposes the instrument's Tone nodes (`sampler.dispose()`, etc.) and removes the card.

**Loads & plays compatibility verdict:** an instrument that reaches **ready** and produces audible sound on "Play note" through the chain **is** the "loads & plays" pass. Make that state obvious (e.g. a green "Ready ✓" chip). A **failed** state is the fail verdict; show why.

### 5.7 Persistence

Persist the instrument **specs** (name, anchor note/url rows, attack, release, volumeDb, durations) — NOT the built Tone nodes — to `localStorage` under a single key (e.g. `instrument-compat:instruments`). On load, restore the specs and re-`buildInstrument` each (which re-fetches the MP3s). Persist the last density and mute selection too (nice-to-have). Provide a **Clear all** button. Do not persist across origins/domains obviously — it's just localStorage.

---

## 6. Behavioral requirements (acceptance checklist)

A correct build satisfies all of these:

1. **Opens and runs** as a single static file (served if `file://` import is blocked). No console errors on load beyond, at most, benign ones.
2. **Audio is silent until the Enable Audio gesture**, then the pink-noise bed becomes faintly audible (when unmuted). This proves the master bus + unlock work.
3. **Adding an instrument** with valid per-note MP3 URLs reaches a **Ready** state, and its **Play note** button produces an audible, correctly-pitched note through the lowpass→widener→reverb→master chain (not a dry raw sample).
4. **A bad URL** (404 / CORS / non-audio) surfaces a clear **failed** state on that instrument's card and does not crash the page or break other instruments.
5. **Density Low/Medium/High** produces a clearly increasing rate of overlapping notes drawn from the added instruments; **Off** stops the stream. The three levels are subjectively distinguishable (sparse → busy).
6. **Mute** silences all note triggers **and** the noise bed immediately, even for crossings already scheduled a moment ago; **unmute** restores both. **Enable Audio** and **Mute** are independent controls.
7. **The notes sound like the app** — same scale (C-minor pentatonic, low octaves), same reverb/filter/width/noise character, same soft dynamics. Someone familiar with the live app should recognize the timbre and space.
8. **Reload preserves** the instrument list (specs re-load and re-fetch), density, and mute.
9. **No app code is required to test a new instrument** — the entire flow is paste-URLs → hear it. (This is the core purpose; verify it end to end.)
10. **Removing an instrument** disposes its audio nodes and it no longer appears in the density mix.

---

## 7. Fidelity notes — do NOT drift from the app

These are the details most likely to be "improved" and thereby break compatibility. Keep them **exactly** as specified:

- **Tone.js major version 15** via `import('https://esm.sh/tone@15')`. A different version can change reverb/filter/compressor voicing.
- **SCALE array verbatim** (§3.3). Do not re-voice, transpose, or extend it.
- **The full per-voice chain in order** (§3.5) — omitting the StereoWidener or the per-voice Reverb, or changing the 1800 Hz filter, changes the character.
- **The shared master bus with compressor + pink-noise bed** (§3.6) — the noise bed is intentional ambient texture, not a bug; keep it and gate it on mute.
- **`await reverb.generate()` before wiring** — skipping it plays notes with no reverb tail (or errors), which is not what the app sounds like.
- **Sampler anchor keys are the true pitches** — the URL-map key must be the note the MP3 actually is, or every pitch is wrong.
- **Humanized velocity + ±20 ms jitter** (§3.7) — cheap, but it's part of the app's non-robotic feel.
- **Fire-time mute re-check** (§5.5) — not just gating at schedule time.

Where the tool intentionally differs from the app (and that's fine): no map/trail/pulse; duration token can be chosen at random (no trail to sync to); instrument selection is uniform-random over added instruments instead of route-hash; crossings are generated locally instead of received from a server.

---

## 8. How to run (put a short version of this in a comment at the top of index.html)

- **Easiest:** open `tools/instrument-compat/index.html` in a modern browser. Click **Enable Audio**. Add an instrument (paste anchor MP3 URLs — e.g. two of the FluidR3 GM soundfont notes), click **Play note** to confirm it loads & plays, then set **Density** to Low/Medium/High to hear it in the soundscape.
- **If `import()` is blocked on `file://`** (some browsers): serve the folder, e.g. from `tools/instrument-compat/` run `python -m http.server 8080` and open `http://localhost:8080/`.
- **Example known-good anchor URLs** (the app's own source, permissive CORS), for a first smoke test:
  - `https://gleitz.github.io/midi-js-soundfonts/FluidR3_GM/cello-mp3/C2.mp3` (as note `C2`)
  - `https://gleitz.github.io/midi-js-soundfonts/FluidR3_GM/cello-mp3/C3.mp3` (as note `C3`)

---

## 9. Suggested build order (for the implementing agent)

1. Skeleton `index.html`: header, Enable-Audio button, empty instruments area, density selector, mute toggle, log area. Inline CSS.
2. Engine module: `getTone`, constants, `getMasterBus` (with noise bed), unlock wiring on the button. Verify the noise bed becomes audible after unlock (acceptance #2).
3. `buildInstrument` + one hardcoded test instrument (cello anchors from §8) + a Play-note button. Verify audible, filtered, reverbed, correctly-pitched note (acceptance #3, #7).
4. Add-instrument form (per-note URL rows, attack/release/volume/durations) → dynamic instrument cards with load state + error handling (acceptance #3, #4, #6-load).
5. Density scheduler + synthetic crossing → instrument/pitch mapping (§5.3–5.4). Tune Low/Med/High rates by ear against the app (acceptance #5).
6. Mute semantics with fire-time re-check + noise-bed start/stop (acceptance #6).
7. `localStorage` persistence + Clear all (acceptance #8). Dispose-on-remove (acceptance #10).
8. Final pass against the whole acceptance checklist (§6) and fidelity notes (§7).

---

## 10. Glossary

- **Crossing** — the app's term for "a vehicle reached a checkpoint," i.e. one note event. This tool generates synthetic crossings.
- **triggerIndex / totalTriggers** — a vehicle's checkpoint position along its route and the route's total checkpoints; maps to a scale degree (pitch).
- **Anchor note** — one of the few sampled MP3s an instrument is built from; the Sampler pitch-shifts between anchors to reach other notes.
- **Palette / slot** — the app's fixed array of instruments and the index a route hashes to. This tool has no fixed palette; instruments are added at runtime.
- **Noise bed** — the continuous, quiet, filtered pink-noise texture layer under the whole mix.
- **Master bus** — the single shared compressor→filter→Destination path all instruments (and the noise bed) feed into.
