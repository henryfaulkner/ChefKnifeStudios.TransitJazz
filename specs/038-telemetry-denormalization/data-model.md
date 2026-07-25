# Data Model: Telemetry Denormalization

One wide record type — `TelemetryEvent` — replaces `SnapEventArgs`, `LerpEventArgs`, and `CycleEventArgs`. Every emitted telemetry event is one row in one parquet table (`telemetry/`). Columns fall into three groups: common (every row), PerCityCycle-only, FullCycle-only, and a shared middle set present on both event types.

## Entity: `TelemetryEvent`

C# record in `src/Server/.../TransitDataWorker/Logging/TelemetryEvent.cs`, implementing `IEventArgs` (the existing bus marker). Serialized via `ParquetSerializer`; each property pins its snake_case parquet column with `[ParquetColumn(Name = "…")]` (see contracts/telemetry-event-schema.md for the C#↔column mapping). Nullable CLR types map to nullable parquet columns.

### Column contract (17 columns)

Kind = how the Go allow-list lets the column be compared (numeric / string / timestamp / bool).

| # | Column | Kind | CLR type | Group | PerCityCycle | FullCycle | Meaning |
|---|--------|------|----------|-------|:---:|:---:|---------|
| 1 | `event_type` | string | `string` | common | required | required | Discriminator: `"PerCityCycle"` or `"FullCycle"`. |
| 2 | `event_id` | string | `string` | common | required | required | This row's own identity (`Guid.NewGuid().ToString("N")`). Replaces `cycle_id`; no cross-row correlation needed anymore. |
| 3 | `observation_utc` | timestamp | `DateTime` | common | required | required | When the row was emitted (UTC). |
| 4 | `city_name` | string | `string?` | per-city only | populated | **null** | City this row is scoped to. |
| 5 | `feed_freshness_seconds` | numeric | `double?` | per-city only | populated¹ | **null** | Age (s) of the feed header timestamp at observation time. Null on failure paths / no feed header. |
| 6 | `cities_processed_count` | numeric | `int?` | full-cycle only | **null** | populated | Number of cities processed this tick. |
| 7 | `cities_processed_csv` | string | `string?` | full-cycle only | **null** | populated | Comma-separated city names processed this tick. |
| 8 | `time_taken_seconds` | numeric | `double?` | shared | per-city | tick-wide | Wall-clock duration (per-city vs. whole tick). |
| 9 | `health_ok` | bool | `bool?` | shared | per-city | tick-wide | Health indicator (see state table). |
| 10 | `tones_emitted` | numeric | `int?` | shared | per-city | **summed** | Detected trigger-point crossings (`crossingRecords.Count`). |
| 11 | `vehicles_processed` | numeric | `int?` | shared | per-city | **summed** | Vehicles processed. |
| 12 | `gc_heap_bytes` | numeric | `long?` | shared | reused² | reused² | `GC.GetTotalMemory(false)`. |
| 13 | `process_working_set_bytes` | numeric | `long?` | shared | reused² | reused² | `Process.GetCurrentProcess().WorkingSet64`. |
| 14 | `vehicle_state_cache_size` | numeric | `int?` | shared | per-city | **summed** | `_vehicleStateCaches[city].Count`. |
| 15 | `crossing_baseline_cache_size` | numeric | `int?` | shared | per-city | **summed** | `_crossingBaselines[city].Count`. |
| 16 | `route_index_size` | numeric | `int?` | shared | per-city | **summed** | `_routeIndex[city].Count`. |
| 17 | `route_trigger_point_cache_size` | numeric | `int?` | shared | per-city | **summed** | `_routeTriggerPoints[city].Count`. |

¹ `feed_freshness_seconds` is populated only when the city ran and had a feed header; null on failure/not-ready and when the header is absent.
² **reused, not summed**: the two memory figures are sampled once per tick and copied verbatim onto every row that tick (memory is process-wide, not partitionable per city — FR-018).

### Field-population rules by event type

- **PerCityCycle** (one row per telemetry-emitting city per tick): columns 1-3 (common), 4-5 (city-only), 8-17 (shared, per-city scope). Columns 6-7 (full-cycle-only) are **null**.
- **FullCycle** (one row per tick): columns 1-3 (common), 6-7 (full-cycle-only), 8-17 (shared, tick-wide scope). Columns 4-5 (per-city-only) are **null**. On this row, `tones_emitted`, `vehicles_processed`, and the four cache-size columns are the **sum** across the tick's cities; the two memory columns are the single per-tick sample.

### Shared-name rule (FR-023)

`time_taken_seconds`, `health_ok`, `tones_emitted`, `vehicles_processed`, and all six memory/cache columns are **one column each** with the same meaning, differing only in scope (per-city vs. tick-wide). They are not duplicated per type.

## State: `health_ok` (per city, per tick)

Evaluated on the path the per-city `try/catch` in `Worker.cs ExecuteAsync` (lines 55-75) takes:

| Path (Worker.cs) | `health_ok` | Processing-derived fields |
|---|:---:|---|
| Exception caught (lines 71-74) | `false` | `tones_emitted`/`vehicles_processed` = 0; `feed_freshness_seconds` = null |
| Route index not ready → `continue` (lines 61-65) | `false` | same as above |
| Ran normally — incl. empty feed (`feed.Entities.Count == 0`) | `true` | populated (0 vehicles is healthy, just nothing to do) |

- An **empty feed is healthy** (`health_ok=true`, `vehicles_processed` may be 0) — nothing failed (FR-011).
- On failure paths, only **cheaply-available** diagnostics are populated: the two memory figures and the four cache sizes (they don't depend on this tick's feed). Processing-derived fields are 0/null (FR-012).
- `health_ok` on the **FullCycle** row reflects the tick overall (e.g. false if any city failed; exact roll-up rule decided in implementation — the contract is "reflects the tick's health").

## Retired fields (not migrated)

From the old `snap`/`lerp`/`cycle` datasets, these are **dropped** (intentional scope reduction, FR-005 / confirmed in spec):

- **All per-vehicle snap detail**: `snap_outcome`, `raw_lat/lon`, `snapped_lat/lon`, `snap_distance_km`, `snap_index`, `route_point_count`, `speed_mps`, `bearing_deg`, `is_stale`, per-row `vehicle_id`/`route_id`.
- **All per-vehicle lerp detail**: `prior_*`, `pos_delta_km`, `speed_delta`, `bearing_delta`, `time_delta_sec`.
- **Old cycle fields folded/renamed/dropped**: `cycle_id`→`event_id`; `cycle_start_utc`/`cycle_end_utc`/`cycle_execution_seconds`→`time_taken_seconds`; `buses_processed`→`vehicles_processed`; the `buses_moved/unchanged/stationary/stale/skipped_*` breakdown → **dropped** (only the total survives); `feed_header_ts`+`duplicate_feed`→`feed_freshness_seconds`; `active_route_ids`/`active_vehicle_ids`→`cities_processed_csv` at city granularity (route/vehicle CSVs dropped); `last_update_cache_size`→**dropped** (was hardcoded 0, dead telemetry — FR-020); `sidecar_buffer_occupancy`/`sidecar_dropped_records`/`sidecar_persist_failures` → **dropped from the row** (sidecar self-health stays in structured logs via `LogSidecarHealth`, `LogEventWorker.cs:152`; not re-added as columns unless a follow-up asks).

> Note: sidecar self-health counters were columns on the old `cycle` row. This design does not carry them as `TelemetryEvent` columns — they remain observable via the existing `Sidecar health:` structured log line. If a future need arises they'd be three more nullable properties (one-line each, the whole point of FR-006).

## Validation rules (enforced downstream by the Go allow-list)

- `event_type` compared as a string literal `'PerCityCycle'` / `'FullCycle'` (the primary scoping filter — FR-027).
- Numeric columns ↔ bare numbers; `health_ok` ↔ bare `true`/`false`; `observation_utc` ↔ `'YYYY-MM-DD'` date string (date granularity).
- A column not in the 17-column set is rejected as unknown (FR-026).
- Nullability is informational to the query grammar (no `IS NULL`); nulls simply appear as empty cells in results.
