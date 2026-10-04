# Data Model: City and Transit Type Insights

**Contract**: `observed-city-category-statistics-v1`  
**Feature**: [spec.md](spec.md)

## Persistent entities

Add two entities to the existing `AppDbContext`, using explicit `IEntityTypeConfiguration<T>` mappings and no `BaseEntity`, generated IDs, audit columns, navigation properties, or new normalized tables. Preserve `CityMinuteStatistic` and its `worker-dashboard-statistics-v1` contract.

| Entity | PostgreSQL table | Composite primary key |
| --- | --- | --- |
| `CityCategoryMinuteStatistic` | `public.city_category_minute_statistics` | `(city_slug, category, stat_minute_utc)` |
| `CityCategoryHourStatistic` | `public.city_category_hour_statistics` | `(city_slug, category, hour_start_utc)` |

Use a dedicated `CategoryCollectionStatus` enum with exactly `Complete`, `Partial`, and `NoData`. Do not extend or reinterpret the older model's `CollectionStatus` enum. Worker aggregate DTOs use their own coverage enum; the WebAPI adapter maps statuses explicitly, keeping Worker independent of Data.

### Fields shared by minute and hour rows

| Column | C# / PostgreSQL type | Nullability and meaning |
| --- | --- | --- |
| `city_slug` | `string / varchar(64)` | Required canonical configured city |
| `category` | `string / varchar(64)` | Required lowercase configured category or observed `unknown` |
| `definition_version` | `string / varchar(64)` | Required; `observed-city-category-statistics-v1` |
| `collection_status` | enum / `varchar(16)` | Required capture completeness |
| `has_conflict` | `bool / boolean` | Required, default false; durable quarantine metadata |
| `healthy_cadence_limit_seconds` | `int / integer` | Required policy actually used for coverage; greater than cycle interval and at most 60 |
| `observed_cycle_count` | `long / bigint` | Required count of completed city-cycle observations, including failed cycles |
| `valid_active_sample_count` | `long / bigint` | Required count of eligible category activity samples, including covered zeros |
| `valid_publish_cycle_count` | `long / bigint` | Required count of known successful or eligible zero crossing outcomes |
| `failed_cycle_count` | `long / bigint` | Required count of cycles with invalid feed/route processing or failed/unknown publication; count a cycle once even with multiple failures |
| `first_cycle_utc` | `DateTime? / timestamptz` | Nullable only without a cycle observation |
| `last_cycle_utc` | `DateTime? / timestamptz` | Nullable only without a cycle observation |
| `max_observation_gap_seconds` | `decimal? / numeric(20,6)` | Nullable when no gap evidence exists; includes boundary evidence |
| `active_vehicle_count_sum` | `long / bigint` | Required sum across eligible active samples |
| `distance_meters_sum` | `decimal / numeric(20,6)` | Required sum of accepted absolute movement |
| `distance_interval_count` | `long / bigint` | Required accepted intervals, including stationary intervals |
| `distance_rejected_count` | `long / bigint` | Required ineligible movement candidates, including first sightings |
| `crossings_published_count` | `long / bigint` | Required count of crossing records after successful publication |

Numerator fields contain sums of accepted observations; their internal zero does not establish availability. Status, sample counts, and coverage must govern interpretation. For `NoData`, output measures are unavailable even when internal sums equal zero. Successful crossing numerators may survive a partial-source cycle diagnostically; activity samples from that cycle are ineligible.

### Minute-only field

| Column | Type | Meaning |
| --- | --- | --- |
| `stat_minute_utc` | `DateTime / timestamptz` | Exact UTC minute start, inclusive; window ends exclusively one minute later |

### Hour-only fields

| Column | Type | Meaning |
| --- | --- | --- |
| `hour_start_utc` | `DateTime / timestamptz` | Exact UTC hour start, inclusive; window ends exclusively one hour later |
| `distinct_active_vehicle_count` | `long? / bigint` | Whole-hour unique eligible active vehicles; null when its set was lost or cannot support an exact denominator |
| `covered_minutes` | `int / integer` | Capture-complete minute contributions, 0..60; only authoritative after durable verification |

An hourly distinct count is nonadditive across minutes. Across complete hours its sum is an observed vehicle-hour denominator: a vehicle in two hours contributes twice. Partial-hour distinct counts can be diagnostic when intact, but cannot yield definitive hourly measures.

## Validation and relationships

- City/category/version values must be nonblank, bounded, and canonical. Category comes from the observation-time loaded city catalog, never a hardcoded bus/rail enumeration.
- UTC period keys must have no residual seconds or subsecond ticks; hour keys additionally have minute zero. Observation timestamps are canonicalized to UTC microsecond precision before freezing.
- Counts and meter/gap values are nonnegative and arithmetic uses checked counters. Canonicalize each accepted distance delta to six decimal places before adding the same decimal to minute and hour totals.
- Valid active, valid publish, and failed cycle counts cannot individually exceed observed cycles; failures and valid samples may overlap because publication can fail after valid activity.
- A `Complete` minute requires positive valid activity sample count, valid activity/publish evidence for every observed cycle, zero failed cycles, proven boundaries, no known capture loss, and maximum gap at or below the stored limit.
- A `NoData` minute/hour has no eligible activity, movement, or publication samples; failure and rejection diagnostics may remain. Otherwise missing or failed evidence produces `Partial`.
- `Complete` hours require `covered_minutes = 60`, a nonnull distinct count, no population loss, and compatible minute definitions/cadence policies. Their additive fields must equal all 60 durable minute sums.
- There are no database foreign keys between hours and minutes. A missing minute must be representable; the store/query explicitly verify the relationship by city, category, and UTC range.
- A `has_conflict` row is never authoritative complete, whatever its capture status. Minute conflict quarantine propagates to an existing parent hour.
- Only the two composite primary keys and check constraints are added initially. No extra indexes, partitions, retention deletion, or per-vehicle rows.

## Persistence state transitions

1. A mutable in-process accumulator collects cycle facts.
2. Minute boundaries remain pending until successor timing evidence arrives or the cadence deadline expires. Finalization creates an immutable `Complete`, `Partial`, or `NoData` payload.
3. Hour finalization freezes additive contributions and the distinct count, then discards its vehicle set. Keep at most current and boundary-pending hours; never queue the set.
4. `TryEnqueue` success accepts an aggregate envelope into bounded process memory; it is not durable acknowledgement.
5. New identity: insert the normalized payload. Identical retry: unchanged. Differing payload: preserve counters and set `has_conflict = true`.
6. Quarantine is monotone. Automatic writes cannot clear it, fill partial observations, or merge counters. Operator reconciliation of an affected identity is separate from ordinary capture.
7. For a complete-hour candidate, lock/read its 60 minute keys and verify them after inserting bundled minutes. Retry temporarily missing backing evidence within the writer's finite budget. If still unverifiable, omit the hourly candidate and emit a safe summary.
8. Process loss before persistence yields missing keys. Enqueue loss known while the hour is open makes it partial. Query-time durable verification handles missing writes or conflicts learned after a payload was frozen.

## Transient entities

| Entity | Contents / lifetime |
| --- | --- |
| `CategoryCycleObservation` | Per-cycle category counters, valid/failure/publication facts, one UTC observation timestamp; no database DTO containing identities |
| `ObservedMovementState` | Per-vehicle route key, source timestamp, along-route meters, geometry generation, last seen; in-process only, pruned at the existing 20-minute vehicle-state horizon |
| Cycle identity set | Distinct eligible vehicle identities; disposed after the cycle |
| Hour population set | Distinct eligible vehicle identities per city/category/hour; discarded on finalization or loss |
| `FinalizedCategoryStatisticsBatch` | Immutable minute/hour rows for one city, aggregate-only |
| `CategoryStatisticsWriteReport` | Inserted/unchanged/conflicting/omitted counts and bounded reason codes; no raw rows or transit identities |
| `CityCategoryInsightsOptions` | Global disabled default, optional city exclusions, cadence limits, queue capacity, batch size, timeout, retries, shutdown drain |
| City configuration | Add validated `TimeZoneId` to the existing `Cities[]` entries; no persistent city/time-zone table |

## Availability metadata

Measure eligibility, denominator rules, or category meaning changes require a new definition version. Ordinary route membership changes under the same category meaning do not. Preserve old rows and group versions separately; apply changed definitions at UTC boundaries and mark transition windows incomplete. The v1 constant must not be reused for incompatible semantics.

The query returns the earliest retained category observation as `first_collectible_utc` for that city/category, labeled as the beginning of retained category capture. It does not imply recoverable observations before that point. Requests with no retained rows return unavailable history rather than a fabricated start date. An operator release record additionally records enablement date; it is not a third statistics entity.
