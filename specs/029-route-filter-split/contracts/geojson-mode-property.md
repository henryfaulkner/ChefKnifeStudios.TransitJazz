# Contract: GeoJSON `mode` Property

The route-shapes REST endpoint (`GET /…/route-shapes`) returns `RouteShapeFeature` GeoJSON. This
feature adds a `mode` property carrying the static transit mode.

## Producer — `GtfsStaticLoader.BuildLineStringFeature` (Server)

`route_type` is read in `ParseRouteMetadata` and threaded into `BuildLineStringFeature`, which
appends `mode` to the hand-serialized `properties` object:

```json
{
  "type": "Feature",
  "geometry": { "type": "LineString", "coordinates": [[lon, lat], ...] },
  "properties": {
    "routeId": "26932",
    "routeShortName": "RED",
    "color": "#cc0000",
    "textColor": "#ffffff",
    "mode": "Rail"
  }
}
```

- `mode` ∈ `{ "Rail", "Bus" }` (string form of `TransitMode`).
- `route_type == 1` → `"Rail"`; any other or missing value → `"Bus"`.

## Consumer — Client deserialization

`HttpService` deserializes with `JsonStringEnumConverter` + camelCase (via `JsonSettings.ApplyTo`),
so `"mode":"Rail"` maps to `TransitMode.Rail` with no extra configuration.

## Backward compatibility

- A payload **without** `mode` deserializes to the record default `TransitMode.Bus` — old caches and
  bus routes (which omit/zero `route_type`) classify as Bus. No breakage.

## Accept / reject vectors

| `route_type` in routes.txt | Emitted `mode` | Resulting section |
|---|---|---|
| `1` | `"Rail"` | Rail |
| `3` (bus) | `"Bus"` | Buses |
| empty / column absent | `"Bus"` | Buses |
| `0` / `2` / other | `"Bus"` | Buses |
