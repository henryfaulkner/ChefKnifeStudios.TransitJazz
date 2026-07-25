# Feature Specification: GTFS Static Route Layer (004-gtfs-route-layer)

**Feature Branch**: `feature/gtfs-route-layer`
**Created**: 2026-05-05
**Status**: Draft
**Depends on**: 003-bus-map-tracker (complete)
**Input**: When a bus marker is clicked/selected on the transit map, display the bus's route as a polyline on the map, sourced from MARTA's GTFS Static feed.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - View Route Polyline for a Selected Bus (Priority: P1)

A user clicks a bus marker on the transit map. The bus's route appears as a colored polyline overlaid on the map, tracing the full path the route travels. The polyline uses the route's official color from GTFS Static data where available, falling back to a default color.

**Why this priority**: Core value of this feature. Without it, the feature has no purpose.

**Independent Test**: Click any bus marker and verify a polyline appears within 2 seconds tracing the bus's route. Verify the polyline clears when clicking elsewhere or another bus.

**Acceptance Scenarios**:

1. **Given** bus markers are visible on the map, **When** the user clicks a bus marker, **Then** a polyline is drawn on the map representing the full route shape for that bus's `RouteId`.
2. **Given** a route polyline is displayed, **When** the user clicks a different bus marker, **Then** the previous polyline is removed and the new route polyline is drawn.
3. **Given** a route polyline is displayed, **When** the user clicks the map background (not a marker), **Then** the polyline is cleared.
4. **Given** a bus marker has no `RouteId` in its event payload (`TripData` is null or `RouteId` is null), **When** the user clicks it, **Then** no polyline is drawn and a brief "No route data" tooltip is shown instead.

---

### User Story 2 - Route Data Loaded from Backend (Priority: P1)

The route shape data (GeoJSON LineStrings) comes from a backend API endpoint that parses MARTA's GTFS Static zip and serves per-route GeoJSON. The client fetches a route shape on demand (first click) and caches it for subsequent clicks on buses of the same route.

**Why this priority**: Route shapes cannot come from the real-time feed; they require GTFS Static parsing which must happen on the backend.

**Independent Test**: Open browser DevTools Network tab, click a bus — observe a single `GET /gtfs/routes/{routeId}/shape` request. Click another bus on the same route — confirm no second network request (served from cache).

**Acceptance Scenarios**:

1. **Given** the user clicks a bus with `RouteId = "110"`, **When** the client fetches `/gtfs/routes/110/shape`, **Then** the backend returns a GeoJSON `Feature` with a `LineString` geometry and a `color` property.
2. **Given** a route shape has already been fetched for `RouteId = "110"`, **When** the user clicks another bus also on route `"110"`, **Then** no additional network request is made (in-memory cache hit).
3. **Given** the backend returns a 404 for a `routeId` (route not in GTFS Static data), **When** the client receives the 404, **Then** no polyline is drawn, the error is logged, and the map state remains clean.

---

### User Story 3 - Route Shape Rendered with Route Color (Priority: P2)

The polyline is styled using the route's official color from GTFS Static `routes.txt` (the `route_color` field). If no color is available, a default blue is used. The polyline has a visible stroke width and is drawn below the bus marker layer.

**Why this priority**: Improves readability but the feature works without exact route colors.

**Acceptance Scenarios**:

1. **Given** the API returns a shape GeoJSON with `color: "#FF0000"`, **When** the polyline is drawn, **Then** the stroke color matches the returned hex value.
2. **Given** the API returns a shape GeoJSON with `color: null` or absent, **When** the polyline is drawn, **Then** the stroke color defaults to `#0078D4` (Azure blue).
3. **Given** a polyline is drawn, **Then** it renders beneath bus marker icons (z-layer ordering: route line below marker symbols).

---

### Edge Cases

- What if GTFS Static data is not yet loaded when the first request arrives? The backend MUST load (or return a loading error with 503) rather than return empty data silently.
- What if the GTFS Static zip is unavailable at startup? The backend logs the error and the `/gtfs/routes/{routeId}/shape` endpoint returns 503 until data is available.
- What if a `TripId` maps to a `shape_id` but the shape has zero points? The endpoint returns 404; the client treats it the same as a missing route.
- What if the route shape has thousands of points? The backend serves it as-is; the Azure Maps LineLayer renders large GeoJSON efficiently. No simplification in v1.
- What if multiple buses on the same route are clicked rapidly in succession? The client serializes requests — only the last selected route's polyline is shown; in-flight requests for a previously selected route are ignored on arrival.

---

## Requirements *(mandatory)*

### Functional Requirements

#### Backend: GTFS Static Data Loading
- **FR-001**: The backend MUST download and parse MARTA's GTFS Static zip on startup (or first request). The GTFS Static URL is `https://itsmarta.com/google_transit_feed/google_transit.zip`.
- **FR-002**: The backend MUST parse `shapes.txt` (shape_id, shape_pt_lat, shape_pt_lon, shape_pt_sequence) and `trips.txt` (trip_id → shape_id mapping) and `routes.txt` (route_id → route_color, route_text_color).
- **FR-003**: The parsed route shape data MUST be stored in-memory (the existing `IKeyValueRepository<T>` / `InMemoryKeyValueRepository<T>` infrastructure). A key per `routeId` storing a pre-built GeoJSON string is sufficient.
- **FR-004**: GTFS Static data loading MUST happen in the `TransitDataWorker` background service (same project that already polls GTFS-RT). It runs once at startup (not on every poll cycle).
- **FR-005**: The backend MUST expose a new endpoint group `GtfsEndpoints` with route `GET /gtfs/routes/{routeId}/shape` returning `200 OK` with a GeoJSON `Feature` body, `404 Not Found` if the routeId is unknown, or `503 Service Unavailable` if GTFS Static data has not yet loaded.

#### Backend: GeoJSON Shape Response
- **FR-006**: The GeoJSON response MUST be a `Feature` object with:
  - `geometry.type = "LineString"`
  - `geometry.coordinates` = array of `[longitude, latitude]` pairs ordered by `shape_pt_sequence`
  - `properties.routeId` = the route ID string
  - `properties.color` = hex color string (e.g. `"#FF5733"`) or `null` if not in GTFS Static data
  - `properties.textColor` = hex color string or `null`
- **FR-007**: The coordinate order in GeoJSON output MUST be `[longitude, latitude]` (GeoJSON / Azure Maps standard), converted from GTFS Static `shape_pt_lat` / `shape_pt_lon`.

#### Client: Route Shape Cache
- **FR-008**: The Blazor client MUST maintain an in-memory dictionary `Dictionary<string, RouteShapeFeature>` keyed by `routeId` for the lifetime of the `TransitMap` page component.
- **FR-009**: On bus marker click, the client MUST check the cache first. Only fetch from the API if the `routeId` is not cached.

#### Client: Map Interop
- **FR-010**: `azure-maps-interop.js` MUST expose `OvercastMap.showRouteShape(containerDivId, geoJsonFeature)` which adds or replaces a `"route-shape"` LineString feature on a dedicated `"route-shapes"` DataSource and `"route-shapes-layer"` LineLayer.
- **FR-011**: `azure-maps-interop.js` MUST expose `OvercastMap.clearRouteShape(containerDivId)` which removes all features from the `"route-shapes"` DataSource.
- **FR-012**: The `"route-shapes-layer"` LineLayer MUST be added to the map below the `"bus-positions-layer"` SymbolLayer so bus markers render on top of route lines.
- **FR-013**: `azure-maps-interop.js` MUST handle bus marker click events on `"bus-positions-layer"` and call back into Blazor via the existing `DotNetObjectReference` / `[JSInvokable]` pattern to notify `TransitMap` of the clicked `vehicleId`.

#### Client: Blazor Integration
- **FR-014**: `Map.razor.Helper.cs` MUST expose `ShowRouteShapeAsync(string geoJson)` and `ClearRouteShapeAsync()` methods that call the corresponding JS interop functions.
- **FR-015**: `TransitMap.razor.cs` MUST handle the bus click callback: resolve `routeId` from a local `Dictionary<string, string>` mapping `vehicleId → routeId` (populated during `HandleBatchAsync`), fetch or cache-hit the route shape, call `ShowRouteShapeAsync`.
- **FR-016**: The `vehicleId → routeId` mapping MUST be updated every time a `VehiclePositionUpdatedEvent` is processed in `HandleBatchAsync` so the map always has the latest trip assignment.

#### API Endpoint Registration
- **FR-017**: The new `GtfsEndpoints` endpoint group MUST be registered in `WebAPI/Program.cs` following the existing `EndpointGroups` pattern.
- **FR-018**: The `ApiEndpoints` constants class in the `Shared` project MUST be updated to include a `Gtfs` nested class with `GetRouteShape = "/gtfs/routes/{0}/shape"` (format string for `routeId` substitution).

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Clicking a bus marker with a valid `RouteId` causes a route polyline to appear on the map within 2 seconds (first fetch from API) or within 200ms (cache hit).
- **SC-002**: The `GET /gtfs/routes/{routeId}/shape` endpoint returns valid GeoJSON with a `LineString` geometry for any routeId present in MARTA's GTFS Static data.
- **SC-003**: Clicking a second bus marker removes the previous route polyline before drawing the new one — only one route polyline is visible at a time.
- **SC-004**: Clicking the same bus twice (or two buses on the same route) generates exactly one network request — the second resolves from cache.
- **SC-005**: The route polyline renders below the bus marker icon layer — markers are always visible on top of the route line.
- **SC-006**: A bus with `TripData = null` or `RouteId = null` produces no polyline and no JS/Blazor exception.
- **SC-007**: The backend `/gtfs/routes/{routeId}/shape` endpoint responds within 500ms for any cached route (data loaded at startup).

---

## Assumptions

- MARTA's GTFS Static zip is publicly accessible at `https://itsmarta.com/google_transit_feed/google_transit.zip` without authentication.
- `trips.txt` reliably maps `trip_id → route_id` and `trip_id → shape_id` for all active MARTA bus routes.
- `shapes.txt` points are pre-ordered by `shape_pt_sequence` when sorted ascending — the backend must sort by sequence before building the coordinate array.
- The existing `IKeyValueRepository<T>` / `InMemoryKeyValueRepository<T>` in `Server.Infrastructure` is suitable for holding the pre-built GeoJSON strings keyed by routeId. No Redis needed for v1.
- The existing `DotNetObjectReference` pattern in `Map.razor.cs` (used for `notifyMapReadyAsync` and `mapBodyClickedAsync`) is the correct mechanism for JS→C# callbacks. No new infrastructure is needed.
- Route shapes do not need to be simplified (Douglas-Peucker or similar) in v1. Azure Maps handles large coordinate arrays efficiently.
- Only one route polyline is shown at a time. Multi-route comparison is out of scope.
- The GTFS Static zip is fetched once at worker startup; there is no scheduled refresh in v1.
