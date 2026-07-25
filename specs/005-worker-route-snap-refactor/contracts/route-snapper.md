# Contract: RouteSnapper (Shared Geospatial Library)

**Namespace**: `ChefKnifeStudios.TransitJazz.Shared.Geospatial`  
**Assembly**: `ChefKnifeStudios.TransitJazz.Shared`

## Types

### RoutePoint

```
readonly record struct RoutePoint(string RouteId, double Lat, double Lon)
```

Moved from `Server.TransitDataWorker` to `Shared.Geospatial`. Unchanged definition.

### Snap

```
readonly record struct Snap(int Index, RoutePoint Point, double DistanceKm)
```

Result of a nearest-point query. `Index` is the position in the source `RoutePoint[]`.

### RouteSnapper (static class)

#### FindNearest

```
static Snap? FindNearest(double lat, double lon, ReadOnlySpan<RoutePoint> points)
```

Returns the closest point to `(lat, lon)` from `points` using Haversine distance. Returns `null` if `points` is empty.

**Consumers**: `Worker.ProcessSpatialReconciliationAsync`, future validate-snap API endpoint.

#### FindNearestN

```
static Snap[] FindNearestN(double lat, double lon, ReadOnlySpan<RoutePoint> points, int n)
```

Returns the `n` closest points sorted by distance ascending. Used by the validate-snap API endpoint (item 2 in roadmap).

**Consumers**: Future validate-snap API endpoint.

## Dependencies

- `HaversineCalculator` — remains in Worker project for now, or can be moved to Shared alongside RouteSnapper. The refactor should move it to `Shared.Geospatial` since `RouteSnapper` depends on it.

## Backward Compatibility

- `RoutePoint` record struct has the same shape — no breaking changes for existing consumers.
- Worker's `FindNearestRoutePoint` method is removed (internal, no external consumers).
- Worker's `_routeSpatialIndex` field is replaced by `_routeIndex` (internal, no external consumers).
