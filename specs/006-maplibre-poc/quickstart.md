# Quickstart: Running the POC Day

**Feature**: 006-maplibre-poc | **Date**: 2026-05-17

This is the operational guide for the single working day during which the MapLibre + MapTiler side-by-side POC is built, measured, and decided. It assumes the implementation tasks (forthcoming in `tasks.md`) have been executed in advance, *or* that the POC day itself includes the implementation work.

If implementation happens in advance, POC day is measurement-only and the schedule compresses. If implementation happens on POC day, this schedule applies.

---

## Pre-day prerequisites

Complete these before the POC day begins (≤30 min total):

- [ ] MapTiler Cloud account created.
- [ ] An API key created in MapTiler, with URL restrictions configured for `https://localhost:*` and (if applicable) `https://www.martajazz.com`.
- [ ] `MapTiler:ApiKey` and `MapTiler:StyleUrl` entries added to `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/appsettings.json` (do not commit the real key — use `appsettings.Development.json` locally if you want to keep the public repo clean).
- [ ] Chosen `StyleUrl`: default to `https://api.maptiler.com/maps/streets-v2/style.json?key={your-key}` for the POC, or `basic-v2` if the streets style feels too busy.
- [ ] Confirm the existing local stack (AppHost + worker + WebAPI) runs and `TransitMap.razor` displays live MARTA data with the existing Azure Maps stack.

---

## POC day schedule

The day is anchored on the noon checkpoint. All times assume a normal working day; if you start later, shift the noon checkpoint accordingly to the four-hour mark from start.

### Morning — Build (start → noon)

**Goal**: By noon, `MapLibreTest.razor` is loadable in a browser and displays MapTiler base map tiles with at least one vehicle marker.

| Time | Activity |
|------|----------|
| 0:00–0:30 | Add MapLibre GL JS CDN script + CSS link to `wwwroot/index.html`. Create `wwwroot/js/maplibre-interop.js` with `ChefMapLibre.createMap` and `ChefMapLibre.maps` registry. Smoke test: a static HTML page that calls `createMap` and renders MapTiler tiles. |
| 0:30–1:30 | Create `Client.Shared/Components/MapLibre.razor` + `.cs` + `.Helper.cs` mirroring `Map.razor` structure, wiring `OnAfterRenderAsync` to call `ChefMapLibre.createMap`. Create `Pages/MapLibreTest.razor` + `.cs` that just renders the component with default camera options. Verify base map tiles load in a browser via Blazor. |
| 1:30–2:30 | Port `vehicle-animator.js` to `maplibre-vehicle-animator.js`: copy lines 19–86 verbatim (haversine, cumDist, findNearest, extractSubPath, interpolateAlongPath). Implement `loadRouteGeometry`, `start`, `stop`. Implement `processNearestPointBatch` with the four touch-point changes from `research.md` R4. Implement `tick` with the per-frame `setData` strategy. |
| 2:30–3:30 | Wire `MapLibreTest.razor.cs` to consume `NotificationService` and `IGtfsEndpointsService` — direct copy of `TransitMap.razor.cs` with `Map` → `MapLibre` substitutions. Add MapLibre layer initialization for routes (line) and vehicles (circle) on map ready. |
| 3:30–4:00 | Buffer for fixing what didn't work. **Noon checkpoint**: tiles visible + at least one vehicle marker rendered. |

**Noon checkpoint — DECISION**

- **PASS** (tiles + ≥1 marker visible): proceed to afternoon.
- **FAIL**: POC outcome is `extend with named blocker`. Write the `decision.md` with the blocker named and stop. Do not push into the afternoon trying to recover.

### Afternoon — Measure (noon → end of day)

**Goal**: All four hard gates measured against both pages under the same conditions, and a decision written.

| Time | Activity |
|------|----------|
| 4:00–4:30 | Add `wwwroot/js/perf-observer.js` (`ChefPerfObserver` namespace). Reference it from both `TransitMap.razor` and `MapLibreTest.razor`. Both pages call `ChefPerfObserver.start('baseline')` / `start('poc')` on map ready. |
| 4:30–5:30 | **Measurement 1 — Cold-load LCP**: For each page in turn, open Chrome DevTools → Network → "Disable cache" + Performance → Reload. Record LCP. Repeat 3 times per page; record median. |
| 5:30–6:30 | **Measurement 2 — Sustained FPS**: For each page in turn, with live MARTA data flowing during peak service hours, open DevTools → Performance → Record. Wait 5 seconds for the SignalR stream to stabilize, then capture a 10-second window. Stop, read median FPS, min FPS, frame timing p50/p95/p99. Count long-task console entries during the same window. |
| 6:30–6:45 | **Measurement 4 — Polyline rendering**: Confirm ≥5 routes render simultaneously on each page without visible defects. Screenshot for the record. |
| 6:45–7:00 | **Measurement 5 — Click handlers**: On each page, click a vehicle marker and an empty area. Verify Blazor-side handlers fire (check console.log output). |
| 7:00–7:15 | **Measurement 6 — Transferred bytes**: Reload each page with cache disabled; read total bytes from DevTools Network panel. |
| 7:15–8:00 | Write `specs/006-maplibre-poc/decision.md` per the template in `data-model.md`. Cite measurements directly. Make the binary decision. |

---

## Decision rules (from spec)

- **All four hard gates pass** (cold-load measurably faster than baseline; sustained FPS ≥45; polylines render; click handlers work) → **migrate**.
- **Any hard gate fails or is borderline** (e.g., 43 FPS) → **don't migrate**. Do not extend to fix borderline results; the indecision tax is worse than the missed opportunity.
- **Noon checkpoint missed** → **extend with named blocker**. The blocker is the single specific obstacle encountered (e.g., "Blazor JS interop failed to register the MapTiler key before the map initialized; needs investigation of OnAfterRenderAsync timing").

---

## What ends up in the repo at end of day

Regardless of outcome:

- `specs/006-maplibre-poc/` — spec, plan, research, data-model, contracts, decision record, checklist, measurement files
- `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/MapLibre.razor*`
- `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/MapLibreTest.razor*`
- `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/maplibre-interop.js`
- `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/maplibre-vehicle-animator.js`
- `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/perf-observer.js`
- Modified `index.html` (MapLibre script + CSS)
- Modified `appsettings.json` (MapTiler key — see security note below)

**Unchanged regardless of outcome**: `Map.razor*`, `TransitMap.razor*`, `azure-maps-interop.js`, `vehicle-animator.js`, `MapsEndpoints.cs`, the worker, the Server-side stack.

---

## Security note on the MapTiler key

The MapTiler API key is a URL-restricted public key (see plan.md Complexity Tracking § II). It is safe to commit to a public repo *if* the key has URL restrictions configured in MapTiler's console limiting it to the project's known origins. If those restrictions are not in place, treat the key as a secret and put it in `appsettings.Development.json` (gitignored) or in user secrets.

**Before the POC day**: verify the URL restrictions are configured in MapTiler before committing any code that embeds the key.

---

## If MARTA service is sleepy on POC day

If peak service hours arrive and fewer than ~150 vehicles are reporting, gate (b) cannot be measured fairly against the spec's "approximately 200 markers" condition. Reschedule the afternoon measurement window to a later day with normal peak service. The morning's build work is preserved; only the measurement step is deferred.
