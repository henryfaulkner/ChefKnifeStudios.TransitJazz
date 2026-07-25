# Phase 1 Data Model: Synchronized Checkpoints

Three entities: one new SignalR payload, one new server-only runtime state, and one **moved** (not
new) shared value type. No persistence — everything is in-memory or on the wire.

---

## 1. RouteCrossingBatchEvent (NEW — `Shared/Events/`)

The wire payload broadcast once per cycle carrying the authoritative crossing set. Implements
`ISignalREvent` and rides inside an `EventEnvelope` alongside `RouteNearestPointBatchEvent`.

| Field | Type | Meaning |
|---|---|---|
| `BatchRecords` | `IEnumerable<RouteCrossingRecord>` | All crossings detected in this cycle, across all moved vehicles for the city. |

**Nested `RouteCrossingRecord`** — mirrors the client's `CrossingEventDto` fields one-to-one so the
client mapping is a trivial projection:

| Field | Type | Meaning | Source |
|---|---|---|---|
| `VehicleId` | `string` | GTFS vehicle id that crossed. | `entity.Vehicle.Vehicle.Id` |
| `RouteId` | `string` | Route key (= `route_short_name`, per-city). | worker route key |
| `TriggerIndex` | `int` | The crossed trigger point's `TriggerPoint.Index` (polyline vertex index). | shared generator |
| `TotalTriggers` | `int` | Count of trigger points on the route. | `triggerPoints.Count` |

**Validation / invariants**
- `TriggerIndex` and `TotalTriggers` MUST be the exact values the shared `TriggerPointGenerator`
  produced for the route — they are the determinism contract (Principle VIII / FR-003 / FR-006).
- Records SHOULD be sorted `(RouteId, VehicleId, TriggerIndex)` to match the existing client contract
  ordering (parity with the deleted `checkpoint-tracker.js` sort).
- An empty cycle (no crossings) emits **no** `RouteCrossingBatchEvent` (the envelope is simply absent
  from the publish), exactly as a no-op batch carries no crossings today.

**Lifecycle**: created in `ProcessSpatialReconciliationAsync`, published, never persisted. Structurally
excluded from `LastBatchCache` (type filter) → never replayed on reconnect (FR-005).

---

## 2. CrossingBaseline (NEW — server runtime state, Worker `Checkpoints/`)

Per-(city, vehicle) bookkeeping the detector uses to decide which trigger points a vehicle newly passed.
Lives in a parallel map, **not** in `VehicleState` (R5).

| Field | Type | Meaning |
|---|---|---|
| `RouteId` | `string` | The route the vehicle was last detected on (for transfer detection). |
| `LastCrossedAlongDistanceM` | `double` | The vehicle's along-route distance at its last detection. |

**Container**: `Dictionary<string /*city*/, ConcurrentDictionary<string /*vehicleId*/, CrossingBaseline>>`,
mirroring `_vehicleStateCaches`.

**State transitions** (mirror `checkpoint-tracker.js` `onTick`, FR-007..FR-011):

| Condition | Action | Crossings emitted |
|---|---|---|
| Vehicle not in map (first observation) | seed baseline at current along-distance | none (FR-007) |
| `baseline.RouteId != currentRouteId` (transfer) | reset baseline to current route + distance | none (FR-010) |
| `delta = current − last ≤ 0` (backward / no move) | leave baseline (or update to current) | none (FR-008) |
| `delta > TELEPORT_DIST_M` (teleport) | reset baseline to current distance | none (FR-009) |
| `0 < delta ≤ TELEPORT_DIST_M` (normal forward) | collect trigger points in `(last, current]`; advance baseline to current | one per trigger point in the window (FR-011) |

**Constants** (carried from `checkpoint-tracker.js`): `TELEPORT_DIST_M = 2000`. `COOLDOWN_MS` is **not**
carried server-side by default (R2); if reintroduced it would store a `LastCrossedUtc` here.

**Pruning** (FR-015): walked alongside `PruneStaleVehicleStatesAsync` (20-min cutoff); a vehicle absent
from the vehicle-state cache is removed from the baseline map in the same pass.

---

## 3. TriggerPoint + TriggerPointGenerator (MOVED — `Client.Shared` → `Shared`)

Not new. Unchanged in shape; relocated so server and client compile one copy (R1, OQ-1).

**`TriggerPoint`** (`Shared/Models/`):

| Field | Type | Meaning |
|---|---|---|
| `Index` | `int` | Polyline vertex index at/just beyond the trigger's along-distance. |
| `AlongDistanceM` | `double` | Distance along the route to this trigger (multiple of 400m spacing). |

**`TriggerPointGenerator.Generate(double[][] coords, double[] cumDist)`**: pure; places a trigger every
`TriggerSpacingMeters = 400.0` along `cumDist`, each at the first vertex at/beyond that distance. Returns
empty when the route is shorter than the spacing. **No behavior change** — same code, new namespace.

**Consumers after the move**:
- *Server* (NEW): `Worker` builds per-route `cumDist` + calls `Generate` once per route (cached with the
  index), then the detector compares snapped `cumDist[snapIndex]` against each `TriggerPoint.AlongDistanceM`.
- *Client* (KEPT): `TransitMap.ConfigureTrackerForRouteAsync` still calls `Generate` to render checkpoint
  **markers** (`AddTriggerPointMarkersAsync`); it no longer feeds the deleted JS detector.

---

## Relationships

```
Route geometry (RoutePoint[])  ──build──►  cumDist[]  ──Generate──►  TriggerPoint[]   (server + client, identical)
        │                                      │                          │
        │ snap (RouteSnapper)                  │ cumDist[snapIndex]        │ AlongDistanceM
        ▼                                      ▼                          ▼
   per-vehicle snap index  ──────────►  along-distance  ──compare──►  crossed triggers
                                                                          │
                                                                          ▼
                                                       RouteCrossingRecord (VehicleId, RouteId,
                                                                  TriggerIndex, TotalTriggers)
                                                                          │ EventEnvelope → PublishBatch
                                                                          ▼
                                              client maps → CrossingEventDto[] → OnCrossingsAsync
                                                                          │
                                                  ┌───────────────────────┼───────────────────────┐
                                                  ▼                       ▼                       ▼
                                            pulse (visibility)     crossing trail (visibility)   note (mute)
                                                  └──────────── all gated by route filter (effectiveIds) ──────────┘
```
