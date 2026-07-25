# Feature Specification: Logging Sidecar Service

**Feature Branch**: `013-logging-sidecar-service`  
**Created**: 2026-06-04  
**Status**: Draft  
**Input**: User description: "Logging sidecar service for the TransitDataWorker — decouple structured event logging from the data-processing hot path, capture Snap / Lerp / Cycle decision telemetry, and flush it durably for later analysis. Model the Event / EventData separation on the existing SignalR-event pattern."

## Overview

The TransitDataWorker continuously ingests the live GTFS-RT vehicle feed every 10 seconds and makes a series of per-vehicle decisions: snapping each raw GPS position to the nearest point on its route shape (**Snap**), computing position/speed/bearing deltas against the prior observation to drive client-side interpolation (**Lerp**), and summarizing each processing pass (**Cycle**). Today these decisions are visible only as transient, free-form log lines — they cannot be queried, aggregated, or audited after the fact, and adding richer logging inline risks slowing the processing loop that must keep pace with the real-time feed.

This feature introduces a **logging sidecar** inside the TransitDataWorker: an in-process, decoupled pathway that captures structured records of each Snap, Lerp, and Cycle decision and durably persists them for later analysis, without adding latency or failure risk to the data-processing hot path.

The durable output is **parquet files in Azure Blob Storage**, queried downstream by the existing `telemetry-query-tool` (a DuckDB-based CLI that reads `azure://…` parquet via the DuckDB Azure extension). The sidecar's persistence pathway maps to the `StructuredLoggingService` in the reference architecture: it formats batched records into parquet and flushes them to blob.

## Clarifications

### Session 2026-06-04

- Q: How should the sidecar build the parquet file? → A: In-process using a .NET parquet library (e.g. Parquet.Net), then upload the finished blob to Azure (no external process / no DuckDB on the worker host).
- Q: How frequently is parquet flushed, and how are files laid out for daily sharding? → A: Flush a parquet part-file every 5 minutes into a daily folder partition `dt=YYYY-MM-DD/`; the query tool reads a day's shard by globbing that folder.
- Q: Are Snap / Lerp / Cycle stored together or as separate parquet datasets? → A: One schema-homogeneous dataset per event type — `snap/dt=YYYY-MM-DD/`, `lerp/dt=YYYY-MM-DD/`, `cycle/dt=YYYY-MM-DD/` — each independently daily-sharded.
- Q: Where does the sidecar's own health telemetry (buffer occupancy, dropped count, persistence failures) go? → A: Folded onto the Cycle record as additional columns; no separate health dataset.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Capture per-cycle health telemetry (Priority: P1)

As an operator running the TransitDataWorker in production, I want each processing cycle to emit a structured summary record (counts of buses moved, unchanged, stationary, stale, and skipped; cycle timing; duplicate-feed detection; cache sizes) so that I can confirm the worker is keeping pace with the live feed and detect degradation before it affects riders.

**Why this priority**: Cycle-level telemetry is the smallest independently valuable slice — it answers "is the worker healthy and processing the feed?" on its own, requires only one event schema and the full sidecar pipeline end-to-end, and is the foundation operators need before finer-grained per-vehicle telemetry is useful. It is the MVP.

**Independent Test**: Run the worker against the live (or a recorded) feed for several cycles and confirm that one structured Cycle record per processing pass is durably persisted with accurate counts and timing, and that the Cycle telemetry does not appear on the hot-path processing thread's critical timing.

**Acceptance Scenarios**:

1. **Given** the worker completes a processing cycle, **When** the cycle finishes, **Then** exactly one Cycle record is captured containing the cycle identifier, start/end time, execution duration, per-outcome bus counts, feed-header timestamp, duplicate-feed flag, and current cache sizes.
2. **Given** a feed poll returns a feed whose header timestamp matches the previous poll, **When** the cycle is summarized, **Then** the Cycle record marks the cycle as a duplicate feed.
3. **Given** the durable persistence destination is temporarily unavailable, **When** a cycle completes, **Then** the worker continues its next processing cycle without error and without blocking on the persistence failure.

---

### User Story 2 - Capture per-vehicle Snap decisions (Priority: P2)

As an analyst tuning the route-snapping logic, I want each vehicle's snap decision (which route point a raw position was matched to, and the resulting outcome) captured as a structured record so that I can measure snapping accuracy and diagnose mis-snaps offline.

**Why this priority**: Snap telemetry is the highest-volume, most detailed per-vehicle data and the primary input for tuning spatial accuracy, but it depends on the same pipeline as Cycle and is only worth capturing once the pipeline is proven healthy (P1).

**Independent Test**: Process a feed containing known vehicles and confirm that each snapped vehicle produces a Snap record with its route number, route position, bus identity, bus position/speed/bearing, and the position delta with timestamp and cycle identifier — and that the categorical snap outcome is recorded as a readable name.

**Acceptance Scenarios**:

1. **Given** a vehicle position is snapped to a route during a cycle, **When** the decision is captured, **Then** a Snap record contains the route number and route position, the bus number/position/speed/bearing, and a position delta carrying a timestamp and the owning cycle identifier.
2. **Given** a snap decision results in a categorical outcome (e.g. first observation, moved, unchanged, stationary, stale), **When** the record is captured, **Then** the outcome is recorded as the human-readable name of that decision category.

---

### User Story 3 - Capture per-vehicle Lerp deltas (Priority: P3)

As an analyst validating client-side motion interpolation, I want each vehicle's frame-to-frame delta (prior route/bus state plus the position, speed, bearing, and time deltas to the current observation) captured as a structured record so that I can verify the interpolation inputs that drive smooth vehicle movement on the map.

**Why this priority**: Lerp telemetry refines understanding of motion quality but is the least urgent of the three — Cycle proves health, Snap proves accuracy, and Lerp is a deeper diagnostic layered on top.

**Independent Test**: Process consecutive observations of the same vehicle and confirm a Lerp record captures the prior route data, prior bus data, and the bus delta (position/speed/bearing/time deltas) tagged with the owning cycle identifier.

**Acceptance Scenarios**:

1. **Given** a vehicle has a prior observation in the current cycle, **When** its delta is computed, **Then** a Lerp record captures the prior route data, prior bus data, and a bus-delta carrying position delta, speed delta, bearing delta, time delta, and the owning cycle identifier.

---

### Edge Cases

- **Telemetry burst exceeds capacity**: When decisions are produced faster than they can be persisted, the sidecar MUST shed load (drop the newest excess records) rather than grow memory without bound or apply back-pressure to the data-processing loop.
- **Persistence failure**: When the durable destination rejects or fails a write, the failure MUST be contained within the sidecar (logged, surfaced via self-telemetry) and MUST NOT propagate into or halt the data-processing loop.
- **Worker shutdown**: When the worker is stopping, the sidecar MUST detach cleanly so shutdown is not blocked indefinitely. The disposition of in-flight buffered records at shutdown is captured as an assumption below.
- **Unrecognized event type**: When an event that is not a logging event is posted to the notification bus, the sidecar MUST ignore it without error.
- **Self-monitoring**: The sidecar itself MUST emit enough operational telemetry (e.g. buffer occupancy, dropped-record count, persistence failures) for an operator to tell whether it is keeping up.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The data-processing components MUST emit logging events through an in-process notification mechanism rather than persisting telemetry directly, so that producing telemetry does not couple the producer to the persistence mechanism.
- **FR-002**: The system MUST decouple telemetry persistence from the data-processing hot path such that the time to persist (or fail to persist) a record never adds latency to, blocks, or fails the feed-processing loop.
- **FR-003**: The system MUST buffer captured telemetry in a bounded in-memory queue and, when that queue is full, MUST drop the newest incoming records (load-shedding) rather than block producers or exhaust memory.
- **FR-004**: The system MUST asynchronously drain buffered telemetry on a background pathway separate from the data-processing loop and persist each record durably for later analysis.
- **FR-004a**: The system MUST persist telemetry as **parquet files in Azure Blob Storage**, readable by the existing `telemetry-query-tool` via the DuckDB Azure extension. Parquet files MUST be built **in-process with a .NET parquet writer** (no external process and no DuckDB dependency on the worker host).
- **FR-004b**: The system MUST flush a parquet **part-file every 5 minutes** (and on graceful shutdown, best-effort), accumulating drained records in memory between flushes. Each flush writes a self-contained parquet file; the system MUST NOT attempt to append to or mutate an already-written blob.
- **FR-004c**: The system MUST lay parquet files out so each day forms a distinct shard: part-files for a given UTC day MUST be written under a **daily partition folder** named `dt=YYYY-MM-DD/`, such that the query tool can read one day's telemetry by globbing that folder's part-files. Part-file names MUST be unique within the day (e.g. carry a flush timestamp) so concurrent or successive flushes never collide.
- **FR-004d**: The system MUST persist each event type as its **own schema-homogeneous parquet dataset**, each independently daily-sharded: `snap/dt=YYYY-MM-DD/`, `lerp/dt=YYYY-MM-DD/`, and `cycle/dt=YYYY-MM-DD/`. A single parquet file MUST contain records of only one event type (no mixed-schema or union files), so each dataset is directly queryable as a uniform table.
- **FR-005**: The system MUST capture a **Cycle** record for every completed processing cycle, containing: cycle identifier, cycle start time, cycle end time, cycle execution duration, buses processed, buses moved, buses unchanged, buses stationary, buses stale, buses skipped for missing route id, buses skipped for unknown route, feed-header timestamp, duplicate-feed indicator, last-update cache size, and vehicle-state cache size.
- **FR-006**: The system MUST capture a **Snap** record for each per-vehicle snap decision, containing route data (route number, route position), bus data (bus number, bus position, bus speed, bus bearing), and a position delta (timestamp, cycle identifier).
- **FR-007**: The system MUST capture a **Lerp** record for each per-vehicle delta computation, containing prior route data, prior bus data, and a bus delta (position delta, speed delta, bearing delta, time delta, cycle identifier).
- **FR-008**: Each event schema that involves a categorical decision MUST record that decision's value as its human-readable name (the name of the category, not an opaque numeric code).
- **FR-009**: Every per-vehicle record (Snap, Lerp) MUST carry the identifier of the Cycle it belongs to, so that per-vehicle telemetry can be correlated with the cycle summary.
- **FR-010**: A persistence failure or destination outage MUST be contained within the sidecar (logged and surfaced through self-telemetry) and MUST NOT propagate to or interrupt the data-processing loop.
- **FR-011**: The sidecar MUST detach from the notification mechanism cleanly on worker shutdown so that shutdown is not blocked.
- **FR-012**: The sidecar MUST emit operational self-telemetry about its own health — at minimum buffer occupancy, count of dropped (shed) records, and persistence-failure occurrences — by including these as additional columns on the **Cycle** record (no separate health dataset), so an operator can confirm it is keeping up in production using the same per-cycle query.
- **FR-013**: All source files added for this feature MUST be consolidated under a single dedicated Logging area within the data-worker so the logging concern is self-contained and discoverable.
- **FR-014**: The Event / event-data separation introduced by this feature MUST follow the same structural pattern already used for the application's existing in-process notification events, for consistency across the codebase.

### Key Entities *(include if data involves)*

- **Logging Event (envelope)**: A marker for "something happened that should be logged." Carried over the in-process notification bus; the sidecar inspects it and persists only recognized logging events. Specializes into Snap, Lerp, and Cycle variants.
- **Snap Record**: Per-vehicle snap decision. Holds route data (route number, route position), bus data (bus number, position, speed, bearing), a position delta (timestamp, cycle identifier), and a categorical snap outcome recorded by name.
- **Lerp Record**: Per-vehicle delta for motion interpolation. Holds prior route data, prior bus data, and a bus delta (position delta, speed delta, bearing delta, time delta, cycle identifier).
- **Cycle Record**: Per-pass summary. Holds the cycle identifier, timing (start, end, execution duration), per-outcome bus counts (processed, moved, unchanged, stationary, stale, skipped-no-route-id, skipped-unknown-route), feed-header timestamp, duplicate-feed flag, and cache sizes (last-update cache, vehicle-state cache). Also carries the sidecar's own health columns: telemetry-buffer occupancy, dropped (shed) record count, and persistence-failure count (per FR-012).
- **Notification bus**: The in-process publish/subscribe mechanism through which data-processing components post logging events and the sidecar subscribes to receive them, decoupling producers from the persistence pathway.
- **Telemetry buffer**: The bounded in-memory queue that absorbs bursts between event capture and durable persistence, with newest-record load-shedding when full.
- **Durable telemetry store**: Azure Blob Storage holding parquet files. Organized as three independent datasets — `snap/`, `lerp/`, `cycle/` — each partitioned into daily folders (`dt=YYYY-MM-DD/`) containing the 5-minute part-files flushed during that UTC day. Read downstream by the `telemetry-query-tool` (DuckDB + Azure extension) by globbing a dataset's day folder.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Enabling the sidecar adds no measurable latency to a processing cycle — median and 95th-percentile cycle execution time with the sidecar enabled are within normal run-to-run variance of the same worker with it disabled.
- **SC-002**: Under sustained normal feed volume, 100% of completed cycles produce exactly one durably persisted Cycle record with counts that reconcile to the worker's own processing totals.
- **SC-003**: When the durable destination is forced offline for the duration of multiple cycles, the worker completes every processing cycle with zero hot-path errors attributable to logging, and resumes persisting telemetry once the destination recovers.
- **SC-004**: When telemetry is produced faster than it can be persisted, memory used by the buffer stays bounded (does not grow without limit), and the number of shed records is reported through self-telemetry.
- **SC-005**: An operator can, after a run, query the persisted telemetry **with the existing `telemetry-query-tool`** to answer "how many buses were stale / skipped / moved in cycle N?" and "what route point was vehicle X snapped to in cycle N?" without inspecting live logs — by globbing a single day's `dt=YYYY-MM-DD/` partition.
- **SC-007**: At most one 5-minute flush interval of telemetry is at risk of loss on an ungraceful crash; durably written parquet part-files for prior intervals remain valid and independently queryable.
- **SC-006**: Worker shutdown completes promptly (no indefinite hang) whether or not the durable destination is reachable.

## Assumptions

- **Reuse of the existing notification pattern**: The in-process event/event-data abstraction is modeled directly on the application's existing notification-service pattern (a notification service that raises an event-received signal carrying a marker event-args type). This feature does not introduce a new messaging paradigm.
- **Scope is the TransitDataWorker only**: The sidecar lives inside the data-worker process. It is not a separate deployable service, and it does not change the SignalR publishing path that streams data to clients.
- **Durable store is parquet in Azure Blob**: Persisted telemetry is parquet flushed to Azure Blob Storage and read by the existing `telemetry-query-tool` (DuckDB Azure extension). The query tool is a fixed downstream consumer; parquet column names/types and the `dt=YYYY-MM-DD/` partition layout form the contract it depends on. Standing up a different storage technology is out of scope.
- **Daily UTC sharding**: "Daily" partitions are keyed on **UTC** date (`dt=YYYY-MM-DD/`), consistent with the worker's UTC timestamps; local-time sharding is not used.
- **Load-shedding over back-pressure**: Protecting the real-time processing loop takes precedence over capturing every single telemetry record; under overload the newest excess records are dropped and counted rather than slowing producers.
- **Shutdown dredge is best-effort**: On shutdown the sidecar detaches cleanly and does not block; it attempts one best-effort final parquet flush, but buffered records remaining after that may be lost, which is acceptable for diagnostic telemetry (at most the current sub-5-minute interval — see SC-007).
- **Self-telemetry placement**: Resolved — the sidecar's own health metrics ride on the Cycle record as additional columns (FR-012); no separate health dataset.
- **Telemetry is non-PII operational data**: Captured records describe vehicle positions and processing decisions for an operational/diagnostic purpose; no special retention or privacy handling beyond existing practice is assumed.
