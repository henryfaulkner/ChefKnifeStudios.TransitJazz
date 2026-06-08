<!-- last verified: 2026-06-07 -->

# Telemetry Schema Reference

The three datasets emitted by the TransitJazz data worker's logging sidecar (feature
013) and served by the `telemetry-query-bridge` MCP tool (feature 014). Column names
are a **frozen snake_case contract** — they are exactly what the query tool's
allow-list accepts. A name not listed here is rejected as an unknown column.

Source of truth in the repo:
- `specs/013-logging-sidecar-service/contracts/parquet-schemas.md` (types)
- `specs/014-transit-datasets/data-model.md` (value-kind classification)
- `tools/telemetry-mcp/internal/validate/validate.go` (`datasetColumns`)

If a query keeps failing on an "unknown column", re-verify against `validate.go` and
update the `last verified` date above.

## Value kinds (how a column may be compared in a filter)

| Kind | Compare against | Example | Notes |
|------|-----------------|---------|-------|
| **numeric** | a bare number `-?\d+(\.\d+)?` | `snap_distance_km > 0.5` | DOUBLE/INT32/INT64 all collapse to numeric |
| **string** | `'...'` with chars `[A-Za-z0-9 _-]` only | `vehicle_id = 'v001'` | no `:`, `T`, `.` etc. inside quotes |
| **timestamp** | a `'YYYY-MM-DD'` **date string** | `observation_utc > '2026-06-04'` | date granularity only — full ISO (`...T12:00:00`) is rejected (the `:` and `T` are forbidden in string literals) |
| **bool** | bare `true` / `false` (unquoted) | `is_stale = false` | `1`, `0`, `'true'` are all rejected |

Nullability below is **informational only** — the filter grammar has no `IS NULL`.

---

## Dataset `snap`

One row per per-vehicle snap decision within a reconciliation cycle: the result of
snapping a raw GPS fix onto the vehicle's route geometry.

| Column | Kind | Null? | Meaning |
|--------|------|-------|---------|
| `cycle_id` | string | no | ID of the reconciliation cycle this decision belongs to (join key to `cycle`). |
| `observation_utc` | timestamp | no | When the observation was taken (UTC). |
| `vehicle_id` | string | no | The bus/vehicle identifier. |
| `route_id` | string | no | Route the vehicle was matched to (carries the GTFS route short name). |
| `snap_outcome` | string | no | One of `FirstObservation`, `Moved`, `Unchanged`, `Stationary`, `Stale`. |
| `raw_lat` | numeric | no | Raw observed latitude before snapping. |
| `raw_lon` | numeric | no | Raw observed longitude before snapping. |
| `snapped_lat` | numeric | no | Latitude after snapping to the route. |
| `snapped_lon` | numeric | no | Longitude after snapping to the route. |
| `snap_distance_km` | numeric | no | Distance from the raw fix to the snapped point (km). High values = poor snap / off-route GPS. |
| `snap_index` | numeric | no | Index of the snapped point along the route polyline. |
| `route_point_count` | numeric | no | Number of points in the route polyline. |
| `speed_mps` | numeric | yes | Estimated speed (m/s). |
| `bearing_deg` | numeric | yes | Estimated heading (degrees). |
| `is_stale` | bool | no | True when the observation is considered stale (not freshly updated). |

## Dataset `lerp`

One row per per-vehicle position delta: how a vehicle changed relative to its prior
recorded state (only vehicles that had a prior state appear).

| Column | Kind | Null? | Meaning |
|--------|------|-------|---------|
| `cycle_id` | string | no | Reconciliation cycle ID (join key to `cycle`). |
| `observation_utc` | timestamp | no | Current observation time (UTC). |
| `vehicle_id` | string | no | The vehicle identifier. |
| `prior_route_id` | string | no | Route ID from the prior state. |
| `prior_snapped_lat` | numeric | no | Snapped latitude in the prior state. |
| `prior_snapped_lon` | numeric | no | Snapped longitude in the prior state. |
| `prior_observation_utc` | timestamp | no | Prior observation time (UTC). |
| `prior_speed_mps` | numeric | yes | Prior speed (m/s). |
| `prior_bearing_deg` | numeric | yes | Prior heading (degrees). |
| `pos_delta_km` | numeric | no | Distance moved since the prior state (km). |
| `speed_delta` | numeric | yes | Change in speed vs. prior. |
| `bearing_delta` | numeric | yes | Change in heading vs. prior. |
| `time_delta_sec` | numeric | no | Seconds elapsed since the prior observation. |

## Dataset `cycle`

One row per completed reconciliation cycle. The system-health dataset: counts, timing,
and sidecar internals. Start here for "is everything healthy" questions.

| Column | Kind | Null? | Meaning |
|--------|------|-------|---------|
| `cycle_id` | string | no | Unique ID of this reconciliation cycle. |
| `cycle_start_utc` | timestamp | no | Cycle start (UTC). |
| `cycle_end_utc` | timestamp | no | Cycle end (UTC). |
| `cycle_execution_seconds` | numeric | no | Wall-clock duration of the cycle. High = slow processing. |
| `buses_processed` | numeric | no | Total vehicles processed in the cycle. |
| `buses_moved` | numeric | no | Vehicles that moved. |
| `buses_unchanged` | numeric | no | Vehicles with no position change. |
| `buses_stationary` | numeric | no | Vehicles considered stationary. |
| `buses_stale` | numeric | no | Vehicles with stale observations. High = feed freshness problem. |
| `buses_skipped_no_route_id` | numeric | no | Vehicles skipped for missing a route id. |
| `buses_skipped_unknown_route` | numeric | no | Vehicles skipped for an unrecognized route (GTFS mapping gap). |
| `feed_header_ts` | numeric | yes | Source feed header timestamp (epoch seconds, INT64). |
| `duplicate_feed` | bool | no | True if this cycle ingested a feed identical to the prior one (no new data). |
| `last_update_cache_size` | numeric | no | Size of the last-update cache. |
| `vehicle_state_cache_size` | numeric | no | Size of the vehicle-state cache. |
| `sidecar_buffer_occupancy` | numeric | no | How full the logging sidecar's bounded channel is. Rising = backpressure. |
| `sidecar_dropped_records` | numeric | no | Records the sidecar dropped under load (DropWrite shedding). >0 = lost telemetry. |
| `sidecar_persist_failures` | numeric | no | Failures persisting telemetry to blob. >0 = upload/credential problem. |

---

## Quick column-to-question map

- **"Is the system healthy right now?"** → `cycle`: `cycle_execution_seconds`,
  `sidecar_dropped_records`, `sidecar_persist_failures`, `buses_stale`.
- **"Are buses being dropped/skipped?"** → `cycle`:
  `buses_skipped_unknown_route`, `buses_skipped_no_route_id`.
- **"Is GPS snapping badly?"** → `snap`: `snap_distance_km`, `snap_outcome`.
- **"Is a specific bus moving as expected?"** → `lerp`: `pos_delta_km`,
  `time_delta_sec`, filtered by `vehicle_id`.
- **"Are we ingesting fresh feeds?"** → `cycle`: `duplicate_feed`, `buses_stale`.
