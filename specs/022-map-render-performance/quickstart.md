# Quickstart: Map Render Performance — Tranche 2

**Date**: 2026-06-20  
**Audience**: Implementing agent or developer

---

## Implementation order

**Do #1 first, measure, then decide whether #2–#4 are needed.** This is the most important instruction.

---

## Change #1 — Server-Side RDP Simplification

### File: `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/GtfsStatic/GtfsStaticLoader.cs`

1. Add constant at the top of the class:
   ```csharp
   const double SimplifyToleranceMeters = 10.0;
   ```

2. Add static `Simplify` helper method (iterative RDP, ~40 lines):
   - Input: `List<(double Lat, double Lon, int Seq)> pts`, `double toleranceMeters`
   - Output: `List<(double Lat, double Lon, int Seq)>`
   - Guard: return `pts` unchanged if `pts.Count < 3`
   - Distance: equirectangular perpendicular distance; scale longitude by `cos(avgLat * π/180)`
   - Tolerance in degrees latitude: `toleranceMeters / 111_320.0`; longitude tolerance: `toleranceMeters / (111_320.0 * cos(avgLat * π/180))`
   - Use an explicit stack (not recursion) to avoid stack overflow on 22k-point routes

3. In `StartAsync`, before `BuildLineStringFeature`:
   ```csharp
   var simplified = Simplify(points, SimplifyToleranceMeters);
   var geoJson = BuildLineStringFeature(routeId, shortName, simplified, color, textColor);
   ```

### Verify #1

```
dotnet build src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI
```
Restart the app so `GtfsStaticLoader` re-ingests.

Then check the API:
```
GET /gtfs/routes/shapes
```
Sum the coordinate counts across all features. Target: ~10k–20k total (was 111k). Max single route: low hundreds (was 22,936). Payload size: under 1 MB (was 2.4 MB).

**Visual check**: Open the map at zoom 9–14. Route lines must look identical to before — no visible corner-cutting.

**Soundscape check**: With routes simplified, trigger-point markers (enable all-checkpoints in settings) should still appear at regular spacing (~every 200 m) along routes.

**Gate**: If the map freeze is acceptable after #1, stop here. #2–#4 are optional polish.

---

## Change #2 — Single Routes Source + Layer (JS)

### File: `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/map-interop.js`

1. Add state: `_routesFeatureCollection: null`

2. Add `addAllRoutes(containerDivId, routes)` function — builds FeatureCollection (with `feature.id = routeId`), seeds color maps, calls `ChefMapAnimator.loadRouteGeometry` per route, upserts `routes` source and `routes-layer` (before `vehicles-layer`), caches the collection, fires `_applyVehicleRouteColors`.

3. Rewrite `focusRoutes` to use `setFeatureState({source:'routes', id:rid}, {focused,dimmed})`.

4. Rewrite `focusRoute` as: `ChefMap.focusRoutes(containerDivId, [routeId])`.

5. Rewrite `clearRouteFocus` to clear all feature-state.

6. Update `setMapStyle` restore closure to re-add `routes` source+layer from `_routesFeatureCollection`.

7. Remove `addRouteShapeFeature` (or stub it as a no-op with a deprecation warning).

### Verify #2

All 86 routes render from one source+layer. Hover and multi-select (feature 020) still dim/emphasize correctly. Vehicle dots still show route colors. Basemap toggle (feature 017) still restores routes.

---

## Change #3 — Single-Marshal Interop (C#)

### File: `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/Map.razor.Helper.cs`

1. Add:
   ```csharp
   public async Task AddAllRoutesAsync(object payload)
   {
       try { await JsRuntime.InvokeVoidAsync("ChefMap.addAllRoutes", ElementId, payload); }
       catch (Exception ex) { Console.WriteLine($"[Map] AddAllRoutes failed: {ex}"); }
   }
   ```

2. Remove `AddRouteShapeFeatureAsync` and `LoadRouteGeometryForAnimationAsync`.

### File: `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs`

1. Rewrite `RenderRoutesAsync`:
   - Build a single payload array from `_routeShapeCache` (filter out null/empty coordinates).
   - Call `await _map!.AddAllRoutesAsync(payload)` once.
   - Remove the per-route `foreach`, `Task.Delay(1)`, and `StateHasChanged()`.
   - Remove `ConfigureTrackerForRouteAsync` call (moved to #4).
   - Remove `FlushTriggerPointsAsync` call (moved to #4).

2. Remove fields: `_routesRenderedCount`, `_routesTotalCount`.

### Verify #3

Build:
```
dotnet build src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp
```
Routes render. Add a temporary `console.count('addAllRoutes')` in JS — should fire 1–2 times (once for initial render, once for basemap toggle if tested). Spinner window visibly shorter.

---

## Change #4 — Defer Tracker Math

### File: `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs`

1. In `OnAfterRenderAsync`, after `RenderRoutesAsync`:
   ```csharp
   _ = ConfigureAllTrackersAsync();  // fire-and-forget
   ```

2. Add `ConfigureAllTrackersAsync`:
   ```csharp
   async Task ConfigureAllTrackersAsync()
   {
       await Task.Yield();
       foreach (var (routeId, feature) in _routeShapeCache)
           await ConfigureTrackerForRouteAsync(routeId, feature);
       if (_map is not null)
           await _map.FlushTriggerPointsAsync();
   }
   ```

3. Remove `FlushTriggerPointsAsync` from the end of `RenderRoutesAsync` (it's now in `ConfigureAllTrackersAsync`).

### Verify #4

Routes visible and map pannable before trigger-point markers appear on the map (can observe this with all-checkpoints enabled in settings). Checkpoint pulses still fire when a bus crosses. All-checkpoints toggle works.

---

## Full build and test sequence

```powershell
# 1. Build
dotnet build src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI
dotnet build src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp

# 2. Run (via Aspire or direct)
# After #1: restart so GtfsStaticLoader re-ingests and check GET /gtfs/routes/shapes

# 3. Browser verification (each change)
# - CPU throttle 4-6x in DevTools, reload /, measure time-to-interactive
# - Confirm all 86 routes visible
# - Test focus: click a route in the grid, confirm emphasis/dim
# - Test multi-select: click several routes
# - Test clear: click Clear-selections
# - Toggle Street Map setting (settings blade) — routes must reappear
# - Enable All Checkpoints (settings blade) — markers at regular spacing
# - Observe a bus crossing (or use a simulated position) — confirm pulse fires
```
