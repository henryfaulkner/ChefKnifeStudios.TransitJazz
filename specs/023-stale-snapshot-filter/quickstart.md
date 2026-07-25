# Quickstart: Stale Snapshot Filter

Server-side only. One production file, one test file.

## Files

- **Modify**: `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/SignalR/ILastBatchCache.cs` — rewrite the `LastBatchCache` class body (interface unchanged).
- **Modify**: `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI.Tests/LastBatchCacheTests.cs` — richer factory, rewrite 3 inverted tests, add 6 new tests.
- **Do NOT touch**: `WorkerTransitHub.cs`, `TransitEndpoints.cs`, `Program.cs`, `WorkerTransitHubTests.cs`, any Shared record, or any client file.

## Implementation sketch (`LastBatchCache`)

```csharp
public sealed class LastBatchCache : ILastBatchCache
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RouteNearestPointBatchEvent.RouteNearestPointRecord> _vehicles = new();
    private IReadOnlyList<EventEnvelope> _current = Array.Empty<EventEnvelope>();

    public IReadOnlyList<EventEnvelope> Current => Volatile.Read(ref _current);

    public void Set(IReadOnlyList<EventEnvelope> batch)
    {
        lock (_gate)
        {
            if (batch is not null)
            {
                foreach (var env in batch)
                {
                    if (env?.Payload is not RouteNearestPointBatchEvent rnp) continue;
                    foreach (var rec in rnp.BatchRecords)
                    {
                        if (rec.IsStale) continue;          // ignore stale (never overwrite, never seed)
                        _vehicles[rec.VehicleId] = rec;      // upsert latest non-stale
                    }
                }
            }

            _current = _vehicles.Count == 0
                ? Array.Empty<EventEnvelope>()
                : new[]
                {
                    new EventEnvelope(
                        nameof(RouteNearestPointBatchEvent),
                        DateTimeOffset.UtcNow,
                        new RouteNearestPointBatchEvent(_vehicles.Values.ToList()))
                };
            // Volatile.Write not strictly required inside the lock for writers, but keep reads lock-free:
            Volatile.Write(ref _current, _current);
        }
    }
}
```

Notes:
- `Set(null)` is tolerated (the null guard skips the merge) and rebuilds from current `_vehicles`; if `_vehicles` is empty, `Current` stays empty — preserving the existing `Set_Null_YieldsEmptyNonNull` intent.
- `_vehicles.Values.ToList()` snapshots the values so the published `RouteNearestPointBatchEvent` is independent of later dictionary mutation.

## Test plan (`LastBatchCacheTests`)

**Factory**: add an overload that builds a batch from explicit records, letting each test control `VehicleId`, position, and `IsStale`. Keep the existing `MakeBatch(params string[])` for untouched tests.

**Rewrite (semantics inverted by the merge):**
- `Set_Then_Current_ReturnsSameBatch` → assert v1 is **present and non-stale** in the snapshot (content, not reference identity).
- `Set_Twice_LatestWins` → assert merge semantics: after `Set(v1)` then `Set(v2)`, snapshot contains **both** v1 and v2.
- `Concurrent_SetAndRead_NeverTornOrNull` → assert each read is non-null and every record is non-stale and belongs to some written batch.

**Add (DoD coverage — map to contract vectors):**
1. All-stale first batch → `Current` empty (vector C).
2. All-stale **after** a good batch → prior snapshot intact (vector I) — the headline edge case.
3. Mixed batch → only non-stale survive, no stale, no empty envelope (vector D).
4. Per-vehicle retention across batches: `Set(v1 non-stale)` then `Set(v1 stale)` → v1 retained (vector E); `Set(v1)`+`Set(v2)` → both present (vector G).
5. Upsert latest-wins: v1 @posA then v1 @posB → @posB (vector F).
6. No-empty-envelope invariant: whenever `Current.Count > 0`, every envelope's `BatchRecords` is non-empty (vector J).

**Keep untouched:** both `WorkerTransitHubTests` (hub still calls `Set(batch)` and relays; the fake cache models neither merge nor filtering, so both tests stay green).

## Verify

```powershell
dotnet build src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/ChefKnifeStudios.MartaJazz.Server.WebAPI.csproj
dotnet test  src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI.Tests/ChefKnifeStudios.MartaJazz.Server.WebAPI.Tests.csproj
```

Definition of done: build green; all existing + new tests pass; `GET /transit/last-batch` returns no stale records and no empty envelopes; live `ReceiveBatch` still carries the full batch.
