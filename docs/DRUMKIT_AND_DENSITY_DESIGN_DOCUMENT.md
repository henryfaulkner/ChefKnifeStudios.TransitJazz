> **SUPERSEDED by specs/049-backfill-texture-selector** — its event-driven-off-transit
> percussion direction is rejected; only this doc's settled synth-drum voice palette
> (§4) is carried over into the continuous-loop backfill percussion texture.

# Drumkit & Data-Density Percussion — Design Document

> **Purpose of this document.** This is a design doc for a **future** percussion voice
> for TransitJazz's soundscape, split out of `docs/SYNTH_REFACTOR_DESIGN_DOCUMENT.md`
> because it raises an open musical/data-modeling question (what should trigger a drum
> hit, given there is no sequencer) that is unresolved and shouldn't block the synth
> refactor, which is otherwise ready to implement. **No implementation is planned yet.**
> This doc exists so the question and its candidate answers aren't lost.

**Status:** DEFERRED — parked pending a decision on what data should drive percussion.
**Depends on:** `docs/SYNTH_REFACTOR_DESIGN_DOCUMENT.md` (the pure-synthesis engine this
drumkit would plug into) landing first.
**Component (future):** `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/transit-synth.js`

---

## 1. Why this is split out

The synth refactor (bowed strings → Pluck/MonoSynth/FMSynth palette) is a well-scoped,
mechanical substitution: same API, same triggering model, same data flowing in — only
the sound-generation method changes. It's ready to build.

The drumkit is not just "add three more Tone.js voices" — it requires deciding **what
real signal a drum hit represents**, and there's no existing precedent in this codebase
for that (unlike the melodic voices, which already had a settled trigger→pitch mapping
from 009-transit-soundscape). Bundling an unresolved design question into a
ready-to-ship refactor would block it for no reason. Hence: separate doc, separate
timeline.

---

## 2. The core open question

TransitJazz has **no sequencer** — every existing sound is event-driven off real
transit telemetry: a vehicle crosses a derived checkpoint
(`TriggerPointGenerator`-generated, ~200m spacing along a route shape) and
`TriggerNoteAsync(routeId, vehicleId, triggerIndex, totalTriggers)` fires once per
crossing (`TransitMap.razor.cs:206`, `transit-synth.js:183`). The reference lo-fi
projects reviewed for synth-config values (`lofi-engine/docs/SYNTH_CONFIG.md`) both
drive drums from a **fixed-tempo `Tone.Sequence`** with humanization/probability —
that model doesn't exist here and porting it wholesale would mean inventing a clock the
rest of the app doesn't have.

So the question isn't "which Tone.js drum voices" (that part is settled, §4) — it's
**which real signal should a kick/snare/hat hit represent**, so the drumkit stays
consistent with the app's "emergent music from real transit events" premise rather than
becoming a bolted-on decorative loop.

---

## 3. Candidate triggering models

| # | Model | Data source | Pros | Cons |
|---|---|---|---|---|
| (a) | **Accent hits** — drum fires alongside the existing melodic note on every crossing | Same event already driving `triggerNote` — zero new plumbing | Simplest; reuses 100% existing data flow | With real vehicle density (dozens of vehicles crossing checkpoints continuously), likely turns into noise-wash — every note gets a drum stacked on it |
| (b) | **Route start/end markers** — kick on `triggerIndex == 0`, a different hit on `triggerIndex == totalTriggers - 1` | Already-available parameters on the existing call | Sparse, structurally meaningful (marks a vehicle entering/leaving its route), zero new plumbing, low collision risk | Musically minimal — infrequent, may feel disconnected from the ongoing soundscape |
| (c) | **Density-driven** — hi-hat (or similar) rate tied to how many vehicles are active system-wide right now | **New** — no current signal exposes "active vehicle count" to `transit-synth.js` | Most musically interesting; genuinely reflects system-wide transit activity ("data density" as rhythm) | Requires new plumbing: a live count needs to reach the JS layer (new interop call or piggybacked on an existing batch event), decoupled from any single vehicle's crossing — a bigger scope add than (a)/(b) |
| (d) | Hybrid — e.g. (b) for structural accents + (c) for an ambient density-driven hat bed | Combination of the above | Gets both a structural "this vehicle started/ended" cue and an ambient "system is busy" texture | Most implementation work; only worth it if both signals are independently valued |

**Recommendation carried over from the main doc's discussion:** (b) is the
lowest-friction option — no new telemetry, reuses parameters already passed into
`TriggerNoteAsync`, and keeps every drum hit tied to a specific real vehicle event
rather than a synthetic heuristic. (c) is the most musically ambitious and is probably
the "real" long-term answer implied by pairing this with **data density** in the title,
but it needs a new plumbing decision (see §5) before it can be scoped.

**This doc does not choose.** It exists to hold the question until the user provides
direction on which model (or combination) to pursue.

---

## 4. Drum voice definitions (settled — not blocked by §2/§3)

Regardless of which triggering model is chosen, the voice definitions themselves are
already settled (carried over from the main synth-refactor doc), modeled on
`lofi-engine/docs/SYNTH_CONFIG.md`'s one-shot-per-hit drum recipe — single fixed pitch
per drum, synthesized instead of sampled:

| Drum | Tone.js voice | Fixed trigger note | Filter | Notes |
|---|---|---|---|---|
| Kick | `Tone.MembraneSynth` | `C1`/`C2` | none or gentle lowpass | full-range, just gain-trimmed |
| Snare | `Tone.NoiseSynth` + `Tone.Filter` (bandpass or lowpass ~6 kHz) | n/a (noise burst) | bandpass/lowpass to tame harshness | "light top-end shave, not muffle" |
| Hat | `Tone.MetalSynth` | fixed | lowpass, darker cutoff (~2.4 kHz analog) | widest stereo image of the three |

Each gets its own filter + volume (+ optionally `Tone.StereoWidener`) rather than a
shared bus, per the lofi-engine per-voice-chain pattern.

---

## 5. If "data density" (option c/d) is the direction: what needs to exist first

Not yet designed, flagged so it isn't forgotten if this is the chosen path:

- A live "how many vehicles are currently active" signal doesn't currently reach
  `transit-synth.js`. The closest existing candidate is whatever `TransitMap.razor.cs`
  already computes for `EvictInactiveRouteAudioAsync`'s `activeRoutes` set
  (`TransitMap.razor.cs:509`) — active *route* count exists; active *vehicle* count
  would need to be derived similarly from the same batch data.
- Would need a new interop call (or piggyback onto the existing per-batch flow) to push
  that count into the JS layer on some cadence — batch arrival (~10s, per existing
  SignalR batch cycle) is the natural cadence, not a new poll/timer.
- Musical mapping (count → hat subdivision/velocity/probability) is undesigned.

---

## 6. Next step

Parked until the user decides which triggering model (§3) — or a variant — to pursue.
No code or further design work should proceed on this until then.
