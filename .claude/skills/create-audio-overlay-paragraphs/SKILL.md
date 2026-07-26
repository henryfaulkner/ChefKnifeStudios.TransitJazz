---
name: create-audio-overlay-paragraphs
description: Write the three hand-authored prose paragraphs (plus header) for a city's Unlock Audio Overlay — the first screen a visitor sees, which explains that this is a live-transit-into-music audio experience and how to listen to it. Use when the user wants to "write the audio overlay paragraphs", "add a new city's unlock screen copy", "write the intro paragraphs", or author the AudioOverlay text for a transit agency.
---

# Create Audio Overlay Paragraphs

You are writing the **three prose paragraphs** (and the one-line header) shown on
the **Unlock Audio Overlay** — the very first thing a visitor sees when they open
the app for a given city, before audio is unlocked. This screen exists because
**the whole app is an audio experience**: real transit vehicles pulled live and
turned into generative music. If the visitor reads nothing else, these paragraphs
must land two things: *what this app does* and *how to experience it* (audio on).

This is hand-authored creative writing, not a templated readout. It is the
**voice of the app** and the first impression. Match the reference tone exactly.

The text lives as resx entries in
`src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Resources/RouteFilterResources.resx`,
keyed `{Prefix}Header`, `{Prefix}Paragraph1`, `{Prefix}Paragraph2`,
`{Prefix}Paragraph3`. The prefix is resolved per-city in
`AudioUnlockOverlay.razor` (`OnInitialized`): MARTA/Atlanta is the bare
`AudioOverlay`; others are `Wmata…`, `Mbta…`, `Nymta…`. Add a new city by adding
its prefix to that switch **and** adding the four resx keys.

**The paragraphs render as raw markup.** `AudioUnlockOverlay.razor` outputs each
paragraph via `(MarkupString)`, so `<strong>…</strong>` in the resx renders as
real bold. Because it's a `.resx` XML file, write the tags **escaped** —
`&lt;strong&gt;…&lt;/strong&gt;` — not literal `<strong>`. Only ever put our own
authored markup here (never interpolate user/external input) since it's rendered
unencoded. The header and Paragraph1/3 are plain prose (no tags needed).

## The guiding star — the MARTA paragraphs

MARTA (Atlanta) is the reference. Every new city's paragraphs must match this
tone, cadence, and three-beat structure:

> **Header:** MARTA - Atlanta, GA, USA
>
> **Paragraph 1:** Every dot is a real bus. Dozens of them, maybe more, moving
> through a city that never agreed on a grid. They follow Peachtree and Cascade
> and Memorial Drive, routes drawn around what Atlanta actually is rather than
> what a planner imagined.
>
> **Paragraph 2:** The harmony is slow and syncopated. A note lands where the bus
> happens to be along its route — deep in Vine City, crossing the connector,
> idling at Edgewood-Candler Park. Morning fills the piece with movement; after
> midnight the map goes quiet and the notes stretch out. What is playing right
> now has not played before.
>
> **Paragraph 3:** MARTA runs on faith. You wait, and then it comes. This is the
> music inside that waiting.

## The three-beat structure — every city follows this

**Header** — `AGENCY - City, State, Country`. Terse, uppercase-agency, factual.
(e.g. `WMATA - Washington, DC, USA`, `NYCT - New York, NY, USA`.)

**Paragraph 1 — WHAT THIS IS (the live system).** This is the paragraph that
tells a first-time visitor what they're looking at.
- **Open with the reveal:** "Every dot is a real bus" / "Every dot is a real bus
  or train" / "Every dot is a real {AGENCY} vehicle". This one sentence carries
  the whole premise — the map is live, not a simulation.
- Give a sense of **scale** ("Dozens of them," "Hundreds of them," "thousands of
  them") appropriate to the actual system size.
- **Name real, specific streets and lines** — the ones locals know (Peachtree,
  Wisconsin Ave, Massachusetts Avenue, the Red Line, the 7 train). Specificity is
  what makes it feel real. Never generic ("various routes").
- Tie the routes to **the city's actual character or history** ("a city that
  never agreed on a grid," "planned on a diagonal and then grew past every
  boundary," "grew outward from a harbor and never stopped").

**Paragraph 2 — HOW TO EXPERIENCE IT (the sound + how to listen).** This is the
paragraph that teaches the listener *how the music works and that it's live* — and
it must make the **audio-ON requirement unmissable**.
- **Open with the bold audio requirement, verbatim:**
  `<strong>This is an audio experience — turn your device's sound on.</strong>`
  This is the single most important sentence on the screen: the app is silent and
  pointless without sound. It leads Paragraph 2 for **every** city, bolded,
  word-for-word identical. Do not paraphrase it per city.
- **Explain the sound-to-vehicle mapping in plain, poetic terms, and bold it:**
  `<strong>A note lands where the vehicle happens to be along its route</strong>`
  Keep it **generic** — say "vehicle" (or "vehicle"), never "bus" or
  "train", because the map renders bus, rail, and subway together and this bolded
  line must be true for all of them. This tells them what they're hearing
  and why; bolding it makes the core mechanic scannable. If a system has a quirk
  worth honoring (NYCT subways are *inferred* between stations; buses report GPS
  precisely), name it — it deepens the "this is real" feeling. For a city whose
  Paragraph 2 doesn't use the literal "A note lands…" phrasing (e.g. NYCT leads
  with the inference explanation), bold the equivalent core how-it-sounds sentence
  instead — but always keep the bold audio-ON line first.
- **Anchor to time-of-day rhythm** — the piece is denser at rush hour, sparse and
  stretched-out late at night. This tells the listener the experience changes with
  when they listen. (Plain text.)
- **Close with the signature line, verbatim:** *"What is playing right now has not
  played before."* Every city's Paragraph 2 ends on this sentence. It is the
  promise of the whole app — generative, never-repeating — and it must be
  identical across cities. (Plain text, not bolded.)

**Bold sparingly.** Only the audio-ON requirement and the one core sound-mapping
phrase get `<strong>`. If everything is bold, nothing is. The rest of the prose
stays plain so the two emphasized phrases actually pull the eye.

**Paragraph 3 — THE CLOSING TURN (the soul, 2–3 short sentences).** A brief,
resonant statement about what this system *means* to its city, ending on the
signature cadence.
- One truth about the agency ("MARTA runs on faith," "The T is the oldest subway
  in America," "The trains run all night, every night").
- **Close with the motion cadence:** *"This is the music inside that {waiting /
  motion}."* MARTA uses "waiting"; the others use "motion." Pick the word that
  matches the city's Paragraph-3 truth (Atlanta waits; DC/Boston/NY move). Keep
  the exact shape "This is the music inside that ___."

## Voice notes (hold all of these)

- **Third person, present tense, quietly declarative.** Short sentences beside
  long ones. Let a fragment breathe.
- **Concrete over abstract.** Real streets, real stations, real times of day.
  Never "beautiful music," "cool vibes," or marketing adjectives.
- **The listener is oriented but not talked down to.** Someone who's never used
  this transit system should still follow it.
- **Two shared anchors are fixed and identical across every city:** the
  Paragraph-2 closer ("What is playing right now has not played before.") and the
  Paragraph-3 cadence ("This is the music inside that ___."). Everything else is
  city-specific and must be *distinct* — never copy a city's streets, metaphor,
  or closing truth onto another.

## Before you write — gather the inputs

1. **Accept the city / agency.** Get the agency acronym, city, and region for the
   header, and enough local knowledge to name real streets and lines. If you're
   unsure of the flagship routes/streets, ask the user or check the GTFS data
   (the `mj-gtfs` / `mj-api` skills can list real routes).
2. **Read the existing cities' paragraphs** in `RouteFilterResources.resx`
   (`AudioOverlay*`, `Wmata*`, `Mbta*`, `Nymta*`) so the new city sits alongside
   them as a set — varied in metaphor and street names, identical in the two
   shared anchors. Do not reuse another city's imagery.
3. **Confirm the prefix** and whether the city is already wired into the
   `OnInitialized` switch in `AudioUnlockOverlay.razor`.

## Output

Write the four finished strings into `RouteFilterResources.resx` as
`{Prefix}Header`, `{Prefix}Paragraph1`, `{Prefix}Paragraph2`,
`{Prefix}Paragraph3` (match the existing `<data name=… xml:space="preserve">`
format). If the city isn't in the `AudioUnlockOverlay.razor` prefix switch yet,
add it there too.

Then show the user the header and all three paragraphs together so they can read
the flow, and call out which streets/lines and which closing truth you chose so
they can correct any local detail. Offer to write the next city, keeping a
running sense of which metaphors and cadences you've already used so the set
reads like a written collection, not a template.
