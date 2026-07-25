# Feature Specification: Telemetry Denormalization

**Feature Branch**: `038-telemetry-denormalization`  
**Created**: 2026-07-11  
**Status**: Draft  
**Input**: User description: "docs/telemetry-denormalization.md — replace the three separate telemetry datasets (snap/lerp/cycle) with one denormalized telemetry table discriminated by an event_type column, introducing two new event types (PerCityCycle and FullCycle) with memory and cache-size diagnostics."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Inspect per-city processing health each tick (Priority: P1)

As the operator of the transit data worker, I want every city to record exactly one health-and-performance row every processing tick — whether that city succeeded, failed with an error, or was skipped because its route data wasn't ready — so I can see when and why a city is unhealthy instead of that failure being silently invisible.

**Why this priority**: This is the core value shift of the feature. Today, a city that throws an exception or isn't ready produces no telemetry at all, so the most important failures are the ones that leave no trace. Making every city emit a row every tick, tagged with a clear healthy/unhealthy signal, is the single most valuable outcome and is independently useful even without the worker-wide summary.

**Independent Test**: Run the worker against a city and force each of the three outcomes (normal run, thrown exception, not-ready route data). Confirm that a per-city row is recorded for every tick in every case, with the health indicator set correctly (healthy on normal runs including empty feeds, unhealthy on exception and not-ready).

**Acceptance Scenarios**:

1. **Given** a city processes normally with vehicles present, **When** the tick completes, **Then** one per-city row is recorded with the health indicator set to healthy and populated counts for tones emitted and vehicles processed.
2. **Given** a city processes normally but its feed is empty, **When** the tick completes, **Then** one per-city row is recorded with the health indicator still set to healthy and a vehicles-processed count of zero.
3. **Given** a city throws an exception during processing, **When** the tick completes, **Then** one per-city row is still recorded with the health indicator set to unhealthy and only cheaply-available diagnostics (memory and cache sizes) populated.
4. **Given** a city's route data is not yet ready and processing is skipped, **When** the tick completes, **Then** one per-city row is still recorded with the health indicator set to unhealthy.

---

### User Story 2 - Inspect a single worker-wide summary per tick (Priority: P2)

As the operator, I want one summary row per processing tick that aggregates across all cities that tick — total time, overall health, totals of tones/vehicles/cache sizes, the count and names of cities processed, plus process-wide memory — so I can answer "how is the whole worker doing this tick" with exactly one row, no matter how many cities are configured.

**Why this priority**: This gives a fast, flat, worker-level view that stays a single row per tick as the city list grows. It builds on Story 1's per-city data but delivers distinct value (the roll-up), so it's valuable on its own once per-city rows exist.

**Independent Test**: Run the worker across multiple cities and confirm exactly one summary row is recorded per tick regardless of the number of cities, with totals matching the sum of that tick's per-city values and a cities-processed list naming each city.

**Acceptance Scenarios**:

1. **Given** the worker processes N cities in a tick, **When** the tick completes, **Then** exactly one summary row is recorded for that tick (not N).
2. **Given** the per-city rows for a tick report certain tones-emitted and vehicles-processed counts, **When** the summary row is recorded, **Then** its tones-emitted and vehicles-processed values equal the sum across those cities.
3. **Given** the worker processed a specific set of cities in a tick, **When** the summary row is recorded, **Then** it lists the count of cities processed and a comma-separated list of their names.

---

### User Story 3 - Add a new telemetry field with a near-one-line change (Priority: P2)

As the sole maintainer, I want adding a new telemetry field to require changing essentially one place on the recording side (plus the query allow-list), instead of the five separate places required today, so that extending telemetry stops being a chore that discourages instrumentation.

**Why this priority**: This is the maintainability motivation behind the denormalization. It's a developer-experience outcome rather than an end-user runtime behavior, so it ranks below the two data-visibility stories, but it's a primary reason the feature exists.

**Independent Test**: Add a hypothetical new nullable field to the telemetry record and confirm the recorded output includes the new column with correct values/nulls without having to edit a separate column-name list, a per-type buffer switch, or a hand-built schema.

**Acceptance Scenarios**:

1. **Given** the single telemetry record, **When** a new nullable field is added to it, **Then** the recorded output automatically includes a matching column with the established naming convention and no separate schema or column-name list has to be edited to make it appear.
2. **Given** a field is present on one event type and absent on another, **When** rows are recorded, **Then** the field is populated on the applicable type's rows and empty (null) on the other type's rows.

---

### User Story 4 - Query one unified telemetry dataset by event type (Priority: P1)

As an analyst using the telemetry query tool, I want to query a single telemetry dataset and scope results by event type, so I can look at per-city rows or worker-summary rows through one consistent interface instead of picking among three separate datasets.

**Why this priority**: The recorded data is only useful if it can be queried. Consolidating to one dataset with an event-type filter is what makes the new shape usable, and it must work from day one alongside Story 1, so it is also P1.

**Independent Test**: Record telemetry, then query the single dataset filtering on each event type and confirm the correct rows return, with fields that don't apply to a given event type showing as empty (null).

**Acceptance Scenarios**:

1. **Given** recorded telemetry, **When** the analyst queries the single dataset filtering to the per-city event type, **Then** only per-city rows are returned and worker-summary-only fields are empty on those rows.
2. **Given** recorded telemetry, **When** the analyst queries filtering to the worker-summary event type, **Then** only summary rows are returned and per-city-only fields are empty on those rows.
3. **Given** a query references any of the defined telemetry fields, **When** the query is validated, **Then** it is accepted; and a query referencing an undefined field is rejected.

---

### Edge Cases

- **All cities fail in a tick**: Each city still records a per-city row marked unhealthy, and the single summary row still records for that tick with its overall-health indicator reflecting the failures.
- **Zero cities configured to emit telemetry**: No per-city rows are produced; the summary row behavior for an empty city set follows the same one-row-per-tick rule with zeroed totals and an empty city list. (Only one city emits telemetry today, so this is a forward-looking case.)
- **Empty feed vs. failure**: An empty feed is explicitly healthy (nothing failed, nothing to do), distinct from a failure; feed staleness is reported in its own freshness field, never folded into the health indicator.
- **Fields not applicable to an event type**: Always recorded as empty (null), never fabricated or defaulted to a misleading value.
- **Memory sampled once per tick**: Process-wide memory figures are captured once per tick and reused unchanged on every row for that tick; they are not summed across cities (memory is not partitionable per city).
- **Loss of per-vehicle detail**: After this change, telemetry can no longer answer per-vehicle questions (e.g., "which vehicle had a bad GPS snap" or "how far did vehicle X move"). This is an accepted, intentional scope reduction.
- **Query tool has no aggregation**: The query interface returns raw rows only (no server-side aggregation), which is precisely why the summary row is kept separate from per-city rows — to avoid duplicating summary fields across every city's row each tick.

## Requirements *(mandatory)*

### Functional Requirements

#### Unified telemetry record & recording

- **FR-001**: The system MUST record all telemetry as rows in a single unified dataset, replacing the three previously separate datasets.
- **FR-002**: Every telemetry row MUST carry a common set of fields present on all rows: an event-type discriminator, a per-row identity, and an observation timestamp.
- **FR-003**: The event-type discriminator MUST take one of two values: a per-city value and a worker-summary value.
- **FR-004**: Fields that apply only to a specific event type MUST be recorded as empty (null) on rows of the other event type; the system MUST NOT fabricate or default such fields to misleading values.
- **FR-005**: The system MUST retire the three previous event shapes and their separate outputs entirely; this is a full replacement, not an addition alongside the old design.
- **FR-006**: Adding a new telemetry field MUST require changing essentially one place on the recording side (the unified record), without editing a separate column-name list or a per-type buffer switch or a hand-maintained schema, and the recorded column name MUST stay in sync with the field automatically.
- **FR-007**: The system MUST preserve the existing load-shedding, buffered-flush, and shutdown-flush behavior of the telemetry pipeline unchanged in observable outcome (records may be dropped under load rather than blocking the processing path; buffered records are flushed on a periodic interval and best-effort on shutdown).

#### Per-city event

- **FR-008**: The system MUST emit exactly one per-city row for each telemetry-emitting city on every processing tick, regardless of whether that city succeeded, failed with an error, or was skipped as not-ready.
- **FR-009**: A per-city row MUST include: the city name, time taken, a health indicator, feed freshness, tones emitted, vehicles processed, and the memory and cache-size diagnostics defined below.
- **FR-010**: The health indicator on a per-city row MUST be set to unhealthy when processing threw an exception, unhealthy when the city's route data was not ready and processing was skipped, and healthy when the city ran normally.
- **FR-011**: An empty feed MUST be treated as healthy (vehicles processed may be zero), not as a failure; feed staleness MUST be reported through the feed-freshness field and MUST NOT affect the health indicator.
- **FR-012**: On per-city failure paths (exception or not-ready), only cheaply-available diagnostics (memory and cache sizes) MUST be populated; processing-derived fields (tones emitted, vehicles processed, feed freshness) MUST be zero or empty since no processing occurred.

#### Worker-summary event

- **FR-013**: The system MUST emit exactly one worker-summary row per processing tick, independent of the number of cities processed that tick.
- **FR-014**: A worker-summary row MUST include: time taken, a health indicator, tones emitted, vehicles processed, count of cities processed, a comma-separated list of city names processed, and the memory and cache-size diagnostics defined below.
- **FR-015**: On the worker-summary row, tones emitted, vehicles processed, and every cache-size field MUST be the sum of the corresponding values across that tick's cities.
- **FR-016**: The count of cities processed and the comma-separated city-name list MUST reflect the cities processed in that tick; these fields apply only to the worker-summary event type.

#### Memory & cache diagnostics

- **FR-017**: The system MUST record two distinct process-wide memory signals — a managed-heap figure and an operating-system resident-set figure — rather than a single combined memory number.
- **FR-018**: The two memory figures MUST be sampled once per tick and reused unchanged on every row recorded for that tick; they MUST NOT be summed across cities.
- **FR-019**: The system MUST record a cache-size field for every in-memory per-city cache the worker maintains keyed by city, read per-city on per-city rows and summed across cities on the worker-summary row.
- **FR-020**: The system MUST NOT carry forward the previous always-zero placeholder cache field; dead/fake telemetry with no real source MUST be dropped rather than migrated.
- **FR-021**: Caches whose contents are rebuilt in lockstep with another already-recorded cache (identical counts by construction) MUST NOT get their own redundant field.

#### Naming & semantics

- **FR-022**: The field previously conceived as a generic "points processed" count MUST be named to reflect its real downstream effect (tones emitted) and MUST be sourced from the count of detected trigger-point crossings for that tick.
- **FR-023**: Fields shared by name across both event types (time taken, health indicator, tones emitted, vehicles processed, and all memory/cache fields) MUST be a single field each with the same meaning, differing only in scope (per-city vs. worker-wide), not duplicated per type.
- **FR-024**: Recorded column names MUST continue to follow the established snake_case naming convention consumed by the query tool.

#### Query-side

- **FR-025**: The query tool MUST recognize exactly one valid telemetry dataset name after this change (replacing the previous three dataset names).
- **FR-026**: The query tool's allow-list MUST accept all fields of the unified record — the common fields plus every per-event-type field — as filterable columns, each with its correct value kind (string, numeric, boolean, or timestamp), and MUST reject any field not in that set.
- **FR-027**: The query tool MUST allow scoping a query to a single event type by filtering on the event-type discriminator, and this MUST be documented as the primary way to select one event shape.
- **FR-028**: The query tool's tokenizer/parser behavior (aside from the dataset name and column set) MUST remain unchanged.

#### Storage layout

- **FR-029**: Telemetry MUST be written to a single date-partitioned location using the same immutable part-file convention as before, without the previous per-dataset path segment.

#### Documentation & tests

- **FR-030**: The telemetry schema and query-guide reference documentation MUST be updated to describe the single unified table, the event-type discriminator, and the event-type filtering pattern, replacing the three-dataset descriptions.
- **FR-031**: Schema tests MUST be consolidated to a single test asserting the unified record's columns and types for both event types; the load-shedding, partition-path, and failure-isolation tests MUST be retained (re-pointed at the single-buffer, single-path service).

### Key Entities *(include if feature involves data)*

- **Telemetry Event**: The single wide record that every telemetry row represents. Carries common fields (event-type discriminator, per-row identity, observation timestamp) plus a set of nullable per-event-type fields. Discriminated into two shapes:
  - **Per-City Cycle**: One row per telemetry-emitting city per tick. Scoped to a single city; includes city name and feed freshness (exclusive to this type) alongside the shared metric and diagnostic fields.
  - **Full Cycle (Worker Summary)**: One row per tick across all cities. Includes the count and comma-separated names of cities processed (exclusive to this type) alongside the shared metric and diagnostic fields, with metric and cache fields summed across the tick's cities and memory fields reused verbatim from the tick's single sample.
- **Cache-Size Diagnostic**: A per-city measurement of the number of entries in each in-memory cache the worker keys by city; read per-city and summed on the worker summary.
- **Memory Signal**: A process-wide measurement captured once per tick in two forms (managed heap and OS resident set), reused unchanged across all of that tick's rows.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For every processing tick, each telemetry-emitting city produces exactly one per-city row — including ticks where the city errored or was skipped — so no tick outcome is invisible (100% of city-ticks accounted for, vs. failures currently producing zero rows).
- **SC-002**: For every processing tick, exactly one worker-summary row is produced regardless of the number of cities configured, so the summary row count per tick stays flat (always 1) as cities are added.
- **SC-003**: Adding a new telemetry field to the recording side requires editing only the single unified record (one place) rather than the five separate places required before, verifiable by adding a field and observing it appear in output with no other recording-side edits.
- **SC-004**: An analyst can retrieve either event shape from one dataset by filtering on the event-type discriminator, with non-applicable fields empty, and all defined fields are accepted by the query tool while undefined fields are rejected.
- **SC-005**: The worker-summary totals for tones emitted, vehicles processed, and each cache size equal the sum of that tick's per-city values, and the two memory figures are identical across every row for that tick.
- **SC-006**: Telemetry lands in a single date-partitioned location with one part-file appearing per flush interval when the worker runs, and both event shapes round-trip correctly (including empty/null values on non-applicable fields).
- **SC-007**: The previous three-dataset outputs and the always-zero placeholder field no longer appear in newly recorded telemetry.

## Assumptions

- **Query interface is filter-only**: The telemetry query tool performs filtering and returns raw rows with no server-side aggregation; the two-event-type design (rather than one merged per-city row) is chosen specifically to keep worker-summary fields from being duplicated across every city's row each tick.
- **Single telemetry-emitting city today**: Only one city currently emits telemetry, so multi-city summing and the empty-city-set case are validated as forward-looking behavior; the design must remain correct as more cities are added.
- **Per-vehicle granularity is intentionally dropped**: Per-vehicle GPS-snap and movement detail are removed with no partial-retention middle ground; telemetry after this change answers only city-level and worker-level questions. This is an accepted scope reduction, not an oversight.
- **Existing pipeline transport is reused**: The in-process event bus, bounded-channel load shedding, periodic flush, and shutdown flush are reused unchanged in structure; only the record shape, buffering (now single-buffer), schema generation (now reflection/attribute-driven), and output path (now single) change.
- **Memory is not partitionable per city**: Because multiple cities are processed in one process, process-wide memory is meaningfully sampled only once per tick and cannot be attributed to an individual city; it is therefore reused across rows rather than summed.
- **This feature supersedes prior telemetry specs**: The earlier logging-sidecar and transit-datasets specifications become historical; this feature replaces their outputs rather than amending them in place.
- **Language/localization not involved**: This is a backend telemetry/data feature with no end-user-facing UI copy, so no localization work is implied.
