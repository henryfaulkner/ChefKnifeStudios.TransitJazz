<!-- last verified: 2026-06-07 -->

# MartaJazz API Schema Reference

The REST API served by `ChefKnifeStudios.MartaJazz.Server.WebAPI`. Use this when
cross-referencing telemetry `route_id` values against live GTFS data, or verifying
that route shapes are loaded and healthy.

See [mj-api-query-guide.md](mj-api-query-guide.md) for how to call the API.

## Base URLs

| Environment | Base URL |
|---|---|
| Production (default) | `https://marta-jazz-dev-ca-server.jollytree-dd5ca774.eastus2.azurecontainerapps.io` |
| Local dev | `https://localhost:5001` |

---

## Endpoints

### `GET /gtfs/debug/keys`

Returns the ordered list of all GTFS keys the server has loaded.

**Response:** `string[]`  
**Example value:** `["__gtfs_static_ready__", "110", "26903", ...]`

Use to confirm:
- GTFS data is loaded (`__gtfs_static_ready__` present in the array)
- Which `routeId` values exist (telemetry `route_id` values map to these keys)

Status codes: `200 OK`.

---

### `GET /gtfs/routes/shapes`

All route shapes as a GeoJSON feature array.

**Response:** `RouteShapeFeature[]`

---

### `GET /gtfs/routes/{routeId}/shape`

Single route shape by ID.

**Response:** `RouteShapeFeature`  
**Status codes:** `200 OK`, `404 Not Found` (unknown route), `503 Service Unavailable` (GTFS data not loaded)

---

## `RouteShapeFeature` schema

```json
{
  "type": "Feature",
  "geometry": {
    "coordinates": [[lon, lat], ...]
  },
  "properties": {
    "routeId": "110",
    "routeShortName": "110",
    "color": "#FF6600",
    "textColor": "#FFFFFF"
  }
}
```

| Field | Type | Notes |
|-------|------|-------|
| `properties.routeId` | string | Matches `route_id` in the `snap` telemetry dataset |
| `properties.routeShortName` | string | Human-readable route label |
| `properties.color` | string | Hex color with `#` prefix |
| `properties.textColor` | string | Hex color with `#` prefix |
| `geometry.coordinates` | `[lon, lat][]` | Longitude first (GeoJSON convention) |

---

## Relationship to telemetry

The telemetry `snap.route_id` and `lerp.prior_route_id` values are **the same identifiers**
as `RouteShapeFeature.properties.routeId`. This means:

- A `route_id` appearing in telemetry but **missing from `/gtfs/debug/keys`** = a GTFS
  mapping gap (corresponds to `buses_skipped_unknown_route > 0` in `cycle`).
- A 503 from the shapes endpoint = GTFS data not loaded = expect high
  `buses_skipped_unknown_route` in telemetry until the worker restarts.
