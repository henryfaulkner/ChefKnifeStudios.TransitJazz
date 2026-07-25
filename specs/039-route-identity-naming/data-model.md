# Phase 1 Data Model: Route Identity Naming Unification

This feature has no new persisted entities. "Data model" here means the full
rename inventory — every identifier that changes name — organized by the two
concepts involved.

## Concepts

### `RouteId` (retained meaning, unchanged)

The true GTFS static `route_id` value from `routes.txt`/`trips.txt` (e.g.
`"26932"`). After this feature, **every** remaining `RouteId`/`routeId`
identifier in the codebase means this and only this.

- Canonical source: `RouteShapeProperties.RouteId` (`Shared/GtfsData/RouteShapeFeature.cs:17`) — unchanged.
- Consumers that already correctly mean this and are **NOT touched**: `GtfsEndpoints.GetRouteShape`'s `routeId` route parameter, `GtfsStaticLoader`'s `allRouteToShape`/`fresh` dictionaries and their `{city}:{routeId}` store keys.
- The GTFS-RT wire model's `Trip.RouteId`/`EntitySelector.RouteId` protobuf fields also keep this name (see research.md R1) even though, for MARTA/WMATA, their *runtime value* is short-name-shaped — the field name mirrors the external spec, not our internal semantics.

### `RouteJoinKey` (new name for the composite/consumed-wire value)

The value actually used to correlate GTFS-RT real-time data against the
static route index and to identify a route across the Worker→SignalR→Client
pipeline. Computed as `RouteShortName ?? RouteId` wherever derived from
`RouteShapeProperties`, or read directly from the GTFS-RT wire's `Trip.RouteId`
value (post any `RailRouteIdMap` remap) wherever consumed as a lookup key.

- New canonical computation: `RouteShapeProperties.JoinKey` (see `contracts/route-join-key-contract.md`).
- Every other current holder of this value is renamed per the table below.

## Rename Inventory

### `src/ChefKnifeStudios.MartaJazz.Shared/`

| File | Line(s) | Before | After |
|---|---|---|---|
| `GtfsData/RouteShapeFeature.cs` | 16-22 | (no helper) | ADD `RouteShapeProperties.JoinKey => RouteShortName ?? RouteId` computed property |
| `Geospatial/RoutePoint.cs` | 3 | `record struct RoutePoint(string RouteId, double Lat, double Lon)` | `record struct RoutePoint(string RouteJoinKey, double Lat, double Lon)` |
| `Events/RouteNearestPointBatchEvent.cs` | 33 | `RouteNearestPointRecord(string VehicleId, string RouteId, ...)` | `RouteNearestPointRecord(string VehicleId, string RouteJoinKey, ...)` |
| `Events/RouteCrossingBatchEvent.cs` | 11 | `RouteCrossingRecord(string VehicleId, string RouteId, ...)` | `RouteCrossingRecord(string VehicleId, string RouteJoinKey, ...)` |

### `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/`

| File | Line(s) | Before | After |
|---|---|---|---|
| `Worker.cs` | 31-37 | `_routeIndex`, `_routeMode`, `_routeCumDist`, `_routeTriggerPoints` — comments say "routeId→..." | Comments updated to "routeJoinKey→..."; dictionary value types unchanged (they're keyed by `string`, the key semantics change, not the type) |
| `Worker.cs` | 211 | `var key = shape.Properties.RouteShortName ?? shape.Properties.RouteId;` | `var key = shape.Properties.JoinKey;` (calls new shared helper) |
| `Worker.cs` | 227 | `var rawId = shape.Properties.RouteId;` | Unchanged in meaning (this IS the true RouteId, used as an alias key) — variable name may stay `rawId` or become `rawRouteId` for clarity; **not** renamed to JoinKey |
| `Worker.cs` | 256-265 | `foreach (var (routeId, coordList) in coordGroups)` ... `cityCumDist[routeId]` | `foreach (var (routeJoinKey, coordList) in coordGroups)` ... `cityCumDist[routeJoinKey]` |
| `Worker.cs` | 361-381 | `string? routeId = entity.Vehicle.Trip?.RouteId;` (translation point — reads the wire field once) | `string? routeJoinKey = entity.Vehicle.Trip?.RouteId;` — this is the R1 translation boundary: RHS (wire field) unchanged, LHS (local var) renamed |
| `Worker.cs` | 348, 365, 369, 409-410, 420, 449, 463, 478, 488, 495, 509, 528, 539-540, 553, 579-580 | all local `routeId` usages, `skippedNoRouteId` counter/log field, `nearest.RouteId`, `prior.RouteId`, event record construction `RouteId:` | Consistent rename to `routeJoinKey`, `skippedNoJoinKey`, `nearest.RouteJoinKey`, `prior.RouteJoinKey`, `RouteJoinKey:` — matches the Shared record rename above |
| `Worker.cs` | 609 | `.OrderBy(r => r.RouteId, StringComparer.Ordinal)` | `.OrderBy(r => r.RouteJoinKey, StringComparer.Ordinal)` |
| `Worker.cs` | 702-703 | `_routeIndex.Count` (log message says "route index") | Unchanged (aggregate count, not a per-route identifier) |
| `VehicleState.cs` | 18-30 | `record VehicleState(..., string RouteId, ...)` + XML doc "The GTFS route identifier the vehicle is currently snapped to." | `record VehicleState(..., string RouteJoinKey, ...)` + doc updated to "The route join key (see RouteShapeProperties.JoinKey) the vehicle is currently snapped to." |
| `Cities/GtfsRtCity.cs` | 37, 60-68 | `ApplyRailRouteIdMap`, `config.RailRouteIdMap`, `entity.Vehicle.Trip.RouteId` | **UNCHANGED** — operates entirely on GTFS-RT wire values (see research.md R2) |

### `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/`

| File | Line(s) | Before | After |
|---|---|---|---|
| `ViewModels/RouteFilterViewModel.cs` | 16 | `class RouteItem { public string RouteId { get; init; } ... }` | `public string RouteJoinKey { get; init; }` |
| `ViewModels/RouteFilterViewModel.cs` | 32-34 | `SelectedRouteId`, `SelectedRouteIds`, `HoveredRouteId` (interface + impl) | `SelectedRouteJoinKey`, `SelectedRouteJoinKeys`, `HoveredRouteJoinKey` |
| `ViewModels/RouteFilterViewModel.cs` | 119-139, 186-268, 283-288 | all `record.RouteId`, `x.RouteId`, `routeItem.RouteId`, `_hoveredRouteId` | Renamed to `RouteJoinKey`/`_hoveredRouteJoinKey` consistently |
| `ViewModels/RouteFilterViewModel.cs` | 226, 229, 230, 232 | `x.Properties.RouteShortName ?? x.Properties.RouteId!` (×3 independent expressions) | `x.Properties.JoinKey` (×3, now calling the shared helper — de-dup per FR-003) |
| `ViewModels/ApplicationViewModel.cs` | 26 | doc comment "routeId (short name or id) → route shape" | "routeJoinKey → route shape" |
| `ViewModels/ApplicationViewModel.cs` | 131 | `var key = feature.Properties?.RouteShortName ?? feature.Properties?.RouteId;` | `var key = feature.Properties?.JoinKey;` |
| `ViewModels/ApplicationViewModel.cs` | 134 | log: `"...skipping feature with no RouteShortName or RouteId"` | `"...skipping feature with no RouteShortName or RouteId to derive a join key from"` (message content may stay descriptive of the underlying GTFS fields — see contracts doc) |
| `Data/RouteBlurbStore.cs` | 9, 26-32 | `GetForRoute(string routeId)` | `GetForRoute(string routeJoinKey)` |
| `wwwroot/js/map-interop.js` | 289-525 | GeoJSON property `routeId`, `_routeColorsByRouteId`, `_triggerPointFeatures[routeId]`, MapLibre `['get', 'routeId']` match expressions, `route.routeId` | Renamed to `routeJoinKey`, `_routeColorsByRouteJoinKey`, `_triggerPointFeatures[routeJoinKey]`, `['get', 'routeJoinKey']`, `route.routeJoinKey` |
| `wwwroot/js/vehicle-animator.js` | 101-459 | `state.routeId`, `rec.routeId`, `this.routeGeometry[routeId]` | `state.routeJoinKey`, `rec.routeJoinKey`, `this.routeGeometry[routeJoinKey]` |

### `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/`

| File | Line(s) | Before | After |
|---|---|---|---|
| `Pages/TransitMap.razor.cs` | 67, 72 | comments "routeId → GeoJSON string", "routeId → consecutive batches..." | "routeJoinKey → GeoJSON string", "routeJoinKey → consecutive batches..." |
| `Pages/TransitMap.razor.cs` | 157-165, 189-213, 351-541 | `RouteFilterViewModel.SelectedRouteIds/HoveredRouteId`, `crossing.RouteId`, `activeRoutes.Add(r.RouteId)` | Follows the interface rename: `SelectedRouteJoinKeys`, `HoveredRouteJoinKey`, `crossing.RouteJoinKey`, `r.RouteJoinKey` |
| `Pages/TransitMap.razor.cs` | 417, 432-461 | `routeId = kvp.Key`, `ConfigureTrackerForRouteAsync(string routeId, ...)` | `routeJoinKey = kvp.Key`, `ConfigureTrackerForRouteAsync(string routeJoinKey, ...)` |
| `Pages/TransitMap.razor.cs` | 576-580 | `var key = routeShapeFeature.Properties?.RouteShortName ?? routeShapeFeature.Properties?.RouteId ?? "(null)";` + log template `RouteId={RouteId}` | `var key = routeShapeFeature.Properties?.JoinKey ?? "(null)";` + log template `RouteJoinKey={RouteJoinKey}` |
| `Pages/TransitMap.razor.cs` | 594 | `record CrossingEventDto(string VehicleId, string RouteId, int TriggerIndex, int TotalTriggers, double OffsetMs)` | `record CrossingEventDto(string VehicleId, string RouteJoinKey, int TriggerIndex, int TotalTriggers, double OffsetMs)` |

### Explicitly out of scope (verify unchanged during implementation)

| File | Identifier | Why unchanged |
|---|---|---|
| `Server.WebAPI/EndpointGroups/GtfsEndpoints.cs` | `GetRouteShape(string routeId)` | Already means true GTFS `route_id` (FR-005) |
| `Server.WebAPI/GtfsStatic/GtfsStaticLoader.cs` | `allRouteToShape`, `fresh["{city}:{routeId}"]` | Already means true GTFS `route_id` (FR-005) |
| `GtfsRtModels.cs` | `TripDescriptor.RouteId`, `EntitySelector.RouteId` | Wire model, mirrors external GTFS-RT spec field name (research.md R1) |
| `Cities/GtfsRtCity.cs` | `RailRouteIdMap`, `ApplyRailRouteIdMap` | Operates on wire-shaped values, not the internal join key (research.md R2) |

### Documentation

| File | Change |
|---|---|
| `.specify/memory/constitution.md` | Principle VI rewritten to name the join-key concept `RouteJoinKey`/`JoinKey`, distinct from `route_id`; PATCH-level wording amendment, no principle redefinition |
| `docs/MULTI_CITY_TRANSIT_DESIGN.md` | Terminology pass: replace `routeId` (where used to mean the short-name-or-fallback join key, e.g. lines ~200-201/365-366 per original audit) with `routeJoinKey` |
| `docs/WMATA_GTFS_COMPATIBILITY.md` | Terminology pass: clarify that "RT `route_id` values" (wire) get consumed into `routeJoinKey` (internal), not renamed at the wire level |
