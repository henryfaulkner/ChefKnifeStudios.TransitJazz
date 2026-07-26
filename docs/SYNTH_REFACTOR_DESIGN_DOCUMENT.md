# Transit Synth Refactor — Design Document

> **Purpose of this document.** This is a design doc for replacing
> `transit-synth.js`'s sample-based (`Tone.Sampler` + FluidR3 soundfont) audio engine with
> a pure-synthesis engine (`Tone.Synth`/`FMSynth`/`MonoSynth`/etc., zero decoded audio) for
> the existing **melodic route voices**. It is a **design + decision record**, not an
> implementation. No code has been changed yet. Read this before touching
> `transit-synth.js`; it explains *why* each choice was made so a future agent doesn't
> re-litigate settled trade-offs.
>
> **Scope note:** percussion/drums are explicitly **out of scope** for this doc — see
> `docs/DRUMKIT_AND_DENSITY_DESIGN_DOCUMENT.md`, split out because it raises an open
> question (what data should drive a drum hit, given there's no sequencer) that would
> otherwise block this refactor, which is unrelated and ready to implement.

**Status:** IMPLEMENTED — `transit-synth.js` is the pure-synthesis engine; the prior
Sampler build is preserved unwired at `transit-synth.legacy.js`.
**Component:** `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/transit-synth.js`
**Related:** [[project_009_transit_soundscape]] (original plan — already specified pure
synthesis before it evolved into the Sampler build), `docs/BROWSER_MEMORY_INVESTIGATION_DESIGN_DOCUMENT.md`
§3.5 (the RAM postmortem this refactor is downstream of), `docs/DRUMKIT_AND_DENSITY_DESIGN_DOCUMENT.md`
(deferred follow-on for percussion).

---

## 0. Why this refactor exists

Two independent problems with the current file, both root-caused to the same design
choice (`Tone.Sampler` over real instrument recordings):

1. **The user doesn't like the sound.** The current palette (contrabass/viola/cello) is
   sustained bowed strings — the hardest instrument family for *any* method to fake, and
   the current implementation compounds it by pitch-shifting from only 2 anchor samples
   (C2/C3) across a 10-note scale, which smears timbre the further a played note is from
   its nearest anchor.
2. **It doesn't scale.** Every additional instrument or denser note map is more decoded
   PCM held resident for the whole session (`Tone.Sampler` fetches + decodes each note to
   a `AudioBuffer`, ~0.5–1 MB per note). This is exactly the mechanism that caused the
   ~1.2 GB→sub-600 MB regression documented in the file's own header comments and in
   `docs/BROWSER_MEMORY_INVESTIGATION_DESIGN_DOCUMENT.md` §3.5. The 2-anchor trick was a
   workaround for that cost, not a quality choice — it's the thing making the sound worse.

**Decision:** move to pure synthesis (oscillator/physical-modeling/FM sources — zero
decoded audio, marginal cost of a new instrument ≈ a config object) and rebuild the
instrument palette around timbres synthesis actually does well, rather than trying to
synth-fake a viola.

---

## 1. Can synthesis sound "real"? (governs instrument selection)

Established during design discussion, carried forward as a hard constraint on palette
choice:

| Family | Synthesis realism | Why |
|---|---|---|
| Plucked/struck strings | **High** — `Tone.PluckSynth` (Karplus-Strong) is a physical model of the actual string mechanism, not a waveform guess | attack + decay both come from the model |
| Percussion (kick/snare/hat/bell) | **High** — `MembraneSynth`/`NoiseSynth`/`MetalSynth` model the physics of a struck membrane/noise burst/metal resonance | single fixed-pitch hit, no scale to smear across |
| Bass | **Good** — subtractive synths (`MonoSynth`/`FMSynth`) get close; bass timbre is mostly harmonic content + envelope | |
| Sustained bowed strings (violin/viola/cello) | **Poor** — the current palette | realism comes from bow noise, pressure micro-variation, formant shift — continuous imperfection no oscillator models |
| Winds/brass | **Poor** | breath noise, embouchure, key-click transients |

**Conclusion:** drop sustained bowed strings from the palette. Rebuild around plucked
strings, bass, and percussion — where pure synthesis is not a compromise, it's the right
tool.

---

## 2. Reference material consulted

Two sibling Tone.js projects were reviewed for concrete, battle-tested config values
(neither is TransitJazz's codebase; both are sample-based projects, used here only for
*parameter* reference — envelope shapes, filter cutoffs, effects-chain patterns — not
for their sample-vs-synth architecture choice, which TransitJazz is deliberately moving
away from):

- `lofi-station/docs/synth-config-notes.md` — effects-chain pattern
  (`instrument.chain(Volume, Reverb, Destination)`, per-voice wet mix varying by
  foreground/background role) and tempo-matched release/reverb timing. Its "how to fake
  sample warmth with an oscillator" section (slow attack, chorus/detune, "avoid sounding
  too clean") is the **opposite** of what TransitJazz wants — that project is emulating
  samples; TransitJazz is deliberately leaving samples. Not carried forward.
- `lofi-engine/docs/SYNTH_CONFIG.md` — per-voice filter/volume/stereo-width chain
  pattern (each instrument gets its own chain rather than a shared bus). Its drum-voice
  recipe (one-shot sample-per-hit at a single fixed pitch) is **not** used here — see
  `docs/DRUMKIT_AND_DENSITY_DESIGN_DOCUMENT.md`, which carries that reference forward
  for the deferred percussion work instead.

---

## 3. New instrument palette

Replaces `PALETTE` (currently 3 sampled bowed-string voices cycling by `djb2(routeId)`).

| Voice | Tone.js type | Role | Notes |
|---|---|---|---|
| Pluck (upright-ish) | `Tone.PluckSynth` | route voice | Karplus-Strong; naturally percussive-plucked, good stand-in for the old "contrabass" slot |
| Sub bass | `Tone.MonoSynth` (sine-ish osc, lowpass filter, short-ish envelope) | route voice | subtractive; close to real bass without sample cost |
| FM bell/mallet | `Tone.FMSynth` | route voice | brighter, mallet/kalimba-like character; fills the "third voice" role the old viola/cello occupied |
| (room for more) | `Tone.AMSynth`, additional `PluckSynth`/`MonoSynth` variants | route voice | palette is now cheap to extend — each entry is a config object, not a sample fetch |

Route→voice assignment keeps the existing deterministic `djb2(routeId) % PALETTE.length`
scheme (`transit-synth.js:87,107,111`) — that mechanism is orthogonal to sample-vs-synth
and doesn't need to change.

Pitch mapping keeps the existing `noteForPosition(scale, triggerIndex, totalTriggers)`
logic and C-minor pentatonic `SCALE` (`transit-synth.js:57,138-143`) unchanged — synthesis
removes the *sample-count* constraint entirely (no anchor notes, no pitch-shift
artifacts), so the full existing scale (or a richer one, if desired later) can be voiced
directly with zero additional cost. This is a strict quality win from the migration with
no design decision required here.

---

## 4. Effects chain (new — the current file has none)

Per the lo-fi reference docs, the current file chains straight to
`sampler.toDestination()` with no processing. Every voice gets its own chain (per-voice,
not a shared bus — matches both reference docs' pattern):

```js
// Per voice, at construction time:
const vol = new Tone.Volume(voiceGainDb);            // per-voice level trim
const filter = new Tone.Filter(cutoffHz, "lowpass");  // tame harsh synth harmonics
const reverb = new Tone.Reverb({ decay: 0.8, preDelay: 0.01, wet: 0.15 });
synth.chain(filter, reverb, vol, Tone.Destination);
```

- **Foreground (route pluck/bass) voices:** near-dry, modest lowpass (~3–4 kHz) just to
  soften raw-oscillator harshness — keep them present, since they're the "melodic" signal.
- **Reverb ships in v1** (settled — not deferred): short decay (~0.8s), low wet (~0.15),
  near-immediate `preDelay` (0.01s), mirroring the lofi-station recipe
  (`Tone.Reverb(decay, preDelay, wet)`) and its "low wet on foreground/lead-like
  instruments" rule. Same value across all 3 voices for the first cut — per-voice wet
  tuning (e.g. bass drier than pluck/bell) is a nice-to-have refinement, not required
  before shipping.
- `Tone.Reverb` has its own internal impulse-response generation cost on first use
  (async `generate()`/ready promise) — build/await it once per voice at construction
  time (same lazy-per-route timing as the existing `Tone.Sampler` build in
  `instrumentFor()`), not per note trigger.

---

## 5. What does NOT change

- `window.TransitSynth` public API surface (`unlock`, `isUnlocked`, `preload`,
  `triggerNote`, `dispose`, `durationSecondsFor`, `disposeInactiveRoutes`) stays
  identical, so `ITransitSynthJsInterop`/`TransitSynthJsInterop.cs` need **zero** changes.
- Lazy-build-per-route + `disposeInactiveRoutes` eviction lifecycle
  (`transit-synth.js:107-134`, `208-219`) is orthogonal to sample-vs-synth and is kept
  as-is. (Its memory stakes are also much lower post-refactor — evicting a synth instance
  frees a Web Audio node graph, not decoded PCM, so this becomes pure hygiene rather than
  a RAM-critical path.)
- `djb2`-based deterministic route→voice and vehicle→duration assignment
  (`transit-synth.js:74-88`) — unchanged mechanism, just fed by the new palette/instrument
  set.
- Unlock-gesture handling (`attachUnlockGesture`/`unlock`, mobile autoplay-trust-window
  logic) — unrelated to the audio source, unchanged.

---

## 6. File plan

Per user instruction: **do not delete or overwrite the current file in place.**

1. Copy current `transit-synth.js` → `transit-synth.legacy.js` (kept in git, unwired,
   deprecated; preserves the working Sampler implementation and its RAM-postmortem
   comments for reference/rollback).
2. New `transit-synth.js` becomes the active file
   (`TransitSynthJsInterop.cs:20` import path is unchanged since the filename is
   unchanged — only `transit-synth.legacy.js` is new/additional).
3. Implementation of the new file's internals follows this design doc.

---

## 7. Decisions confirmed (resolved during design review)

1. **Palette size:** stays at **3 melodic voices** (Pluck / sub bass / FM bell) for this
   first cut — a straight swap for the existing 3-voice PALETTE, not an expansion.
   Rationale: validate the *type* of sound before committing to a larger, harder-to-retune
   palette.
2. **Reverb:** **ships in v1**, not deferred — see §4 for the settled parameters
   (`decay: 0.8, preDelay: 0.01, wet: 0.15`, per-voice chain, uniform wet across all 3
   voices for now).

No open decisions remain for the melodic-voice refactor. Ready for implementation.

---

## 8. Follow-on work (explicitly out of scope here)

Percussion/drumkit and any "data density" driven audio — see
`docs/DRUMKIT_AND_DENSITY_DESIGN_DOCUMENT.md`. That doc is parked pending a decision on
what real signal should drive a drum hit; it does not block this refactor.
