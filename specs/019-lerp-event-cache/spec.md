# Feature Specification: Last Lerp Event Cache

**Feature Branch**: `019-lerp-event-cache`  
**Created**: 2026-06-16  
**Status**: Draft  
**Input**: User description: "Cache last lerp event data on the WebAPI / Cache can be simply an in-memory object / Expose cache as an API endpoint / Client should call endpoint on load to avoid lag btw events"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Buses appear immediately on page load (Priority: P1)

A rider opens the transit map. Today the map waits, blank of buses, until the next periodic real-time push arrives (up to one full polling interval — roughly ten seconds). The rider should instead see the buses in their most recent known positions the moment the map finishes loading, then watch them update naturally as new pushes arrive.

**Why this priority**: This is the entire point of the feature — eliminate the dead window between page load and the first real-time push. Without it, the feature delivers nothing.

**Independent Test**: Load the map immediately after a real-time push has occurred but before the next one is due. Buses render right away (within the load itself) rather than after a multi-second wait. Fully delivers the feature's value on its own.

**Acceptance Scenarios**:

1. **Given** at least one real-time vehicle-position batch has been published since the service started, **When** a new client loads the map, **Then** the client obtains the most recent batch immediately on load and renders those vehicles without waiting for the next periodic push.
2. **Given** a client has obtained the most recent batch on load, **When** the next periodic push arrives, **Then** the client transitions smoothly to the pushed data with no duplicate, flicker, or teleport of vehicles.
3. **Given** two clients load the map at different moments within the same polling interval, **When** each completes its load, **Then** both render the same most-recent batch.

---

### User Story 2 - Graceful behavior before any data exists (Priority: P2)

A rider opens the map during the brief window right after the service starts, before any real-time batch has been produced. The map should load cleanly and simply show no buses yet, then populate when the first real push arrives — never an error, spinner-forever, or broken state.

**Why this priority**: Protects the cold-start window. Lower priority than P1 because it covers a short-lived edge condition, but it must not crash or block the map.

**Independent Test**: Restart the service and load the map before the first real-time push. The map loads normally and shows no vehicles, then populates on the first push.

**Acceptance Scenarios**:

1. **Given** no real-time batch has been published yet, **When** a client requests the most recent batch on load, **Then** the response is a successful, well-formed empty batch (no error), and the map renders with no vehicles.
2. **Given** the empty-batch response was returned, **When** the first real-time push subsequently arrives, **Then** vehicles appear normally.

---

### User Story 3 - The cache always reflects the latest push (Priority: P3)

As real-time pushes continue over time, the cached snapshot must always equal the most recently published batch, so any client loading at any later moment gets current data rather than a stale early snapshot.

**Why this priority**: Ensures correctness over the service lifetime rather than just at first load. Important but subordinate to the load-time behavior itself.

**Independent Test**: Trigger several successive real-time pushes, then load a fresh client after each; each load returns the batch from the immediately preceding push.

**Acceptance Scenarios**:

1. **Given** multiple batches have been published in sequence, **When** a client loads after the Nth batch, **Then** the client receives the Nth (most recent) batch, not any earlier one.
2. **Given** a batch is being published at the same moment a client requests the cached snapshot, **When** the request is served, **Then** the client receives a complete, internally consistent batch (either the prior or the new one in full — never a partially updated mixture).

---

### Edge Cases

- **Cold start (no data yet):** The endpoint returns a successful, well-formed *empty* batch rather than an error or a missing resource. The client treats this as "no vehicles yet."
- **Concurrent read during write:** A client reading the snapshot while a new batch is being cached receives one complete batch, never a half-updated object.
- **Service restart:** The cache is in-memory only and resets on restart; the first load after restart hits the cold-start path until the first new push.
- **Rapid reloads:** Many clients loading within the same interval all receive the same most-recent batch without additional load on the upstream data producer.
- **Stale-flagged records:** The cached batch carries records exactly as published, including any stale markers; the client applies its existing handling for those records unchanged.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The WebAPI MUST retain the most recently published real-time vehicle-position batch (the same batch payload it relays to connected clients) in an in-memory snapshot.
- **FR-002**: The cached snapshot MUST be replaced with each newly published batch so it always equals the latest batch relayed to clients.
- **FR-003**: The WebAPI MUST expose a read-only endpoint that returns the current cached batch snapshot.
- **FR-004**: Before any batch has been published, the endpoint MUST return a successful response containing a well-formed empty batch (an empty set of records), not an error or missing-resource response.
- **FR-005**: The client MUST request the cached snapshot once during map load and render the returned vehicles immediately, before — and independently of — receiving any periodic push.
- **FR-006**: When the next periodic push arrives after a load-time snapshot has been rendered, the client MUST transition to the pushed data without duplicating, flickering, or teleporting vehicles.
- **FR-007**: Reading the cached snapshot MUST NOT trigger any new fetch from the upstream real-time data producer; the endpoint MUST serve only what is already cached.
- **FR-008**: Reading the snapshot while it is being updated MUST yield one internally consistent batch (the prior or the new one in whole), never a partially updated object.
- **FR-009**: The cached snapshot's shape and field semantics MUST match the batch already delivered over the real-time channel, so the client can reuse its existing rendering logic with no per-field divergence.
- **FR-010**: The feature MUST NOT alter the existing periodic real-time push behavior; the cache is additive and the live channel continues unchanged.

### Key Entities *(include if data involved)*

- **Cached batch snapshot**: The single most-recent batch of vehicle route-position records held in memory on the WebAPI. Holds the same records (vehicle identity, prior/current snapped position, timestamps, speed, bearing, stale marker) that are relayed live; superseded in whole on every new push; empty until the first push; lost on restart.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: When a batch has been published recently, a newly loaded map shows vehicles within the page load itself rather than after waiting for the next push — eliminating the up-to-one-interval (~10s) blank-of-buses window.
- **SC-002**: 100% of fresh loads occurring after at least one published batch render the most-recent batch's vehicles immediately on load.
- **SC-003**: Loads occurring before any batch exists complete successfully and show an empty map with no error, in 100% of cold-start attempts.
- **SC-004**: The transition from the load-time snapshot to the first subsequent push produces no visible duplicate, flicker, or teleport of any vehicle.
- **SC-005**: Requesting the cached snapshot adds no additional calls to the upstream real-time data producer (zero extra upstream fetches per snapshot request).

## Assumptions

- "Last lerp event data" refers to the most recent real-time vehicle-position batch the WebAPI relays to clients to animate buses along routes — i.e. the data that drives on-map bus motion — not the internal telemetry/logging delta records of the same conceptual name. (Confirmed with stakeholder.)
- The cache lives on the WebAPI at the point where it already relays each published batch to clients, so it observes every batch without any new cross-service call. (Confirmed with stakeholder.)
- The cold-start response is a successful empty batch (not a no-content / missing-resource status), to minimize client branching. (Confirmed with stakeholder.)
- An in-memory object is sufficient; durability across restarts is explicitly out of scope ("Cache can be simply an in-memory object").
- A single most-recent snapshot is sufficient; no history, list, or per-client buffering is required.
- The client already maintains rendering logic for batch records received over the live channel and can reuse it for the snapshot.
- Authentication/authorization for the new read endpoint follows the same posture as the existing public read endpoints serving map data; no new secret or credential is introduced.

## Dependencies

- The existing real-time relay path on the WebAPI that receives each published batch and forwards it to clients (the cache hooks in here).
- The existing client map-load sequence, into which the one-time snapshot request is added.
- The existing batch record shape shared between server and client, reused unchanged by the snapshot endpoint.
