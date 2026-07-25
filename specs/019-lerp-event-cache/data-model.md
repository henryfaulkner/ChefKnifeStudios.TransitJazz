# Phase 1 Data Model: Last Lerp Event Cache

This feature introduces **no new wire data shapes**. It caches and re-serves an existing one. The only new construct is the in-memory cache holder.

## Reused shape — `EventEnvelope` batch (unchanged)

The cached snapshot is exactly the payload already relayed over SignalR: `List<EventEnvelope>`.

| Field | Type | Notes |
|-------|------|-------|
| `EventType` | `string` | e.g. `"RouteNearestPointBatchEvent"` |
| `Timestamp` | `DateTimeOffset` | When the batch was produced |
| `Payload` | `ISignalREvent` | Polymorphic; for bus motion this is `RouteNearestPointBatchEvent` |

`RouteNearestPointBatchEvent.RouteNearestPointRecord` (unchanged, see `Shared/Events/RouteNearestPointBatchEvent.cs`): `VehicleId`, `RouteId`, `PriorNearestLat/Lon`, `PriorUtcNow`, `CurrentNearestLat/Lon`, `CurrentUtcNow`, `SpeedMetersPerSec?`, `Bearing?`, `IsStale`.

**Serialization contract**: Both SignalR (`JsonSettings.ApplyTo`) and the WebAPI HTTP pipeline (`ConfigureHttpJsonOptions` copying `JsonOptions.Get()`) use the same converters, so the polymorphic `Payload` round-trips identically over WSS and HTTP. The snapshot endpoint MUST NOT introduce its own serializer settings.

## New construct — Last Batch Cache (in-memory)

A single-slot, atomically-swapped holder living as a WebAPI singleton.

| Member | Type | Behavior |
|--------|------|----------|
| `Current` (read) | `IReadOnlyList<EventEnvelope>` | Returns the latest stored snapshot via `Volatile.Read`. Never null — seeded to an empty list. |
| `Set(batch)` (write) | `void` | Replaces the reference via `Volatile.Write`. Called by `WorkerTransitHub.PublishBatch` on every relay. |

### Invariants / validation rules

- **INV-1 (always non-null)**: `Current` is an empty list before the first `Set`, never null (FR-004).
- **INV-2 (latest wins)**: After `Set(b)`, `Current` equals `b` until the next `Set` (FR-002).
- **INV-3 (atomic, no tearing)**: A read concurrent with a `Set` returns either the prior or the new list in whole — never a partially mutated object (FR-008). Achieved by swapping a reference, never mutating a list in place.
- **INV-4 (no upstream fetch)**: Reading `Current` performs no I/O and no call to the Worker or GTFS feed (FR-007).
- **INV-5 (volatile/restart)**: In-memory only; `Current` resets to empty on process restart (spec scope).

### State transitions

```
[start] ──> Current = []            (cold start; endpoint returns 200 + [])
   │
   │  WorkerTransitHub.PublishBatch(b1)
   ▼
Current = b1                         (endpoint returns b1)
   │
   │  WorkerTransitHub.PublishBatch(b2)
   ▼
Current = b2                         (latest wins; endpoint returns b2)
```

## Relationships

- **Worker → WorkerTransitHub**: Worker invokes hub method `PublishBatch`; hub writes cache then relays. (No Worker change.)
- **Cache ← WorkerTransitHub.PublishBatch**: write path.
- **Cache → TransitEndpoints (GET last-batch)**: read path.
- **TransitEndpoints → Client (TransitMap load)**: snapshot delivered once on load, fed to `HandleVehicleBatchAsync` (same path as a live `ReceiveBatch`).
