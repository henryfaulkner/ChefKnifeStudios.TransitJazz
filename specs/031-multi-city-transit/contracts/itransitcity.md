# Contract: ITransitCity (Worker strategy interface)

**Location**: `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Cities/ITransitCity.cs`

```csharp
public interface ITransitCity
{
    /// SignalR group key, KV-store prefix, URL param, telemetry partition. Lowercase, stable.
    string Name { get; }

    /// COMPLETE, NORMALIZED live feed: bus + rail merged, route_ids already remapped to
    /// match the static route index. The loop never knows how this was assembled.
    Task<FeedMessage> FetchVehiclesAsync(CancellationToken ct);

    /// Capability flag — does this city emit snap/lerp/cycle telemetry? (MARTA-only today.)
    bool EmitsTelemetry { get; }
}
```

## Loop contract (Worker.cs — must hold)

```csharp
foreach (var city in _cities)            // injected IEnumerable<ITransitCity>
{
    try
    {
        var feed  = await city.FetchVehiclesAsync(ct);
        var index = _routeIndex[city.Name];                  // per-city index (Q4), loop-owned
        var batch = Reconcile(feed, index, city.EmitsTelemetry);
        await _publisher.PublishBatchAsync(city.Name, batch, ct);  // city param (Q2)
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "City {City} tick failed; other cities unaffected", city.Name);
    }
}
```

## Invariants (testable)

- **INV-1 (no name branching)**: The loop branches only on `EmitsTelemetry` (declared) or on the
  returned `FeedMessage` shape — never on `city.Name` via `if`/`switch`. (FR-008, Principle anti-drift)
- **INV-2 (fault isolation)**: An exception in one city's `FetchVehiclesAsync`/reconcile is caught
  per-city; remaining cities still process and publish this tick. (FR-009 / SC-005)
- **INV-3 (telemetry gate)**: `PostEvent(...)` for snap/lerp/cycle executes iff `city.EmitsTelemetry`
  is true. (FR-015 / Q6)
- **INV-4 (normalized feed)**: `FetchVehiclesAsync` returns route_ids that already match the city's
  static index keys; no remap happens in the loop. (Q7)

## Acceptance vectors

| Scenario | Expectation |
|---|---|
| Two cities registered, both feeds healthy | Both publish to their own group this tick. |
| City A feed throws | A skipped + error logged; City B publishes normally. |
| MARTA (`EmitsTelemetry=true`) | Snap/lerp/cycle events posted. |
| WMATA (`EmitsTelemetry=false`) | Zero telemetry events posted. |
| WMATA rail vehicle with `route_id=BLUE` + `RailRouteIdMap` | Returned feed has `route_id=B`, matching `wmata:B` index. |
