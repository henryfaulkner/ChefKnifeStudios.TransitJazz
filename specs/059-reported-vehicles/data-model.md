# Data Model: Reported Vehicles v1

Proposed fields below; existing statistics tables remain unchanged. Canonical city/hour identity uses UTC-aligned timestamptz; preserve exact ordinal keys with deterministic C collation on identity text. No trimming/case normalization of vehicle values. Whitespace-only strings are unidentifiable.

## In-memory contracts (Worker)

- `ReportedVehicleIdentity`: namespace, kind (vehicle-descriptor-id or certified agency-train-id/fallback kind), exact value. City belongs to its envelope. Validate namespace/kind <=64 UTF-8 bytes, value <=512, nonblank, no PostgreSQL-invalid NUL. Oversized/unrepresentable values indicate identity loss, not ordinary missing ID.
- `ReportedVehicleSourceDefinition`: source key, namespace, permitted identity rule/version, expected cadence/gap policy. Source key uniquely corresponds to enabled endpoint; stable source fingerprint includes identity rules and cadence, excludes URL/credential. Explicit membership reaches generic/MARTA/NYC adapters and both hosts.
- `ReportedVehicleSourceObservation`: source key, receipt UTC, outcome success/empty/failure, immutable bounded distinct identities, raw vehicle-record count, unidentified/fallback counts, integrity indicator and bounded limitation codes. Includes raw successful data despite downstream map failure. Failure contains no inferred empty population.
- `FinalizedReportedVehicleHourBatch`: one semantic header plus complete deduplicated membership population; validates item/byte caps, supported policy, header/member count and uniqueness before admission.

## `public.city_reported_vehicle_hours`

| Field | Type / invariant |
| --- | --- |
| city_slug, hour_start_utc | Composite PK; city <=64 characters, nonblank; aligned UTC hour |
| definition_version | Nonblank <=64; reported-vehicles-v1 |
| capture_run_id | uuid, new per process/capture start |
| source_set_fingerprint, cadence_policy_fingerprint | Nonblank stable digests, supported server policy |
| collection_status | Complete, Partial, NoData; conflict/loss are independent exclusion flags |
| identity_integrity | boolean; false excludes entire hour |
| has_conflict | boolean default false, sticky; only store sets true |
| membership_count | bigint nonnegative, <=configured cap; equals actual complete membership rows |
| successful_poll_count, failed_poll_count | bigint nonnegative summed diagnostic counts |
| unidentifiable_record_count, fallback_record_count | bigint nonnegative; counts records across responses, not distinct unidentified vehicles |
| expected_source_count | integer 1..32 |
| source_diagnostics | bounded jsonb array of exact expected source keys, namespace/rule, first/last successful receipt, predecessor/successor witnesses, success/failure counts, max gap, boundaries, unidentified/fallback counts and reasons |
| incomplete_reasons, identity_limitations | bounded text arrays of allowlisted codes |
| observed_start_utc, observed_end_utc | nullable receipt bounds; half-open hour identity assignment; source successor witnesses may lie beyond hour |
| persisted_at_utc | UTC server write timestamp; excluded from semantic retry equality |

Complete requires every source complete, supported/stable policies, identity integrity, no missing IDs or failures, valid boundary continuity and matching memberships. Documented fallback limitations may remain even when observed-identity cadence is complete. NoData means no successful response and no memberships; it never establishes zero.

## `public.city_reported_vehicle_hour_memberships`

| Field | Type / invariant |
| --- | --- |
| city_slug, hour_start_utc | Composite FK to header, ON DELETE CASCADE |
| identity_namespace, identity_kind, identity_value | Exact ordinal text; nonblank; <=64, <=64, <=512 UTF-8 bytes respectively |

Composite PK is all five fields, providing bounded city/hour prefix reads and exact population deduplication. Validate index key byte envelope under real PostgreSQL fixture; do not hash away equality. Table contains no route, trip, coordinate, raw feed or credential. Add cleanup-support hour index on headers if measured plan requires it; no unsolicited new indices on old history tables.

## Atomic state transitions

1. Open current city/hour and predecessor grace-hour are mutable, bounded Worker state. Raw responses add identities to their receipt hour only; successor updates predecessor continuity metadata only.
2. Finalize immutable Complete/Partial/NoData envelope after boundaries/deadline; identity-loss envelope retains exclusion diagnostic and no claimed usable population.
3. Enqueue accepted envelope without blocking. Rejected/failed delivery emits diagnostics and yields missing database coverage; no zero substitution.
4. Store acquires reported-specific city/hour advisory lock in read-committed transaction and reads existing envelope after lock. Missing => insert header and all chunks atomically. Semantic equality => unchanged. Any differing payload/run => set sticky conflict. Never union or replace. Conflict cannot be cleared by exact retry.
5. Expired header is deleted in bounded retention transaction; cascade removes memberships. No active Stats-window deletion.

Clock regression/config mutation marks affected open cohorts excluded; startup without predecessor continuity yields Partial; shutdown without successor continuity yields Partial. Restart never upgrades an older partial cohort. Diagnostics can persist invalid/lost evidence only if bounded; missing evidence is the fallback when storage cannot be reached.

## Public summaries (Shared)

`StatsReportedVehicleSummary` (independent of StatsCoverage): window today/last24; nullable long count; state complete/partial/unavailable/pending; scope cityFeeds/sumOfCityFeeds; definition version; expected/contributing/fullyCovered city-hours; expected/contributing city counts; per-city bounded coverage; allowlisted limitations/exclusions. Counts within JavaScript safe integer range; null explicitly serializes.

Per-city coverage carries city slug, existing window reference, count, state, expected/contributing/fully-covered hours, boundary-excluded hours, missing/excluded reason counts, contributing UTC bounds, record-based unidentified/fallback diagnostic counts and source limitations. Original per-city date/zone/window boundaries remain in StatsCityWindow and are referenced, not recalculated. Sources may appear only as approved public keys/limitation codes, never URLs/raw identities.

Expected city-hours count every server-generated eligible city/hour. Contributing counts intact cohorts with a successful raw observation; fully-covered counts those also Complete. All sums per-city populations and denominators. Missing identity diagnostics describe records, not a guessed number of omitted distinct vehicles.
