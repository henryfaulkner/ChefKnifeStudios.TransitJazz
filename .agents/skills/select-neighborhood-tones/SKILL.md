---
name: select-neighborhood-tones
description: Choose and fill in the per-neighborhood featured-route tone/voice assignments for the Neighborhood Focus Mode (feature 011) data file. Defines named, curated Tone.js voices and maps a neighborhood's featured routes onto them. Use when the user wants to "select tones", "pick voices", "fill out the tone sections", or assign instruments for a neighborhood.
---

# Select Neighborhood Tones

You are filling in the **tone / voice assignments** for one or more Atlanta
neighborhoods in the Neighborhood Focus Mode data file (feature
`011-neighborhood-focus-mode`). Each focused neighborhood plays a hand-curated
arrangement: only its *featured routes* make sound, and each featured route is
assigned a specific, named instrument **voice** that overrides feature 009's
deterministic hash assignment.

This skill picks the voices and writes the `featuredRoutes` mapping. It does
**not** write the prose blurb — that is `create-neighborhood-blurb`. But the two
are co-authored: the voices you pick here are what the blurb will describe, so
keep the labels evocative and accurate.

## Before you start — read the model

1. Read `specs/009-transit-soundscape/data-model.md` for the existing palette,
   the C-minor pentatonic scale (`[48,51,53,55,58,60,63,65,67,70]`), and the
   djb2 hashing. The neighborhood voices are an **expanded, curated** palette
   layered on top of those base Tone.js synths — not a replacement for the
   scale or the pitch model. Pitch still comes from `vehicleId`; you are only
   overriding the *instrument*.
2. Read `specs/011-neighborhood-focus-mode/spec.md` — especially FR-007
   (author voice overrides global), FR-008 (silence non-featured routes), and
   FR-009 (voice is **neighborhood-scoped**: the same route may use a different
   voice in a different neighborhood).
3. Read the current `011` plan/data-model if it exists, to confirm the exact
   file path and JSON shape the neighborhood data lives in. If the data file
   does not exist yet, create it consistently with the plan; if the plan does
   not exist yet, default to GeoJSON `Feature` properties as shown below and
   note the assumption.
4. Find the route identifiers actually present in the app. Featured routes are
   keyed by `routeShortName` (the same key 009 hashes). Do not invent route
   numbers — confirm them against route data the map loads.

## The curated voice palette

Voices are **named** and each is backed by one of 009's base Tone.js synth
types plus an envelope/effects configuration. Keep the engine work small:
prefer the six base synths (`Synth`, `AMSynth`, `PluckSynth`, `FMSynth`,
`MembraneSynth`, `MetalSynth`) shaped with attack/release/filter/effects into a
recognizable character. Each voice has:

- `id` — kebab-case, stable, referenced by `featuredRoutes`
- `label` — the human-readable name the blurb will use ("jazz trombone")
- `base` — which Tone.js synth it builds on
- `config` — envelope / effect notes sufficient for implementation

Maintain the voice definitions in one place in the data file (a top-level
`voices` block), and have each neighborhood's `featuredRoutes` reference voices
by `id`. This keeps voices reusable across neighborhoods while still allowing a
route to map to different voices in different neighborhoods.

Starter voices (extend as needed, keep them sonically distinct):

| id            | label          | base          | character |
|---------------|----------------|---------------|-----------|
| `trombone`    | jazz trombone  | FMSynth       | slow attack, lowpass, lyrical and brassy |
| `rhodes`      | Rhodes piano   | AMSynth       | chorus + light tremolo, warm electric-piano |
| `upright-bass`| upright bass   | Synth (sine)  | short pluck-ish envelope, low register, round |
| `vibraphone`  | vibraphone     | FMSynth       | bright bell-like, medium decay, slight reverb |
| `brushed-kit` | brushed snare  | MetalSynth    | very short, low volume, percussive accent |
| `warm-pad`    | warm pad       | Synth (tri)   | long attack/release, soft sustained bed |
| `pluck`       | nylon pluck    | PluckSynth    | bright transient, decays fast |

## How to choose voices for a neighborhood

1. **Identify featured routes.** Pick the small set of routes that genuinely
   characterize the neighborhood (the ones whose buses are usually present, or
   that are iconic for the area). Featuring 2–4 routes per neighborhood is
   typical — remember every non-featured route goes silent under focus
   (FR-008), so featuring fewer makes a cleaner arrangement.
2. **Assign each featured route a voice** so the *combination* sounds like an
   intentional ensemble, not a pile-up. Think register and role: give one route
   a bass voice, one a lead/melody voice, maybe one a percussive or pad accent.
   Avoid two lead voices fighting in the same register.
3. **Make it harmonize with the scale.** Pitch is still C-minor pentatonic from
   `vehicleId`; you are choosing timbres, not notes. Favor voices whose
   character fits that modal, jazzy palette.
4. **Differentiate across neighborhoods.** Because voices are
   neighborhood-scoped (FR-009), deliberately give the same route a *different*
   voice in different neighborhoods so each place sounds distinct. Note when you
   do this so the blurbs stay consistent.
5. **Keep labels blurb-ready.** The `label` you choose is the exact phrase the
   `create-neighborhood-blurb` skill will use ("you're hearing a jazz
   trombone"). Make it concrete and singable.

## Output shape

Write into the neighborhood's entry in the `011` data file. Default shape
(adjust to match the plan's actual schema):

```json
{
  "type": "Feature",
  "geometry": { "type": "Polygon", "coordinates": [ /* ... */ ] },
  "properties": {
    "id": "midtown",
    "name": "Midtown",
    "blurb": "",
    "featuredRoutes": {
      "110": { "voice": "trombone" },
      "2":   { "voice": "rhodes" },
      "27":  { "voice": "upright-bass" }
    }
  }
}
```

with the shared voice table at the top level:

```json
"voices": {
  "trombone": { "label": "jazz trombone", "base": "FMSynth", "config": { "attack": 0.4, "release": 0.8, "filter": { "type": "lowpass", "frequency": 1200 } } },
  "rhodes":   { "label": "Rhodes piano", "base": "AMSynth", "config": { "effects": ["chorus", "tremolo"] } }
}
```

## Finishing

- Confirm every featured route key is a real `routeShortName`.
- Confirm every `voice` referenced exists in the `voices` table.
- Report, per neighborhood: the featured routes, the voice each got, and a
  one-line rationale for the ensemble. Then hand off to
  `create-neighborhood-blurb` so the prose can describe exactly these voices.
- If you defined new voices, list them so the implementation can add the
  matching Tone.js builders.
