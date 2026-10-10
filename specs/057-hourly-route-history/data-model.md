# Data Model: Hourly Route History

**Date**: 2026-10-10  
**Definition**: `observed-city-route-hour-statistics-v1`  
**Status**: Proposed schema and capture contracts; no migration is created or applied by planning.

## Persistent entity

`CityRouteHourStatistic` maps to `public.city_route_hour_statistics`. It is a focused aggregate, using the existing statistics pattern rather than the application entity's audit/soft-delete fields.

**Primary key**: `(city_slug, route_join_key, hour_start_utc)`  
**Secondary index**: `(city_slug, hour_start_utc, route_join_key)`  
**Relationships**: No foreign key to today's route catalog. The whole stored city/hour row set is its historical route cohort; no separate manifest, route dimension, or route-minute entity.

| Group | Fields and PostgreSQL types | Meaning |
| --- | --- | --- |
| Identity | `city_slug varchar(64)`, `route_join_key text`, `hour_start_utc timestamptz` | Canonical city; ordinal resolved route; aligned UTC start |
| Metadata | `route_short_name text NULL`, `static_route_id text NULL`, `category varchar(64)`, `route_catalog_fingerprint varchar(64)`, `catalog_changed boolean`, `definition_version varchar(64)` | First route metadata in this hour; single static ID only when unique; route content identity |
| Provenance | `capture_run_id uuid`, `persisted_at_utc timestamptz` | One run ID per process; database-generated retention timestamp |
| Coverage | `collection_status varchar(16)`, `has_conflict boolean`, `incomplete_reasons text[]`, `healthy_cadence_limit_seconds integer` | Complete/Partial/NoData capture claim; monotone quarantine and fixed reasons |
| Samples | `observed_cycle_count bigint`, `valid_active_sample_count bigint`, `valid_publish_cycle_count bigint`, `failed_cycle_count bigint` | Observations and eligibility, shared city-cycle evidence per route |
| Timing | `first_cycle_utc timestamptz NULL`, `last_cycle_utc timestamptz NULL`, `max_observation_gap_seconds numeric(20,6) NULL`, `start_boundary_ok boolean`, `end_boundary_ok boolean` | In-hour completion times and continuity including boundary gaps |
| Activity | `active_vehicle_count_sum bigint`, `peak_active_vehicle_count bigint`, `distinct_active_vehicle_count bigint NULL` | Per-cycle population sum/peak and exact hourly route population when intact |
| Processing | `vehicle_observations_processed_count bigint`, `stale_observations_count bigint` | Actual processing observations, not unique vehicles |
| Movement | `distance_meters_sum numeric(20,6)`, `distance_interval_count bigint`, `distance_rejected_count bigint` | Accepted absolute along-route movement, intervals including zero, rejected representatives |
| Crossings | `crossings_detected_count bigint`, `crossings_published_count bigint` | Actual detected records and records in known successful publications |
| Suppression | `crossings_suppressed_first_seen bigint`, `crossings_suppressed_delta_leq_zero bigint`, `crossings_suppressed_teleport bigint`, `crossings_suppressed_transfer bigint` | Observations receiving each live suppression reason |

Counter defaults are zero; flags default false; reasons default to an empty array. Nullable evidence is not replaced with a fabricated zero. `persisted_at_utc` uses the database `CURRENT_TIMESTAMP` default: transaction-start time of the successful retention attempt, not precise commit time. No vehicle/trip/listener ID, feed body, source credential, or exception text is stored.

## Canonicalization and validation

Producer DTO validation and Data validation enforce the same rules; schema checks enforce constraints expressible per row.

1. City is canonical/nonblank and <=64 characters; route key is nonblank and <=512 UTF-8 bytes (`octet_length` in the database), preserving case/spelling. Category uses the established normalized category convention, is nonblank and <=64 characters; static/display identifiers are metadata and are not used as keys.
2. Route-key equality is ordinal in capture/cohort comparison. Configure deterministic case-sensitive database key equality; do not use case-insensitive/nondeterministic collations. Catalog preflight rejects ambiguous geometry mappings rather than merging case-distinct keys.
3. Definition is nonblank and <=64 characters; fingerprint is 64 lower-case hexadecimal characters; run ID is nonempty. Reasons are nonnull, unique, ordinally sorted, and only the vocabulary below. Metadata and source values cannot be silently truncated.
4. UTC hour keys are aligned; all persisted times are UTC and canonicalized to microseconds before freezing/retry equality. Accepted meter deltas are rounded once to six places, away from zero, then summed without re-rounding observations.
5. Counts/distances/gaps are nonnegative and finite. Samples/failed cycles <=observed cycles; stale <=processed; published <=detected; peak <=activity sum; intact distinct >=peak. Checked accumulation overflow invalidates capture rather than wrapping.
6. With observed cycles, first/last times are present, ordered and within `[hour_start_utc, hour_start_utc+1 hour)`; with zero cycles both are absent. Cadence limit is greater than the configured interval and <=60 seconds; the row stores its applied policy.
7. Complete requires positive observations, active and publication samples equal to observed cycles, failed cycles zero, both proven boundaries, known maximum gap <=cadence, intact distinct population, stable catalog, and no incomplete reason. Rejected movement observations alone do not make activity coverage incomplete.
8. NoData requires zero eligible activity/publication samples, activity sum/peak, accepted movement/intervals and published crossings. Processing/detection/rejection/suppression diagnostics may be nonzero. Partial covers remaining incomplete observed cases. Startup, shutdown, catalog change and loss cannot become Complete even if counters otherwise look healthy.
9. Changed catalog rows share `catalog_changed=true` and incomplete coverage across the whole cohort. A lost exact population sets the distinct count null; never use an evicted set's count as exact. When honest cohort construction exceeds a route limit, discard the whole envelope.

**Reason vocabulary**: `startup_fragment`, `shutdown_fragment`, `source_failure`, `route_index_unavailable`, `processing_failure`, `publication_unavailable`, `boundary_unproven`, `gap_exceeded`, `catalog_changed`, `clock_regression`, `capture_failure`, `identity_limit_exceeded`.

No `covered_minutes` field exists. Continuity is a bounded capture claim rather than independently recoverable minute evidence or proof of service availability.

## Transient entities

| Entity | Contents | Lifetime / boundary |
| --- | --- | --- |
| `RouteHourCatalog` | Immutable ordinal canonical entries, alias resolution, contributing static IDs, actual ordered geometry fingerprints, cohort content token | Same snapshot as worker routing; never derive cohort from aliased index keys |
| Route cycle | Per-route counters, first eligible vehicle representative, transient identity sets, source/processing/publication evidence, initial content snapshot, once-only completion | One city pass; deduplicate activity only; enforce resource caps during recording |
| Movement state | Ordinal vehicle identity, resolved route/geometry token, finite along-route baseline, monotonic timestamp watermark, last observation time | Across hours; existing 20-minute pruning; explicit tracked-identity cap |
| City/hour accumulator | Route cohort union/first metadata, exact route population sets, additive counters, timing/outcome evidence, reasons and policy | At most current + boundary-pending hours; cap memberships across all routes |
| `FinalizedRouteHourStatisticsBatch` | City, aligned UTC hour, process run ID, ordinal-sorted immutable route rows | Queue/retention; aggregate-only, nonempty, validated, no recyclable event lists or vehicle identities |

The run ID is generated once per process and reused for all frozen rows. Cross-route transfers can enter two route-hour sets; their counts are not city uniqueness. All counters use the single completing city-cycle timestamp; movement's source time only establishes freshness.

## Lifecycle and capture states

`Collecting -> BoundaryPending -> Frozen -> Admitted -> Retained` is the healthy lifecycle. Deadline expiry or shutdown can freeze incomplete observed windows. Finalization is once-only; the independent <=15-second sweep cannot reopen sealed hours for a late completion. No infinite replay after pauses.

Frozen capture state is Complete, Partial, or NoData. Rejected admission/process loss/exhausted retries has no durable state and becomes Missing in queries. Retained capture values are immutable. `has_conflict` can only move `false -> true`; effective query state then becomes Conflict without changing the stored capture status.

Capture errors use a safe narrow instrumentation boundary and bounded loss evidence, including during begin/record/commit/refresh/prune/sweep/flush. If invalid identity or empty/unavailable catalog prevents constructing an honest cohort, report loss rather than persist a fake empty manifest.

## Whole-city/hour retention protocol

1. Validate every row and envelope identity, common run/hour, unique sorted keys, bounded row count, and compatible cohort-level evidence before opening a transaction.
2. Open a fresh context/connection/read-committed transaction. Apply transaction-local statement/lock timeouts and the total linked attempt cancellation budget.
3. In its own command, acquire `pg_advisory_xact_lock` for a stable feature-namespaced hash of canonical city and UTC hour. Hash length-prefixed strings and UTC timestamp with SHA-256, interpret the first eight bytes as a big-endian signed 64-bit key. Hash collisions only serialize unrelated envelopes. Every writer and later maintenance operation must honor this lock.
4. In a separate subsequent command, read the entire stored city/hour cohort. The separate post-lock lookup sees the preceding committed envelope after a wait; do not combine lock acquisition and lookup into one pre-wait snapshot.
5. No existing rows: insert all frozen sorted rows in bounded commands, then commit once. Any later-command failure rolls back every inserted row. An empty catalog cannot represent a stored envelope with this table-only design.
6. Identical cohort and producer payload: return Unchanged without replacing values. Equality excludes only generated persistence time and conflict flag; includes run, definition, metadata/fingerprint, ordered reasons, policy, all timing/flags/counts. Keep existing quarantine.
7. Any differing key set or payload: set `has_conflict=true` on every original city/hour row and commit quarantine. Preserve original values; no incoming route insertion, fragment merge, partial-field improvement, or replacement.
8. Retry recognized transient failures up to three whole attempts, same frozen payload, one/two-second delays. Each failed attempt disposes/rolls back before retry. Lost commit acknowledgement is resolved through identical retry; bounded exhaustion drops the whole envelope and reports loss.

Default budgets: five-second commands, 30-second entire attempt including connection/lock/chunks/commit, 15-second shutdown drain. No database wait runs on the worker. SQL reads use one statement snapshot or one read-only repeatable-read transaction for related statements so quarantine cannot be missed between coverage and measures.

## Migration and retention

Add exactly this table, checks, key and secondary index through one schema-only EF migration; update the existing snapshot and model-wide entity count from three to four. Preserve old tables/migrations and existing migration bundle/secret. No feed reads, backfill, seed observations, destructive cleanup or automatic deletion job. Future retention must remove whole city/hour cohorts using the same lock and disclose retained starting coverage.
