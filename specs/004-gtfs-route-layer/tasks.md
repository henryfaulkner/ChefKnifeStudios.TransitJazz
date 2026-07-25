# Tasks: GTFS Static Route Layer (004-gtfs-route-layer)

**Feature Branch**: `feature/gtfs-route-layer`
**Plan**: [plan.md](plan.md)
**Created**: 2026-05-05

---

## Phase 1 — Backend: GTFS Static Loader

> New file: `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/GtfsStatic/GtfsStaticLoader.cs`
> Note: Loader placed in WebAPI project (not TransitDataWorker) — worker is a separate process with no access to the shared IKeyValueRepository singleton.

- [x] **T-01** — Create `GtfsStatic/` subdirectory in `WebAPI` and add `GtfsStaticLoader.cs`. Implements `IHostedService`. Constructor injection: `IHttpClientFactory`, `IKeyValueRepository<string>`, `ILogger<GtfsStaticLoader>`.

- [x] **T-02** — Implement `ParseTrips(ZipArchive)`: reads `trips.txt`, finds column indices by header name, returns `Dictionary<string, (string RouteId, string ShapeId)>` keyed by `trip_id`. Handles missing `shape_id` column gracefully.

- [x] **T-03** — Implement `ParseShapes(ZipArchive)`: reads `shapes.txt`, returns `Dictionary<string, List<(double Lat, double Lon, int Seq)>>` keyed by `shape_id`. Sorts each list by `Seq` ascending.

- [x] **T-04** — Implement `ParseRouteColors(ZipArchive)`: reads `routes.txt`, returns route color dictionary. Normalizes bare hex strings (e.g. `FF5733` → `#FF5733`). Empty/whitespace → `null`.

- [x] **T-05** — Implement `BuildLineStringFeature`: produces GeoJSON `Feature` string with `LineString` geometry, `[lon, lat]` coordinate pairs (flipped from GTFS), and `properties` with `routeId`, `color`, `textColor`.

- [x] **T-06** — Implement `StartAsync` (IHostedService): downloads GTFS Static zip, calls parsers, builds `routeId → shapeId` map (one shape per route: first trip's `shapeId`), stores each GeoJSON in `IKeyValueRepository<string>`, sets `__gtfs_static_ready__` sentinel. Wrapped in try/catch.

- [x] **T-07** — Register in `WebAPI/Program.cs`: `builder.Services.AddHttpClient()`, `builder.Services.AddHostedService<GtfsStaticLoader>()`.

---

## Phase 2 — Backend: New API Endpoint

> New file: `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/EndpointGroups/GtfsEndpoints.cs`

- [x] **T-08** — Added `public static class Gtfs` to `ApiEndpoints.cs` with `GetRouteShape = "/gtfs/routes/{routeId}/shape"`.

- [x] **T-09** — Created `GtfsEndpoints.cs`: `MapGtfsEndpoints` extension method, `MapGet` handler checks `__gtfs_static_ready__` (503 if absent), looks up `routeId` (404 if absent), returns `Results.Text(geoJson, "application/json")` on success.

- [x] **T-10** — Registered `.MapGtfsEndpoints()` in `WebAPI/Program.cs`. Solution builds: 0 errors.

- [ ] **T-11** — Smoke-test the endpoint manually: start the full stack, wait for "GtfsStaticLoader: loaded N route shapes" in logs, call `GET /gtfs/routes/{routeId}/shape` via Scalar UI or browser. Confirm 200 OK with valid GeoJSON LineString body.

---

## Phase 3 — Client: JavaScript Interop

> File: `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/azure-maps-interop.js`

- [x] **T-12** — Updated `initDataSourceForBusPositions` signature to accept `mapComponent` as second parameter. Updated call site in `createMap` ready handler.

- [x] **T-13** — Added `"route-shapes"` DataSource and `"route-shapes-layer"` LineLayer inside `initDataSourceForBusPositions`, added before `busLayer` so route lines render below bus markers.

- [x] **T-14** — Added `click` event on `busLayer` → calls `mapComponent.invokeMethodAsync('BusMarkerClickedAsync', vehicleId)`.

- [x] **T-15** — Added `showRouteShape(containerDivId, geoJsonString)` to `OvercastMap`: clears `"route-shapes"` DataSource, parses and adds GeoJSON feature.

- [x] **T-16** — Added `clearRouteShape(containerDivId)` to `OvercastMap`: clears `"route-shapes"` DataSource.

- [ ] **T-17** — Smoke-test in browser: navigate to `/maps`, confirm map still loads without JS errors.

---

## Phase 4 — Client: Blazor

> Files: `Map.razor.cs`, `Map.razor.Helper.cs`, `TransitMap.razor`, `TransitMap.razor.cs`

- [x] **T-18** — Added `[Parameter] EventCallback<(Map, string)> OnBusMarkerClicked` and `[JSInvokable("BusMarkerClickedAsync")]` to `Map.razor.cs`.

- [x] **T-19** — Added `ShowRouteShapeAsync(string geoJson)` to `Map.razor.Helper.cs`.

- [x] **T-20** — Added `ClearRouteShapeAsync()` to `Map.razor.Helper.cs`.

- [x] **T-21** — Injected `IHttpClientFactory` into `TransitMap.razor.cs`. Uses named client `"TransitJazzAPI"` (already registered in `Program.cs` via `appsettings.json`).

- [x] **T-22** — Added `_vehicleRouteMap`, `_routeShapeCache`, `_selectedRouteId` fields to `TransitMap.razor.cs`.

- [x] **T-23** — Updated `HandleBatchAsync` to populate `_vehicleRouteMap[vehicleId] = routeId` from `evt.Trip?.RouteId`.

- [x] **T-24** — Implemented `OnBusMarkerClickedAsync`: route cache lookup, API fetch on miss, `ShowRouteShapeAsync` on success.

- [x] **T-25** — Implemented `OnMapBodyClickedAsync`: calls `ClearRouteShapeAsync`, resets `_selectedRouteId`. Wired `OnMapBodyClicked` and `OnBusMarkerClicked` in `TransitMap.razor`. Solution builds: 0 errors.

- [x] **T-26** — Client WebApp builds: 0 errors, 0 warnings.

---

## Phase 5 — Verification

- [ ] **T-27** — Start the full stack. Watch WebAPI logs — confirm "GtfsStaticLoader: loaded N route shapes" appears. Confirm no exceptions during startup.

- [ ] **T-28** — Navigate to `/transit-map`. Wait for bus markers. Click a bus marker. Confirm a colored polyline appears tracing the route. Confirm it renders below bus marker icons. (SC-001, SC-005)

- [ ] **T-29** — Click a second bus marker on a different route. Confirm the first polyline disappears and the new one appears. Only one polyline at a time. (SC-003)

- [ ] **T-30** — Click the same bus or another bus on the same route. Open DevTools Network tab. Confirm no second `GET /gtfs/routes/{routeId}/shape` request (cache hit). (SC-004)

- [ ] **T-31** — Click the map background. Confirm the route polyline clears. (SC-003)

- [ ] **T-32** — Click a bus with no `RouteId` (watch for debug log "No routeId for vehicle"). Confirm no polyline drawn, no exceptions. (SC-006)

- [ ] **T-33** — Verify polyline color matches GTFS `route_color` for a known route. Confirm fallback to `#0078D4` when color is absent.

- [ ] **T-34** — Navigate away from `/transit-map` and back. Confirm clean state — no stale route polyline.

- [ ] **T-35** — Confirm no uncaught JS exceptions and no Blazor `ObjectDisposedException` in console during a full session.

---

## Task Summary

| Phase | Tasks | Covers |
|-------|-------|--------|
| 1 — GTFS Static Loader | T-01 – T-07 | FR-001, FR-002, FR-003, FR-004 |
| 2 — API Endpoint | T-08 – T-11 | FR-005, FR-006, FR-007, FR-017, FR-018 |
| 3 — JS Interop | T-12 – T-17 | FR-010, FR-011, FR-012, FR-013 |
| 4 — Blazor | T-18 – T-26 | FR-008, FR-009, FR-014, FR-015, FR-016 |
| 5 — Verification | T-27 – T-35 | SC-001 – SC-007 |

**Total tasks: 35 | Completed: 25 | Remaining: T-11, T-17, T-27 – T-35 (browser verification)**
