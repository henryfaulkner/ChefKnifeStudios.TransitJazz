# Feature Specification: Stale Snapshot Filter

**Feature Branch**: `023-stale-snapshot-filter`  
**Created**: 2026-06-20  
**Status**: Draft  
**Input**: User description: "Filter stale records out of the /transit/last-batch snapshot so it only serves useful data (the grill-me results)"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Cold-start map shows buses immediately (Priority: P1)

A user opens the transit map. Before any live update arrives, the map fetches the most recent server-held snapshot of bus positions so buses appear within the first moment of page load instead of after the next live update (~10 seconds later). The snapshot the user receives contains only meaningful, last-known positions for the buses the system currently knows about — never "this bus hasn't moved" duplicate readings.

**Why this priority**: This is the entire purpose of the feature. The cold-start snapshot exists solely to avoid a blank map on load; today it can deliver a snapshot composed entirely of stale duplicate readings, which the map discards, leaving the user staring at an empty map until the first live update. This story restores the snapshot's reason for existing.

**Independent Test**: Load the map immediately after the system's most recent update happened to be all-duplicate readings; verify buses still appear from the snapshot (not only after the next live update).

**Acceptance Scenarios**:

1. **Given** the system's most recent update contained only stale duplicate readings, **When** a user loads the map and the snapshot is fetched, **Then** the snapshot still contains the last-known meaningful position for each currently-known bus (it is not empty).
2. **Given** the system's most recent update contained a mix of fresh and stale readings, **When** the snapshot is fetched, **Then** it contains only the fresh meaningful readings and excludes every stale one.
3. **Given** the snapshot is fetched, **When** it is delivered to the client, **Then** it contains no empty groupings (no envelope carrying zero records).

---

### User Story 2 - Live updates remain complete and unchanged (Priority: P1)

A user already watching the map continues to receive the full live stream of position updates, including stale duplicate readings, so their already-moving buses can re-anchor correctly. The snapshot cleanup must not alter what live viewers receive.

**Why this priority**: Equal-priority guardrail. The fix is only correct if it leaves the live path untouched. Clients with existing motion state rely on stale readings to re-anchor; stripping them from the live stream would break ongoing animation. This story protects against a regression introduced by the snapshot change.

**Independent Test**: Trigger an update containing stale readings and confirm the live broadcast still carries every record (including stale ones), byte-for-byte unchanged from before this feature.

**Acceptance Scenarios**:

1. **Given** an update containing both fresh and stale readings is published, **When** it is broadcast to live viewers, **Then** the broadcast includes all records, including the stale ones, with no filtering applied.
2. **Given** the snapshot served to new visitors is filtered, **When** the same update is broadcast live, **Then** the live broadcast and the snapshot are produced independently and the live broadcast is unmodified.

---

### User Story 3 - Buses persist across updates even when their latest reading is stale (Priority: P2)

A bus whose most recent reading is a stale duplicate still appears in the cold-start snapshot at its last meaningful position, because the system retains the last meaningful reading per bus across updates rather than only remembering the single most recent update.

**Why this priority**: This is the design decision that makes Story 1 robust rather than fragile. Without per-bus retention, a bus whose latest reading is stale would vanish from the snapshot even though the system knows where it last was. It is P2 because Story 1's headline benefit is already delivered for the common case by retention; this story names and guarantees the retention behavior explicitly.

**Independent Test**: Publish a fresh reading for a bus, then publish a later update where that bus's only reading is stale; fetch the snapshot and confirm the bus is still present at its earlier fresh position.

**Acceptance Scenarios**:

1. **Given** a bus had a fresh reading in an earlier update, **When** a later update contains only a stale reading for that bus, **Then** the snapshot still includes that bus at its last fresh position.
2. **Given** a bus has produced multiple fresh readings over time, **When** the snapshot is fetched, **Then** the bus appears once, at its most recent fresh position.
3. **Given** several buses have each contributed fresh readings across different updates, **When** the snapshot is fetched, **Then** every such bus appears in a single combined snapshot.

---

### Edge Cases

- **All-stale update before any fresh reading exists**: If the very first update the system ever receives contains only stale readings (no prior meaningful reading for any bus), the snapshot is empty rather than containing placeholder or empty groupings. The map remains blank until a meaningful reading arrives — acceptable because there is genuinely no meaningful position to show.
- **All-stale or empty update after meaningful readings exist**: An update that contributes no new meaningful readings leaves the previously accumulated snapshot intact; it does not wipe known positions.
- **A bus reported only once, as stale, never seen fresh**: That bus is absent from the snapshot (the system cannot invent a position it never received meaningfully). The live stream will correct this on the next meaningful reading.
- **A bus changes which route it is associated with**: The bus is tracked as a single entity; its latest meaningful reading wins regardless of route, so it never appears as two simultaneous map markers.
- **Buses that have left service**: A bus's last meaningful position persists in the snapshot until the system restarts; there is no time-based expiry in this feature. A slightly-old-but-present bus is preferable to an absent one for cold start, and the live stream supersedes it within one update cycle.
- **An update arriving while a snapshot is being read**: Snapshot reads always return a complete, internally consistent picture and never a partially-updated state.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The cold-start snapshot served to new visitors MUST exclude all stale duplicate readings.
- **FR-002**: The cold-start snapshot MUST NOT contain any empty groupings (no record-group carrying zero records); when there is nothing meaningful to serve, the snapshot MUST be an empty collection.
- **FR-003**: The system MUST retain, per bus, the most recent meaningful (non-stale) reading across successive updates, rather than only the contents of the single most recent update.
- **FR-004**: When an update contains a meaningful reading for a bus, the system MUST replace that bus's retained reading with the newer one (latest meaningful reading wins).
- **FR-005**: When an update contains only a stale reading for a bus, the system MUST keep that bus's previously retained meaningful reading and MUST NOT overwrite it with the stale reading.
- **FR-006**: When an update contains a stale reading for a bus that has no previously retained meaningful reading, the system MUST omit that bus from the snapshot (it MUST NOT invent or seed a position).
- **FR-007**: An update that contributes no new meaningful readings (empty update, or one containing only stale readings) MUST leave the previously accumulated snapshot unchanged.
- **FR-008**: Each bus MUST appear at most once in the snapshot, identified by its bus identifier, regardless of route association.
- **FR-009**: The live broadcast of updates to already-connected viewers MUST remain unchanged by this feature and MUST continue to include stale readings.
- **FR-010**: Snapshot filtering and live broadcasting MUST be produced independently, such that filtering the snapshot can never alter the live broadcast.
- **FR-011**: The snapshot MUST be served in the same structural shape that live updates use, so that the client consumes it through the same path without distinguishing snapshot from live data.
- **FR-012**: Reading the snapshot concurrently with an update being applied MUST always yield a complete, internally consistent picture and never a partially-updated or torn result.
- **FR-013**: The system MUST NOT apply any time-based expiry to retained readings within this feature; retained positions persist until the system restarts.
- **FR-014**: The meaning, structure, and fields of the underlying reading and update records MUST NOT change.

### Key Entities *(include if feature involves data)*

- **Bus reading**: A single observed nearest-route-point reading for one bus at one moment, carrying the bus identifier, its position, motion attributes (speed, bearing), associated route, observation timestamps, and a flag indicating whether the reading is a stale duplicate of the prior observation.
- **Update**: A group of bus readings produced once per system cycle, broadcast live to connected viewers and used to refresh retained per-bus state.
- **Retained snapshot state**: The system's accumulated record of the most recent meaningful reading per bus, assembled on demand into the snapshot served to new visitors.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a cold page load occurring after an all-duplicate update, buses appear on the map from the snapshot rather than only after the next live update (cold-start map population time drops from ~10 seconds to immediate in this scenario).
- **SC-002**: 100% of snapshots served contain zero stale readings and zero empty groupings.
- **SC-003**: The live update stream delivered to connected viewers is identical before and after this feature, verified by comparing the broadcast contents for representative updates.
- **SC-004**: Every bus that has contributed at least one meaningful reading since the system last started appears exactly once in the snapshot, at its most recent meaningful position.
- **SC-005**: The change is delivered with automated tests covering an all-stale update, a mixed update, per-bus retention across updates, the stale-never-seen case, latest-meaningful-wins, and the no-empty-grouping guarantee; all existing and new tests pass and the build succeeds.

## Assumptions

- The grill-me decisions are authoritative: filtering/merging occurs at update-write time (not serve time), retained state is keyed by bus identifier storing the full meaningful reading, there is no eviction or time-based expiry, the snapshot is assembled as a single combined group, and the existing client-side workaround is intentionally left in place.
- The nearest-route-point update is the only kind of data flowing through this snapshot path today; other data kinds, if introduced later, are out of scope and would warrant revisiting this design.
- The retained per-bus state lives in process memory for the lifetime of the serving process; a restart legitimately clears it, after which the snapshot repopulates as new meaningful readings arrive.
- The fleet size is bounded (hundreds of buses), so accumulating one retained reading per bus identifier seen has negligible memory impact.
- This feature is server-side only; no client behavior, record shapes, broadcast contracts, or service registration change.
