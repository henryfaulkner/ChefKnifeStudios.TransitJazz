# Feature Specification: Worker Route-Snap Refactor

**Feature Branch**: `005-worker-route-snap-refactor`  
**Created**: 2026-05-13  
**Status**: Draft  
**Input**: User description: "Worker route-snap refactor — replace cross-route geohash spatial index with per-route index keyed by routeId"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Per-Route Bus Snapping (Priority: P1)

When the transit data worker processes a GTFS-RT vehicle position update, it snaps the bus to the nearest point on the bus's own route (identified by routeId) rather than the nearest point across all routes in the system.

**Why this priority**: The current cross-route snapping produces incorrect results when routes run close together (e.g., parallel bus corridors, shared stops). A bus on Route 1 may incorrectly snap to a nearby point on Route 2. This is the core problem the refactor solves.

**Independent Test**: Can be fully tested by feeding the worker a vehicle position with a known routeId and verifying it snaps to a point on that specific route, not a closer point on a different route.

**Acceptance Scenarios**:

1. **Given** a vehicle reports position (lat, lon) with Trip.RouteId = "Route1", **When** the spatial reconciliation processes this vehicle, **Then** the nearest point is found exclusively among Route1's shape points.
2. **Given** a vehicle on Route1 is equidistant from a point on Route1 and a closer point on Route2, **When** snapping occurs, **Then** the vehicle snaps to the Route1 point (not the Route2 point).
3. **Given** the route index is built from route shape data, **When** a vehicle with a valid routeId is processed, **Then** the system uses only that route's points for nearest-point calculation.

---

### User Story 2 - Graceful Fallback for Missing Route Information (Priority: P2)

When a vehicle's GTFS-RT feed entry lacks a routeId or reports an unknown routeId, the worker skips that vehicle and logs the occurrence rather than crashing or producing incorrect results.

**Why this priority**: Real-world GTFS-RT feeds frequently contain vehicles without trip/route information (e.g., deadheading buses, vehicles not yet assigned). The system must handle these gracefully to maintain continuous operation.

**Independent Test**: Can be tested by injecting feed entities with null/empty routeId and unknown routeId values and verifying they are skipped with appropriate counter logging.

**Acceptance Scenarios**:

1. **Given** a vehicle entity with a null or empty Trip.RouteId, **When** spatial reconciliation processes the feed, **Then** the vehicle is skipped and the "skippedNoRouteId" counter increments.
2. **Given** a vehicle entity with a Trip.RouteId that does not exist in the route index, **When** spatial reconciliation processes the feed, **Then** the vehicle is skipped and the "skippedUnknownRoute" counter increments.
3. **Given** a feed cycle with mixed valid and invalid routeId vehicles, **When** the cycle completes, **Then** both skip counters are logged alongside the moved/unchanged counts.

---

### User Story 3 - Route Index Construction and Lifecycle (Priority: P3)

The worker builds a route index keyed by routeId at startup from the route shape data, replacing the current geohash-keyed spatial index. The index is refreshed on the same 24-hour cycle.

**Why this priority**: The index is foundational infrastructure for the per-route snapping. It must be correctly built and maintained, but its correctness is validated through the snapping behavior in P1.

**Independent Test**: Can be tested by verifying the index contains the expected routes and point counts after construction from known route shape data.

**Acceptance Scenarios**:

1. **Given** route shape data is fetched from the API, **When** the route index is built, **Then** each routeId maps to an array of RoutePoints derived from that route's GeoJSON coordinates (in [lon, lat] order, converted to (lat, lon)).
2. **Given** the worker has been running for 24 hours, **When** the refresh cycle triggers, **Then** the route index is rebuilt from fresh data while the old index remains available until replacement completes.
3. **Given** the route shapes API returns empty or fails, **When** index initialization retries, **Then** it uses the same exponential backoff strategy as the current implementation.

---

### Edge Cases

- What happens when a vehicle's routeId changes between consecutive feed cycles (e.g., bus reassigned to a different route)? The vehicle should snap to its new route's points.
- What happens when a routeId exists in the feed but has zero shape points in the index? The vehicle should be treated as "unknown route" and skipped.
- What happens when the route shapes API is down during the 24-hour refresh? The existing index should be retained, same as current behavior.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST maintain a route index data structure keyed by routeId, where each entry contains an array of RoutePoint values for that route.
- **FR-002**: System MUST build the route index by projecting each RouteShapeFeature's GeoJSON coordinates (in [lon, lat] order) into RoutePoint records (lat, lon) grouped by routeId.
- **FR-003**: During spatial reconciliation, the system MUST read the vehicle's Trip.RouteId from the GTFS-RT entity to determine which route's points to search.
- **FR-004**: System MUST find the nearest point to the vehicle's position using only the points belonging to the vehicle's own route.
- **FR-005**: System MUST skip vehicles whose Trip.RouteId is null or empty, incrementing a "skippedNoRouteId" counter.
- **FR-006**: System MUST skip vehicles whose Trip.RouteId is not present in the route index, incrementing a "skippedUnknownRoute" counter.
- **FR-007**: System MUST log both skip counters (skippedNoRouteId and skippedUnknownRoute) alongside existing moved/unchanged/skipped counts at the end of each reconciliation cycle.
- **FR-008**: The route index MUST replace the current geohash-keyed spatial index (`ILookup<string, RoutePoint>`).
- **FR-009**: System MUST retain existing initialization retry logic (exponential backoff, max 5 attempts) and 24-hour refresh cycle for the route index.

### Key Entities

- **RoutePoint**: A single coordinate on a transit route — contains RouteId, Lat, Lon. Used as the fundamental unit in the route index.
- **Route Index**: A dictionary mapping routeId strings to arrays of RoutePoint values, replacing the geohash-based spatial index.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every vehicle with a valid routeId snaps to a point on its own route, never to a point on a different route.
- **SC-002**: Vehicles without route information are gracefully skipped without affecting processing of other vehicles in the same feed cycle.
- **SC-003**: Skip reasons (no routeId vs. unknown route) are distinguishable in operational logs, enabling operators to diagnose feed quality issues.
- **SC-004**: Route index construction and refresh complete within the same time envelope as the current spatial index (no regression in startup or refresh duration).

## Assumptions

- The GTFS-RT feed's `entity.Vehicle.Trip.RouteId` values correspond to the `RouteShapeFeature.Properties.RouteId` values from the route shapes API.
- The existing nearest-point algorithm (Haversine distance comparison) remains appropriate for per-route snapping; no change to the distance calculation method is needed.
- The geohash-based spatial index is fully replaced — no backward-compatible fallback to the old cross-route algorithm is required.
- The existing vehicle state tracking, delta detection, and event publishing logic remain unchanged; only the candidate-selection mechanism changes.
- Route shape data volume per individual route is small enough that linear scan of a single route's points is performant without geohash bucketing.
