# Contract: Feed Adapter Output → Reconciliation (outbound)

**Direction**: `RailRealtimeAdapter` → `Worker.ProcessSpatialReconciliationAsync` (via merge).
**Goal**: The reconciliation loop MUST NOT be able to distinguish trains from buses. The adapter
emits the **existing** `FeedEntity` shape (`GtfsRtModels.cs`), and the merge is purely additive.

## Adapter interface (NEW)

```csharp
public interface IRailRealtimeAdapter
{
    // Best-effort: never throws; returns empty on failure or when disabled.
    Task<IReadOnlyList<FeedEntity>> FetchAsync(CancellationToken ct);
}
```

## Emitted `FeedEntity` contract (per de-duped train)

Only the fields the reconciliation loop reads are populated (see `Worker.cs:142-373`):

| Field read by reconciliation | Adapter MUST set | Value |
|------------------------------|------------------|-------|
| `entity.Id` | yes | `TRAIN_ID` |
| `entity.Vehicle` | yes | non-null `VehiclePosition` |
| `entity.Vehicle.Position` | yes | non-null (else the loop `continue`s at `Worker.cs:146`) |
| `entity.Vehicle.Position.Latitude/Longitude` | yes | parsed live coords (float) |
| `entity.Vehicle.Position.Speed` | optional | `null` (tolerated; bus path also omits ~40%) |
| `entity.Vehicle.Position.Bearing` | optional | `null` |
| `entity.Vehicle.Trip.RouteId` | yes | `LINE` — MUST match an `_routeIndex` key (`RED/GOLD/BLUE/GREEN`) |
| `entity.Vehicle.Vehicle.Id` | yes | `TRAIN_ID` (falls back to `entity.Id` if absent — keep consistent) |
| `entity.Vehicle.Timestamp` | recommended | `EVENT_TIME` as Unix seconds (drives staleness at `Worker.cs:197`) |

## Merge contract (in `Worker.ExecuteAsync`)

```text
busFeed  = await FetchGtfsRtFeedAsync(ct)        // FeedMessage? (null on failure)
railEnts = await railAdapter.FetchAsync(ct)      // IReadOnlyList<FeedEntity> (empty on failure)

merged   = busFeed ?? new FeedMessage()          // tolerate null bus feed
merged.Entities.AddRange(railEnts)               // additive only

if (merged.Entities.Count > 0 && _routeIndex != null)
    await ProcessSpatialReconciliationAsync(merged, ct)
```

## Invariants (verifiable)

| # | Invariant | Maps to |
|---|-----------|---------|
| I1 | With rail `Enabled=false` (or empty feed), `merged.Entities` equals the bus feed exactly. | FR-009 / SC-004 |
| I2 | A rail-fetch failure never propagates an exception into `ExecuteAsync`; buses still process. | FR-008 / SC-005 |
| I3 | Each train contributes exactly one entity per cycle. | FR-003 / SC-001 |
| I4 | Every emitted `RouteId` is a key present in `_routeIndex` (else the train is `skippedUnknownRoute`, surfacing a wiring bug). | FR-002 / SC-003 |
| I5 | Train vehicle IDs and route keys never collide with bus IDs/keys in `_vehicleStateCache`. | (design §4.2) |
| I6 | No API key appears in committed config; app starts from env/secrets (or keyless). | FR-012 / SC-007 |

## Non-goals (explicit)

- No change to `ProcessSpatialReconciliationAsync`, `RouteSnapper`, telemetry event args, SignalR,
  or any client file.
- No ETA-paced motion, derived speed, or rail-distinct voice family in v1.
