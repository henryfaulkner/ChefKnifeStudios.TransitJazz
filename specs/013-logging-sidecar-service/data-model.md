# Data Model: Logging Sidecar Service

Defines the in-memory event-args types (the in-process contract over the notification bus) and the flat parquet row schemas (the durable contract the `telemetry-query-tool` reads). Column names are snake_case and frozen — see `contracts/parquet-schemas.md`. All timestamps are UTC.

---

## In-memory abstractions (notification bus)

Mirrors the existing `Client.Core/Services/EventNotificationService.cs` (FR-014), placed server-side under `TransitDataWorker/Logging/`.

```
IEventArgs                         // empty marker
EventReceivedEventHandler          // delegate void(object sender, IEventArgs e)
IEventNotificationService          // event EventReceived; void PostEvent(object, IEventArgs)
EventNotificationService           // raises EventReceived (singleton)

LogEventArgs : IEventArgs          // abstract base for all logging events; carries CycleId
 ├─ SnapEventArgs : LogEventArgs
 ├─ LerpEventArgs : LogEventArgs
 └─ CycleEventArgs : LogEventArgs
```

The `LogEventWorker` consumer switches on the concrete type to route each event to its dataset accumulator. Non-`LogEventArgs` events are ignored (edge case: "Unrecognized event type").

---

## Enum

### SnapDecision (logged as `nameof`, FR-008)

`FirstObservation | Moved | Unchanged | Stationary | Stale`

Stored in parquet as its string name in `snap_outcome`. (Derived from the existing `Worker.cs` outcome strings.)

---

## Entity: Snap row (`snap/` dataset)

One row per per-vehicle snap decision in a cycle.

| Column (snake_case) | Type | Source in `Worker.cs` | Notes |
|---|---|---|---|
| `cycle_id` | string | generated per cycle | FR-009 correlation key |
| `observation_utc` | timestamp | `now` | when this observation was processed |
| `vehicle_id` | string | `vehicleId` | bus number / GTFS vehicle id |
| `route_id` | string | `nearest.RouteId` | route number (route short name, Constitution VI) |
| `snap_outcome` | string | `outcome` | `SnapDecision` name (FR-008) |
| `raw_lat` | double | `lat` | bus position (raw GPS) |
| `raw_lon` | double | `lon` | bus position (raw GPS) |
| `snapped_lat` | double | `nearest.Lat` | route position (snapped) |
| `snapped_lon` | double | `nearest.Lon` | route position (snapped) |
| `snap_distance_km` | double | `snapValue.DistanceKm` | raw→snap distance |
| `snap_index` | int | `snapValue.Index` | index in route point array (route position) |
| `route_point_count` | int | `routePoints.Length` | denominator for `snap_index` |
| `speed_mps` | double? | `Position.Speed` | bus speed |
| `bearing_deg` | double? | `Position.Bearing` | bus bearing |
| `is_stale` | bool | `isStale` | passthrough/stale sample |

"Position delta (timestamp, cycle id)" from the spec is represented by `observation_utc` + `cycle_id`.

---

## Entity: Lerp row (`lerp/` dataset)

One row per per-vehicle delta computation (vehicles with a prior observation this cycle).

| Column | Type | Source | Notes |
|---|---|---|---|
| `cycle_id` | string | per cycle | FR-009 |
| `observation_utc` | timestamp | `now` | current observation time |
| `vehicle_id` | string | `vehicleId` | |
| `prior_route_id` | string | `prior.RouteId` | **prior route data** |
| `prior_snapped_lat` | double | `prior.NearestLat` | **prior route data** |
| `prior_snapped_lon` | double | `prior.NearestLon` | **prior route data** |
| `prior_observation_utc` | timestamp | `prior.LastUpdated` | **prior bus data** |
| `prior_speed_mps` | double? | `prior.SpeedMetersPerSec` | **prior bus data** |
| `prior_bearing_deg` | double? | `prior.Bearing` | **prior bus data** |
| `pos_delta_km` | double | `DeltaFromPriorSnapKm` | **bus delta**: position delta |
| `speed_delta` | double? | current − prior speed | **bus delta**: speed delta |
| `bearing_delta` | double? | current − prior bearing | **bus delta**: bearing delta |
| `time_delta_sec` | double | `SecondsSincePriorObservation` | **bus delta**: time delta |

---

## Entity: Cycle row (`cycle/` dataset)

Exactly one row per completed processing cycle (FR-005). Includes sidecar self-health columns (FR-012).

| Column | Type | Source | Notes |
|---|---|---|---|
| `cycle_id` | string | per cycle | primary identifier |
| `cycle_start_utc` | timestamp | cycle start stopwatch anchor | |
| `cycle_end_utc` | timestamp | cycle end | |
| `cycle_execution_seconds` | double | end − start | |
| `buses_processed` | int | total entities considered | |
| `buses_moved` | int | `movedCount` | |
| `buses_unchanged` | int | `unchangedCount` | |
| `buses_stationary` | int | `stationaryCount` | |
| `buses_stale` | int | `staleCount` | |
| `buses_skipped_no_route_id` | int | `skippedNoRouteId` | |
| `buses_skipped_unknown_route` | int | `skippedUnknownRoute` | |
| `feed_header_ts` | long? | `feedTs` (Unix sec) | nullable |
| `duplicate_feed` | bool | `feedIsDuplicate` | |
| `last_update_cache_size` | int | `_lastUpdateCache.Count` | |
| `vehicle_state_cache_size` | int | `_vehicleStateCache.Count` | |
| `sidecar_buffer_occupancy` | int | `LogEventWorker` channel count est. | **self-health** (FR-012) |
| `sidecar_dropped_records` | long | running shed counter | **self-health** (FR-012, SC-004) |
| `sidecar_persist_failures` | long | running persist-failure counter | **self-health** (FR-012) |

---

## Validation rules

- `cycle_id` MUST be non-empty on every row of every dataset.
- Exactly one Cycle row per cycle (SC-002); Snap/Lerp rows reference an existing `cycle_id`.
- A part-file contains rows of **one** dataset only (FR-004d) — schema homogeneous.
- Numeric counters are non-negative.
- Nullable columns (`speed_mps`, `bearing_deg`, deltas, `feed_header_ts`) are written as parquet nulls, not sentinels.

## Lifecycle / state

1. **Capture** — `Worker` posts `*EventArgs` during a cycle; non-blocking enqueue, `DropWrite` on overflow (increment `sidecar_dropped_records`).
2. **Accumulate** — consumer appends typed rows to per-dataset in-memory buffers.
3. **Flush** — every 5 min (and on stop) each non-empty buffer → one parquet part-file → uploaded to its `dt=` partition; buffer cleared. Failure increments `sidecar_persist_failures`, logs via `ILogger`, and is swallowed (FR-010).
4. **Reset** — buffers reset post-flush; counters are cumulative for the process lifetime and reported on each Cycle row.

## Edge cases captured in schema

- **Midnight UTC straddle** (R5): a 5-min flush is attributed to the flush-instant UTC date; rows in that file are not split across `dt=` folders.
- **Empty dataset at flush** (e.g. no Lerp rows because all first-observations): no part-file written for that dataset that interval (avoids empty-file noise).
- **Overflow**: dropped rows are counted, never block; surfaced via `sidecar_dropped_records` on the next Cycle row.
