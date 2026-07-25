---
name: neighborhood-focus-mode
description: Let a visitor click an Atlanta neighborhood on the map to focus on it — muting and dimming everything outside, playing a hand-curated musical arrangement of only that neighborhood's featured routes, and surfacing an authored prose blurb plus live transit stats in a bottom sheet
---
# Feature Specification: Neighborhood Focus Mode

**Feature Branch**: `011-neighborhood-focus-mode`
**Created**: 2026-05-29
**Status**: Draft
**Input**: User description: "Neighborhood Focus Mode"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Focus a neighborhood and hear its curated arrangement (Priority: P1)

A visitor is listening to the full-city emergent soundscape (the existing default experience). They recognize a neighborhood name they care about — Midtown, West End, Old Fourth Ward — drawn as a subtle outline on the map. They click its polygon. The rest of the city dims and falls silent; only the buses inside that neighborhood continue to make sound, now playing a hand-curated arrangement of instruments chosen specifically for that place. A bottom sheet rises with a short, evocative piece of writing describing what they are hearing and why that neighborhood sounds the way it does.

**Why this priority**: This is the entire feature. It transforms the app from "ambient full-city listening" into "a place you can tune into." Neighborhood names give non-transit-savvy visitors an emotional handle the route-based model lacks. If only this story ships, the feature is demonstrable and valuable.

**Independent Test**: Open the deployed site when buses are active. Click a neighborhood polygon. Within seconds, buses outside the neighborhood dim and go silent, buses inside continue sounding with the curated voices, and a bottom sheet appears with the authored blurb for that neighborhood. Clicking the map background restores the full soundscape.

**Acceptance Scenarios**:

1. **Given** the full-city soundscape is playing and at least one bus is inside neighborhood N, **When** the visitor clicks N's polygon, **Then** within 2 seconds buses outside N are visually dimmed and produce no further notes, while featured-route buses inside N continue producing notes.
2. **Given** a neighborhood is focused, **When** the visitor reads the bottom sheet, **Then** it displays the authored prose blurb for that neighborhood.
3. **Given** a neighborhood is focused, **When** the visitor clicks the map background (outside any polygon), **Then** focus is released, all buses return to full brightness, the bottom sheet dismisses, and the full-city soundscape resumes.
4. **Given** neighborhood N is focused, **When** the visitor clicks a different neighborhood M's polygon, **Then** focus shifts directly to M — M's arrangement and blurb replace N's without an intermediate unfocused state.

---

### User Story 2 - Only curated voices speak in a focused neighborhood (Priority: P1)

When a neighborhood is focused, the visitor hears a deliberately composed arrangement — not whatever the global hash assigned. Each featured route in that neighborhood plays the specific instrument the author chose for it, and the author's blurb accurately describes those voices. Routes the author did not feature are silent even if their buses are physically inside the neighborhood.

**Why this priority**: The authored prose ("you're hearing a jazz trombone") is only truthful if the sound matches what was written. Co-P1 with Story 1 because the curation *is* the product — an uncurated voice bleeding in breaks the illusion and contradicts the blurb.

**Independent Test**: Focus a neighborhood whose curated arrangement is known. Confirm that featured routes play their authored instruments (not their global default voices), and that a bus whose route is not in the neighborhood's featured set produces no sound while focus is active, even when it is inside the polygon.

**Acceptance Scenarios**:

1. **Given** neighborhood N features route R with an authored voice V, **When** a bus on route R triggers a note inside N while N is focused, **Then** the note sounds with voice V (the authored voice), not route R's global default voice.
2. **Given** neighborhood N does not feature route Q, **When** a bus on route Q is inside N while N is focused, **Then** that bus produces no notes for the duration of focus.
3. **Given** route R is featured in neighborhood N as voice V1 and in neighborhood M as voice V2, **When** the visitor focuses N versus M, **Then** route R sounds as V1 under N and V2 under M.

---

### User Story 3 - See live activity for the focused neighborhood (Priority: P2)

Alongside the authored blurb, the bottom sheet shows a small number of live statistics derived from the real-time feed and route data — how many buses are active in the neighborhood right now, and how many distinct routes they are on — so the writing feels anchored to the living system the visitor is watching and hearing.

**Why this priority**: The blurb alone carries the feature; live stats are enrichment that ties the prose to the moment. P2 because Story 1 ships and delights without it — this is the layer that makes the bottom sheet feel alive rather than static.

**Independent Test**: Focus a neighborhood with multiple active buses. Confirm the bottom sheet shows an active-vehicle count and active-route count that visibly update as buses enter and leave the neighborhood.

**Acceptance Scenarios**:

1. **Given** a neighborhood is focused with K active buses inside it across J distinct routes, **When** the visitor reads the bottom sheet, **Then** it shows counts consistent with K and J.
2. **Given** a focused neighborhood's live counts are displayed, **When** a bus enters or leaves the neighborhood, **Then** the displayed counts update to reflect the change within a few seconds.

---

### Edge Cases

- **No buses inside the focused neighborhood**: Focusing a neighborhood with zero active buses inside it shows the blurb and zeroed live stats, plays no sound, and produces no errors. When a bus later enters, its note plays (if its route is featured).
- **Only non-featured buses inside**: A focused neighborhood containing only buses on non-featured routes is silent but otherwise fully functional (blurb shown, stats may show non-zero vehicle count but zero featured activity).
- **Bus straddling a boundary / GPS jitter at the edge**: A bus oscillating across a neighborhood boundary must not cause focus-state thrash or rapid muting/unmuting artifacts; membership changes should be debounced or hysteretic enough to avoid audible flicker.
- **Overlapping neighborhood polygons**: If hand-authored polygons overlap, a click resolves to exactly one neighborhood deterministically; a bus in the overlap is attributed consistently.
- **Click during the pre-audio-gesture period**: If the visitor's first interaction with the page is clicking a neighborhood (before any audio gesture), audio initializes cleanly on that gesture and the focused arrangement begins without errors.
- **Neighborhood data not loaded**: If neighborhood polygon/blurb data has not finished loading, polygons are simply not yet clickable; the full-city soundscape continues unaffected.
- **Route geometry not loaded for a featured route**: If a featured route's geometry is not yet available, that route is silently inactive within focus until geometry loads, consistent with the existing soundscape behavior.
- **Focus shift mid-note**: Shifting focus from N to M (or unfocusing) while notes are sounding must not leave stuck/hung notes; in-flight notes resolve naturally and no new non-curated notes begin.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST render a set of hand-authored Atlanta neighborhood boundaries as a subtle, non-intrusive map layer that does not visually compete with bus markers in the unfocused state.
- **FR-002**: System MUST allow a visitor to focus a neighborhood by clicking its polygon on the map.
- **FR-003**: System MUST release focus when the visitor clicks the map background outside any neighborhood polygon, returning to the unfocused full-city state.
- **FR-004**: System MUST allow focus to shift directly from one neighborhood to another via a single click on the new neighborhood, with no intermediate unfocused state.
- **FR-005**: System MUST treat each neighborhood polygon as a focus toggle target such that clicking it focuses that neighborhood; the unfocused state is reached only by clicking the map background.
- **FR-006**: While a neighborhood is focused, System MUST mute all buses outside the focused neighborhood (they produce no notes) and visually dim them relative to buses inside the focused neighborhood.
- **FR-007**: While a neighborhood is focused, System MUST play, for each bus inside the neighborhood whose route is in that neighborhood's featured set, the author-specified instrument voice for that route — overriding the global deterministic voice assignment.
- **FR-008**: While a neighborhood is focused, System MUST silence any bus whose route is not in that neighborhood's featured set, even if the bus is physically inside the neighborhood.
- **FR-009**: System MUST scope featured-route voice assignments per neighborhood, such that the same route MAY be assigned a different voice in different neighborhoods.
- **FR-010**: System MUST display, while focused, the neighborhood's hand-authored prose blurb in a bottom sheet that opens on focus and dismisses on unfocus.
- **FR-011**: System MUST display, while focused, live statistics for the focused neighborhood — at minimum the count of active buses inside the neighborhood and the count of distinct routes those buses are on — and MUST update these as buses enter and leave. These statistics MUST be derived on the fly from the existing real-time vehicle stream already in memory on the client; the System MUST NOT persist them, store any history, or introduce any database or server-side storage to compute them.
- **FR-018**: System MUST NOT introduce any historical or aggregate statistics (e.g., averages over time, "busiest route this week", ridership trends) in this feature. All displayed statistics are instantaneous and transient. Historical/aggregate statistics are explicitly deferred to a future feature.
- **FR-012**: System MUST source neighborhood definitions — polygon geometry, display name, prose blurb, and the featured-route-to-voice mapping (with human-readable instrument labels) — from a single hand-authored data file, with no external runtime dependency.
- **FR-013**: System MUST preserve the existing full-city soundscape behavior as the unfocused default: all routes audible with their global deterministic voice assignments, no filtering, no dimming.
- **FR-014**: System MUST handle neighborhood-boundary crossing jitter without producing audible muting/unmuting flicker for a bus oscillating across a boundary.
- **FR-015**: System MUST resolve a click on overlapping polygons to exactly one neighborhood deterministically, and MUST attribute a bus in an overlap region to a neighborhood consistently.
- **FR-016**: System MUST initialize audio cleanly when a neighborhood click is the visitor's first page interaction, beginning the focused arrangement without errors or undelivered-event accumulation.
- **FR-017**: System MUST NOT leave stuck or hung notes when focus shifts between neighborhoods or is released while notes are sounding.

### Key Entities *(include if feature involves data)*

- **Neighborhood**: A hand-authored Atlanta place with a polygon boundary, a display name recognizable to laypeople, an authored prose blurb describing its sonic character, and a featured-route set. Roughly 10–15 neighborhoods in v1. Defined entirely in the hand-authored data file; adds no server-side data.
- **Featured Route Assignment**: Within a neighborhood, a mapping from a route identifier to a specific instrument voice and a human-readable instrument label. Scoped to its neighborhood — the same route may map to different voices in different neighborhoods. Only routes present in this set produce sound while the neighborhood is focused.
- **Blurb**: The authored short-form prose for a neighborhood, describing the musical experience of that place (e.g., the instruments and their character). Co-authored with the featured-route voice choices so the writing matches the sound. Stored with the neighborhood in the data file.
- **Focus State**: The transient app state of either "unfocused" (full-city default) or "focused on neighborhood N." Drives muting/dimming, voice overrides, the bottom sheet, and live-stat computation. Not persisted.
- **Live Neighborhood Stats**: Transient, derived values for the focused neighborhood — active vehicle count inside the polygon and distinct active-route count — computed on the fly from the existing real-time vehicle stream already held in client memory. Recomputed as the in-polygon vehicle set changes. Never persisted; no database, no history, no server-side storage. There is no stored entity here — these are computed counts, listed only to make the data flow explicit.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A visitor can go from the full-city soundscape to a focused neighborhood — buses outside muted and dimmed, featured buses inside sounding with curated voices, blurb visible — within 2 seconds of clicking a neighborhood polygon.
- **SC-002**: While a neighborhood is focused, 100% of audible notes originate from buses inside that neighborhood on featured routes; no note originates from outside the neighborhood or from a non-featured route (verifiable by listening against the known featured set).
- **SC-003**: For a route featured in two different neighborhoods with two different authored voices, a listener can confirm the route sounds with the correct voice under each neighborhood's focus.
- **SC-004**: Clicking the map background while focused restores the full-city soundscape (all routes audible, full brightness, bottom sheet dismissed) within 2 seconds, with no stuck notes.
- **SC-005**: The bottom sheet's live vehicle/route counts for a focused neighborhood reflect buses entering and leaving within 5 seconds of the change.
- **SC-006**: A bus oscillating across a neighborhood boundary produces no audible muting/unmuting flicker over a 60-second observation.
- **SC-007**: Zero unhandled errors appear in the browser console during a 5-minute session that includes focusing, shifting focus directly between neighborhoods, unfocusing, and focusing as the very first page interaction.
- **SC-008**: Initial page-load and time-to-first-vehicle show no observable regression relative to the prior production build (the neighborhood layer and data do not degrade startup).

## Assumptions

- The full-city emergent soundscape from feature 009 (transit-soundscape) is the baseline this feature extends; its route=instrument / vehicle=pitch / shared-scale model and its real-time vehicle position stream are in place and reused. Unfocused behavior is exactly 009's behavior.
- Neighborhood polygons are hand-authored by the developer as a static data file checked into the repo (~10–15 well-known Atlanta neighborhoods). Boundary accuracy need only be "good enough for a music app," not survey-grade; minor inaccuracies are acceptable for v1.
- Blurbs and featured-route voice assignments are hand-authored together as a single creative act, and are stored in the same data file as the polygons. Writing 10–15 blurbs is in-scope developer effort.
- The instrument voices referenced by featured-route assignments are drawn from the existing soundscape's instrument palette (or a curated extension of it); this feature does not require a new audio synthesis engine.
- Determining whether a bus is "inside" a neighborhood uses the bus's live position against the hand-authored polygon; the existing position stream is sufficient and no new server-side data is required.
- The target audience is desktop browser visitors at the production site, consistent with 009. Mobile-specific layout/touch optimization for the bottom sheet and polygon clicking is out of scope for v1, though the bottom-sheet pattern is mobile-friendly by nature.
- Live stats are limited to counts derived on the fly from data already in client memory (active vehicles in polygon, distinct routes). They are computed, not stored. Richer GTFS facts and externally-sourced neighborhood facts are out of scope for v1.
- **No database and no persistence.** This feature stores nothing — not the live stats, not focus state, not any neighborhood activity. Computing the live counts requires no database, no server-side storage, and no new ingestion path; they are a count over the in-memory vehicle set. Introducing persistence would contradict the frontend-only, zero-persistence design DNA of feature 009 that this feature extends.
- **Historical/aggregate statistics are a deferred future feature, not this one.** "Cool" stats like time-averaged activity, busiest-route-this-week, or ridership trends would require recording observations over time and therefore a database; that is a separate feature (candidate 012) and is explicitly out of scope here.
- Neighborhood focus introduces no new persistent state and no server changes; it is a frontend-only feature like 009.
