# Data Model: Map Render Performance — Tranche 2

**Date**: 2026-06-20

---

## Entities

### 1. Simplified Route Shape (server-side, in-memory)

Produced by `GtfsStaticLoader` during GTFS ingest. Stored as a JSON string in `IKeyValueRepository<string>`.

**Before tranche 2**: `List<(double Lat, double Lon, int Seq)>` with ~1,298 points/route average, up to 22,936 max.

**After tranche 2**: Same type, passed through `Simplify(points, 10.0)`. Expected result: ~100–400 points/route average, with the payload shrinking from 2.4 MB to ~0.3–0.5 MB.

**Contract unchanged**: `RouteShapeFeature` JSON shape is identical — `geometry.coordinates` is still `[[lon,lat],...]`; only the array length shrinks.

**Stored format** (unchanged):
```json
{
  "type": "Feature",
  "geometry": { "type": "LineString", "coordinates": [[lon, lat], ...] },
  "properties": {
    "routeId": "26932",
    "routeShortName": "74",
    "color": "#hex",
    "textColor": "#hex"
  }
}
```

---

### 2. All-Routes Interop Payload (new, WASM→JS, ephemeral)

Built in `TransitMap.RenderRoutesAsync`, passed to `ChefMap.addAllRoutes` in a single call. Not persisted anywhere — rebuilt from `_routeShapeCache` each time `RenderRoutesAsync` is called.

```ts
// TypeScript-style shape (the actual payload is an anonymous C# array)
Array<{
    routeId: string,     // route short name (e.g. "74")
    color: string,       // hex color (e.g. "#FF0000"), fallback "#6b7280"
    coordinates: [number, number][]  // [lon, lat] pairs
}>
```

**Size after #1**: ~10–20k coordinate pairs total, in one JSON crossing.

---

### 3. Routes FeatureCollection (JS-side, long-lived)

Built inside `ChefMap.addAllRoutes`, stored as `ChefMap._routesFeatureCollection`. Used by:
- MapLibre `routes` source (`addSource`/`setData`).
- `setMapStyle` restore path.

```js
{
  type: 'FeatureCollection',
  features: [
    {
      id: "74",               // string feature ID — required for feature-state
      type: 'Feature',
      geometry: { type: 'LineString', coordinates: [[lon, lat], ...] },
      properties: {
        routeId: "74",
        color: "#FF0000"
      }
    },
    // ... 85 more
  ]
}
```

**Key design constraint**: `feature.id` must be set (as string) for `map.setFeatureState` to work. MapLibre feature-state is keyed by `{source, id}`.

---

### 4. Feature-State Schema (JS-side, per feature)

Set via `map.setFeatureState({source: 'routes', id: routeId}, state)`.

| State key | Type | Meaning |
|-----------|------|---------|
| `focused` | boolean | Route is in the active selection/hover set — emphasize |
| `dimmed` | boolean | Selection is active but this route is not in it — de-emphasize |

Default (neither state set, or both false): opacity 0.7, own route color. This is the unscoped/no-selection state.

---

### 5. Simplification Tolerance Constant

```csharp
const double SimplifyToleranceMeters = 10.0;
```

Located at the top of `GtfsStaticLoader`. Governs the RDP epsilon. Lowering it produces more points and closer fidelity; raising it produces fewer points with more visible corner-cutting. 10 m is the initial value; verified against checkpoint pulse positions before finalizing.

---

## State Transitions

### Route Geometry Lifecycle (per app load)

```
GTFS zip downloaded
    → ParseShapes → List<(Lat,Lon,Seq)> (dense, ~1298/route avg)
    → Simplify(10m) → List<(Lat,Lon,Seq)> (sparse, ~100-400/route avg)
    → BuildLineStringFeature → JSON string
    → IKeyValueRepository.SetAsync (in-memory)
    → REST GET /gtfs/routes/shapes → RouteShapeFeature[]
    → TransitMap._routeShapeCache (Dictionary<string, RouteShapeFeature>)
    → RenderRoutesAsync → All-Routes Interop Payload (single object)
    → ChefMap.addAllRoutes → Routes FeatureCollection (JS)
    → MapLibre 'routes' source + 'routes-layer' (rendered)
```

### Focus State Transitions

```
No selection active:   all features → focused:false, dimmed:false → opacity 0.7, own color
Selection applied:     focused routes → focused:true → opacity 0.95, own color
                       other routes → dimmed:true → opacity 0.3 (line-color unchanged)
Selection cleared:     all features → focused:false, dimmed:false → opacity 0.7, own color
```
