# Observed Route Hour Statistics v1

**Definition**: `observed-city-route-hour-statistics-v1`  
**Status**: Planning contract; [SQL recipes](route-hour-insights.sql) require implementation and real PostgreSQL verification before operator use.

## Capture and operator contract

The existing `HistoricalStatistics.Enabled`, `DryRun`, and host-resolved `Cities` govern both city history and route capture. Empty city selection retains the existing fallback to configured cities. Global disable wins over dry run. Enabled dry run captures/validates/reports without route writes. Enabled persistence retains whole finalized city/hour cohorts. Category-insights enablement is unrelated. Limits/cadence alone live under `HistoricalStatistics:RouteHours`; no independent route enablement, dry-run, city selector or exclusion list.

The existing host still requires its configured database and source credentials when history is enabled, including dry run. Route capture does not reread Grafana or use the city's collector grace/overlap settings for hourly bucketing. Standalone persistence must have an explicitly provided real sink.

`IRouteHourStatisticsSink.TryEnqueue(FinalizedRouteHourStatisticsBatch)` returns immediate admission success/failure. Success means memory admission, not retention. Envelopes are nonempty aggregate-only immutable rows for one city/UTC hour/run, sorted by ordinal route key. The Data store returns Inserted, Unchanged or Quarantined; writer diagnostics separately report rejection, retries, exhausted failure, dry-run validation and shutdown-undrained work. Never send recyclable event lists, vehicle/trip/listener IDs, raw feeds, credentials or exception text.

## Query inputs and result sets

The initial analyst interface is parameterized internal read-only SQL, with no public endpoint. The accompanying SQL produces one JSON result containing related sets in a single statement snapshot.

| Parameter | Type | Contract |
| --- | --- | --- |
| `$1` city | text | Valid canonical configured or retained city slug; preserve key identity |
| `$2` route keys | text[] or NULL | Nonempty distinct ordinal keys for explicit selection; NULL discovers all retained keys overlapping the range. An empty array is invalid, not shorthand for all. |
| `$3` from UTC | timestamptz | Inclusive exact requested bound |
| `$4` to UTC | timestamptz | Exclusive finite bound, strictly greater than from |
| `$5` supported definition | text | `observed-city-route-hour-statistics-v1`; incompatible stored definitions remain visible but cannot contribute |
| `$6` local zone | text | Configured/validated IANA zone; defaults to the city's configured zone, not server/session zone |

Validate bounds/keys/definition/zone before executing; use parameters, never string-built SQL. Range/resource limits are the established internal query caller's responsibility; the recipe does not create a server endpoint. Inputs and SQL are not migration commands.

**Result sets**:

- `request`: exact bounds, city, selected/discovered keys, definition, zone, and unknown-historical-membership notice for all-route discovery.
- `city_hour_coverage`: intersected UTC hour grid, full-containment flag, retained cohort size, missing/quarantine evidence; detects entirely missing windows even when all-route discovery finds no keys.
- `hourly`: every selected route/intersected-hour pair with stored status, effective coverage, full-containment flag, definition compatibility, metadata/fingerprint, all counters/evidence, units, and definitive measures only when eligible. Missing pairs remain explicit with null stored data.
- `route_coverage`: requested/fully-contained/contributing counts; Missing, Partial, NoData, Conflict and excluded-definition counts; earliest retained route hour, without a continuity claim. Definition compatibility and boundary containment are separate from stored/effective coverage.
- `periods`: per-route and stored definition/cadence partition; numerator/denominator sums, peak, complete contributing-hour count and derived measures. A route with no retained policy still has a zero-contributor/null-means placeholder; route coverage remains the companion for whole-request completeness.
- `typical_local_hours`: per route/definition/cadence/local hour-of-day, summed published opportunities and complete contributing UTC hours with their ratio. Individual local dates/offsets/UTC keys are in `hourly`; no fabricated skipped DST hour.

The UTC grid includes hours intersecting the exact request. Only fully contained hours can contribute definitive results. Boundary context is whole-hour diagnostic context, with exact original bounds; never prorate. A supported definition does not erase different stored definitions from coverage.

## Coverage and historical membership

| Effective coverage | Meaning | Definitive contribution |
| --- | --- | --- |
| Complete | Stable cohort, healthy boundaries/gaps and all eligible cycles, intact capture/populations | Yes, only fully contained and compatible definition/cadence |
| Partial | Useful eligible observations with incomplete evidence | No; diagnostics only |
| NoData | Known cohort but no eligible activity/publication samples | No; diagnostics only |
| Conflict | Original immutable city/hour cohort quarantined by a differing finalized producer/cohort | No |
| Missing | No retained row for requested route/hour | No; never zero |

Queries conservatively treat any quarantine in a city/hour as Conflict for every retained row of that cohort. The writer normally sets all row flags atomically. Complete is an observed capture claim, not a durable minute ledger or transit service guarantee.

Explicit keys remain queryable with entirely absent history. NULL/all-route keys are discovered only from retained rows intersecting the range, including removed historical routes. A discovered route may have Missing pairs when membership cannot be established for that hour; Missing does not claim that service existed or was absent. Today's catalog never fills missing historical membership. City-level coverage remains present even if no keys are discoverable.

## Measures and units

| Measure | Numerator / denominator over contributing hours | Units / caveat |
| --- | --- | --- |
| Mean observed active vehicles | summed active count / eligible active samples | vehicles observed per collection sample; stationary vehicles count |
| Peak observed active vehicles | maximum hourly peak | vehicles per sample |
| Processed observations | summed processed count | updates, including duplicate processing, not unique vehicles |
| Stale fraction | summed stale / summed processed | ratio, not percent unless explicitly converted |
| Accepted movement | summed accepted distance | meters along resolved route; not completed-trip distance |
| Movement per route-vehicle-hour | summed meters / summed intact hourly route populations | meters per observed route-vehicle-hour; transfers can contribute to multiple routes |
| Movement per accepted update | summed meters / summed accepted interval count | meters/update; fresh stationary zero intervals count |
| Published cadence | summed known published records / complete contributing UTC hours | published opportunities/hour, not audible notes |
| Suppression diagnostics | observation counts by existing reason | observations suppressed, not estimated notes lost |

Use numeric division and null for zero-denominator means. Never average displayed hourly averages, sum hourly distinct values and label them period/city uniqueness, extrapolate partial hours, or allocate city feed timing/wire bytes/memory to routes. Missing means absence of evidence, while complete inactive route samples are known zeros.

## Local time and consistency

UTC keys never change. Retain UTC identity, local date/hour, and offset for individual results. Repeated DST local hours contribute two distinct UTC hours; skipped hours create no invented rows. Typical-hour cadence divides summed published records by complete contributing UTC hours, not by calendar days or number of distinct local labels.

The provided one-statement recipe uses one snapshot. If split into separate statements in implementation, execute related coverage/measures under one read-only repeatable-read transaction. Recheck quarantine within that snapshot; a separate prior read of coverage cannot authorize later measures.

## Reference acceptance examples

- Canonical catalog fixture: city `atlanta`; raw aliases `R17`, `r17`, and `shape-a` resolve to the single ordinal canonical key `Route-A`; contributing static IDs are `shape-b` and `shape-a`; normalized category is `bus`; ordered points are `(33.749,-84.388)`, `(33.75,-84.387)`. Alias count and input ID order do not add rows or alter the fingerprint. The distinct key `route-a` remains a separate ordinal route. If case-insensitive auxiliary geometry cannot distinguish both keys, report `route_index_unavailable` for that capture rather than merging them.
- Fingerprint encoding `route-catalog-v1`: UTF-8 bytes; every string is preceded by its unsigned 32-bit big-endian byte length. Field order is version, canonical key, normalized category, static-ID count, ordinal-sorted distinct static IDs, point count, then each ordered point's latitude and longitude as invariant-culture round-trip (`R`) strings. Counts are unsigned 32-bit big-endian integers. Coordinates must be finite. For the preceding fixture, the exact input bytes in hexadecimal are `00000010726f7574652d636174616c6f672d763100000007526f7574652d4100000003627573000000020000000773686170652d610000000773686170652d62000000020000000633332e373439000000072d38342e3338380000000533332e3735000000072d38342e333837`; SHA-256 is `3ad7499f2cca2bfa43575267b7ed839a8d972a9a90b8e1a50818e39cc972a6d7` (lower-case hex). Reordering contributing IDs yields the same bytes; changing point order changes them.
- Healthy-empty fixture: for the two canonical keys `Route-A` and `route-a`, one eligible healthy empty cycle yields one valid active sample per route with active sum and peak zero, processed/stale/movement/crossing/publication/suppression counts zero, and an intact distinct route-vehicle population of zero. Movement means with zero denominators are null. An unhealthy or missing cycle cannot supply these zero samples.
- Hour A: active sum 20 / 2 samples; B: sum 9 / 3 samples. Period mean is 29/5 = 5.8, not (10+3)/2 = 6.5.
- Complete inactive route: positive eligible samples, active/processed/crossing totals zero, intact distinct population zero; zero-denominator movement means are null.
- 100 meters across two route-vehicle-hours yields 50 meters/route-vehicle-hour; it does not imply two unique vehicles in the period or a 100-meter completed trip.
- Request 10:30-12:15 UTC exposes 10:00, 11:00 and 12:00 context; only 11:00 can contribute definitive hourly data.
- A conflicting extra route or a different capture run quarantines the original entire city/hour cohort; it never inserts the extra route or merges counts.
- An entirely missing all-route range returns no invented route identities, an explicit city-hour Missing grid, and empty route arrays. Explicitly supplied keys instead receive Missing route/hour pairs and null means.
- On 2026-11-01 in `America/New_York`, complete UTC hours beginning 05:00Z and 06:00Z both map to local 01:00, with offsets -04:00 and -05:00. Both contribute independently to the typical-hour numerator and denominator. On 2026-03-08, 06:00Z maps to 01:00 -05:00 and 07:00Z maps to 03:00 -04:00; no local 02:00 row is synthesized.

Correctness, database execution, performance and operational acceptance remain implementation tasks. No planning artifact claims deployed route history exists.
