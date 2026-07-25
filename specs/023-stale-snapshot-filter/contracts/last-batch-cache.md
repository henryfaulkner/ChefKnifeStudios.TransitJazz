# Contract: `ILastBatchCache` behavior

The **public interface is unchanged**. This contract specifies the new *behavioral* guarantees of the `LastBatchCache` implementation behind that interface, plus the unchanged REST/SignalR contracts it sits behind.

## Interface (UNCHANGED)

```csharp
public interface ILastBatchCache
{
    IReadOnlyList<EventEnvelope> Current { get; }
    void Set(IReadOnlyList<EventEnvelope> batch);
}
```

- `Set` and `Current` keep their exact signatures. Callers (`WorkerTransitHub`, `TransitEndpoints`) compile and behave without edits.

## `Set(batch)` behavioral contract

Given an incoming `batch` (zero or more envelopes), `Set` MUST:

1. For each envelope whose `Payload is RouteNearestPointBatchEvent rnp`, iterate `rnp.BatchRecords`:
   - record with `IsStale == false` ⇒ store/replace `_vehicles[record.VehicleId] = record`.
   - record with `IsStale == true` ⇒ make no change to `_vehicles`.
2. Skip any envelope whose payload is not a `RouteNearestPointBatchEvent` (defensive; never occurs today).
3. Rebuild `_current`:
   - `_vehicles` empty ⇒ `_current = Array.Empty<EventEnvelope>()`.
   - else ⇒ `_current` = single envelope `EventEnvelope(nameof(RouteNearestPointBatchEvent), DateTimeOffset.UtcNow, new RouteNearestPointBatchEvent(_vehicles.Values))`.
4. Perform steps 1–3 atomically under a lock; publish `_current` via `Volatile.Write`.

Postconditions:
- No snapshot record has `IsStale == true`.
- No snapshot envelope has empty `BatchRecords`.
- A `batch` that contributes no non-stale records leaves `_current` byte-identical to before the call.

## `Current` behavioral contract

- Returns the most recently published `_current` via `Volatile.Read`.
- Never returns `null`; before any `Set`, returns `Array.Empty<EventEnvelope>()`.
- Never returns a torn/partial state: a concurrent `Set` either has or has not published; readers see one or the other whole snapshot.

## Accept / reject vectors

| # | Input sequence | Expected `Current` |
|---|----------------|--------------------|
| A | (no `Set`) | empty list |
| B | `Set([env{v1 non-stale}])` | 1 envelope, `BatchRecords` = [v1] |
| C | `Set([env{v1 stale}])` (v1 never seen non-stale) | empty list (v1 dropped) |
| D | `Set([env{v1 non-stale, v2 stale}])` | 1 envelope, `BatchRecords` = [v1] only |
| E | `Set([env{v1 non-stale}])` then `Set([env{v1 stale}])` | 1 envelope, v1 at its **non-stale** position (retained) |
| F | `Set([env{v1 non-stale @posA}])` then `Set([env{v1 non-stale @posB}])` | 1 envelope, v1 @posB (latest wins) |
| G | `Set([env{v1 non-stale}])` then `Set([env{v2 non-stale}])` | 1 envelope, `BatchRecords` = [v1, v2] (cross-batch retention) |
| H | `Set([env{v1 non-stale}])` then `Set([])` (empty batch) | unchanged: 1 envelope, [v1] |
| I | `Set([env{v1 non-stale}])` then `Set([env{v1 stale, v2 stale}])` (all-stale) | unchanged: 1 envelope, [v1] |
| J | any non-empty `Current` | every envelope's `BatchRecords` is non-empty |

## Unchanged surrounding contracts

### `GET /transit/last-batch` (REST)
- Still returns `Results.Ok(cache.Current)` as `IEnumerable<EventEnvelope>`, HTTP 200, `AllowAnonymous`. Same JSON shape; now guaranteed stale-free and empty-envelope-free.

### `WorkerTransitHub.PublishBatch` (SignalR)
- Still calls `_lastBatchCache.Set(batch)` then `_clientHub.Clients.All.SendAsync("ReceiveBatch", batch)`.
- The relayed `batch` is the **full, unmodified** incoming batch (including stale records). Filtering touches only the cached copy. *(FR-009, FR-010)*
