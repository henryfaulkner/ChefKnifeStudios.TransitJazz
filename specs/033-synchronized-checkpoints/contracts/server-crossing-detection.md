# Contract: Server Crossing Detection

**Location**: `Server.TransitDataWorker/Checkpoints/CrossingDetector.cs` (new) + wiring in
`Worker.cs` `ProcessSpatialReconciliationAsync`. Mirrors `checkpoint-tracker.js` `onTick`, but over
**snapped cumulative distance** instead of animated position.

## Per-route precompute (once, with the route index)

When `BuildRouteIndex` runs (init + 24h refresh), additionally build, per (city, routeId):

```
cumDist[i]   = cumDist[i-1] + HaversineCalculator.DistanceMeters(pt[i-1], pt[i]);  cumDist[0] = 0
triggerPoints = TriggerPointGenerator.Generate(coords, cumDist)   // coords from the same RoutePoint[]
```

Cache these parallel to `_routeIndex` (e.g. `_routeCumDist`, `_routeTriggerPoints`, keyed by
`city → routeId`). `coords` here is the `[lon,lat][]` form the generator expects, derived from
`RoutePoint` (`Lon`, `Lat`).

> Parity note: this is byte-identical to the client's `ConfigureTrackerForRouteAsync` cumDist build
> (`TransitMap.razor.cs:421-426`) and `Generate` call — the same inputs and the same shared code.

## Per-vehicle detection (each cycle, inside the existing reconciliation loop)

Run only for vehicles that produced a non-stale snap this cycle (the same gate the V2 record uses).
Inputs available at the call site: `vehicleId`, `routeId`, current `snapValue.Index`, and the
per-route `cumDist` + `triggerPoints`.

```
currentDistM = cumDist[snapValue.Index]
baseline     = baselineMap[city].GetValueOrDefault(vehicleId)

if baseline is null:                              // FR-007 first observation
    baselineMap[city][vehicleId] = { routeId, currentDistM };  emit nothing

else if baseline.RouteId != routeId:              // FR-010 transfer
    baseline.RouteId = routeId; baseline.LastCrossedAlongDistanceM = currentDistM;  emit nothing

else:
    delta = currentDistM - baseline.LastCrossedAlongDistanceM
    if delta <= 0:                                // FR-008 backward / no move
        emit nothing (do not move baseline backward)
    else if delta > TELEPORT_DIST_M (2000):       // FR-009 teleport
        baseline.LastCrossedAlongDistanceM = currentDistM;  emit nothing
    else:                                         // FR-011 normal forward
        for tp in triggerPoints
            if tp.AlongDistanceM > baseline.LastCrossedAlongDistanceM
               and tp.AlongDistanceM <= currentDistM:
                emit RouteCrossingRecord(vehicleId, routeId, tp.Index, triggerPoints.Count)
        baseline.LastCrossedAlongDistanceM = currentDistM
```

## Rules (MUST)

- Forward-only: never move the baseline backward; never emit on `delta <= 0` (FR-008).
- First-observation, transfer, and teleport each **reset baseline + emit nothing** (FR-007/009/010).
- Emit **all** in-window trigger points (FR-011); no server-side cooldown by default (R2).
- Use the **snapped** index (`snapValue.Index`), not the raw GPS position — the server's authoritative
  position is the snap (this is what makes the set identical across clients).
- Skip routes with no trigger points (route shorter than spacing / geometry not built) → emit nothing
  (FR-016), exactly as the client `onTick` skipped `triggers.length === 0`.

## Pruning (FR-015)

In `PruneStaleVehicleStatesAsync`, after pruning a city's `vehicleStateCache`, remove any
`baselineMap[city]` entries whose `vehicleId` is no longer in that vehicle-state cache (or apply the same
20-min cutoff). Bounded state, isolated from the V2 record path.

## Verification hooks

- Log per cycle: `crossingsEmitted` count (extends the existing reconciliation log line).
- Unit-testable in isolation: feed a synthetic `cumDist` + `triggerPoints` + a sequence of snap indices
  and assert the emitted `(triggerIndex)` set for each transition kind.
