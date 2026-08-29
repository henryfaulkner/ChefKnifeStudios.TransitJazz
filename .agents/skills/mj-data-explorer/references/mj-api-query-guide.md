<!-- last verified: 2026-06-07 -->

# MartaJazz API Query Guide

How to call the MartaJazz REST API from within the data explorer. Use this when you
need to cross-reference telemetry findings against live GTFS data — for example, to
confirm a `route_id` exists, or to look up which routes are currently loaded.

See [mj-api-schema.md](mj-api-schema.md) for endpoint and response schemas.

## When to reach for the API

The telemetry datasets (snap/lerp/cycle) tell you *what the worker processed*. The API
tells you *what GTFS data the server currently has*. The two are complementary:

| Question | Source |
|----------|--------|
| "Is GTFS data loaded on the server?" | `GET /gtfs/debug/keys` — non-empty + `__gtfs_static_ready__` present |
| "Does route X exist in the server's GTFS data?" | `GET /gtfs/debug/keys` → look for the routeId |
| "Why are buses on route X skipped?" | `cycle` telemetry → `buses_skipped_unknown_route`, then verify route in API |
| "What routes are available?" | `GET /gtfs/routes/shapes` → enumerate `properties.routeId` values |
| "What does route X's path look like?" | `GET /gtfs/routes/{routeId}/shape` |

## How to call the API (PowerShell — always use `-UseBasicParsing`)

```powershell
$base = "https://marta-jazz-dev-ca-server.jollytree-dd5ca774.eastus2.azurecontainerapps.io"

# Health check
(Invoke-WebRequest -Uri "$base/gtfs/debug/keys" -UseBasicParsing).Content

# All route IDs (extracted)
(Invoke-WebRequest -Uri "$base/gtfs/routes/shapes" -UseBasicParsing).Content |
  ConvertFrom-Json | ForEach-Object { $_.properties.routeId }

# Single route shape
(Invoke-WebRequest -Uri "$base/gtfs/routes/110/shape" -UseBasicParsing).Content
```

> **Note:** `curl` silently fails in this environment — always use `Invoke-WebRequest -UseBasicParsing` instead.
> For local dev, swap `$base` to `https://localhost:5001`.

## Common cross-reference workflows

### Verify a `route_id` from telemetry is recognized by the server

1. From telemetry: filter `snap` for `route_id = '<id>'` to confirm the ID exists in the data.
2. Call `GET /gtfs/debug/keys` and check if the ID appears in the returned array.
3. If missing → GTFS mapping gap; this is the root cause of `buses_skipped_unknown_route`.

### Check whether a 503 from the shapes endpoint explains telemetry gaps

1. Call `GET /gtfs/routes/shapes` — if 503, GTFS data isn't loaded.
2. Correlate: query `cycle` telemetry for `buses_skipped_unknown_route > 0` on the
   same day to see the impact in the data.

### Enumerate routes to focus a telemetry query

1. Call `GET /gtfs/routes/shapes` to get all `routeId` values.
2. Use a specific `route_id` from that list in a `snap` filter:
   `route_id = '110'` (string column — quoted).

## Error handling

| Response | What it means |
|----------|---------------|
| 200 with `[]` from `/gtfs/debug/keys` | Server is up but GTFS not loaded yet |
| 503 from `/gtfs/routes/shapes` | GTFS data unavailable (worker hasn't run or failed) |
| 404 from `/gtfs/routes/{id}/shape` | Route ID not in server's GTFS data |
| No response / silent failure | Use `Invoke-WebRequest -UseBasicParsing`, not `curl` |
