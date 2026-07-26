# Phase 1 Data Model: NYC Subway Position Interpolation

Entities are grouped by where they live: **Shared** (cross the WebAPI↔Worker boundary),
**WebAPI-internal** (transient, discarded after the offset table is built), and
**Worker-internal** (cached synthesis state). Existing types reused as-is are noted, not
redefined.

---

## Shared DTOs (WebAPI produces, Worker consumes — `src/ChefKnifeStudios.TransitJazz.Shared/GtfsData/`)

### `SubwayStopOffset` (new file `SubwayStopOffset.cs`)

The endpoint payload: per route+direction, the ordered stations with coordinate and
cumulative distance along that route's interpolation polyline.

```csharp
public sealed record SubwayStopOffsetSet(
    string RouteJoinKey,                 // line label, e.g. "A", "6", "7X" (== Trip.RouteId for NYC subway)
    string Direction,                    // "N" | "S" (from stop_id suffix); routes served once per direction
    double[][] Coordinates,              // [ [lon,lat], ... ] the polyline the worker interpolates on
    double[] CumulativeDistanceMeters,   // parallel to Coordinates; [0]=0, monotonic increasing
    SubwayStop[] Stops                   // ordered stops along this route+direction
);

public sealed record SubwayStop(
    string StopId,                       // platform code incl. direction suffix, e.g. "H13S"
    double Lat,
    double Lon,
    double DistanceAlongShapeMeters      // where this stop sits on CumulativeDistanceMeters
);
```

**Why the polyline travels with the offsets** (design decision D1): the worker must interpolate
on the **same** polyline the server measured `DistanceAlongShapeMeters` against, or the two
drift (a simplified vs. raw geometry mismatch would place trains off-line). Serving
`Coordinates` + `CumulativeDistanceMeters` together with the stops closes that gap and lets
`NymtaCity` avoid depending on the worker's separate `_routeCumDist` (which is keyed differently
and built after synthesis). The `Coordinates` are the (optionally simplified) shape used for both
measurement and interpolation — one source of truth per route.

**Validation rules**:
- `CumulativeDistanceMeters.Length == Coordinates.Length`, `[0] == 0`, strictly non-decreasing.
- `Stops` ordered by `DistanceAlongShapeMeters` ascending; each in `[0, last cumulative]`.
- `Direction ∈ {"N","S"}`; a route with both directions yields two `SubwayStopOffsetSet`s.
- Empty `Stops` or empty `Coordinates` → the set is omitted from the payload (route unusable).

### Constants (existing files)

- `CityNames.Nymta = "nymta"` — `Shared/CityNames.cs`.
- `ApiEndpoints.Gtfs.GetSubwayStopOffsets = "/gtfs/subway/stop-offsets"` — `Shared/ApiEndpoints.cs`.

---

## WebAPI-internal (transient — `GtfsStatic/SubwayStopOffsetBuilder.cs`)

These exist only during the `GtfsStaticLoader` refresh cycle and are **discarded** after the
`SubwayStopOffsetSet[]` is built and stored (FR-013: raw `stop_times.txt` never leaves the server).

| Type | Fields | Lifetime |
|------|--------|----------|
| `StopRow` | `StopId`, `Lat`, `Lon` (from `stops.txt`) | discarded after ordering |
| `StopTimeRow` | `TripId`, `StopSequence`, `StopId` (from `stop_times.txt`) | discarded after collapse |
| `RouteDirKey` | `(RouteJoinKey, Direction)` grouping key | build-time only |

**Collapse algorithm** (build-time, once per refresh):
1. Parse `stops.txt` → `Dictionary<stopId, (lat,lon)>` (reuse `SplitCsvLine`, header-index idiom).
2. Parse `stop_times.txt` → per-trip ordered `stop_id` list (group by `trip_id`, order by
   `stop_sequence`). Millions of rows streamed once; keep only the distinct ordered stop
   sequence per `(route, direction)` (derive route from `trips.txt` `trip_id→route_id`, already
   parsed at `GtfsStaticLoader.cs:198-223`; direction from `stop_id` suffix).
3. For each `(route, direction)`: take the route's shape polyline (already produced), build its
   `CumulativeDistanceMeters` (Haversine, same as `Worker.cs:259-262`), and for each ordered
   stop find its nearest polyline vertex → `DistanceAlongShapeMeters`.
4. Emit `SubwayStopOffsetSet`; discard all `StopRow`/`StopTimeRow`.

**Store key**: `{city}:__subway_offsets__` in the existing `IKeyValueRepository<string>` (JSON
blob), so the endpoint reads it the same way route shapes are read. Only built for the subway
city (guarded by `city.Name == CityNames.Nymta`).

---

## Worker-internal (cached synthesis state — `TransitDataWorker/Subway/`)

### `StopOffsetTable` (`StopOffsetTable.cs`)

The worker-side cached, lookup-optimized form of the fetched `SubwayStopOffsetSet[]`.

```csharp
public sealed class StopOffsetTable
{
    // station lookup: stopId (with direction suffix) → (lat, lon)
    IReadOnlyDictionary<string, (double Lat, double Lon)> StationCoord;

    // per (routeJoinKey, direction): ordered stops + the interpolation polyline & cumdist
    IReadOnlyDictionary<(string Route, string Dir), SubwayStopOffsetSet> Sets;

    (double Lat, double Lon)? TryStation(string stopId);
    SubwayStop? StationBefore(string stopId, string route, string direction); // null at terminal
    (double Lat, double Lon) PointOnShapeAtDistance(string route, string dir, double distMeters);
}
```

**Built once** from the endpoint payload on first fetch; **read-only** thereafter (rebuilt only
on the 24 h re-fetch). `PointOnShapeAtDistance` binary-searches `CumulativeDistanceMeters` and
lerps between the two bracketing coordinates.

### `SubwaySynthesisOptions` (`SubwaySynthesisOptions.cs`)

```csharp
public sealed class SubwaySynthesisOptions
{
    public double NominalRunSeconds { get; set; } = 90;   // design §3.3 constant
    public string[] GtfsRtUrls { get; set; } = [];        // 8 line-group feeds (from Cities: config)
}
```

### `NymtaCity` cached fields

| Field | Type | Note |
|-------|------|------|
| `_table` | `volatile StopOffsetTable?` | null until first fetch; never recomputed per tick |
| `_fetchedAtUtc` | `DateTime` | drives the 24 h lazy re-fetch (Principle VII) |
| counters | `int synthesizedStopped, synthesizedInTransit, skippedUnknownStation` | logged per cycle, not telemetered |

---

## Reused existing types (NO change)

| Type | Location | Use |
|------|----------|-----|
| `FeedMessage`, `FeedEntity`, `VehiclePosition`, `TripDescriptor`, `Position`, `VehicleDescriptor` | `TransitDataWorker/GtfsRtModels.cs` | RT decode + emitted normalized entity |
| `VehicleStopStatus` (`IncomingAt/StoppedAt/InTransitTo`) | `Shared/EventData/PositionData.cs:16` | drives the synthesis switch |
| `ITransitCity` | `TransitDataWorker/Cities/ITransitCity.cs` | `NymtaCity` implements it unchanged |
| `HaversineCalculator.DistanceMeters` | `Shared/Geospatial/` | cumulative-distance build (server + helper) |
| `IKeyValueRepository<string>` | `WebAPI/Interfaces/` | stores the offset JSON blob |
| `RouteShapeProperties.JoinKey` | `Shared/GtfsData/RouteShapeFeature.cs:31` | line label already == join key for NYC subway |

---

## State transitions (per subway entity, inside `NymtaCity.Synthesize`)

```
entity (no Position)
  │
  ├─ status == StoppedAt   ─┐
  ├─ status == IncomingAt   ├─→ Position = StationCoord[stopId]           → emit
  ├─ status == null         ┘   (missing status treated as StoppedAt, FR-015)
  │
  └─ status == InTransitTo
        │
        ├─ StationBefore == null (terminal)  ─→ Position = StationCoord[stopId]   → emit
        │
        └─ prev exists ─→ frac = clamp(elapsed / NominalRunSeconds, 0, 1)
                          dCurr = dPrev + frac*(dTarget - dPrev)
                          Position = PointOnShapeAtDistance(route, dir, dCurr)    → emit

  (StationCoord[stopId] missing at any branch → skip entity, skippedUnknownStation++)
  (route has no offset set  → skip entity)
```

Every emitted entity is an ordinary `FeedEntity` with `Vehicle.Trip.RouteId = route`,
`Vehicle.Position = { lat, lon }`, `Vehicle.Timestamp = entity.timestamp`,
`Vehicle.Vehicle.Id = trainId` — indistinguishable downstream from `MartaCity`'s rail entities.
