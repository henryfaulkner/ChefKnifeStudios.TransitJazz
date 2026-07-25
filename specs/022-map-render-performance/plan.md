# Implementation Plan: Map Render Performance — Tranche 2

**Branch**: `main` | **Date**: 2026-06-20 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/022-map-render-performance/spec.md`

## Summary

Reduce map load time and freeze by attacking raw data volume at four levels in priority order: (1) server-side Ramer–Douglas–Peucker simplification of route geometry at ingest, (2) collapsing 86 individual MapLibre route sources+layers into one data-driven source+layer, (3) replacing ~258 per-route WASM→JS interop calls with a single bulk call, and (4) deferring trigger-point/tracker math off the critical render path. Each change is measured before proceeding to the next; #1 alone may be sufficient.

## Technical Context

**Language/Version**: C# / .NET 10.0 (server + Blazor WASM client); JavaScript (MapLibre GL JS interop)
**Primary Dependencies**: MapLibre GL JS (map rendering); Blazor WASM (client framework); ASP.NET Core (server); IJSRuntime (WASM↔JS interop)
**Storage**: In-memory `IKeyValueRepository<string>` (route GeoJSON strings, keyed by GTFS route_id); no database
**Testing**: Manual browser verification (DevTools CPU throttle, coordinate count check, visual diff); no automated test suite for this layer
**Target Platform**: Blazor WebAssembly (WASM) + browser JS; server side is ASP.NET Core Minimal API
**Performance Goals**: Total coordinate count down ~5–10× (111k → ~10–20k); API payload under 1 MB (from 2.4 MB); route layer count 86 → 1; WASM↔JS crossings for route geometry ~258 → ~1–2
**Constraints**: No new NuGet packages; no new npm/JS libraries; `RouteShapeFeature` JSON contract unchanged (same shape, fewer coordinates); must not regress features 017 (basemap toggle), 020 (multi-select focus), and 009 (soundscape trigger spacing)
**Scale/Scope**: 86 MARTA bus routes; ~111k coordinates pre-simplification; 4 files changed (1 server, 3 client)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Decoupled Cloud Architecture | ✅ Pass | Server change is in WebAPI/GtfsStatic only; client changes are Blazor WASM only; no new deployable units |
| II. No Frontend Secrets | ✅ Pass | No secrets involved |
| III. Two-Pass Real-Time Data Pipeline | ✅ Pass | Worker pipeline untouched; route geometry is static GTFS, not RT data |
| IV. OpenTelemetry Observability | ✅ Pass | No observability changes; existing structured logging preserved |
| V. Azure DevOps CI/CD | ✅ Pass | No pipeline changes |
| VI. GTFS ID Mapping | ✅ Pass | `route_short_name` join key unchanged; `RouteShapeProperties` untouched |
| VII. OpenStreetMap-Based Cartography | ✅ Pass | Routes remain GeoJSON layers on top of basemap; basemap restore path updated to re-add single `routes` source |
| VIII. Generative Transit Music | ✅ Pass | Trigger-point spacing preserved within 10 m tolerance; soundscape wiring unchanged |
| IX. Persistent Multi-Selection Interaction Model | ✅ Pass | Focus/hover rewritten for single layer using feature-state; all selection mechanics preserved |
| X. Zoom-Adaptive Controls | ✅ Pass | No grid changes |
| XI. Snappy, Reversible Overlays | ✅ Pass | No overlay timing changes |
| XII. Internationalized, Settings-Driven Presentation | ✅ Pass | No string or settings changes |

All principles pass. No complexity tracking required.

## Project Structure

### Documentation (this feature)

```text
specs/022-map-render-performance/
├── design.md            # Tranche 1 design (already implemented)
├── design-tranche-2.md  # Tranche 2 design (this feature's source)
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (files changed by this feature)

```text
src/
├── Server/
│   └── ChefKnifeStudios.MartaJazz.Server.WebAPI/
│       └── GtfsStatic/
│           └── GtfsStaticLoader.cs          # #1: Add RDP Simplify helper + tolerance const
│
└── Client/
    ├── ChefKnifeStudios.MartaJazz.Client.Shared/
    │   ├── Components/
    │   │   └── Map.razor.Helper.cs          # #3: Add AddAllRoutesAsync; retire per-route methods
    │   └── wwwroot/js/
    │       └── map-interop.js               # #2 + #3: single routes source/layer, addAllRoutes,
    │                                        #          feature-state focus, setMapStyle restore
    │
    └── ChefKnifeStudios.MartaJazz.Client.WebApp/
        └── Pages/
            └── TransitMap.razor.cs          # #3 + #4: bulk payload, defer tracker math
```

**Structure Decision**: Single-project web application (server + WASM client). Changes span the server ingest layer and the client rendering+interop layer. No new projects, no new files outside the spec directory.

---

## Phase 0: Research

*All research findings have been resolved from direct code reading. No external unknowns remain.*

See [research.md](research.md) for full findings.

---

## Phase 1: Design

### Change #1 — Server-Side RDP Simplification

**File**: `GtfsStaticLoader.cs`

**Where**: In `StartAsync`, the `points` list (type `List<(double Lat, double Lon, int Seq)>`) is passed directly to `BuildLineStringFeature`. The simplification call is inserted **before** `BuildLineStringFeature`, on the sorted `points` list after `shapes[shapeId]` is retrieved.

**Algorithm**: Ramer–Douglas–Peucker with equirectangular perpendicular distance.
- Operate in lon/lat space; scale longitude distance by `cos(avgLat)` in radians so the distance unit is approximately meters.
- Epsilon = `SimplifyToleranceMeters / 111_320.0` degrees latitude (1° lat ≈ 111,320 m). Longitude degree length = `111_320.0 * cos(avgLat * π/180)`.
- Implementation: a `static List<(double,double,int)> Simplify(List<(double,double,int)> pts, double toleranceMeters)` method. Returns the input list unchanged if `pts.Count < 3`. RDP is recursive; use a stack-based iterative version to avoid stack overflow on the 22,936-point monster route.
- Named constant: `const double SimplifyToleranceMeters = 10.0;`

**Insertion point** (line ~53 in current file):
```csharp
// Before:
var geoJson = BuildLineStringFeature(routeId, shortName, points, color, textColor);

// After:
var simplified = Simplify(points, SimplifyToleranceMeters);
var geoJson = BuildLineStringFeature(routeId, shortName, simplified, color, textColor);
```

**Guard**: `Simplify` returns `pts` as-is when `pts.Count < 3`.

---

### Change #2 — Single Routes Source + Data-Driven Layer (JS)

**File**: `map-interop.js`

Replace the 86-layer pattern with:

**New state**:
```js
_routesFeatureCollection: null,  // cached FeatureCollection for setMapStyle restore
```

**New function** `addAllRoutes(containerDivId, routes)`:
- `routes` is an array of `{ routeId, color, coordinates }` objects.
- Builds a single GeoJSON FeatureCollection: one LineString Feature per route, with `feature.id = routeId` (string ID for feature-state), `properties.routeId`, `properties.color`.
- Seeds `_routeColors` and `_routeColorsByRouteId` in the same loop.
- Calls `ChefMapAnimator.loadRouteGeometry(routeId, coordinates)` in the same loop — eliminating the separate animation geometry crossings.
- Upserts the `routes` source (`addSource` on first call, `setData` on subsequent).
- Adds the `routes-layer` (type `line`, data-driven color) on first call, inserted **before** `vehicles-layer` to preserve z-order (routes < vehicles < trigger-points/pulse).
- Fires `_applyVehicleRouteColors` once after the loop.

**Layer paint** for `routes-layer`:
```js
paint: {
    'line-color': ['coalesce', ['get', 'color'], '#6b7280'],
    'line-width': 2,
    'line-opacity': [
        'case',
        ['boolean', ['feature-state', 'focused'], false], 0.95,
        ['boolean', ['feature-state', 'dimmed'], false], 0.3,
        0.7   // default unfocused, no selection active
    ]
}
```

**`focusRoutes(containerDivId, routeIds)`** — rewrite:
- Clear all feature-state on `routes` source first (`setFeatureState({source:'routes',id:rid}, {focused:false,dimmed:false})` for all known route IDs).
- For each `rid` in `routeIds`: `setFeatureState({source:'routes', id:rid}, {focused:true, dimmed:false})`.
- For each `rid` NOT in `routeIds`: `setFeatureState({source:'routes', id:rid}, {focused:false, dimmed:true})`.

**`focusRoute(containerDivId, routeId)`** — rewrite as a thin wrapper: `focusRoutes(containerDivId, [routeId])`.

**`clearRouteFocus(containerDivId)`** — rewrite:
- Clear all feature-state (`focused:false, dimmed:false`) for all known route IDs.

**`setMapStyle` restore path** — add after vehicles/trigger-points restore:
```js
// Re-add the consolidated routes source+layer from cached data.
if (ChefMap._routesFeatureCollection && !map.getSource('routes')) {
    map.addSource('routes', { type: 'geojson', data: ChefMap._routesFeatureCollection });
    map.addLayer({ id: 'routes-layer', type: 'line', source: 'routes',
        layout: { 'line-join': 'round', 'line-cap': 'round' },
        paint: { /* same data-driven paint as above */ }
    }, 'vehicles-layer');
}
```
The C# side calls `RenderRoutesAsync()` after `SetBasemapStyleAsync`, which calls `AddAllRoutesAsync` again — this will hit the `setData` branch and re-populate route geometry. The restore block is a safety net for the case where route data is already present in the cache and the full re-render path is not awaited before the resolve.

**Remove**: `addRouteShapeFeature` function (dead after #3). Keep as a no-op stub if any call path still reaches it, or delete entirely after confirming `TransitMap.razor.cs` no longer calls it.

---

### Change #3 — Single-Marshal Interop (C# + JS)

**File**: `Map.razor.Helper.cs`

Add:
```csharp
public async Task AddAllRoutesAsync(object payload)
{
    try { await JsRuntime.InvokeVoidAsync("ChefMap.addAllRoutes", ElementId, payload); }
    catch (Exception ex) { Console.WriteLine($"[Map] AddAllRoutes failed: {ex}"); }
}
```

Retire (remove or leave unused — prefer removing to avoid dead paths):
- `AddRouteShapeFeatureAsync`
- `LoadRouteGeometryForAnimationAsync`

These are replaced by the single `AddAllRoutesAsync` call and the JS-side loop in `addAllRoutes`.

**File**: `TransitMap.razor.cs` — `RenderRoutesAsync` rewrite:

```csharp
async Task RenderRoutesAsync()
{
    Logger.LogDebug("TransitMap.RenderRoutesAsync: pushing {Count} cached routes to map", _routeShapeCache.Count);

    if (_routeShapeCache.Count == 0)
    {
        Logger.LogWarning("TransitMap.RenderRoutesAsync: route cache is empty — routes will not render");
        return;
    }

    var payload = _routeShapeCache
        .Where(kvp => kvp.Value.Geometry?.Coordinates is { Length: > 0 })
        .Select(kvp => (object)new
        {
            routeId = kvp.Key,
            color = kvp.Value.Properties?.Color ?? "#6b7280",
            coordinates = kvp.Value.Geometry!.Coordinates
        })
        .ToArray();

    if (_map is not null)
        await _map.AddAllRoutesAsync(payload);

    // Trigger-point config is deferred (#4); FlushTriggerPointsAsync is called after deferred config completes.

    Logger.LogDebug("TransitMap.RenderRoutesAsync: route geometry push complete");
}
```

Remove from `RenderRoutesAsync`:
- The per-route `foreach` loop.
- Per-route `await Task.Delay(1)` and `StateHasChanged()`.
- Per-route `ConfigureTrackerForRouteAsync` call (moved to deferred path — see #4).
- The `_routesRenderedCount`/`_routesTotalCount` progress fields and their updates (no longer needed with single bulk call).

Remove fields:
```csharp
// Delete these:
bool _routesRendered;
int _routesRenderedCount;
int _routesTotalCount;
```
Keep `_routesLoaded` (still used for the first-render gate in `OnAfterRenderAsync`).

The overlay spinner from tranche 1 stays; it now covers a shorter window.

---

### Change #4 — Defer Tracker/Trigger-Point Math

**File**: `TransitMap.razor.cs`

After `RenderRoutesAsync` completes (routes visible, map interactive), kick off a background continuation for the tracker config loop:

```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (_mapReady && _routesLoaded && !_routesRendered && _map is not null)
    {
        _routesRendered = true;
        await _map.SetCheckpointVisibilityAsync(false);
        await RenderRoutesAsync();                          // fast: single bulk call
        _ = ConfigureAllTrackersAsync();                    // fire-and-forget: deferred math
        var settings = SettingsService.GetSettings();
        await _map.SetCheckpointVisibilityAsync(settings.AreCheckpointsVisible);
        await _map.SetAllCheckpointsVisibilityAsync(settings.AreAllCheckpointsVisible);
        await _map.SetVehiclesVisibleAsync(settings.IsBusesVisible);
    }
}

async Task ConfigureAllTrackersAsync()
{
    await Task.Yield();  // release the render thread before starting math
    foreach (var (routeId, feature) in _routeShapeCache)
        await ConfigureTrackerForRouteAsync(routeId, feature);
    if (_map is not null)
        await _map.FlushTriggerPointsAsync();
}
```

`ConfigureTrackerForRouteAsync` is unchanged internally (same Haversine + TriggerPointGenerator + AddTriggerPointMarkersAsync + CheckpointTracker.ConfigureRouteAsync logic).

**Guard for `SetAllCheckpointsVisibilityAsync` during deferred config**: The `trigger-points` source is created lazily inside `addTriggerPointMarkers` (JS side) — the first call creates it with empty data. `setAllCheckpointsVisibility` checks `map.getLayer('trigger-points-layer')` before operating and returns early if absent. So if the user toggles all-checkpoints visibility before any tracker config completes, the toggle is a no-op; once the source appears it will respect the current visibility setting (which is applied via `SetAllCheckpointsVisibilityAsync` in `OnAfterRenderAsync` before the deferred work starts). This is acceptable per the spec edge case.

**`HandleSettingsEventReceived` for basemap GIS toggle** — the existing call to `RenderRoutesAsync()` after `SetBasemapStyleAsync` remains. That re-call now uses the fast single-bulk-call path, so the basemap restore is also fast.

---

## Complexity Tracking

No constitution violations. No complexity justification required.
