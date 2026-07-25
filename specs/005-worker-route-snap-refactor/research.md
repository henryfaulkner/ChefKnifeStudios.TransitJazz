# Research: Worker Route-Snap Refactor

**Date**: 2026-05-13  
**Feature**: [spec.md](spec.md)

## R1: Per-Route Index vs. Geohash Spatial Index

**Decision**: Replace the global `ILookup<string, RoutePoint>` (geohash-keyed, all routes mixed) with `IReadOnlyDictionary<string, RoutePoint[]>` (keyed by routeId).

**Rationale**: The current geohash index finds the nearest point across *all* routes. When routes run parallel corridors or share stops, a bus on Route A can snap to Route B's geometry. Keying by routeId guarantees candidate points belong to the vehicle's own route. The bus's routeId is available via `entity.Vehicle.Trip?.RouteId` in the GTFS-RT feed.

**Alternatives considered**:
- **Geohash index per route** (nested `Dictionary<routeId, ILookup<hash, RoutePoint>>`): Adds complexity for marginal performance gain. Individual routes in MARTA's system have hundreds to low-thousands of shape points — linear scan with Haversine is fast enough at that scale (~0.1ms per vehicle).
- **Keep global geohash, filter by routeId post-lookup**: Still pays the geohash encoding cost and risks incorrect results when a route's points fall in different geohash cells than the vehicle.

## R2: RouteSnapper Extraction

**Decision**: Introduce a static `RouteSnapper` class (or equivalent static method) that encapsulates the "find nearest RoutePoint from a list" logic, returning a result struct with `Index`, `Point`, and `DistanceKm`.

**Rationale**: The user's design notes (worker-route-snap-refactor.txt item 2) specify extracting `RouteSnapper.FindNearest()` into `Shared/Geospatial/` so the Worker and WebAPI can share the algorithm. This refactor prepares the Worker side for that extraction. The immediate task is to make the Worker call `RouteSnapper.FindNearest()` on the per-route point array.

**Alternatives considered**:
- **Keep FindNearestRoutePoint as a Worker method**: Prevents code sharing with the planned validate-snap API endpoint. Refactoring now avoids a second pass.

## R3: Fallback Strategy for Missing RouteId

**Decision**: Two distinct skip paths with separate counters:
1. `skippedNoRouteId` — `entity.Vehicle.Trip?.RouteId` is null or empty.
2. `skippedUnknownRoute` — routeId is present but not found in the route index.

**Rationale**: These represent different data quality issues. "No routeId" typically means the vehicle is deadheading or the agency's feed doesn't populate Trip. "Unknown route" could mean a new route was added but shapes haven't refreshed yet. Separate counters let operators distinguish feed gaps from stale index data.

**Alternatives considered**:
- **Single "skipped" counter**: Loses diagnostic value. Operators can't tell whether to investigate the feed or trigger an index refresh.
- **Default to global index as fallback**: Reintroduces the cross-route snapping bug for fallback vehicles. Clean skip is preferred.

## R4: Performance Impact of Linear Scan

**Decision**: Linear scan of a single route's `RoutePoint[]` is acceptable without geohash bucketing.

**Rationale**: MARTA has ~100 bus routes. The largest route shapes contain ~2,000-3,000 coordinate points. A linear Haversine scan of 3,000 points takes <1ms on modern hardware. With ~200 vehicles per feed cycle, total reconciliation cost stays under 200ms — well within the 10-second polling interval. Geohash bucketing per route would add memory overhead and code complexity for negligible latency improvement.

**Alternatives considered**:
- **Per-route geohash sub-index**: Complexity not justified given point counts. Could revisit if expanding to agencies with 10,000+ points per route.

## R5: Index Lifecycle (Build and Refresh)

**Decision**: Reuse the existing lifecycle pattern — build at startup with exponential backoff retry, refresh every 24 hours. Replace `BuildSpatialIndex` with `BuildRouteIndex`, replace `_routeSpatialIndex` with `_routeIndex`.

**Rationale**: The current lifecycle is proven in production. The only change is the shape of the data structure (geohash lookup → routeId dictionary). Initialization, retry, and refresh logic remain structurally identical.

**Alternatives considered**:
- **Event-driven refresh (webhook from shapes API)**: Not available in the current architecture. 24-hour polling is sufficient since route shapes change infrequently (schedule releases).
