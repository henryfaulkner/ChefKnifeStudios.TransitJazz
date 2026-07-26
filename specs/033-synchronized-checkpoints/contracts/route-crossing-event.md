# Contract: RouteCrossingBatchEvent

**Type**: SignalR event payload (`ISignalREvent`), Shared project, `Shared/Events/`.
**Transport**: rides inside an `EventEnvelope` in the existing per-city `PublishBatch` — no hub,
publisher, or client-wiring change.

## Shape

```csharp
namespace ChefKnifeStudios.TransitJazz.Shared.Events;

public sealed record RouteCrossingBatchEvent(
    IEnumerable<RouteCrossingBatchEvent.RouteCrossingRecord> BatchRecords
) : ISignalREvent
{
    public sealed record RouteCrossingRecord(
        string VehicleId,
        string RouteId,
        int TriggerIndex,
        int TotalTriggers
    );
}
```

Mirrors `RouteNearestPointBatchEvent`'s nested-record shape so it serializes through the same
`AddJsonProtocol` / `JsonSettings` path with no special handling.

## Envelope

```csharp
var crossingEnvelope = new EventEnvelope(
    nameof(RouteCrossingBatchEvent),
    DateTimeOffset.UtcNow,
    new RouteCrossingBatchEvent(crossingRecords)
);
// Published in the SAME batch as the position envelope:
await transitHubPublisher.PublishBatchAsync(
    city.Name,
    new List<EventEnvelope> { positionEnvelope, crossingEnvelope }, ct);
```

## Invariants (MUST)

1. **Determinism (Principle VIII / FR-003 / FR-006):** `TriggerIndex` = `TriggerPoint.Index` and
   `TotalTriggers` = the route's trigger-point count, both from the **shared** `TriggerPointGenerator`.
   These are the exact values the client's generator produces for the same route → identical note.
2. **Route key:** `RouteId` is the per-city route key (`route_short_name`), identical to the value the
   client uses as its `_routeShapeCache` key and the `OnCrossingsAsync` filter key.
3. **Ordering:** records SHOULD be sorted `(RouteId, VehicleId, TriggerIndex)` (parity with the deleted
   client sort).
4. **Empty cycles:** if no crossings are detected, **no** `RouteCrossingBatchEvent` is added to the
   publish (do not emit an empty-list envelope).
5. **Reconnect (FR-005):** because `LastBatchCache` retains only `RouteNearestPointBatchEvent`, this
   event is never cached or replayed. (Asserted, not enforced here.)

## Accept / reject vectors

| Scenario | Expectation |
|---|---|
| Vehicle moves forward past 2 trigger points in a cycle | 2 `RouteCrossingRecord`s for that vehicle |
| Vehicle first seen this cycle | 0 records for it (baseline seeded) |
| Vehicle reverses / no movement | 0 records |
| Vehicle teleports (>2000m snap jump) | 0 records (baseline reset) |
| Vehicle changes route | 0 records on the transfer cycle |
| Same `(VehicleId, TriggerIndex)` already emitted last cycle, no new forward progress past it | not re-emitted |
| New client reconnects mid-session | replay carries position records only; 0 crossings |
