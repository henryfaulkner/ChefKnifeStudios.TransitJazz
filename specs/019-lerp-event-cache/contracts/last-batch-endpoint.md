# Contract: Last Batch Snapshot Endpoint

## Route

`GET /transit/last-batch`

Constant: `ApiEndpoints.Transit.GetLastBatch` (new `Transit` nested class in `Shared/ApiEndpoints.cs`).

Registered via a new `TransitEndpoints.MapTransitEndpoints()` group (mirrors `GtfsEndpoints`), mapped in `Program.cs` alongside `MapGtfsEndpoints()`. Anonymous access (same posture as existing GTFS read endpoints).

## Request

No parameters. No body. No auth header required.

## Responses

| Status | When | Body |
|--------|------|------|
| `200 OK` | Always (data present **or** cold start) | `IEnumerable<EventEnvelope>` — the cached batch, or `[]` if no batch has been published yet (FR-004) |

There is intentionally **no** `404`/`204`/`503` path: the cache is always readable and always returns a well-formed list. Reading never fails on "not loaded yet."

### Serialization

Uses the WebAPI's existing configured `System.Text.Json` options (`ConfigureHttpJsonOptions` ← `JsonOptions.Get()`), including the polymorphic `ISignalREvent` converters, so `EventEnvelope.Payload` (e.g. `RouteNearestPointBatchEvent`) deserializes on the client exactly as it does over SignalR.

### Example — data present

```json
[
  {
    "eventType": "RouteNearestPointBatchEvent",
    "timestamp": "2026-06-16T14:03:11.482+00:00",
    "payload": {
      "batchRecords": [
        {
          "vehicleId": "1234",
          "routeId": "74",
          "priorNearestLat": 33.751, "priorNearestLon": -84.39,
          "priorUtcNow": "2026-06-16T14:03:01.4Z",
          "currentNearestLat": 33.752, "currentNearestLon": -84.389,
          "currentUtcNow": "2026-06-16T14:03:11.4Z",
          "speedMetersPerSec": 8.3, "bearing": 142.0, "isStale": false
        }
      ]
    }
  }
]
```

### Example — cold start

```json
[]
```

## Contract behaviors (map to FRs)

- **C-1**: Returns the most recent batch relayed to clients (FR-001, FR-002).
- **C-2**: Returns `200` + `[]` before any push (FR-004).
- **C-3**: Read performs no upstream fetch (FR-007).
- **C-4**: Concurrent read during a write yields one whole batch (FR-008).
- **C-5**: Body shape matches the live `ReceiveBatch` payload (FR-009).

## Producibility note (`.Produces`)

```
group.MapGet(ApiEndpoints.Transit.GetLastBatch, handler)
     .WithName(nameof(ApiEndpoints.Transit.GetLastBatch))
     .Produces<IEnumerable<EventEnvelope>>(StatusCodes.Status200OK);
```

## Verification note

Endpoint HTTP behavior (routing, `200` + cold-start `[]`, polymorphic `Payload` round-trip) is verified via the **quickstart** (Steps 1–2, 6) — integration tests are out of scope for this feature. Unit tests cover the cache and hub write-path only. See [`tests.md`](./tests.md).
