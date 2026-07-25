# Quickstart: Worker Route-Snap Refactor

**Date**: 2026-05-13  
**Feature**: [spec.md](spec.md)

## What This Changes

The TransitDataWorker currently snaps each bus to the nearest point across *all* route shapes using a geohash spatial index. This refactor changes it to snap each bus to the nearest point on *its own route* using a per-route index keyed by routeId.

## Files Modified

| File | Change |
|------|--------|
| `src/Server/.../Worker.cs` | Replace `_routeSpatialIndex` with `_routeIndex`; replace `BuildSpatialIndex` with `BuildRouteIndex`; update `ProcessSpatialReconciliationAsync` to look up by routeId; add skip counters |
| `src/Server/.../Worker.cs` | Remove `FindNearestRoutePoint` method (replaced by `RouteSnapper.FindNearest`) |
| `src/ChefKnifeStudios.TransitJazz.Shared/Geospatial/RouteSnapper.cs` | New — static `FindNearest()` and `FindNearestN()` methods with `Snap` result type |
| `src/ChefKnifeStudios.TransitJazz.Shared/Geospatial/RoutePoint.cs` | Moved from Worker project — same `readonly record struct` |
| `src/Server/.../RoutePoint.cs` | Deleted (moved to Shared) |
| `src/Server/.../GeohashEncoder.cs` | No change (retained for potential future use or removed if unused) |
| `src/Server/.../HaversineCalculator.cs` | No change (used by RouteSnapper) |

## How to Test

1. Run the Worker with the WebAPI providing route shapes
2. Observe logs for `Spatial reconciliation: {Moved} moved, {Unchanged} unchanged, {SkippedNoRouteId} skippedNoRouteId, {SkippedUnknownRoute} skippedUnknownRoute`
3. Verify vehicles snap to their own route's points (compare routeId in log output)

## Key Design Decisions

- **Linear scan over per-route arrays** instead of geohash bucketing — MARTA routes have <3,000 points, making linear Haversine scan fast enough
- **Two skip counters** (skippedNoRouteId, skippedUnknownRoute) for operational diagnostics
- **RouteSnapper in Shared** — prepares for the validate-snap API endpoint (item 2 in worker-route-snap-refactor.txt)
