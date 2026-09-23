# Feature Specification: Historical Transit Statistics

**Feature Branch**: `055-database`  
**Created**: 2026-09-20  
**Status**: Draft  
**Input**: User description: "Create a statistical model in the TransitJazz data store for historical worker data, so future users can receive useful insights and stories about transit patterns. Every stored statistic must be reproducible from the current Grafana worker dashboard. Backdate the initial history from the metrics currently available there."

## Clarifications

### Session 2026-09-20

- Q: Should v1 retain worker-wide dashboard measures as contextual values on city rows? → A: Store only city-scoped metrics in v1; do not duplicate worker-wide values.

- Q: How should the historical statistics model be structured? → A: Store all statistics in one simple, deliberately denormalized table; do not apply complex normalization.
- Q: What should be the aggregation grain? → A: Store one row per city and source time bucket; make aggregations city-by-city.
- Q: What time bucket should each city row use? → A: One minute.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Preserve historical city patterns (Priority: P1)

An analyst can retrieve a city's historical transit activity, input quality, soundscape activity, and operational health for a chosen period from one straightforward historical-record format, instead of losing that context when the operational dashboard's short history expires.

**Why this priority**: Long-lived, trustworthy history is the foundation for every future insight or city story. Without it, the project cannot distinguish a one-off quiet period from a recurring pattern.

**Independent Test**: Import a known interval for one city, retrieve its observations for that interval, and compare each stored measure with the corresponding worker-dashboard value at the same source-aligned time.

**Acceptance Scenarios**:

1. **Given** the worker dashboard contains observations for a configured city during an available historical interval, **When** that interval is imported, **Then** the historical record preserves the canonical city identity, the source observation time, each approved measure, and whether an observation was unavailable.
2. **Given** historical city records are available, **When** an analyst selects a day or week, **Then** they can derive comparable transit-activity, input-quality, soundscape-activity, and health trends without relying on expired dashboard history.
3. **Given** a source measure has a meaningful zero value, **When** it is stored, **Then** it remains distinguishable from a missing, stale, or unavailable observation.

---

### User Story 2 - Backdate available dashboard history safely (Priority: P1)

A release owner can populate the new historical record with all worker-dashboard history that is still available at rollout, with clear proof of what was imported and what was no longer available.

**Why this priority**: The initial backfill is a time-sensitive opportunity. Once the operational source ages out, missing periods cannot be faithfully recreated.

**Independent Test**: Run the import for a bounded source period twice, compare the result with the dashboard at representative timestamps for every configured city, and inspect the import report.

**Acceptance Scenarios**:

1. **Given** a bounded period of dashboard history is available, **When** the historical import runs, **Then** it imports every returned source-aligned observation exactly once and records the requested and covered time ranges.
2. **Given** the same import is retried after a partial failure or an operator rerun, **When** it completes, **Then** it creates no duplicate observations and does not replace a confirmed value with a conflicting value without recording the discrepancy.
3. **Given** a requested source range contains a gap, a reset, or expired history, **When** the import completes, **Then** it reports the affected measure, city where applicable, and time range; it does not fabricate, interpolate, or infer records from a different source.

---

### User Story 3 - Trust the origin of every statistic (Priority: P2)

A product owner can see which approved dashboard measure and calculation produced each stored statistic, so future insight and story features do not present an unsupported claim as transit history.

**Why this priority**: Provenance keeps the new history trustworthy as dashboards, metric meanings, and future storytelling features evolve.

**Independent Test**: Select a stored city-specific field and verify that its source definition identifies the matching approved dashboard measure, calculation, unit, and time-bucket rule.

**Acceptance Scenarios**:

1. **Given** a statistic is stored, **When** a reviewer examines its source definition, **Then** they can identify the dashboard measure, calculation, unit, and observation interval used to produce it.
2. **Given** the dashboard definition changes or a source measure becomes unavailable, **When** new history would otherwise be collected, **Then** the system preserves the prior history, marks the affected collection as incomplete, and prevents silently mixing incompatible meanings.
3. **Given** a future insight groups or compares periods, **When** it uses the historical record, **Then** it can limit its calculation to records with compatible source definitions and known coverage.

### Edge Cases

- Dashboard history may already have expired before the first import; the result must record an empty or partial coverage result rather than synthesizing history.
- The worker or a city source may be unavailable for part of a requested period; no returned observation is not automatically a zero-value observation.
- Counters may restart and duration distributions may be represented as windowed aggregates; imported values must retain the approved dashboard calculation and time bucket rather than treating a reset or aggregate as a raw event.
- A city can be added, renamed, or removed; records must retain its canonical city identity at the observation time and must not merge distinct city identities.
- Source history can include delayed or repeated samples; the import must use deterministic source-aligned observation keys in the single statistics table and record discrepancies rather than duplicate data.
- A source query can fail after only part of a requested period has been processed; the completed portion must remain auditable and safely resumable.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST store all long-lived historical observations in one simple, deliberately denormalized, city-first statistics table. Each row represents one canonical city for one UTC minute; no separate worker-observation table is permitted.
- **FR-002**: Each row in the statistics table MUST retain the start of its UTC minute, canonical city identity, source-definition version, collection status, and the approved city-specific statistic values applicable to that city and minute.
- **FR-003**: The initial approved city-statistics catalogue MUST cover the current dashboard's city health; input fetch success, records, presence, timestamp knowledge, lag, and source failures; vehicles processed; tones emitted; published batch size; the four crossing-suppression classifications; city cycle rate, error rate, duration percentile, cycle age, and work age; and city cache and route-index counts.
- **FR-004**: The v1 catalogue MUST contain only city-scoped dashboard measures. The statistics table MUST NOT store or repeat worker-wide measures as contextual values on city rows.
- **FR-005**: Every stored statistic MUST be reproducible from an explicitly approved current worker-dashboard measure and calculation. The historical model MUST NOT introduce raw transit-entity identifiers, free-text diagnostic data, secret-bearing data, or a measurement that the dashboard cannot derive.
- **FR-006**: Each statistics row MUST identify the version of the published source-definition contract that describes its dashboard measures, calculations, units, one-minute rule, and zero-versus-unavailable behavior. The model MUST NOT split those definitions, import runs, or coverage gaps into separately normalized statistics tables.
- **FR-007**: The system MUST preserve source semantics: a true zero, a missing observation, a stale observation, a source error, and an unknown value MUST remain distinguishable wherever the underlying dashboard measure distinguishes them.
- **FR-008**: The system MUST continuously add new source-aligned historical observations after the initial import, without changing the worker dashboard's metric meaning, label set, retention policy, alerts, or operational purpose.
- **FR-009**: The initial historical import MUST request all currently queryable worker-dashboard history for every approved source series before that history expires, and MUST store one city row for each returned UTC minute.
- **FR-010**: The historical import MUST be resumable and idempotent. Reprocessing the same source definition, city scope, and observation time MUST result in one authoritative historical observation, with any conflict preserved as import evidence rather than silently discarded.
- **FR-011**: Each import execution MUST produce an auditable result that records the requested range, returned coverage, source-definition version and city scope processed, created and unchanged row counts, missing ranges, failures, and discrepancies, without adding a separate normalized import-history table.
- **FR-012**: The system MUST reconcile imported observations against representative dashboard results for every approved source definition and configured city where applicable, and MUST flag a mismatch outside the documented source-calculation precision.
- **FR-013**: When source history is unavailable, incomplete, or incompatible, the system MUST preserve all confirmed history, clearly mark the affected coverage, and MUST NOT reconstruct missing values from structured logs, legacy telemetry files, external feed payloads, or estimates.
- **FR-014**: Historical observations MUST support direct aggregation for one city by UTC day, week, and user-selected period for activity, soundscape, input-quality, and operational-reliability trends.
- **FR-015**: The v1 scope MUST provide the trustworthy historical model, ongoing collection, import evidence, and backdated records. Presenting end-user insight cards, narrative stories, recommendations, alerts, or changes to the existing dashboard is outside this feature's scope.

### Key Entities *(include if feature involves data)*

- **Historical Statistics Record**: The only persistent database entity in v1. One wide row represents one canonical city for one UTC minute. It carries only city-scoped statistic values together with simple scalar metadata needed to identify its source-definition version and collection status.
- **Source Definition Contract**: A published versioned reference that explains the dashboard measure, calculation, unit, time-bucket rule, and availability semantics of fields in the historical statistics record. It is not a separately normalized statistics table.
- **Import Result**: The auditable output of one historical backfill or retry, stating requested and covered ranges, counts, gaps, discrepancies, and outcome. It is not a separately normalized statistics table.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Before the available dashboard history expires, the initial import processes 100% of returned one-minute observations for every approved city-scoped source series and configured city, or creates a coverage gap explaining each unprocessed minute range.
- **SC-002**: For 100% of approved source definitions, representative imported values from at least three timestamps per applicable city match the corresponding dashboard result within the documented source-query precision.
- **SC-003**: Re-running a completed import for the same requested scope produces 0 duplicate rows in the single statistics table and a run report that identifies all observations as unchanged, reconciled, or discrepant.
- **SC-004**: Historical records retain sufficient coverage and provenance for an analyst to calculate daily and weekly activity, tones-emitted, input-quality, and health trends for every configured city with confirmed observations, after the dashboard's operational-history window has elapsed.
- **SC-005**: Every stored field in the v1 model has exactly one active source definition, and 100% of source definitions identify a dashboard measure, calculation, unit, time-bucket rule, and availability semantics.
- **SC-006**: No stored historical record contains a vehicle identifier, route identifier, feed payload, feed URL, credential, free-text exception, or unbounded diagnostic value.

## Assumptions

- The current worker dashboard is the authoritative source for the v1 statistic catalogue; the dashboard definitions stored with the project describe the intended calculation semantics.
- The operational metrics source is expected to expose a rolling 14-day history at rollout. The import preserves exactly the history actually returned and cannot recover data already outside that window.
- One UTC minute is the fixed source-aligned time bucket for this model. The feature does not claim individual worker-cycle or vehicle-event precision when the source cannot establish it.
- The v1 data model uses one wide statistics table rather than separate tables for worker observations, city observations, source definitions, import runs, or coverage gaps. Simple scalar metadata on each row is preferred over relational normalization.
- Every persistent statistics row and stored metric is city-scoped. Worker-wide dashboard values are excluded from the v1 catalogue so no city row duplicates a global value.
- The configured city set and canonical city identities remain bounded. A future city onboarding change updates the approved source catalogue and coverage expectations before it begins collecting history.
- The new history is intended to outlive the operational dashboard's short retention window; its detailed retention and access policies will be determined during implementation planning.
- Existing dashboard visualization and alerting continue to serve operations. This feature creates a durable, provenance-rich foundation for later user-facing transit insights and stories rather than replacing those operational tools.

## Dependencies

- The worker dashboard's approved measures and calculations remain queryable through the existing read-only metrics access path for the initial import and ongoing collection.
- The centralized-logging work remains a separate diagnostic path. Its sparse events can explain anomalies but are not an approved source for reconstructing historical statistics.
- A provisioned TransitJazz data store is available before the initial import begins.
