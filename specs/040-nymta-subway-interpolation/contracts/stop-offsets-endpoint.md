# Contract: `GET /gtfs/subway/stop-offsets`

Mirrors the existing `GET /gtfs/routes/shapes` (`GtfsEndpoints.cs:75-109`) in shape,
readiness-gate, and `IKeyValueRepository<string>` sourcing. Only the subway city populates it.

## Request

```
GET /gtfs/subway/stop-offsets?city=nymta
```

| Param | Required | Default | Notes |
|-------|----------|---------|-------|
| `city` | no | `nymta` | Only `nymta` returns data today; any other city returns `[]`. |

Endpoint constant: `ApiEndpoints.Gtfs.GetSubwayStopOffsets = "/gtfs/subway/stop-offsets"`.
Registered in `GtfsEndpoints.MapGtfsEndpoints` (same group, auto-mapped by `Program.cs`).

## Responses

| Status | When | Body |
|--------|------|------|
| `200 OK` | offset table built and stored | `SubwayStopOffsetSet[]` (JSON) |
| `200 OK` | subway city configured but no offsets yet stored | `[]` |
| `503 Service Unavailable` | GTFS static data not yet loaded (`ReadyKey` absent) | — |

**200 body** (`SubwayStopOffsetSet[]`, see data-model):

```json
[
  {
    "routeJoinKey": "7",
    "direction": "N",
    "coordinates": [[-73.98,40.75],[-73.96,40.75], ...],
    "cumulativeDistanceMeters": [0, 1723.4, ...],
    "stops": [
      { "stopId": "726N", "lat": 40.7554, "lon": -73.9874, "distanceAlongShapeMeters": 0 },
      { "stopId": "725N", "lat": 40.7519, "lon": -73.9772, "distanceAlongShapeMeters": 1201.6 }
    ]
  }
]
```

## Invariants (asserted by `SubwayStopOffsetBuilderTests`)

- **INV-E1**: `cumulativeDistanceMeters.Length == coordinates.Length`, `[0] == 0`, non-decreasing.
- **INV-E2**: `stops` ordered by `distanceAlongShapeMeters` ascending; every value in
  `[0, last cumulative]`.
- **INV-E3**: a route appearing in both directions yields two sets (`direction` `"N"` and `"S"`).
- **INV-E4**: the endpoint response contains **no** raw `stop_times` rows — only the collapsed
  offset sets (FR-013).
- **INV-E5**: readiness gate identical to `/gtfs/routes/shapes` — `503` before `ReadyKey`.
- **INV-E6**: a route with an empty shape or zero mapped stops is omitted (not a `null`/empty set).

## Server-side production

Built inside `GtfsStaticLoader`'s refresh cycle by `SubwayStopOffsetBuilder`, guarded to the
subway city, stored under `{city}:__subway_offsets__` in `IKeyValueRepository<string>`. Runs on
the same `Gtfs:StaticRefreshHours` cadence as route shapes; a failed subway zip fetch keeps the
last-good offsets (same last-good-wins policy as `GtfsStaticLoader.cs:77-87`).
