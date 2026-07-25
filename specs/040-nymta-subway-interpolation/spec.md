# Feature Specification: NYC Subway Position Interpolation

**Feature Branch**: `040-nymta-subway-interpolation`
**Created**: 2026-07-12
**Status**: Draft
**Input**: User description: "docs/nymta-subway-interpolation-design.md" — Add a NymtaCity transit adapter that makes NYC subway trains appear and move on the map, even though the NYC subway GTFS-RT feed never carries a vehicle coordinate. It is a stop-arrival-prediction feed (route, current stop sequence, current status, target stop platform code, timestamp — no lat/lon). The adapter synthesizes a plausible position per train by pinning stopped/arriving trains to their target station coordinate and, for trains in transit between stations, estimating how far along the segment they are from elapsed time and walking the route's shape geometry (not a straight chord) to that distance. All synthesis is quarantined inside the adapter so the shared transit loop and every downstream stage (snap, lerp, crossing detection, synth audio) are untouched and treat a synthesized train exactly like a real MARTA bus. New static data (station coordinates from stops.txt and per-route stop-distance-along-shape from stop_times.txt) is computed server-side once at static-load time and served to the worker via a new endpoint, never recomputed on the hot path. The subway real-time feed fans out across ~8 line-group feeds that are fetched, decoded, synthesized, and merged into one normalized feed message.

## User Scenarios & Testing *(mandatory)*

<!--
  This feature's primary "user" is the person viewing the TransitJazz map with
  NYC (nymta) selected as the active city, plus the developer/operator who
  configures and runs the worker. The value delivered is: NYC subway trains
  become visible and move believably on a map, despite a real-time feed that
  contains no coordinates at all.
-->

### User Story 1 - NYC subway trains appear on the map (Priority: P1)

A person viewing the TransitJazz map with NYC (nymta) selected sees subway
trains rendered at their stations. When a train is stopped at or arriving at a
platform, it appears exactly at that station's location. Today, with the
generic config-driven ingestion, every NYC subway entity is discarded because
it has no coordinate, and the map stays empty.

**Why this priority**: This is the irreducible core of the feature — without a
synthesized position, nothing about NYC subway is visible and no other user
story can exist. Pinning stopped/arriving trains to their station is the
simplest slice that produces a populated, correct-at-rest map.

**Independent Test**: Can be fully tested by running the worker with the nymta
city configured, observing that trains with status "stopped at" or "arriving"
render precisely on their target station coordinates, and confirming the map is
no longer empty for NYC.

**Acceptance Scenarios**:

1. **Given** a subway train reported as stopped at a known station platform,
   **When** the adapter produces its position, **Then** the train is placed
   exactly at that station's coordinate.
2. **Given** a subway train reported as arriving at a known station platform,
   **When** the adapter produces its position, **Then** the train is placed at
   that station's coordinate (treated as effectively at the platform).
3. **Given** a train whose target station code is not found in the static
   station data, **When** the adapter processes it, **Then** the train is
   skipped (not rendered) and counted as an unknown-station skip, and no other
   train is affected.

---

### User Story 2 - In-transit trains drift believably between stations (Priority: P2)

A person watching the map sees a train that is running between two stations
move smoothly along the actual curve of the subway line, rather than teleporting
from station to station or cutting in a straight line across city blocks. The
train's position is estimated from how much time has elapsed since the last
report, spread across the segment between the previous station and the target
station, and the estimate is anchored so the train is exactly right whenever it
reaches either endpoint.

**Why this priority**: This is what makes the map read as "live" rather than a
list of dots snapping between platforms. It builds directly on Story 1 (which
supplies the endpoint coordinates and station ordering) and is the larger,
higher-value half of the visible experience, but the map is already useful
without it.

**Independent Test**: Can be tested by observing an in-transit train over
successive updates and confirming it moves along the route's drawn shape (not a
straight line between stations), advances with elapsed time, and coincides with
the station coordinate at both the start and end of the segment.

**Acceptance Scenarios**:

1. **Given** a train in transit toward a station that has a known previous
   station on its line and direction, **When** the adapter estimates its
   position, **Then** the train is placed on the route's shape geometry at a
   distance proportional to elapsed time between the two stations' shape
   offsets.
2. **Given** a train in transit whose elapsed time meets or exceeds the nominal
   run time for the segment, **When** the adapter estimates its position,
   **Then** the train is placed at (not beyond) the target station coordinate.
3. **Given** a train in transit toward the first station on its line (no
   previous station in that direction), **When** the adapter estimates its
   position, **Then** the train is placed at the target station coordinate.
4. **Given** two adjacent stations connected by a curved section of line,
   **When** an in-transit train is placed at a mid-segment fraction, **Then**
   its position follows the drawn curve rather than a straight line between the
   two stations.

---

### User Story 3 - A synthesized train is indistinguishable from a real vehicle downstream (Priority: P2)

A developer confirms that once a train leaves the NYC adapter, it is an ordinary
normalized vehicle carrying a real coordinate, and every downstream stage — the
shared transit loop, position snapping, movement smoothing, checkpoint-crossing
detection, and the audio synth — treats it exactly like a MARTA bus, with no
NYC-specific branching anywhere outside the adapter.

**Why this priority**: The whole justification for the adapter pattern is that
it contains the NYC-specific algorithm in one place. If NYC logic leaks into the
shared loop, the design has failed regardless of whether trains render. It is
P2 rather than P1 because it is an architectural guarantee validated alongside
Stories 1–2, not a separately visible outcome.

**Independent Test**: Can be tested by confirming the shared transit loop and
all downstream stages contain no NYC-specific conditional, and that a
synthesized NYC train flows through snapping, smoothing, crossing detection, and
audio identically to a real bus.

**Acceptance Scenarios**:

1. **Given** a synthesized NYC subway train, **When** it enters the shared
   transit loop, **Then** it carries a real coordinate and is processed by the
   same code path as a real vehicle, with no NYC-specific condition in the
   shared loop.
2. **Given** a NYC subway line and a real bus route running concurrently,
   **When** both flow downstream, **Then** checkpoint-crossing detection and
   audio behave for the subway train exactly as they do for the bus.

---

### User Story 4 - Static station and offset data is prepared once and reused (Priority: P3)

An operator running the worker sees that the station coordinates and per-route
stop-distance-along-shape data needed for interpolation are computed once
(server-side, at static-load time) and fetched once by the worker on startup and
on its normal periodic refresh, then reused on every real-time tick — never
recomputed and never re-fetched on the hot path, and the large raw schedule data
is never shipped to the worker.

**Why this priority**: This is a performance and correctness discipline that
underpins Stories 1–2 (they depend on the station and offset lookups existing),
but it is an internal quality attribute rather than a user-visible behavior, so
it ranks below the visible outcomes.

**Independent Test**: Can be tested by confirming the station/offset data is
produced once server-side, retrieved by the worker on startup and on the
existing refresh cadence, and read from cache on every tick with no per-tick
recomputation or re-fetch, and that raw schedule rows are not delivered to the
worker.

**Acceptance Scenarios**:

1. **Given** the worker has started and fetched the station/offset data,
   **When** it processes real-time ticks, **Then** it reads the cached data and
   performs no additional fetch or recomputation of station coordinates or
   shape offsets per tick.
2. **Given** the periodic static-refresh interval already used by the worker
   elapses, **When** the refresh runs, **Then** the station/offset data is
   refreshed on that same cadence, not on every tick.

---

### Edge Cases

- **Train's target station code is not in the static station data**: the train
  is skipped (not rendered) and recorded in an unknown-station skip counter,
  mirroring how unknown routes are already skipped; other trains are unaffected.
- **Train's route has no shape geometry**: the train is skipped because the
  route index simply does not contain that route.
- **Train in transit toward the first station on its line (no previous
  station)**: it is pinned to the target station coordinate rather than being
  dropped.
- **Elapsed time far exceeds the nominal run time for a segment**: the along-
  segment fraction clamps to the endpoint so the train sits at the target
  platform (correct behavior for a late or held train), never overshooting past
  the station.
- **A train reappears far along a line after a real-time gap**: the station
  endpoints re-anchor its position, and the shared loop's existing snap window
  re-establishes its snapped position — no special handling is required in the
  adapter.
- **A train has no reported movement status**: it is treated as stopped at its
  target station (pinned to the station), the safest default that never leaves
  the train without a position.
- **One of the ~8 line-group real-time feeds is unavailable or fails to
  decode**: only that line group's trains are missing for that tick; the other
  line groups still render, because each feed is fetched and decoded
  independently.
- **Direction ambiguity at a terminal**: the direction encoded in the station
  platform code is used to determine which neighboring station is the
  "previous" one, so the segment is unambiguous even at line ends.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST make NYC subway trains visible on the map for the
  nymta city, despite the NYC subway real-time feed carrying no vehicle
  coordinate.
- **FR-002**: For a train reported as stopped at or arriving at a known station,
  the system MUST place the train exactly at that station's coordinate.
- **FR-003**: For a train reported as in transit toward a known station that has
  a known previous station on its line and direction, the system MUST place the
  train on the route's shape geometry at a distance along the segment
  proportional to the estimated fraction of the segment completed.
- **FR-004**: The in-transit fraction MUST be estimated from the time elapsed
  since the train's reported observation timestamp against a nominal inter-
  station run time, and MUST be clamped so that a train never appears before its
  previous station or beyond its target station.
- **FR-005**: In-transit position MUST be computed by walking the route's drawn
  shape polyline by cumulative distance (not by drawing a straight line between
  the two station coordinates), so trains follow curved sections of line
  correctly.
- **FR-006**: A synthesized train's position MUST coincide with the station
  coordinate at both endpoints of a segment (the previous station at fraction 0
  and the target station at fraction 1), so the feed's only ground-truth points
  are rendered exactly.
- **FR-007**: The "previous station" for an in-transit train MUST be determined
  using the direction encoded in the station platform code, so the segment is
  unambiguous including at terminals.
- **FR-008**: Every train that receives a synthesized position MUST be emitted
  as an ordinary normalized vehicle (carrying route identity, a real coordinate,
  and a timestamp) that is indistinguishable downstream from a real vehicle such
  as a MARTA bus.
- **FR-009**: All NYC-subway-specific synthesis logic MUST be contained within
  the NYC adapter; the shared transit loop and every downstream stage (position
  snapping, movement smoothing, checkpoint-crossing detection, audio synth) MUST
  remain unchanged and free of any NYC-specific conditional.
- **FR-010**: The system MUST fetch all NYC subway real-time line-group feeds,
  decode each, run synthesis, and merge the results into a single normalized
  feed message for the city; a failure of one line-group feed MUST NOT prevent
  the others from rendering.
- **FR-011**: The static data required for interpolation — each station's
  coordinate, and each route's ordered stations with their cumulative distance
  along the route shape — MUST be computed once, server-side, at static-load
  time, co-located with the existing shape/cumulative-distance processing.
- **FR-012**: The worker MUST obtain the station/offset data via a served
  endpoint once on startup and refresh it only on the existing periodic static-
  refresh cadence; it MUST NOT recompute or re-fetch this data on the real-time
  tick.
- **FR-013**: The raw schedule data used to derive the offsets MUST be processed
  and discarded server-side and MUST NOT be shipped to the worker.
- **FR-014**: A train whose target station is not present in the station data,
  or whose route has no shape, MUST be skipped rather than rendered, and skips
  MUST be counted (e.g. an unknown-station counter analogous to the existing
  unknown-route counter).
- **FR-015**: A train with no reported movement status MUST be treated as
  stopped at its target station.
- **FR-016**: The NYC adapter MUST be registered as its own bespoke city
  adapter (not a generic config-driven entry), selected by the city name
  "nymta", with its subway static data source and its set of line-group real-
  time feed sources supplied via configuration.
- **FR-017**: The NYC subway adapter MUST NOT emit telemetry initially,
  consistent with the existing decision to keep telemetry limited to MARTA;
  local operational counters for synthesized positions and skips are still
  permitted.

*Scope boundary (out of scope for this feature):*

- **NYC bus** is explicitly out of scope. NYC bus is compatible with the
  existing generic config-driven ingestion (needing only a route-ID normalizer
  and its own static/real-time configuration) and does not depend on this
  feature; it can be added separately.
- Refining the nominal inter-station run time from scheduled timetable data is
  out of scope; a constant nominal run time is acceptable for this feature
  because the endpoints anchor the motion.

### Key Entities

- **NYC subway train (real-time entity)**: A reported train carrying its route,
  the station it is working toward (target platform code, whose suffix encodes
  direction), its movement status (stopped / arriving / in transit), and an
  observation timestamp. It carries **no coordinate** — that is the entire
  problem this feature solves.
- **Station**: A subway platform identified by a station platform code, with a
  fixed coordinate. Direction is encoded in the platform code suffix.
- **Route ordered station list**: For a given route and direction, the ordered
  sequence of stations, each annotated with its cumulative distance along the
  route's shape. Used to find the previous station and to place an in-transit
  train at a distance along the shape.
- **Route shape**: The existing drawn geometry for a route, along which
  cumulative distance is measured; in-transit trains are placed on this
  polyline, not on a chord between stations.
- **NYC subway adapter (NymtaCity)**: The bespoke city adapter that fetches the
  line-group real-time feeds, synthesizes a coordinate per train, and emits
  normalized vehicles — the single place all NYC-subway-specific logic lives.
- **Station/offset dataset**: The server-computed, worker-consumed table of
  station coordinates and per-route stop-distance-along-shape offsets, produced
  once at static-load time and cached by the worker.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With the nymta city active, subway trains are visible on the map;
  the map is no longer empty (whereas today 100% of NYC subway entities are
  discarded for lack of a coordinate).
- **SC-002**: Every train reported as stopped at or arriving at a known station
  renders exactly on that station's coordinate (0 m endpoint error at rest).
- **SC-003**: In-transit trains render on the route's drawn shape and advance
  with elapsed time between updates, and coincide exactly with the station
  coordinate at both segment endpoints.
- **SC-004**: The shared transit loop and all downstream stages contain zero
  NYC-specific conditionals; a synthesized NYC train and a real bus follow the
  same downstream code path (verifiable by inspection).
- **SC-005**: On every real-time tick, no station coordinate or shape offset is
  recomputed and the station/offset dataset is not re-fetched; it is fetched
  only on startup and on the existing refresh cadence.
- **SC-006**: A single failing line-group real-time feed reduces only that
  line group's visible trains; trains from all other line groups still render.
- **SC-007**: Trains whose target station is unknown, or whose route has no
  shape, are skipped and reflected in a skip counter, with no effect on other
  trains.

## Assumptions

- The NYC subway route identifiers in the real-time feed (e.g. single-letter and
  numbered line labels) already align with the static route index keys, so no
  route-ID remapping is required for the subway (unlike NYC bus, which is out of
  scope).
- The direction encoded in the station platform code suffix is sufficient to
  disambiguate the "previous" station for an in-transit train, including at
  terminals.
- A constant nominal inter-station run time is acceptable for the first
  implementation; the endpoints anchor the motion so a constant produces
  believable movement. Refining it from scheduled timetable data is deferred.
- The existing route shape geometry and its cumulative-distance measurement are
  reused as-is for both server-side offset computation and worker-side in-
  transit placement; no new shape math is introduced beyond locating a point at
  a given distance along an existing polyline.
- The worker's existing periodic static-refresh cadence is the correct cadence
  for refreshing the station/offset dataset; no new refresh mechanism is
  introduced.
- Registering the NYC adapter reuses the existing bespoke-city registration
  pattern (one selection branch by city name), consistent with how the MARTA
  adapter is registered; no change to the shared city-selection contract is
  required.
- Telemetry remains limited to MARTA for this feature; the NYC adapter emits no
  telemetry, though local operational counters are retained for diagnostics.
- The real-time feeds are decoded via the codebase's existing binary
  protobuf-stream decode path, avoiding the text-encoding corruption pitfall
  noted in the feed evaluation; no new decode approach is introduced.
