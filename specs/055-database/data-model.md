# Data Model: Historical Transit Statistics

## Table: `city_minute_statistics`

This is the only persistent table introduced by the feature. Each row is a dashboard-parity observation for one canonical city during `[stat_minute_utc, stat_minute_utc + 1 minute)`. The metrics query is evaluated at the closed end of that minute.

`city_slug` and `stat_minute_utc` form the primary key. The primary key is the only required index because every v1 insight query starts with a city and a bounded UTC range.

### Identity and provenance

| Column | Type | Rules |
|---|---|---|
| `city_slug` | short text | Primary-key part; one of the configured canonical city slugs; never a display name. |
| `stat_minute_utc` | UTC timestamp | Primary-key part; exactly aligned to `:00` seconds; represents the start of a closed one-minute observation interval. |
| `source_definition_version` | short text | Required; initially `worker-dashboard-statistics-v1`; identifies the source-controlled mapping contract. |
| `collection_status` | bounded enum | Required: `Complete`, `Partial`, `NoData`, or `Discrepant`. `Partial` means at least one source field is missing; `NoData` means no approved source value is available for the city minute; `Discrepant` preserves the first confirmed values but records that a rerun disagreed. |

No surrogate key, audit base fields, soft-delete marker, source URL, query, secret, payload, entity identifier, or import-run identifier is stored.

### City heartbeat and cycle values

| Column | Type | Source meaning |
|---|---|---|
| `last_cycled_unix_seconds` | nullable integer | Latest completed city-cycle timestamp reported by the source. Historical cycle age is derived as minute close minus this value. |
| `last_worked_unix_seconds` | nullable integer | Latest work-producing city-cycle timestamp. Historical work age is derived from the minute close. |
| `cycle_rate_per_second` | nullable decimal | One-minute city-cycle rate, including idle cycles. |
| `cycle_error_rate_per_second` | nullable decimal | One-minute non-cancellation city error rate. |
| `cycle_duration_p95_seconds` | nullable decimal | One-minute estimated p95 city-cycle duration from source histogram buckets. |
| `healthy` | nullable boolean | Latest city health value for the minute. |

### Input quality values

| Column | Type | Source meaning |
|---|---|---|
| `input_fetch_ok` | nullable boolean | At least one configured input source succeeded. |
| `input_records_valid` | nullable integer | Valid normalized input-record count. |
| `has_input_records` | nullable boolean | At least one valid input record was present. |
| `input_lag_seconds` | nullable decimal | Lag from a known input timestamp; only meaningful when `input_timestamp_known` is true. |
| `input_timestamp_known` | nullable boolean | Whether the source timestamp was known. |
| `input_source_failures` | nullable integer | Number of city input sources that failed. |

`input_lag_seconds = 0` with `input_timestamp_known = false` means unknown, not fresh. A null remains unavailable source data, not zero.

### Transit, soundscape, and bounded worker-state values

| Column | Type | Source meaning |
|---|---|---|
| `vehicles_processed` | nullable integer | Latest city vehicles processed observation. |
| `tones_emitted` | nullable integer | Latest city tone-emission observation. It is a sample, not a minute total. |
| `batch_wire_bytes` | nullable integer | Latest published batch size. The source itself represents no batch as zero. |
| `crossings_suppressed_first_seen` | nullable integer | Latest first-seen suppression observation. |
| `crossings_suppressed_delta_leq_zero` | nullable integer | Latest no-distance-advance suppression observation. |
| `crossings_suppressed_teleport` | nullable integer | Latest teleport suppression observation. |
| `crossings_suppressed_transfer` | nullable integer | Latest route-transfer suppression observation. |
| `vehicle_state_cache` | nullable integer | Latest vehicle-state cache size. |
| `crossing_baseline_cache` | nullable integer | Latest crossing-baseline cache size. |
| `route_index` | nullable integer | Latest route-index size. |
| `route_trigger_point_cache` | nullable integer | Latest route trigger-point cache size. |

The source contract, not a database table, defines whether an insight may use average, minimum, maximum, percent-of-minutes, or another aggregation. In particular, sampled gauges such as `vehicles_processed` and `tones_emitted` must not be summed and represented as exact daily totals.

## Entity and validation rules

- Implement `CityMinuteStatistic` without inheriting `BaseEntity`; its composite key expresses the true identity and avoids unneeded `Id`, audit, and soft-delete fields.
- Configure the primary key as `(city_slug, stat_minute_utc)` and preserve UTC timestamps as `timestamptz`.
- Require a nonblank, bounded city slug and source-definition version; validate the city against the host's configured canonical city set before persistence.
- Reject a timestamp not aligned to an exact UTC minute.
- Reject negative values for counts, bytes, timestamps, durations, lag, and rates. Do not coerce a missing value to zero.
- Map 0/1 source ratio gauges to nullable booleans only after validating they are exactly 0 or 1.
- On an existing key, retain a byte-for-byte-equivalent confirmed value as unchanged. A different source definition or measure value becomes a discrepancy; it must not overwrite the stored values.
- `NoData` rows have no numeric or boolean observations. `Partial` rows can contain confirmed values and null fields. A rerun may improve a `Partial` or `NoData` row only when it supplies compatible data from the same source definition.

## Lifecycle

```text
Closed city minute
  -> collector finds all approved values       -> Complete row
  -> collector finds some values               -> Partial row
  -> collector finds no values                 -> NoData row
  -> retry with compatible missing values      -> Complete/Partial row improves
  -> retry with incompatible confirmed value   -> Discrepant row; preserve original values
```

The database does not retain a second competing value or a separate import history. The collector's safe structured summary and release report provide that operational evidence.

## Volume and retention

The initial expected scope is seven cities × 1,440 minutes/day × 14 queryable days = **141,120 rows**. New collection adds at most 10,080 rows/day at the current city set. No partitioning, materialized rollup, JSON column, or additional index is justified for v1. Database retention is deliberately long-lived; operational Grafana retention remains a separate, short-lived source constraint.
