# Phase 1 Data Model: MapLibre Migration

**Feature**: 007-maplibre-migration | **Date**: 2026-05-18

There is no runtime data model in this feature. The "entities" here are *files in the repo* — the complete catalog of every file touched, with the exact change applied. The task generator and reviewer should both verify completeness against this list.

The Blazor in-browser data model (animator state, GeoJSON sources, `RouteShapeFeature`, etc.) is unchanged from POC 006's `data-model.md`. After this migration, those entities continue to exist exactly as they did in the POC — only the files containing them have moved to their final production paths.

---

## File catalog

### A. Renames (POC artifact → production name)

These files exist today under POC names and become the production map component. The content moves with the file; namespace and JS-identifier renames are applied as a global find-and-replace inside the renamed file.

| Current path | New path | Internal change inside the file |
|---|---|---|
| `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/MapLibre.razor` | `…/Components/Map.razor` | None (markup uses no provider-specific identifiers) |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/MapLibre.razor.cs` | `…/Components/Map.razor.cs` | Class declaration: `public partial class MapLibre` → `public partial class Map`; `EventCallback<MapLibre>` → `EventCallback<Map>`; `EventCallback<(MapLibre, string)>` → `EventCallback<(Map, string)>`; `ElementId` prefix `cks-maplibre-` → `cks-map-` |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/MapLibre.razor.Helper.cs` | `…/Components/Map.razor.Helper.cs` | `public partial class MapLibre` → `public partial class Map`; all `ChefMapLibre.*` JS calls → `ChefMap.*`; all `ChefMapLibreAnimator.*` JS calls → `ChefMapAnimator.*`; the `[MapLibre]` log prefixes → `[Map]` |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/maplibre-interop.js` | `…/wwwroot/js/map-interop.js` | `window.ChefMapLibre = {` → `window.ChefMap = {`; internal `ChefMapLibre.maps[...]` self-references → `ChefMap.maps[...]`; `ChefMapLibreAnimator.vehicles[...]` lookup in `centerVehiclePin` → `ChefMapAnimator.vehicles[...]`; log prefix `[ChefMapLibre]` → `[ChefMap]` |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/maplibre-vehicle-animator.js` | `…/wwwroot/js/vehicle-animator.js` (after the old one is deleted in §B) | `window.ChefMapLibreAnimator = {` → `window.ChefMapAnimator = {`; cross-namespace lookup `ChefMapLibre.maps[containerDivId]` → `ChefMap.maps[containerDivId]`; log prefix `[ChefMapLibreAnimator]` → `[ChefMapAnimator]` |

**Rename rule for namespaces:** the JS-namespace rename is purely cosmetic but required so the post-migration code reads as if it always had been the production map. Future readers should not need to know there was ever a "MapLibre" version.

**Validation**: After rename, `grep -r "MapLibre" src/Client` returns zero hits (excluding spec artifacts). `grep -r "ChefMapLibre" src/Client/.../wwwroot` returns zero hits.

### B. Deletions

| Path | Reason |
|---|---|
| `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Map.razor` | Azure Maps version. Deleted *before* the rename in §A overwrites the same path. |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Map.razor.cs` | Azure Maps version. Same. |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Map.razor.Helper.cs` | Azure Maps version. Same. |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/AzureMapsTest.razor` | Azure-specific test page. Spec FR-009. |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/AzureMapsTest.razor.cs` | Azure-specific test page. Spec FR-009. |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/MapLibreTest.razor` | POC page, no production purpose. Spec FR-008. |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/MapLibreTest.razor.cs` | POC page, no production purpose. Spec FR-008. |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/azure-maps-interop.js` | Azure Maps JS interop. Spec FR-004. |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/js/vehicle-animator.js` | Azure Maps-coupled animator. Deleted *before* the rename in §A overwrites the same path. Spec FR-004. |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/css/azure-maps-styles.css` | Empty `body` + unused `.job-site-pin` (research R2). |
| `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/EndpointGroups/MapsEndpoints.cs` | Whole file — only contained the Azure Maps auth token endpoint. Spec FR-007. |

**Ordering rule (critical)**: For the two name-collision pairs in §A — `Map.razor*` and `vehicle-animator.js` — the §B deletion of the old file MUST happen before the §A rename of the new file. The task generator must produce these in the right order.

### C. Single-line / single-block edits

| Path | Edit |
|---|---|
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs` | No type-name changes needed — `Map` was already the type name in the call sites; the rename in §A means `Map` now refers to the MapLibre-backed component. Remove the `await JsRuntime.InvokeVoidAsync("ChefPerfObserver.start", "baseline");` line from `OnMapReadyAsync` (single-page perf marker no longer compares against a baseline). The `[Inject] IJSRuntime JsRuntime` line added in POC 006 can also be removed *if* nothing else uses it; otherwise leave it. |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor` | Unchanged — already references `<Map …/>`. |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/index.html` | Remove: `<link rel="stylesheet" href="https://atlas.microsoft.com/.../atlas.min.css">`, `<link href="css/azure-maps-styles.css">`, `<script src="https://atlas.microsoft.com/.../atlas.min.js">`, `<script src="/js/azure-maps-interop.js">`, `<script src="/js/vehicle-animator.js">` (this last one referred to the OLD animator; the new animator now lives at the same path because of the rename in §A, so a fresh `<script src="/js/vehicle-animator.js">` reference is re-added). Update: `<script src="/js/maplibre-interop.js">` → `<script src="/js/map-interop.js">`. Keep: `<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/maplibre-gl@4/dist/maplibre-gl.css">`, `<script src="https://cdn.jsdelivr.net/npm/maplibre-gl@4/dist/maplibre-gl.js">`, `<script src="/js/perf-observer.js">`. |
| `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/appsettings.json` | Remove the `"AzureMaps": { "AccountClientId": "…" }` block. Keep the `"MapTiler"` block. |
| `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs` | Remove `.MapMapsEndpoints()` from the endpoint-mapping chain (currently at line 124). |
| `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.Development.json` | Remove the `"AzureMaps": { "ManagedIdentityClientId": "…", "TenantId": "…" }` block. |
| `src/ChefKnifeStudios.TransitJazz.Shared/ApiEndpoints.cs` | Remove the `public static class Maps { … }` block in its entirety. |
| `.specify/memory/constitution.md` | Apply the amendment described in `contracts/constitution-amendment.md`: rewrite Principle II, update the Sync Impact Report block at the top, revise the "Frontend (Blazor WASM)" subsection in Tech Stack & Architecture to remove Azure Maps language, bump version 2.0.0 → 3.0.0, update Last Amended date. |

### D. Untouched (positive assertion)

These files are deliberately *not* edited by this feature. Listing them prevents future-me from re-introducing scope creep.

- `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Models/CameraOptions.cs` — provider-agnostic
- `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Models/Position.cs` — provider-agnostic
- All `Shared/Events`, `Shared/Geospatial`, `Shared/GtfsData` files
- `Server.TransitDataWorker/*` — Worker is untouched
- `Server.Core`, `Server.BL`, `Server.Infrastructure` — unaffected
- `Server.WebAPI/EndpointGroups/GtfsEndpoints.cs` — unaffected
- `Server.WebAPI/GtfsStatic/GtfsStaticLoader.cs` — unaffected
- `wwwroot/js/perf-observer.js` — provider-agnostic; remains in place
- `AppHost`, `ServiceDefaults` — Aspire orchestration; unaffected
- `specs/006-maplibre-poc/*` — archived POC artifact; not touched, not deleted (decision record is permanent)

---

## State after migration

A grep for any of the SC-003 search terms in production-path files should return nothing:

```text
atlas.microsoft.com   # gone from index.html, gone with azure-maps-interop.js, gone with MapsEndpoints.cs
ChefMap (old)         # the JS namespace is renamed; the *type* name `Map` returns, but bound to MapLibre
mapAccClientId        # gone with Map.razor.cs (old) and the appsettings.json AzureMaps block
MapLibreTest          # page deleted, no other refs existed
```

The "ChefMap" search term is intentionally ambiguous — it must distinguish between the *old* JS namespace `window.ChefMap` (Azure Maps; deleted) and the *new* JS namespace `window.ChefMap` (MapLibre; the renamed POC file). The verification step in `quickstart.md` resolves this by grepping for distinguishing Azure-only strings (`atlas.Map`, `atlas.data.Feature`, `atlas.source.DataSource`, `getShapeById`) rather than the namespace alone.
