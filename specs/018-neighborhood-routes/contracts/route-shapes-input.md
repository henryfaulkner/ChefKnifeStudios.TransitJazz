# Contract: Consumed Input — `GET /gtfs/routes/shapes`

The tool consumes (does not own) the MJ API route-shapes endpoint. This documents the shape the parser depends on.

## Request

```
GET {api}/gtfs/routes/shapes
```

- Public, unauthenticated GET (no key/secret — constitution II is satisfied: nothing secret is introduced).
- Timeout ≥ 30s (Azure Container App may cold-start).

## Response (expected)

A JSON **array** of `RouteShapeFeature` objects:

```json
[
  {
    "type": "Feature",
    "geometry": {
      "type": "LineString",
      "coordinates": [[-84.28052, 33.90255], [-84.28041, 33.90260]]
    },
    "properties": {
      "routeId": "26922",
      "routeShortName": "39",
      "color": "#FF6600",
      "textColor": "#FFFFFF"
    }
  }
]
```

## Fields the tool reads

| Path | Type | Used as |
|------|------|---------|
| `[].geometry.coordinates` | `[[lon, lat], …]` (WGS84) | shapely `LineString` for the join |
| `[].properties.routeId` | string | lean `routes[].routeId` |
| `[].properties.routeShortName` | string | lean `routes[].routeShortName` |

`color` / `textColor` are ignored.

## Facts / assumptions

- ~86 routes as of 2026-06-14.
- Coordinates are `[longitude, latitude]` (WGS84) — same frame as the GeoJSON, so no reprojection (research D3).

## ⚠️ Verify-at-implementation

A live probe of this endpoint could not be completed during planning (non-interactive shell). Before trusting output, the implementer MUST confirm against a real response:
1. Top level is a bare array (vs. a wrapper like `{ "features": [...] }`). If wrapped, adapt the parser.
2. Property names are exactly `routeId` / `routeShortName`.
3. `geometry.type` is `LineString` (handle/skip any non-LineString gracefully).

The parser MUST fail loudly (clear error, non-zero exit, no output files) if the shape differs — it MUST NOT silently produce an empty/zero-match result (FR-014).
