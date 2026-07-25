# Contract: Per-Entity Synthesis + `pointOnShapeAtDistance`

The core algorithm. Pure functions over `StopOffsetTable` + one RT `FeedEntity`; deterministic
given the same inputs (except `now`, which drives the in-transit fraction). Every branch below
maps to a spec FR and an acceptance scenario.

## Inputs (from the decoded entity)

| Field | Source | Role |
|-------|--------|------|
| `route` | `entity.Vehicle.Trip.RouteId` | route → offset set key (== `RouteJoinKey`) |
| `target` | `entity.Vehicle.StopId` | station heading to / stopped at (has `N`/`S` suffix) |
| `status` | `entity.Vehicle.CurrentStatus` (`VehicleStopStatus?`) | parked vs. moving |
| `tstamp` | `entity.Vehicle.Timestamp` | observation time → elapsed for `frac` |
| `direction` | last char of `target` (`N`/`S`) | which neighbour is "previous" |

## Algorithm (`ShapeInterpolator.Synthesize`)

```
targetCoord = table.TryStation(target)
if targetCoord is null:            skippedUnknownStation++; return DROP        // FR-014, edge case
set   = table.Sets[(route, direction)]
if set is null:                    return DROP                                 // route has no shape, FR-014

switch (status ?? StoppedAt):      // FR-015: missing status → StoppedAt
  StoppedAt:   synthesizedStopped++;    return targetCoord                     // FR-002, US1 AS1
  IncomingAt:  synthesizedStopped++;    return targetCoord                     // FR-002, US1 AS2
  InTransitTo:
      prev = table.StationBefore(target, route, direction)
      if prev is null:  synthesizedStopped++; return targetCoord               // terminal, FR-007 edge
      elapsed = max(0, nowUnix - tstamp)
      frac    = clamp(elapsed / NominalRunSeconds, 0, 1)                        // FR-004
      dCurr   = prev.DistanceAlongShapeMeters
              + frac * (targetStop.DistanceAlongShapeMeters - prev.DistanceAlongShapeMeters)
      synthesizedInTransit++
      return table.PointOnShapeAtDistance(route, direction, dCurr)             // FR-003, FR-005
```

## `PointOnShapeAtDistance(route, dir, distMeters)`

Walk the **polyline** (`set.Coordinates`), not the chord:

```
cd = set.CumulativeDistanceMeters
d  = clamp(distMeters, 0, cd[^1])                     // FR-004: never before start / past end
i  = upper_bound(cd, d) - 1                            // binary search, O(log n)
if i == cd.Length-1:  return coord[i]                  // exactly at the last vertex
t  = (d - cd[i]) / (cd[i+1] - cd[i])                   // guard cd[i+1]==cd[i] → t=0
lon = lerp(coord[i].lon, coord[i+1].lon, t)
lat = lerp(coord[i].lat, coord[i+1].lat, t)
return (lat, lon)
```

## Endpoint-exactness invariants (asserted by `SubwaySynthesisTests`)

- **INV-A1 (FR-006, SC-002)**: `frac == 0` (elapsed 0) → position == `prev` station coord;
  `frac == 1` (elapsed ≥ `NominalRunSeconds`) → position == `target` station coord. The two feed
  ground-truth points are rendered **exactly**.
- **INV-A2 (FR-005, US2 AS4)**: on a deliberately curved test polyline, a mid-segment `frac`
  places the train **on the polyline**, not on the straight chord between the two stations
  (assert perpendicular distance from chord > 0 where the polyline bends).
- **INV-A3 (FR-002)**: `StoppedAt` / `IncomingAt` → exactly `targetCoord` (0 m error).
- **INV-A4 (FR-015)**: `CurrentStatus == null` → same result as `StoppedAt`.
- **INV-A5 (FR-014)**: unknown `StopId` → `DROP` + `skippedUnknownStation++`, and the merged feed
  for that tick still contains the other trains (no throw).
- **INV-A6 (FR-004)**: `elapsed` far exceeding `NominalRunSeconds` clamps to the target platform
  (never overshoots past `target`).
- **INV-A7 (FR-007)**: `InTransitTo` with `StationBefore == null` (line terminal) → `targetCoord`.

## Notes / boundaries

- `NominalRunSeconds` is a constant (`SubwaySynthesisOptions.NominalRunSeconds`, default 90).
  Refining it from `stop_times` scheduled deltas is **out of scope** (spec) — endpoints anchor
  the motion, so the constant already reads as believable.
- Direction comes from the `stop_id` suffix; `StationBefore` walks the `(route, direction)`
  ordered `Stops` list to the entry immediately before `target`.
- No shared-pipeline code is touched: this whole file executes inside `NymtaCity` before the
  `FeedEntity` reaches `Worker`.
