# Contract: SignalR transport (city routing)

## Worker → WebAPI (worker is a SignalR client)

**Method**: `PublishBatch`

| | Before | After |
|---|---|---|
| `ITransitHubPublisher.PublishBatchAsync` | `(List<EventEnvelope> batch, CancellationToken ct)` | `(string city, List<EventEnvelope> batch, CancellationToken ct)` |
| `SignalRHubPublisher` invoke | `InvokeAsync("PublishBatch", batch, ct)` | `InvokeAsync("PublishBatch", city, batch, ct)` |
| `WorkerTransitHub.PublishBatch` | `(List<EventEnvelope> batch)` | `(string city, List<EventEnvelope> batch)` |

`WorkerTransitHub.PublishBatch(city, batch)` body:
```csharp
_lastBatchCache.Set(city, batch);
await _clientHub.Clients.Group(city).SendAsync("ReceiveBatch", batch);
```

## WebAPI → Client (clients connect to TransitHub)

**New hub method**: `JoinCity`
```csharp
public async Task JoinCity(string city)
{
    await Groups.AddToGroupAsync(Context.ConnectionId, city);
    var current = _lastBatchCache.Current(city);   // immediate replay (FR-012 / SC-007)
    if (current.Count > 0)
        await Clients.Caller.SendAsync("ReceiveBatch", current);
}
```

**Client receive**: `ReceiveBatch` handler is **unchanged** (`List<EventEnvelope>`). `EventEnvelope`
stays city-free (Q2).

**Client call**: `SignalRNotificationService`, after `_hubConnection.StartAsync()`:
```csharp
await _hubConnection.InvokeAsync("JoinCity", city);   // city from URL, default "marta"
```
On reconnect (`Reconnected`), re-invoke `JoinCity(city)` so group membership survives a drop.

## Cache contract (`ILastBatchCache`)

| Member | Before | After |
|---|---|---|
| read | `IReadOnlyList<EventEnvelope> Current` | `IReadOnlyList<EventEnvelope> Current(string city)` |
| write | `void Set(IReadOnlyList<EventEnvelope> batch)` | `void Set(string city, IReadOnlyList<EventEnvelope> batch)` |

Backed by `Dictionary<string, LastBatchCache>` (one per-vehicle upsert map per city). The per-vehicle
upsert + stale-skip logic inside each city's map is unchanged from today.

## Invariants (testable)

- **INV-T1 (isolation)**: A client in group `wmata` receives zero `marta` batches and vice versa. (SC-001)
- **INV-T2 (replay)**: A client invoking `JoinCity(c)` with a non-empty `Current(c)` receives one
  immediate `ReceiveBatch` with that city's current vehicles. (SC-007)
- **INV-T3 (collision-free cache)**: Identical `vehicleId` published under two cities upserts into
  two separate maps; neither overwrites the other. (FR-011)
- **INV-T4 (reconnect)**: After a transport drop + reconnect, the client is still in its city group.
