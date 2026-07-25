# Data Model: Worker Route-Snap Refactor

**Date**: 2026-05-13  
**Feature**: [spec.md](spec.md)

## Entities

### RoutePoint (existing, unchanged)

A single coordinate on a transit route. Already defined in the Worker project.

| Field   | Type   | Description                          |
|---------|--------|--------------------------------------|
| RouteId | string | GTFS route identifier                |
| Lat     | double | Latitude in degrees                  |
| Lon     | double | Longitude in degrees                 |

**Location**: `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/RoutePoint.cs`

### VehicleState (existing, unchanged)

Tracks a vehicle's most recent nearest route point for delta detection.

| Field            | Type     | Description                              |
|------------------|----------|------------------------------------------|
| NearestLat       | double   | Latitude of nearest route point          |
| NearestLon       | double   | Longitude of nearest route point         |
| LastUpdated      | DateTime | UTC timestamp of last state update       |
| RouteId          | string   | GTFS route identifier vehicle is on      |
| SpeedMetersPerSec| float?   | Vehicle speed from GTFS-RT feed          |
| Bearing          | float?   | Vehicle bearing in degrees               |

**Location**: `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/VehicleState.cs`

### Snap (new)

Result of a nearest-point search. Returned by `RouteSnapper.FindNearest()`.

| Field      | Type       | Description                                    |
|------------|------------|------------------------------------------------|
| Index      | int        | Index of the nearest point in the route array  |
| Point      | RoutePoint | The nearest RoutePoint                         |
| DistanceKm | double     | Haversine distance from query point to nearest |

**Location**: `src/ChefKnifeStudios.TransitJazz.Shared/Geospatial/RouteSnapper.cs` (nested type or companion record)

## Data Structures

### Route Index (replaces Spatial Index)

**Before** (current):
```
ILookup<string, RoutePoint> _routeSpatialIndex
  key: 5-char geohash prefix
  values: all RoutePoints across all routes in that geohash cell
```

**After** (refactored):
```
IReadOnlyDictionary<string, RoutePoint[]> _routeIndex
  key: routeId (e.g., "39382")
  values: all RoutePoints for that specific route, in shape order
```

### State Diagram: Vehicle Processing

```
Vehicle Entity
  │
  ├─ Trip?.RouteId is null/empty → skip (skippedNoRouteId++)
  │
  ├─ RouteId not in _routeIndex → skip (skippedUnknownRoute++)
  │
  └─ RouteId found in _routeIndex
       │
       ├─ RouteSnapper.FindNearest(lat, lon, points) → Snap result
       │
       ├─ Compare with VehicleState
       │    ├─ Position changed → add to batch, update state (movedCount++)
       │    └─ Position same → skip (unchangedCount++)
       │
       └─ No prior state → set initial state
```

## Relationships

- **RouteShapeFeature** (input) → `BuildRouteIndex()` → **RoutePoint[]** per routeId (route index)
- **Route Index** + **Vehicle Position** → `RouteSnapper.FindNearest()` → **Snap** result
- **Snap.Point** → compared against **VehicleState** → **RouteNearestPointBatchEvent** (if changed)
