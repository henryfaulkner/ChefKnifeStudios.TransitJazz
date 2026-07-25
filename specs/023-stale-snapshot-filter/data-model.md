# Data Model: Stale Snapshot Filter

No persisted or shared data types change. The only new structure is **internal** to `LastBatchCache`. Existing shared records are listed read-only for reference.

## Existing types (UNCHANGED — reference only)

### `EventEnvelope` (Shared/Events/EventEnvelope.cs)
```
EventEnvelope(string EventType, DateTimeOffset Timestamp, ISignalREvent Payload)
```
Wrapper for every event. The snapshot reuses this verbatim.

### `RouteNearestPointBatchEvent` (Shared/Events/RouteNearestPointBatchEvent.cs)
```
RouteNearestPointBatchEvent(IEnumerable<RouteNearestPointRecord> BatchRecords) : ISignalREvent
```
The only `ISignalREvent` implementer flowing through this path.

### `RouteNearestPointRecord` (nested)
```
RouteNearestPointRecord(
    string   VehicleId,
    string   RouteId,
    double   PriorNearestLat,
    double   PriorNearestLon,
    DateTime PriorUtcNow,
    double   CurrentNearestLat,
    double   CurrentNearestLon,
    DateTime CurrentUtcNow,
    float?   SpeedMetersPerSec,
    float?   Bearing,
    bool     IsStale)
```
`IsStale == true` ⇒ upstream GTFS-RT delivered the same per-vehicle timestamp as the prior observation (duplicate reading, no new motion). This is the discriminating field for the merge.

## New internal state (inside `LastBatchCache`)

### Per-vehicle accumulator
| Field | Type | Description |
|-------|------|-------------|
| `_vehicles` | `Dictionary<string, RouteNearestPointRecord>` | Keyed by `VehicleId`. Value = the most recent **non-stale** record seen for that vehicle. Mutated only under `_gate`. |
| `_gate` | `object` | Lock guarding the merge-and-rebuild read-modify-write. |
| `_current` | `IReadOnlyList<EventEnvelope>` | Prebuilt immutable snapshot published after each merge. Read via `Volatile.Read`; written via `Volatile.Write` inside the lock. Initialized to `Array.Empty<EventEnvelope>()`. |

### State transitions (per incoming record during `Set`)

| Incoming record | `_vehicles[VehicleId]` exists? | Action |
|-----------------|-------------------------------|--------|
| `IsStale == false` | (either) | **Upsert** — `_vehicles[VehicleId] = record` |
| `IsStale == true` | yes | **Ignore** — keep existing entry |
| `IsStale == true` | no | **Drop** — vehicle absent from snapshot |

After processing all records in the batch:

| `_vehicles` after merge | `_current` rebuilt as |
|-------------------------|------------------------|
| empty | `Array.Empty<EventEnvelope>()` |
| non-empty | one-element list: `[ EventEnvelope(nameof(RouteNearestPointBatchEvent), DateTimeOffset.UtcNow, new RouteNearestPointBatchEvent(_vehicles.Values.ToList())) ]` |

Invariants:
- The snapshot never contains a record with `IsStale == true` (only non-stale records are ever stored). *(FR-001)*
- The snapshot never contains an envelope with zero records. *(FR-002)*
- Each `VehicleId` appears at most once in the snapshot. *(FR-008)*
- An empty or all-stale batch leaves `_vehicles` and therefore `_current` unchanged. *(FR-007)*
- No entry is ever removed except by process restart (no eviction / TTL). *(FR-013)*

## Lifecycle

- **Scope**: `LastBatchCache` is a DI **singleton** (`Program.cs:72`); `_vehicles` accumulates across all batches and all requests for the process lifetime.
- **Reset**: process restart clears `_vehicles` and `_current`; the snapshot repopulates as new non-stale batches arrive.
