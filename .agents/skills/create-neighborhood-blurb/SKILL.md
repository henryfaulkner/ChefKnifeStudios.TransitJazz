---
name: create-neighborhood-blurb
description: Write the hand-authored prose blurb for an Atlanta neighborhood in the Neighborhood Focus Mode (feature 011) data file. The blurb describes the curated musical experience of that neighborhood — the named voices and their character — in short, evocative prose. Use when the user wants to "write a blurb", "create the neighborhood writing", "fill out the blurb", or author the bottom-sheet text for a neighborhood.
---

# Create Neighborhood Blurb

You are writing the **prose blurb** for one Atlanta neighborhood in the
Neighborhood Focus Mode data file (feature `011-neighborhood-focus-mode`). When
a visitor clicks a neighborhood, a bottom sheet rises with this writing. It is
the soul of the feature — short, evocative, first-person-to-the-listener prose
that describes *what they are hearing and why this place sounds the way it
does*.

This is hand-authored creative writing, not a templated data readout. It is the
**voice of the app**.

## The target voice — match this exactly

The reference blurb the user wrote and approved:

> "You're hearing a jazz trombone. The A/C/E share this brass voice — a slow
> lyrical line that breathes for as long as the train cluster lasts."

Notes on that voice, which every blurb should hold:

- **Second person, present tense.** "You're hearing…" — speak to the listener
  in the moment.
- **Names the actual instrument(s)** the neighborhood was assigned — these come
  from the `featuredRoutes` voice `label`s. Use the exact label phrase.
- **One musical metaphor with a physical image** ("a slow lyrical line that
  breathes"). Concrete, sensory, never generic ("nice music," "cool vibes").
- **Ties the sound to the living system** ("for as long as the train cluster
  lasts") — the music is driven by real buses moving right now.
- **Two to three sentences. No more.** It must read at a glance in a bottom
  sheet. Resist listing facts.

## Before you write — gather the inputs

The blurb must be *truthful about the sound* and grounded in real demographic character.
So first:

1. **Accept a neighborhood name or `objectId`.** Either form is valid; an objectId
   lets you look up the lean record directly.
2. **Read the lean data file** at `tools/neighborhood-routes/neighborhood_routes.json`.
   Find the entry matching the neighborhood name (or `objectId`). Extract the
   structured signals that will inform the prose:
   - `routes` and `routeShortName` values — how many routes, which ones
   - `transitCommutePercent` — how transit-dependent this neighborhood is
   - `workFromHomePercent` — how much of the movement is absent
   - `medianHouseholdIncome` — economic character
   - `npu` — planning unit / part of city
   - `population` + `sqMiles` — density feel
   Use these signals as background color for the prose — they shape *why this place
   sounds the way it does* without being listed as data points in the blurb itself.
   If you need full demographic detail (explicit user request), look up
   `full["neighborhoods"][str(objectId)]` in `neighborhood_routes_full.json`.
3. **Read `specs/011-neighborhood-focus-mode/spec.md`** (FR-010 blurb requirement,
   the user stories) so you honor scope: the blurb describes the *sonic
   character*, not GTFS facts and not external neighborhood trivia (those are
   out of scope per the spec's Assumptions).
4. **Read the neighborhood's `featuredRoutes`** and the shared `voices` table in
   the `011` data file. The blurb may only describe voices that are actually
   featured for that neighborhood. If the tones aren't assigned yet, run
   `select-neighborhood-tones` first (or ask the user to) — you cannot write
   truthful prose about voices that don't exist.
5. Note the *ensemble*: which voice is the lead, which is bass/pad/percussion,
   how many routes. The blurb should reflect the actual texture (a lone
   trombone reads differently from a trombone-over-bass duo).

## How to write one

1. **Open with the dominant voice.** Lead with the instrument the listener will
   most notice ("You're hearing a jazz trombone.").
2. **Name the routes by their role, not jargon.** Reference what the routes
   *are* in lay terms when it helps ("the crosstown lines," "the trains
   threading downtown") rather than bare route numbers — but you may name a
   route if it's iconic. Keep the listener, who may not know MARTA, oriented.
3. **Give it one breathing metaphor.** Connect the timbre to a feeling or a
   physical motion. Make the sentence itself have rhythm.
4. **Anchor to live movement.** Remind the listener the sound exists only
   because buses/trains are moving through that place right now.
5. **Stop at 2–3 sentences.** Cut anything that reads like a brochure.

## Consistency rules

- Each neighborhood must sound **distinct** in prose, mirroring the
  neighborhood-scoped voice choices (FR-009). If route R is a trombone in
  Midtown and a Rhodes in Old Fourth Ward, the two blurbs must describe those
  different voices — never copy-paste a blurb between neighborhoods.
- Across the ~10–15 neighborhoods, vary the metaphors and sentence shapes so the
  set reads like a written collection, not a fill-in-the-blank template.
- The instrument names in the blurb must be **verbatim** the `label` strings
  from the `voices` table. If you want a different phrase, change the label in
  the tone assignment (via `select-neighborhood-tones`) so sound and prose stay
  in lockstep.

## Output

Write the finished string into the neighborhood's `blurb` property in the `011`
data file (the same entry that holds its `featuredRoutes`). Then show the user:

- the neighborhood name,
- its featured voices (so they can verify the prose matches), and
- the blurb itself.

Offer to write the next neighborhood, and keep a running sense of which
metaphors/voices you've already used so the collection stays varied.
