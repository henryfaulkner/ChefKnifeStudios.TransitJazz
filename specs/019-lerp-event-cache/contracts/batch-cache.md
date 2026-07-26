# Contract: Last Batch Cache (in-memory singleton)

## Interface

```csharp
namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.SignalR;

public interface ILastBatchCache
{
    /// Latest relayed batch; empty list before the first publish. Never null.
    IReadOnlyList<EventEnvelope> Current { get; }

    /// Replace the snapshot with the most recently relayed batch.
    void Set(IReadOnlyList<EventEnvelope> batch);
}
```

## Implementation contract

```csharp
public sealed class LastBatchCache : ILastBatchCache
{
    private IReadOnlyList<EventEnvelope> _current = Array.Empty<EventEnvelope>();

    public IReadOnlyList<EventEnvelope> Current => Volatile.Read(ref _current);

    public void Set(IReadOnlyList<EventEnvelope> batch)
        => Volatile.Write(ref _current, batch ?? Array.Empty<EventEnvelope>());
}
```

- Registered: `builder.Services.AddSingleton<ILastBatchCache, LastBatchCache>();` in WebAPI `Program.cs`.
- Atomic reference swap (`Volatile.Read`/`Volatile.Write`) — no lock, lock-free read path.
- Never mutates a stored list in place → no torn reads (INV-3 / FR-008).
- Seeded to an empty array → `Current` never null (INV-1 / FR-004).

## Write-path integration — `WorkerTransitHub.PublishBatch`

`WorkerTransitHub` gains a constructor-injected `ILastBatchCache`. In `PublishBatch`, cache before relaying:

```csharp
public async Task PublishBatch(List<EventEnvelope> batch)
{
    _lastBatchCache.Set(batch);                                   // NEW — cache first
    await _clientHub.Clients.All.SendAsync("ReceiveBatch", batch);
    _logger.LogInformation("Relayed {Count} events from worker", batch.Count);
}
```

Order rationale: caching first guarantees that a client which connects-and-fetches the instant a relay fires never observes a "relayed but not-yet-cached" gap.

## Behaviors (map to FRs / invariants)

| Behavior | FR / INV |
|----------|----------|
| `Current` non-null, empty before first `Set` | FR-004 / INV-1 |
| Each `Set` makes `Current` equal the newest batch | FR-002 / INV-2 |
| Concurrent read during `Set` returns a whole batch | FR-008 / INV-3 |
| Read does no I/O / no upstream fetch | FR-007 / INV-4 |
| Resets to empty on restart | INV-5 |

## Tests

`LastBatchCache` and the `WorkerTransitHub` write-path are covered by `LastBatchCacheTests` and `WorkerTransitHubTests` — full assertions in [`tests.md`](./tests.md). Summary: empty/non-null cold start, latest-wins on successive `Set`, defensive null handling, concurrent set/read never torn (FR-008), and `PublishBatch` caches then relays.
