---
name: transit-soundscape-v1
description: Replace the hand-authored checkpoint POC with a derived, route-based musical model where each route is an instrument and each vehicle is a tone, producing emergent music as buses move through the city
---
# Feature Specification: Emergent Transit Soundscape v1

**Feature Branch**: `009-transit-soundscape`
**Created**: 2026-05-22
**Status**: Draft
**Input**: User description: "Emergent transit soundscape v1: replace the hand-authored checkpoints POC (008) with a derived, route-based musical model. Generate checkpoints procedurally by uniformly spacing points along each route polyline. Move crossing detection out of the animator into a dedicated tracker. Each route is one instrument; each vehicle is one tone on its route's instrument; concurrent vehicles harmonize on a shared scale. Checkpoints are pure triggers with no note metadata. Out of scope: per-route/suburb isolation UI, visible checkpoint markers, history blurbs."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Hear emergent music from live transit (Priority: P1)

A first-time visitor opens the transit map and, after a single interaction (required by the browser to permit audio), begins hearing musical notes that correspond to real MARTA buses traveling through the city. Each route has a recognizably different instrumental sound; different buses on the same route play distinct, harmonically-compatible pitches. With nothing on screen but the map and moving vehicles, the visitor can sit, listen, and perceive Atlanta's transit network as a living piece of music.

**Why this priority**: This is the entire elevator pitch — "instrument per route, tone per vehicle, emergent music." Every other story is supporting infrastructure or refinement. If only this story ships, the project's core thesis is demonstrable.

**Independent Test**: Open the deployed site at a time when buses are active. Click once to satisfy the browser's audio-gesture requirement. Within 60 seconds of opening the map, audible notes play in response to vehicle movement. Listening for two minutes, the listener can identify (a) that different routes have different timbres and (b) that the same route can produce multiple simultaneous pitches when it has multiple active vehicles.

**Acceptance Scenarios**:

1. **Given** the map has loaded and at least one bus is in motion, **When** the visitor performs any click or key press on the page, **Then** within the next 30 seconds at least one musical note is audible in response to bus movement.
2. **Given** two different bus routes each have at least one active vehicle, **When** vehicles on both routes are moving for 60 seconds, **Then** the listener can perceive two distinct instrument timbres (one per route) in the soundscape.
3. **Given** a single route has two or more active vehicles, **When** those vehicles trigger notes within a short window of each other, **Then** the simultaneous pitches sound harmonically compatible rather than dissonant.
4. **Given** the same vehicle has been moving for several minutes, **When** it triggers notes over that period, **Then** every note from that vehicle is the same pitch on the same instrument.

---

### User Story 2 - Audio reflects real movement, not stationary noise (Priority: P1)

A bus stopped at a red light or layover position must not produce a stream of repeated notes from minor GPS jitter or animation oscillation. The soundscape should reflect actual progress through the city — silence from stationary buses, rhythm from moving ones.

**Why this priority**: Without this, the experience is unlistenable. A single stopped vehicle next to a trigger point would dominate the soundscape with rapid repetition, drowning out the emergent-music effect from Story 1. This is co-P1 because Story 1 is not viable without it.

**Independent Test**: Identify a vehicle that is stationary on the live map (at a stop, traffic light, or end-of-line layover). Observe for 60 seconds. Confirm that the soundscape does not contain a repeating note attributable to that vehicle. When the vehicle resumes moving, notes from it should resume.

**Acceptance Scenarios**:

1. **Given** a vehicle has stopped near a position that would normally trigger a note, **When** that vehicle remains stopped for 60 seconds, **Then** at most one note is heard for that vehicle in that period.
2. **Given** a stopped vehicle that has just triggered a note, **When** that vehicle resumes forward motion and advances past additional trigger points, **Then** subsequent triggers produce notes as normal once the vehicle is clearly moving again.
3. **Given** a vehicle oscillates back and forth near a trigger point due to position-update jitter, **When** the oscillation continues for any duration, **Then** the system does not produce a burst of rapidly-repeated notes for that vehicle.

---

### User Story 3 - Soundscape rhythm tracks bus speed (Priority: P2)

A bus traveling at typical road speed produces a steady, recognizable rhythm of notes — not so frequent that it becomes a drone, not so sparse that the listener cannot connect successive notes from the same vehicle. The cadence should make it intuitively understandable that *this rhythm comes from this bus traveling through the city*.

**Why this priority**: The musicality of the experience depends on rhythm being perceptible. P2 because Story 1 can ship without optimal cadence — it would just sound less musical. This story is the tuning pass that takes the experience from "it works" to "it sounds good."

**Independent Test**: Observe a single moving vehicle in isolation (a route with only one active bus, or by focusing attention on one bus). Time the interval between its successive notes during steady travel. The interval should feel rhythmic — neither rapid-fire nor minute-long pauses.

**Acceptance Scenarios**:

1. **Given** a vehicle is traveling at a typical urban bus speed (15–30 mph), **When** the listener attends to that vehicle for 60 seconds of continuous motion, **Then** the listener perceives a regular cadence of notes from that vehicle (interpretation of "regular" is qualitative; absence of long silent gaps and absence of rapid bursts during steady motion is the test).
2. **Given** the chosen note-spacing tuning is in effect, **When** a route has multiple buses operating concurrently, **Then** the combined cadence across the route does not produce continuous overlapping noise.

---

### Edge Cases

- **Browser audio autoplay restriction**: When the page loads before the visitor has interacted, no audio context can be created. The system must not crash, log errors, or produce a degraded experience after the first interaction — audio must begin cleanly on the next trigger after interaction.
- **Vehicle appears mid-route**: A vehicle that becomes visible to the system in the middle of a route (not at the start) must not retroactively trigger every checkpoint it appears to be "past." Only forward motion from its first observed position triggers notes.
- **Vehicle teleports / GPS glitch**: If a vehicle's position updates jump by an implausibly large distance (e.g., several kilometers in one update), the system must not trigger a burst of notes for every checkpoint between the two positions.
- **Route polyline is very short or very long**: An unusually short route (few hundred meters) and an unusually long route (tens of kilometers) must both produce a sensible number of trigger points — neither zero nor thousands.
- **Two vehicles, same route, same checkpoint, same instant**: Two simultaneous triggers on the same instrument with different pitches must layer correctly (both notes audible) rather than one cancelling the other.
- **No active vehicles**: With zero buses in motion, the system is silent and produces no errors or audible artifacts.
- **Route data not yet loaded**: If a vehicle position arrives for a route whose geometry has not yet loaded, the system silently drops that vehicle's triggers until geometry is available, then resumes normally.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST generate trigger points for every route automatically from that route's geometry — no human authoring step.
- **FR-002**: System MUST space trigger points along each route at a configurable, uniform distance with a single global tuning value applied to all routes.
- **FR-003**: System MUST assign each route a deterministic instrument timbre such that a given route's instrument is the same across page loads, sessions, and visitors.
- **FR-004**: System MUST assign each vehicle a deterministic pitch on its route's instrument such that the same vehicle always sounds the same note for the duration of a session.
- **FR-005**: System MUST constrain the set of available pitches to a single shared musical scale across all routes so that concurrent notes from different routes are harmonically compatible.
- **FR-006**: System MUST detect when a vehicle's position has crossed a trigger point and emit exactly one audio event per genuine crossing.
- **FR-007**: System MUST suppress repeated triggers from a stationary or oscillating vehicle such that a non-moving bus produces at most one note within a short suppression window.
- **FR-008**: System MUST defer audio playback gracefully until the visitor has performed the browser-required user interaction; the system MUST NOT log errors, crash, or accumulate undelivered audio events during the pre-interaction period.
- **FR-009**: System MUST handle a vehicle's first observation without retroactively triggering trigger points it appears to have already passed.
- **FR-010**: System MUST handle implausibly large position jumps (e.g., GPS glitches or vehicle disappearing/reappearing on a different segment) without producing a burst of rapid notes.
- **FR-011**: System MUST silently drop vehicle triggers for routes whose geometry is not yet loaded, and MUST resume triggers automatically once the geometry becomes available.
- **FR-012**: System MUST support simultaneous notes — multiple vehicles triggering at nearly the same moment MUST all be audible, not mutually-cancelling.
- **FR-013**: System MUST remove all of the 008 POC's hand-authored checkpoint data, per-checkpoint note metadata, and animator-embedded crossing detection. The 008 architecture MUST NOT remain side-by-side with the new architecture.
- **FR-014**: System MUST NOT introduce any visible map markers, marker animations, or other on-map visual elements for trigger points. The trigger layer is audio-only in this feature.
- **FR-015**: System MUST NOT introduce route-isolation, suburb-isolation, history blurbs, or any other interactive musical-filtering UI in this feature.

### Key Entities *(include if feature involves data)*

- **Route**: A transit line with a geometric polyline. Each route is associated with exactly one instrument timbre, derived from its public route identifier. Routes are sourced from the existing route data already loaded by the map; this feature adds no new route data.
- **Vehicle**: A bus reporting real-time position along a route. Each vehicle is associated with exactly one pitch on its route's instrument, derived from its vehicle identifier. Vehicles enter and leave the system as MARTA reports them.
- **Trigger Point**: A position on a route's polyline at which a vehicle crossing causes that vehicle's note to play. Trigger points are derived from route geometry at runtime — they have no authored properties, no per-point note, and no visible representation. The set of trigger points for a route changes only if the route's geometry changes.
- **Crossing Event**: A transient event representing that a specific vehicle has just crossed a specific trigger point. Crossing events drive audio output and are not persisted.
- **Instrument**: One of a small palette of musical timbres. The palette size, the choice of timbres, and the mapping from route identifier to timbre are tuning decisions for this feature.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new visitor can hear at least one bus-driven musical note within 30 seconds of performing their first interaction on the page (assuming at least one bus is in motion).
- **SC-002**: In a listening session of 2 minutes with at least three active routes, a listener can correctly identify that the soundscape contains multiple distinct instrument timbres (one per route).
- **SC-003**: In a listening session of 2 minutes with any single route that has multiple active vehicles, the soundscape contains no audibly-dissonant concurrent pitches (subjective; the qualitative test is "this sounds like music, not noise").
- **SC-004**: A vehicle that is stopped for 60 seconds adjacent to a trigger point produces no more than one note in that period.
- **SC-005**: A moving vehicle traveling at typical urban speeds produces notes at a perceptible, regular cadence — at least one note every 30 seconds, no more than one every 5 seconds, during continuous motion.
- **SC-006**: Initial page-load time-to-first-vehicle shows no observable regression relative to the prior production build.
- **SC-007**: Zero unhandled errors appear in the browser console during a 5-minute session that includes the pre-interaction phase, the first-interaction transition, and steady-state listening.
- **SC-008**: All artifacts of the 008 POC (hand-authored checkpoint file, per-checkpoint note metadata, animator-embedded detection) are absent from the codebase after this feature ships.

## Assumptions

- The existing route polyline data already loaded by the map is geometrically accurate enough that uniform along-polyline spacing produces a perceptibly-uniform along-the-road experience. (Routes with sharp turns may have minor visual-vs-road discrepancies; these are acceptable for v1.)
- The existing vehicle position stream from the worker (current SignalR event flow) is the only input needed; no new server-side data is required.
- The existing vehicle animator continues to be the authoritative source of per-frame vehicle position. This feature consumes its position stream; it does not replace it.
- Browser audio capabilities are available in all target browsers (Chrome/Edge/Firefox latest), consistent with the prior 008 baseline.
- A small palette (≈4–8 timbres) is sufficient to give MARTA's bus routes audibly-distinct instruments. With more routes than palette entries, multiple routes may share an instrument; this is acceptable for v1.
- A single shared musical scale (e.g., a pentatonic or modal scale) across all routes is acceptable as the v1 harmonization strategy. More sophisticated harmonization (per-route key signatures, modulation over time, etc.) is out of scope.
- The "single global tuning value" for trigger-point spacing will be tuned by the developer during manual verification, not exposed as a runtime configuration to visitors.
- The visitor population is desktop browser users at the production site. Mobile-specific layout, touch-interaction, or low-end-device performance optimization is out of scope.
