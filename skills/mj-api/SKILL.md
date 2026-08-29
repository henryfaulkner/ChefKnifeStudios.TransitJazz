---
name: mj-api
description: Call the MartaJazz REST API to fetch GTFS route/shape data and check API health/status. Use when the user wants to query routes, fetch route shapes, inspect API keys, check if the API is running, or test a MartaJazz endpoint.
---

# Call MartaJazz API

## Base URLs

| Environment | Base URL |
|---|---|
| Production (default) | `https://marta-jazz-dev-ca-server.jollytree-dd5ca774.eastus2.azurecontainerapps.io` |
| Local dev | `https://localhost:5001` |

## Endpoints

### Health / Debug
- `GET /gtfs/debug/keys` — returns all ordered GTFS key strings (use to verify data is loaded)
- `GET /scalar` — Scalar API reference UI (browser only)
- `GET /openapi/v1.json` — raw OpenAPI spec

### GTFS Routes
- `GET /gtfs/routes/shapes` — all route shapes as GeoJSON `RouteShapeFeature[]`
- `GET /gtfs/routes/{routeId}/shape` — single route shape by ID; returns 404 if not found, 503 if data unavailable

### SignalR / Test
- `GET /test/signalr` — triggers a SignalR broadcast to all transit hub clients

## Quick start

```powershell
# Check API is up and data is loaded
(Invoke-WebRequest -Uri "https://marta-jazz-dev-ca-server.jollytree-dd5ca774.eastus2.azurecontainerapps.io/gtfs/debug/keys" -UseBasicParsing).Content

# Fetch all route shapes
(Invoke-WebRequest -Uri "https://marta-jazz-dev-ca-server.jollytree-dd5ca774.eastus2.azurecontainerapps.io/gtfs/routes/shapes" -UseBasicParsing).Content

# Fetch a single route shape
(Invoke-WebRequest -Uri "https://marta-jazz-dev-ca-server.jollytree-dd5ca774.eastus2.azurecontainerapps.io/gtfs/routes/110/shape" -UseBasicParsing).Content
```

> Use `curl -k` with `-k` only on localhost (self-signed cert). For local dev swap the base URL to `https://localhost:5001`.
> `curl` silently fails in this shell — always use `Invoke-WebRequest -UseBasicParsing`.

## RouteShapeFeature schema

```json
{
  "type": "Feature",
  "geometry": { "coordinates": [[lon, lat], ...] },
  "properties": {
    "routeId": "110",
    "routeShortName": "110",
    "color": "#FF6600",
    "textColor": "#FFFFFF"
  }
}
```

## Workflows

### Verify the API is healthy
1. `GET /gtfs/debug/keys` — non-empty list means GTFS data loaded successfully
2. If empty or 503 → data worker may not have run; check TransitDataWorker logs

### Fetch and inspect a route
1. Call `GET /gtfs/routes/shapes` to enumerate all available route IDs
2. Pick a `routeId` from the response properties
3. Call `GET /gtfs/routes/{routeId}/shape` for that route's geometry

### Hit local dev instead of prod
- Replace the prod base URL with `https://localhost:5001`
- Add `-k` flag if using `curl` (self-signed cert)
- No auth headers required in either environment
