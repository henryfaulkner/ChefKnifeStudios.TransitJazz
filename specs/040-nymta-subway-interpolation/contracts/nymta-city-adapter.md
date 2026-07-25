# Contract: `NymtaCity : ITransitCity`

A bespoke adapter (sibling of `MartaCity`) that fetches ~8 subway RT feeds, synthesizes a
`Position` per train, and merges into one normalized `FeedMessage`. Implements the existing
`ITransitCity` contract with no interface change.

## Interface surface

```csharp
public string Name => CityNames.Nymta;      // "nymta"
public bool EmitsTelemetry => false;         // telemetry stays MARTA-only (design §7)
public Task<FeedMessage> FetchVehiclesAsync(CancellationToken ct);
```

## `FetchVehiclesAsync` behavior contract

1. **Ensure offset table** — if `_table == null` OR `now - _fetchedAtUtc > 24h`, fetch
   `GET /gtfs/subway/stop-offsets?city=nymta` via the `"RouteShapeApi"` named `HttpClient`
   (same client `Worker` uses), build `StopOffsetTable`, set `_fetchedAtUtc`. On fetch failure
   with a non-null cached table → keep the cache (log a warning). On fetch failure with a null
   table → return an empty `FeedMessage` (the tick is a no-op; next tick retries).
   - **INV-N1 (Principle VII)**: the table is fetched at most once per 24 h; **never** per tick,
     **never** recomputed on the worker's hot path.

2. **Fan-out** — for each of the configured `GtfsRtUrls` (the 8 line groups), in its own
   try/catch: fetch, stream-decode (`ProtoBuf.Serializer.Deserialize<FeedMessage>(stream)` —
   NEVER `.Content` string read), run synthesis on each entity, add results to the merged feed.
   - **INV-N2 (FR-010 / SC-006)**: one failing feed logs and is skipped; other feeds' trains
     still appear. (Mirrors `GtfsRtCity.cs:23-39`.)

3. **Synthesize** each entity per `interpolation-algorithm.md`. Entities that can't be placed
   (unknown station, or route with no offset set) are **dropped** and counted, not emitted.
   - **INV-N3 (FR-008)**: every emitted `FeedEntity` carries `Vehicle.Trip.RouteId` (line label),
     a real `Vehicle.Position`, `Vehicle.Timestamp` (from the source entity), and
     `Vehicle.Vehicle.Id` (train id) — identical shape to `MartaCity.cs:97-107`, so the shared
     loop treats it as a bus.

4. **Merge & return** one `FeedMessage`. Log per-cycle counters:
   `synthesizedStopped`, `synthesizedInTransit`, `skippedUnknownStation` (structured, mirroring
   `Worker`'s `skippedUnknownRoute` log at `Worker.cs:578-580`). No telemetry event posted
   (`EmitsTelemetry => false`; the worker's gate at `Worker.cs:94` already skips it).

## Registration (`Program.cs`)

```csharp
builder.Services.AddSingleton<NymtaCity>();
// in the city-registry factory loop:
else if (string.Equals(cfg.Name, CityNames.Nymta, StringComparison.OrdinalIgnoreCase))
    cities.Add(sp.GetRequiredService<NymtaCity>());
```

`SubwaySynthesisOptions.GtfsRtUrls` bound from the `nymta` `Cities:` entry's `GtfsRtUrls`.

## Fault-isolation invariant (worker-level, unchanged)

- **INV-N4**: `NymtaCity` throwing does not block other cities — guaranteed by the existing
  per-city try/catch in `Worker.cs:71-92`. Covered by the same pattern as
  `CityLoopTests.FaultIsolation_ThrowingCity_DoesNotBlockOtherCity`.
