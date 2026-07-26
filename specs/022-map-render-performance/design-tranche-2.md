# 022 — Map Render Performance: Tranche 2 (Server-Side Polyline Simplification + Single-Layer Routes + Single-Marshal Interop + Deferred Tracker Math)

**Status:** Ready for implementation
**Scope:** Server (ingest) + Client (`src/Client/`). One new shared concern (a simplification tolerance constant). No wire-contract shape change — `RouteShapeFeature` JSON stays identical, just with fewer coordinates.
**Audience:** The implementing agent. Self-contained.
**Predecessor:** `specs/022-map-render-performance/design.md` (tranche 1 — non-blocking overlay, yielding render loop, trigger-point quadratic fix). Tranche 1 is **already implemented and merged into the working tree.** This tranche addresses the part tranche 1 deliberately deferred: the actual work volume.

---

## 1. Why tranche 1 wasn't enough (measured)

Tranche 1 fixed the *perceived* freeze (overlay keeps animating, routes appear progressively) and removed the O(N²) trigger-point `setData`. It did **not** reduce the dominant cost, because that cost is raw data volume. Measured against the live API (`GET /gtfs/routes/shapes`):

| Metric | Value |
|---|---|
| Routes | **86** |
| Total coordinates | **111,627** |
| Max coords on a single route | **22,936** |
| Avg coords/route | ~1,298 |
| Payload size | **2.4 MB** |

For each of the 86 routes, `TransitMap.RenderRoutesAsync` currently:
1. `AddRouteShapeFeatureAsync` — marshals the route's coords C#→JS, `map.addSource` + `map.addLayer` (**86 separate MapLibre line layers**, each add triggers a style re-validation/repaint).
2. `LoadRouteGeometryForAnimationAsync` — marshals the **same** coords again (JS rebuilds cumulative distances).
3. `ConfigureTrackerForRouteAsync` — a **C# Haversine pass over every coordinate** (~111k trig-heavy haversines in WASM), `TriggerPointGenerator.Generate`, then `AddTriggerPointMarkersAsync` marshals coords a **third** time.

So: ~111k coordinate pairs marshalled across the WASM↔JS boundary **~3×** (~335k pairs), ~111k WASM haversines on the critical path, and **86 layers** created. Tranche 1's `await Task.Delay(1)` per route also added 86 forced timer round-trips + 86 `StateHasChanged` renders — good for perceived smoothness, neutral-to-negative for total wall-clock.

**This tranche attacks volume at four levels, in priority order. #1 is by far the biggest win and should be implemented and measured first.**

---

## 2. The four changes (priority order)

### #1 — Server-side polyline simplification (DO FIRST, MEASURE, THEN CONTINUE)

**Decision (confirmed with stakeholder):** simplify **server-side, at ingest**, not client-side. Rationale: it happens once at GTFS load, shrinks what's *stored*, shrinks *every* serve, and shrinks the 2.4 MB download itself — so the client never even pays to receive or parse the dense geometry.

**Where:** `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/GtfsStatic/GtfsStaticLoader.cs`, in `StartAsync`, on the `points` list **before** `BuildLineStringFeature` (line ~53). This is the single ingest point; it runs once per data load.

**Algorithm:** Ramer–Douglas–Peucker (RDP) line simplification on the ordered `(Lat, Lon, Seq)` points.
- Implement a small static RDP helper in C# (no new NuGet dependency — it's ~30 lines). Operate on the lon/lat sequence; use perpendicular distance. For geographic coordinates at city scale a planar (equirectangular-projected) perpendicular distance is perfectly adequate — convert degrees→meters with a cos(lat) longitude scale, or simply use a degree-space epsilon tuned empirically (see tolerance below).
- **Always keep the first and last point** (RDP does this inherently). Guard: if a route has < 3 points, skip simplification.

**Tolerance:** target ~**10 meters** (≈ `0.0001°` latitude; scale longitude by `cos(latitude)`). At MARTA's zoom range (minZoom 7, maxZoom 18 per `map-interop.js`) this is visually lossless for transit lines. Make the tolerance a **named constant** at the top of `GtfsStaticLoader` (e.g. `const double SimplifyToleranceMeters = 10.0;`) so it's tunable. Expectation: the 22,936-point monster route drops to a few hundred points; total ~111k → roughly **~10–20k** coordinates (≈5–10×), payload 2.4 MB → ~0.3–0.5 MB.

**Acceptance for #1:**
- Re-run `GET /gtfs/routes/shapes` after the worker reloads; total coordinate count drops ~5–10×, payload well under 1 MB, max-route coords in the low hundreds.
- Visually diff a few routes on the map at zoom 9–14 — lines look identical (no visible corner-cutting at stops/turns).
- **Re-measure the freeze on a throttled device before implementing #2–#4.** There is a real chance #1 alone gets you to "acceptable," in which case #2–#4 become optional polish.

> **Important interaction with checkpoint trigger points:** `TriggerPointGenerator.Generate` and the C# `HaversineMeters`/cumulative-distance pass run on whatever coordinates the client receives. Simplification changes the vertex set but **not** the line's length or shape meaningfully, so trigger-point spacing (the 009 ~200m derived spacing) is preserved within tolerance. Verify after #1 that checkpoints still pulse at sane positions along each route. If trigger spacing looks off, the tolerance is too aggressive — lower it.

---

### #2 — Collapse 86 route layers → 1 source + 1 data-driven line layer

**Why:** 86 `addLayer` calls each force a MapLibre style re-validation. One source + one layer styled by a per-feature `color` property is dramatically cheaper to add and to restyle. It also fixes `focusRoutes`/`clearRouteFocus`/`focusRoute` (`map-interop.js`), which currently **iterate every `route-layer-*` layer** calling `setPaintProperty` per layer.

**JS (`src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/map-interop.js`):**
- Add **one** source `routes` (FeatureCollection of LineStrings, each feature carries `properties.routeId` and `properties.color`) and **one** layer `routes-layer` (`type: 'line'`), inserted beneath `vehicles-layer` (preserve current z-order: routes under vehicles under trigger-points/pulse).
- `line-color`: data-driven — `['coalesce', ['get', 'color'], '#6b7280']` for the base, OR drive focus via **feature-state** (preferred) so focus/unfocus is a `setFeatureState` call, not a paint rebuild.
- **Focus/hover** (`focusRoutes`, `focusRoute`, `clearRouteFocus`): reimplement using `line-opacity`/`line-color` expressions keyed off feature-state (`['case', ['boolean', ['feature-state','focused'], false], 0.95, 0.3]`) or a `match` expression on `routeId`. Set feature-state via `map.setFeatureState({source:'routes', id: <featureId>}, {focused:true})`. NOTE: feature-state requires each feature to have a stable **`id`** (numeric or string) — assign `feature.id = routeId` (or an index) when building the collection.
- Keep `ChefMap._routeColors` / `_routeColorsByRouteId` populated as before (the vehicle-dot coloring in `_applyVehicleRouteColors` and the basemap-restore path in `setMapStyle` both read these).
- **`setMapStyle` restore (017):** the restore handler re-adds `vehicles` and `trigger-points` after a style swap. It must now also re-add the single `routes` source+layer from cached data (the client re-calls the render path after style swap, so simplest is to let `RenderRoutesAsync` re-push the `routes` source via the new single interop call — verify the 017 toggle still shows routes afterward).

**Acceptance for #2:** all 86 routes render from one source/layer; hover-preview and multi-select focus (020) still dim/emphasize correctly; vehicle dots still colored per route; basemap Street-map toggle (017) still restores routes.

---

### #3 — Single-marshal interop: push all routes in ONE call

**Why:** today each route crosses the WASM↔JS boundary ~3× × 86 routes ≈ 258 interop calls. Replace with **one** call carrying all route geometry; JS loops internally with zero per-route boundary cost and zero `Task.Delay` yielding.

**C# (`Map.razor.Helper.cs`):** add `AddAllRoutesAsync(object payload)` invoking a new `ChefMap.addAllRoutes(containerDivId, payload)`. `payload` is built **once** in `RenderRoutesAsync` from `_routeShapeCache`: an array of `{ routeId, color, coordinates }`. This single object marshals all ~10–20k (post-#1) coordinate pairs in one crossing.

**JS:** `ChefMap.addAllRoutes` builds the single `routes` FeatureCollection (for #2) in a tight loop and does one `addSource`/`setData`. It also seeds `_routeColors`/`_routeColorsByRouteId` and (if you fold animation geometry in) hands the per-route coords to `ChefMapAnimator.loadRouteGeometry` in the same loop — eliminating the separate `LoadRouteGeometryForAnimationAsync` round-trips.

**`RenderRoutesAsync` rewrite (`TransitMap.razor.cs`):**
- Build the single payload from cache, `await _map.AddAllRoutesAsync(payload)` once.
- **Remove the per-route `await Task.Delay(1)` and per-route `StateHasChanged`** — with one bulk call there's no long loop to yield within. (Keep the overlay spinner from tranche 1; it now covers a much shorter window.) The `_routesRenderedCount/_routesTotalCount` progress UI can be dropped or repurposed to a simple indeterminate state.
- Still call `FlushTriggerPointsAsync()` once (from tranche 1) after trigger features are populated.

**Acceptance for #3:** boundary crossings for route geometry drop from ~258 to ~1–2; routes still render correctly; no functional regression vs #2.

---

### #4 — Defer / relocate the trigger-point + tracker math off the critical path

**Why:** the ~111k (now ~10–20k after #1) C# haversines + `TriggerPointGenerator.Generate` + `CheckpointTracker.ConfigureRouteAsync` in `ConfigureTrackerForRouteAsync` block first-interactive but are **not needed until a bus actually crosses a checkpoint.** The user needs to *see* routes; checkpoint tracking can warm up a beat later.

**Options (pick one; (a) is lowest-risk):**
- **(a) Defer:** after routes render and the map is interactive, run the trigger/tracker configuration loop in the background — e.g. schedule it after a yield / via a low-priority continuation, or behind `requestIdleCallback` (JS) invoked once routes are up. Keep the existing per-route logic; just move *when* it runs to after first-interactive. Guard against the user toggling all-checkpoints visibility before config completes (the `trigger-points` source may be partially filled — acceptable, it fills in; or gate the toggle until done).
- **(b) Relocate to JS:** compute cumulative distances + trigger points in JS (the animator already has `buildCumulativeDistances`), eliminating the WASM haversine pass and the third coord marshal entirely. Bigger change; do only if (a) is insufficient.

**Acceptance for #4:** routes are visible/interactive measurably sooner than checkpoint config completes; checkpoints still pulse correctly once a bus crosses; all-checkpoints toggle still works.

---

## 3. Files to change (checklist)

| File | Change | Tranche-2 part |
|---|---|---|
| `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/GtfsStatic/GtfsStaticLoader.cs` | Add RDP `Simplify(...)` static helper + `SimplifyToleranceMeters` const; simplify `points` before `BuildLineStringFeature`. | #1 |
| `src/Client/.../wwwroot/js/map-interop.js` | New `routes` single source/layer + data-driven/feature-state color; rewrite `focusRoutes`/`focusRoute`/`clearRouteFocus`; new `addAllRoutes`; update `setMapStyle` restore for the single routes source. | #2, #3 |
| `src/Client/.../Components/Map.razor.Helper.cs` | Add `AddAllRoutesAsync`. (Old `AddRouteShapeFeatureAsync`/`LoadRouteGeometryForAnimationAsync` may be retired or kept unused — prefer removing dead paths.) | #3 |
| `src/Client/.../Pages/TransitMap.razor.cs` | Rewrite `RenderRoutesAsync` to build one payload + single call; drop per-route `Task.Delay(1)`/`StateHasChanged`; defer `ConfigureTrackerForRouteAsync` loop past first-interactive. | #3, #4 |

No `RouteShapeFeature` contract change (same JSON, fewer points). No `.resx`. No DI.

---

## 4. Test & verification plan

1. **Build server + client:** `dotnet build` both `Server.WebAPI` and `Client.WebApp` — 0 errors.
2. **#1 data check:** restart worker/API so `GtfsStaticLoader` re-ingests; `GET /gtfs/routes/shapes` total coords down ~5–10×, payload < 1 MB, max route coords in low hundreds. Visually confirm routes unchanged at zoom 9–14.
3. **#1 freeze re-measure (gate):** throttle CPU 4–6× in DevTools, reload `/`, measure time-to-interactive. **Decide here whether #2–#4 are still needed.**
4. **#2 regressions:** hover-preview (single route emphasis), multi-select focus (020), vehicle-dot per-route colors, basemap Street-map toggle (017) all correct with the single routes layer.
5. **#3:** route-geometry boundary crossings ~1–2 (confirm via temporary `console.count` in `addAllRoutes`); routes render; spinner window much shorter.
6. **#4:** routes visible before checkpoint config finishes; checkpoint pulses still fire at correct positions on crossing; all-checkpoints toggle works.
7. **Soundscape integrity (009):** trigger spacing preserved post-simplification — buses still trigger notes at sane intervals, not bunched/sparse.

---

## 5. Risk notes for the implementer

- **Do #1 first and re-measure before building #2–#4.** Don't build the whole tranche blind; #1 may suffice. This is the single most important instruction in this doc.
- **Simplification tolerance is the soundscape's risk surface.** Too aggressive → trigger-point spacing (009, ~200m) and checkpoint positions drift. Start at 10 m, verify checkpoints, lower if needed. Keep it a named constant.
- **Feature-state needs stable feature `id`s** for the single-layer focus to work — assign `feature.id` when building the `routes` collection.
- **Preserve z-order:** routes under vehicles under trigger-points/pulse. The single `routes-layer` must be inserted with the `vehicles-layer` beforeId (as the per-route layers are today, `map-interop.js:478`).
- **017 basemap restore** reads `_routeColorsByRouteId`/`_triggerPointFeatures` and re-adds sources after `setStyle`; update it for the single `routes` source and verify the Street-map toggle still shows routes + checkpoints.
- **Don't regress tranche 1:** the overlay spinner + `FlushTriggerPointsAsync` (single trigger-points flush) stay. Only the per-route yield loop is replaced by the bulk call.
- **No new dependencies:** RDP is hand-rolled (~30 lines); no turf/topojson on server or client.
