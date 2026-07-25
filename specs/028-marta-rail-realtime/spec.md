# Feature Specification: MARTA Rail Realtime

**Feature Branch**: `028-marta-rail-realtime`
**Created**: 2026-06-23
**Status**: Draft
**Input**: User description: "docs/MARTA_RAIL_REALTIME_DESIGN_DOCUMENT.md — Add MARTA heavy-rail trains (RED / GOLD / BLUE / GREEN) to the soundscape by ingesting the MARTA Rail Realtime API and normalizing it into the existing vehicle-position reconciliation pipeline."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Trains appear and move on the map (Priority: P1)

A person watching the live Atlanta transit map sees MARTA heavy-rail trains on the four
rail lines (RED, GOLD, BLUE, GREEN) moving along their tracks in real time, alongside the
buses that already appear. Each train glides smoothly along its rail line rather than
freezing in place or teleporting between positions.

**Why this priority**: This is the core of the feature — without trains visibly on the
map, nothing else matters. It is the minimum that delivers the "richer Atlanta transit
soundscape" value the feature exists for, and it is independently demonstrable on its own.

**Independent Test**: Open the running app while MARTA rail is operating and confirm that
train markers appear on the RED/GOLD/BLUE/GREEN lines, are positioned on the rail
geometry, and advance smoothly along the track over time without freezing or jumping
visibly off-route.

**Acceptance Scenarios**:

1. **Given** the rail feed is reporting active trains, **When** the map is open, **Then**
   each active train appears as a marker positioned on its rail line.
2. **Given** a train's reported position has not changed across several updates, **When**
   the map continues to render, **Then** the train continues to glide forward along the
   rail rather than freezing, and re-settles onto the next real reported position when it
   arrives.
3. **Given** the rail feed reports a physically impossible jump in a train's position,
   **When** the map renders the next update, **Then** the train re-settles onto the new
   position without animating an absurd high-speed dash across the map.

---

### User Story 2 - Trains contribute their musical voice to the soundscape (Priority: P2)

A person listening to the soundscape hears the rail lines contribute musical notes as
trains pass trigger points along their routes, the same way buses already do — so the
Atlanta soundscape now includes the heavy-rail network as part of its sound.

**Why this priority**: Audio is the headline experience of the app, but it depends on
trains first existing on the map (P1). Once trains are present, their voices follow
through the same path buses already use, making this a natural second slice.

**Independent Test**: With rail trains visible and audio enabled, confirm that as a train
passes its route's trigger points a musical note plays, and that each rail line is
associated with a consistent instrument voice.

**Acceptance Scenarios**:

1. **Given** audio is enabled and a train is moving along a rail line, **When** the train
   passes a trigger point on that line, **Then** a musical note plays.
2. **Given** the four rail lines are active, **When** their voices play, **Then** each
   line consistently maps to an instrument voice (note: with only a few shared voices,
   some lines may share a voice — acceptable for v1).

---

### User Story 3 - Rail integration never degrades the existing bus experience (Priority: P1)

A person using the app continues to see and hear buses exactly as before, even when the
rail feed is slow, empty, returning errors, or temporarily unavailable. Adding trains is
purely additive and never removes, breaks, or delays the bus soundscape.

**Why this priority**: The bus soundscape is the existing, shipped product. Any regression
to it is unacceptable, so this is co-critical with P1. It is independently testable by
toggling/failing the rail feed and confirming buses are unaffected.

**Independent Test**: Disable or force-fail the rail feed and confirm bus markers,
bus motion, and bus audio are identical to behavior before this feature; then enable the
rail feed and confirm buses are still unchanged and trains are added on top.

**Acceptance Scenarios**:

1. **Given** the rail feed returns an error or empty result, **When** an update cycle
   runs, **Then** buses still appear, move, and play normally and no error surfaces to the
   user.
2. **Given** rail is toggled off versus on, **When** comparing the bus population over an
   update cycle, **Then** the set of buses is identical — rail only adds trains, never
   alters buses.

---

### Edge Cases

- **Non-live positions**: When the feed marks a train's position as schedule-estimated
  rather than a genuine live fix, that train is excluded so only honest real positions are
  shown.
- **Duplicate listings per train**: The feed lists a train once per upcoming station, so
  one train appears many times in a single response; the system must show each train
  exactly once, not once per station.
- **Contract drift**: If the feed ever stops sharing a single consistent position across a
  train's duplicate listings, the system must surface this loudly (it signals the "live
  position" assumption has broken) rather than silently mis-rendering trains.
- **No trains running**: Off-peak or overnight, few or no trains may be active; the map
  should simply show fewer/no train markers without error and without emptying the bus
  map.
- **Missing speed/direction data**: The feed provides no speed and no bearing; the system
  must tolerate their absence the same way it already tolerates buses that omit them.
- **Feed reachable without credentials**: The feed may currently respond without an API
  key; the system must still treat the key as configurable and must not hard-depend on
  keyless access.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST ingest live MARTA heavy-rail train positions for the RED, GOLD,
  BLUE, and GREEN lines and surface them to the same live map/soundscape experience that
  already shows buses.
- **FR-002**: System MUST place each train on its correct rail line's existing geometry,
  matching the feed's line designation to the already-loaded rail route with no manual
  translation.
- **FR-003**: System MUST represent each active train exactly once per update, collapsing
  the feed's multiple per-station listings of a single train into one train.
- **FR-004**: System MUST exclude train listings that are not marked as genuine live
  positions (schedule-estimated rows) before they enter the experience.
- **FR-005**: System MUST animate each train smoothly along its rail line between position
  updates, continuing forward motion during periods when the feed reports no positional
  change, and re-settling onto each newly reported real position.
- **FR-006**: System MUST avoid rendering physically implausible motion: when the feed
  reports an abrupt large position jump, the train re-settles onto the new position rather
  than animating an absurd high-speed traversal.
- **FR-007**: System MUST tolerate the absence of train speed and direction data without
  error, consistent with how buses already behave when those values are missing.
- **FR-008**: Rail ingestion MUST be best-effort: a failed, empty, or slow rail fetch MUST
  NOT prevent, delay, alter, or break the bus experience for the same update cycle.
- **FR-009**: Rail trains MUST be additive only — the set of buses shown and heard MUST be
  identical whether rail is enabled or disabled.
- **FR-010**: When audio is enabled, each rail line MUST contribute a musical voice to the
  soundscape as its trains pass trigger points, using the existing route-to-instrument
  model (no manual per-line voice configuration required for v1).
- **FR-011**: Rail lines MUST surface in the existing route filter/selection experience
  automatically, as ordinary routes.
- **FR-012**: The MARTA Rail Realtime API key and base endpoint MUST be loaded from
  configuration/environment/secrets and MUST NOT be committed to the repository.
- **FR-013**: The system MUST detect and loudly surface a violation of the feed's expected
  contract that a single train shares one consistent position across its duplicate
  listings, so a silent change in the feed's meaning cannot go unnoticed.
- **FR-014**: The system MUST stamp each train with the freshness time of its reported
  position so that existing stale-sample handling applies to trains as it does to buses.

### Key Entities *(include if data involved)*

- **Rail Train**: A single moving MARTA heavy-rail vehicle on one of the four lines.
  Identified by a stable train identifier; carries a live position, a line designation
  (RED/GOLD/BLUE/GREEN), and a freshness timestamp. Has no reliable speed or direction
  from the source.
- **Rail Line**: One of the four heavy-rail routes (RED, GOLD, BLUE, GREEN), already known
  to the system with its track geometry and (via the existing model) an instrument voice.
- **Rail Position Listing**: A single row from the feed pairing a train with one upcoming
  station; many listings exist per train per update and must be collapsed to one train.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: When MARTA rail is operating, all four lines (RED, GOLD, BLUE, GREEN) that
  have active live trains are represented on the map, with each active train shown exactly
  once (no train duplicated per station).
- **SC-002**: Trains visibly glide along their rail lines; over a multi-minute observation
  no train freezes in place during feed holds and none performs a visible teleport or
  implausible cross-map dash on catch-up updates.
- **SC-003**: 100% of rendered trains sit on their line's track geometry (no train snapped
  to the wrong line or floating off-route).
- **SC-004**: The set of buses shown and heard is identical with rail enabled versus
  disabled across an update cycle (zero bus regression).
- **SC-005**: With the rail feed forced to fail or return empty, the bus experience is
  unchanged and no user-visible error occurs, 100% of the time.
- **SC-006**: When audio is enabled, each active rail line plays a musical note as its
  trains pass trigger points, and each line maps to a consistent instrument voice across
  the session.
- **SC-007**: No API key is present in committed configuration, and the app starts and
  ingests rail data using only environment/secret-supplied credentials (or keyless access
  where the endpoint permits, without committing a key).

## Assumptions

- The four MARTA heavy-rail lines and their track geometry are already loaded by the
  system today (the design confirms rail routes are already indexed under the keys
  `RED`/`GOLD`/`BLUE`/`GREEN`), so no static route/geometry work is required.
- The feed's live coordinate is the train's true track position (verified during design:
  one consistent coordinate across all of a train's per-station listings), so any one
  listing per train is representative after de-duplication.
- The existing smooth-motion model (route-aware extrapolation with a coast cap and
  re-anchoring) is sufficient for the feed's coarse, irregular update cadence without a
  new client subsystem. ETA-paced motion is an optional future refinement, only if
  empirical coasting looks noisy in the running app — out of scope for v1.
- The existing audio model auto-assigns an instrument voice by route key, so the four rail
  lines receive voices automatically; a distinct rail-only voice family is a future
  enhancement, not a v1 requirement.
- This feature is server-side: trains ride the existing realtime delivery and the existing
  client map/audio path. No client code change is anticipated; the only possible client
  touch would be voice data, and only if voices were not auto-assigned (they are).
- Out of scope for v1: adding additional cities; ETA-paced interpolation; derived train
  speed; a rail-distinct instrument family; and retaining schedule-estimated
  (non-live) trains. Derived direction-of-travel is free and may be included.
- The system polls and merges the rail feed on the same update cadence as the existing bus
  feed, with no separate timer or cache.
