<!-- last verified: 2026-07-11 -->

# Telemetry Schema Reference

One denormalized dataset, `telemetry`, emitted by the TransitJazz data worker's
logging sidecar (feature 013, denormalized in feature 038) and served by the
`telemetry-query-bridge` MCP tool (feature 014, updated in feature 038). Every row
carries an `event_type` discriminator — `PerCityCycle` (one row per
telemetry-emitting city per tick) or `FullCycle` (one row per worker tick across all
cities). Column names are a **frozen snake_case contract** — they are exactly what
the query tool's allow-list accepts. A name not listed here is rejected as an
unknown column.

Source of truth in the repo:
- `specs/038-telemetry-denormalization/contracts/telemetry-event-schema.md` (types)
- `specs/038-telemetry-denormalization/data-model.md` (value-kind classification,
  field-population rules by event type)
- `tools/telemetry-mcp/internal/validate/validate.go` (`datasetColumns`)

If a query keeps failing on an "unknown column", re-verify against `validate.go` and
update the `last verified` date above.

## Value kinds (how a column may be compared in a filter)

| Kind | Compare against | Example | Notes |
|------|-----------------|---------|-------|
| **numeric** | a bare number `-?\d+(\.\d+)?` | `vehicles_processed > 0` | DOUBLE/INT32/INT64 all collapse to numeric |
| **string** | `'...'` with chars `[A-Za-z0-9 _-]` only | `city_name = 'MARTA'` | no `:`, `T`, `.` etc. inside quotes |
| **timestamp** | a `'YYYY-MM-DD'` **date string** | `observation_utc > '2026-07-11'` | date granularity only — full ISO (`...T12:00:00`) is rejected (the `:` and `T` are forbidden in string literals) |
| **bool** | bare `true` / `false` (unquoted) | `health_ok = false` | `1`, `0`, `'true'` are all rejected |

Nullability below is **informational only** — the filter grammar has no `IS NULL`.
`event_type` is the primary scoping filter: most questions should start with
`event_type = 'PerCityCycle'` or `event_type = 'FullCycle'`.

---

## Dataset `telemetry` (17 columns)

| Column | Kind | Group | PerCityCycle | FullCycle | Meaning |
|--------|------|-------|:---:|:---:|---------|
| `event_type` | string | common | required | required | Discriminator: `"PerCityCycle"` or `"FullCycle"`. |
| `event_id` | string | common | required | required | This row's own identity (`Guid.NewGuid().ToString("N")`). No cross-row correlation needed. |
| `observation_utc` | timestamp | common | required | required | When the row was emitted (UTC). |
| `city_name` | string | per-city only | populated | **null** | City this row is scoped to. |
| `feed_freshness_seconds` | numeric | per-city only | populated¹ | **null** | Age (s) of the feed header timestamp at observation time. Null on failure paths / no feed header. |
| `cities_processed_count` | numeric | full-cycle only | **null** | populated | Number of cities processed this tick. |
| `cities_processed_csv` | string | full-cycle only | **null** | populated | Comma-separated city names processed this tick. |
| `time_taken_seconds` | numeric | shared | per-city duration | tick-wide duration | Wall-clock duration. |
| `health_ok` | bool | shared | per-city | tick-wide | `false` on route-index-not-ready or exception paths; `true` otherwise (an empty feed is still healthy). |
| `tones_emitted` | numeric | shared | per-city count | **summed** | Detected trigger-point crossings (each fires a synthesized note). |
| `vehicles_processed` | numeric | shared | per-city count | **summed** | Vehicles processed. |
| `gc_heap_bytes` | numeric | shared | reused² | reused² | `GC.GetTotalMemory(false)`, sampled once per tick. |
| `process_working_set_bytes` | numeric | shared | reused² | reused² | `Process.GetCurrentProcess().WorkingSet64`, sampled once per tick. |
| `vehicle_state_cache_size` | numeric | shared | per-city | **summed** | Size of that city's vehicle-state cache. |
| `crossing_baseline_cache_size` | numeric | shared | per-city | **summed** | Size of that city's crossing-baseline cache. |
| `route_index_size` | numeric | shared | per-city | **summed** | Size of that city's route index. |
| `route_trigger_point_cache_size` | numeric | shared | per-city | **summed** | Size of that city's trigger-point cache. |

¹ Null on failure/not-ready paths and when the feed header is absent.
² **Reused, not summed**: the two memory figures are process-wide (not partitionable
per city), so the same tick's sample appears verbatim on every row that tick.

## Retired fields (not in this dataset)

Per-vehicle snap/lerp detail (`snap_outcome`, `raw_lat/lon`, `snapped_lat/lon`,
`snap_distance_km`, `pos_delta_km`, `speed_delta`, `bearing_delta`, per-row
`vehicle_id`/`route_id`, etc.) from the old `snap`/`lerp` datasets is **gone** —
scope reduced to two aggregate event types. The old `cycle` dataset's
`buses_moved/unchanged/stationary/stale/skipped_*` breakdown is dropped (only the
`vehicles_processed` total survives); `active_route_ids`/`active_vehicle_ids` are
replaced by `cities_processed_csv` at city granularity; `last_update_cache_size` is
dropped (was hardcoded to 0 — dead telemetry). Sidecar self-health
(`sidecar_buffer_occupancy`/`sidecar_dropped_records`/`sidecar_persist_failures`) is
no longer a column — it's still observable via the `Sidecar health:` structured log
line, not queryable here.

---

## Quick column-to-question map

- **"Is the system healthy right now?"** → `event_type = 'FullCycle' AND health_ok = false`
  to find unhealthy ticks; `time_taken_seconds` for slow ticks.
- **"Did a city fail or get skipped?"** → `event_type = 'PerCityCycle' AND health_ok = false`
  — this is the visibility fix (038): failed/not-ready ticks now emit a row, where the
  old `cycle` dataset silently emitted nothing on those paths.
- **"Is GPS snapping / crossing detection working?"** → `tones_emitted` (crossings
  detected) and `vehicles_processed`, filtered by `city_name` (PerCityCycle only).
- **"Are we ingesting fresh feeds?"** → `feed_freshness_seconds` (PerCityCycle only;
  high = stale feed).
- **"Which cities ran this tick?"** → `event_type = 'FullCycle'`:
  `cities_processed_csv`, `cities_processed_count`.
- **"Is memory growing?"** → `gc_heap_bytes`, `process_working_set_bytes` (either
  event type — the values are identical within a tick; query `FullCycle` for one row
  per tick).
- **"Is a cache growing unbounded?"** → `vehicle_state_cache_size`,
  `crossing_baseline_cache_size`, `route_index_size`, `route_trigger_point_cache_size`
  (per-city on `PerCityCycle`, tick-wide sum on `FullCycle`).
