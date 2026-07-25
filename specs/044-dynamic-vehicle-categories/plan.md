# Implementation Plan: Dynamic Per-City Vehicle Categories

**Branch**: `044-dynamic-vehicle-categories` | **Date**: 2026-07-18 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/044-dynamic-vehicle-categories/spec.md`
**Design source**: `docs/DYNAMIC_VEHICLE_CATEGORY_DESIGN_DOCUMENT.md` (completed 12-question decision record; this plan follows its resolved decisions)

## Summary

Replace the hardcoded, city-agnostic `TransitMode { Bus, Rail }` enum with an open-ended, per-city, config-driven set of **vehicle categories** (plain lowercase strings like `bus`, `rail`, `streetcar`, `unknown`). WebAPI is the single classification authority: it reads an optional per-city `RouteTypeCategories` map from `appsettings.json`, stamps a category string (and the raw `route_type` int, for client-side display ordering) onto each route's shape properties at GTFS-static load time, and — cities that declare nothing keep today's exact `route_type` 0/1/2 → `rail`, else → `bus` rule. The category rides the shape JSON to both the Worker (which reads it into its route→category map and passes it through on the SignalR `RouteNearestPointRecord`, `Key(10)` retyped enum→string) and the client (which fetches shapes once). The client rewrites its two hardcoded `@if` filter sections and two count rows into `@foreach` loops over a dynamically-derived, `route_type`-ascending category order, with resx-driven labels + running-count phrases (with graceful fallbacks), `data-category` CSS attribute selectors (light + dark), and a map paint-expression re-key that makes rail dots render larger for the first time (fixing a latent case-mismatch bug). This is a **breaking MessagePack wire change** deployed atomically across server+worker and client, per the project's existing wire-contract deploy discipline.

Primary requirement served: **Toronto streetcars get their own filter section, running-count row, and filter** (FR-001, FR-006, FR-007, SC-001), while the four existing cities are **byte-for-byte unchanged** via the fallback rule (FR-003, SC-002).

## Technical Context

**Language/Version**: C# / .NET 10.0 (all server + client projects); JavaScript (ES modules, browser) for the two map/animation interop files
**Primary Dependencies**: Blazor WebAssembly + MatBlazor (client UI), MessagePack (SignalR wire contract), MapLibre GL JS over MapTiler (map), SignalR (WSS transport), CommunityToolkit.Mvvm (`[ObservableProperty]`/`ObservableObject` in the client ViewModel), `IStringLocalizer<RouteFilterResources>` (localization), xUnit (WebAPI.Tests)
**Storage**: N/A — no persistence change. WebAPI's in-memory `IKeyValueRepository<string>` shape store is written with an extra property; no schema/store change.
**Testing**: xUnit — existing `WebAPI.Tests/GtfsStaticLoaderTests.cs` (classifier fixtures + assertions) and `EventEnvelopeMessagePackTests.cs` (wire round-trip); optional new Worker-side and client ViewModel assertions
**Target Platform**: Browser (Blazor WASM client) + Linux-container background worker + ASP.NET Core WebAPI (Azure-hosted per constitution)
**Project Type**: Web application — decoupled 3-unit cloud architecture (WebAPI + TransitDataWorker + Blazor WASM client), plus a `Shared` contracts project
**Performance Goals**: No new latency budget. The only per-tick wire cost is `Key(10)` growing from a 1-byte packed enum to a short MessagePack string (~5–10 bytes/record) — a bounded, accepted regression against feature 040's thinning. Display-order int (`RouteType`) rides the once-per-startup shape catalog, adding **zero** per-tick bytes.
**Constraints**: Breaking MessagePack wire change → server (WebAPI+Worker) and client MUST deploy in one coordinated window, no dual-field transition (design §3.14/§5.6). Client MUST NOT hardcode any fixed category list (FR-015). New color-bearing CSS MUST ship light + dark (Principle XIII).
**Scale/Scope**: 5 configured cities (Toronto is the only one needing a `RouteTypeCategories` block day one); ~14 files across 4 projects (`Shared`, `WebAPI`, `TransitDataWorker`, `Client.Shared`, `Client.WebApp`); the two client UI components (`RouteFilters`, `TransitRunningLabel`) get their `@if`-pair → `@foreach` rewrites; two JS interop files re-keyed.

**Resolved unknowns** (design doc pre-resolved all 12 grill-me questions; no open NEEDS CLARIFICATION). See `research.md` for the decision record digest.

## Constitution Check

*GATE: evaluated against `.specify/memory/constitution.md` v3.3.2. Re-checked after Phase 1 (below).*

| Principle | Relevance | Verdict |
|---|---|---|
| **I. Decoupled Cloud Architecture** | Change spans all 3 units + Shared, each still independently deployable; communication stays SignalR/HTTPS. | ✅ Pass — no architectural coupling introduced. |
| **V. GitHub Actions CI/CD** | Breaking wire change requires coordinated multi-lane deploy (server+worker atomic, client separate; MartaJazz ships from `deploy/marta-jazz`). Plan documents the sequencing; no pipeline change. | ✅ Pass — handled by deploy discipline, not code. |
| **VI. GTFS ID Mapping (`RouteJoinKey`)** | Feature adds a *category* field; join-key logic (`RouteShapeProperties.JoinKey`, Worker `BuildRouteIndex` keying) is untouched. `RouteId`/`RouteJoinKey` naming preserved. | ✅ Pass — no join-key change. |
| **VII. OSM Cartography (data layers persist)** | Map change is a paint-expression re-key on the existing `vehicles` layer + a GeoJSON property rename (`transitMode`→`category`); no source/layer lifecycle or basemap change, no re-fetch. | ✅ Pass. |
| **VIII. Generative Music** | Explicitly out of scope — no synth/`transit-synth.js` change; instruments stay per-route (design §3.7). | ✅ Pass — no audio change. |
| **IX. Persistent Multi-Selection** | Filter panel generalizes 2 sections → N, but keeps the persistent selection set, per-section Select-all/Clear, and scoping semantics. `SelectAll`/`ClearSelection`/`HasSelectionFor` retype `TransitMode`→`string category`. | ✅ Pass — mechanics preserved, generalized. |
| **XII. Internationalized, Settings-Driven** — single resx / no inline copy | New category labels + running-noun phrases go **only** into `Client.Shared/Resources/RouteFilterResources.resx` via `IStringLocalizer<RouteFilterResources>`; no inline copy, no new resource file. | ✅ Pass on structure. |
| **XII. — English + Spanish both supported** | New keys are **EN-only** this change (`.es` deferred). | ⚠️ **Tracked deviation** — see Complexity Tracking. Consistent with the constitution's already-accepted deferral for features 015/016; not a new violation of intent, and the removed `Rail`/`Buses`/`NumTrainsRunning`/`NumBusesRunning` keys were themselves EN-only. |
| **XIII. Dark-Mode Parity** | New `data-category` color-bearing CSS (filter section styling, count-row icons) MUST ship light + dark counterparts in the same change; migrate the existing `--rail`/`--buses` color rules to `[data-category]` selectors preserving both themes. | ✅ Pass — an explicit obligation baked into the plan/tasks, sourced from `ColorConstants.Dark` where applicable. |

**Gate result: PASS.** One tracked, precedented deviation (Spanish deferral) recorded in Complexity Tracking; no unjustified violations.

## Project Structure

### Documentation (this feature)

```text
specs/044-dynamic-vehicle-categories/
├── plan.md              # This file
├── research.md          # Phase 0 — decision-record digest + verification of design-doc claims against as-built code
├── data-model.md        # Phase 1 — Vehicle Category, City Category Config, retyped wire/shape records, client ViewModel state
├── quickstart.md        # Phase 1 — build/run/verify steps incl. Toronto manual check + regression checks for existing cities
├── contracts/
│   ├── wire-contract.md         # RouteNearestPointRecord.Key(10) int→string; RouteShapeProperties {Category, RouteType}; GeoJSON category/routeType
│   ├── category-config.md       # WebAPI appsettings RouteTypeCategories shape + fallback + unmapped rules
│   └── client-ui-contract.md    # CategoryOrder derivation, ActiveCountsByCategory reactivity, resx keys, data-category CSS, map paint expr
└── checklists/
    └── requirements.md          # (from /speckit-specify)
```

### Source Code (repository root — real paths, as globbed)

```text
src/
├── ChefKnifeStudios.MartaJazz.Shared/
│   ├── Events/RouteNearestPointBatchEvent.cs     # remove enum TransitMode; Key(10) TransitMode→string Category="bus"
│   ├── GtfsData/RouteShapeFeature.cs             # RouteShapeProperties.Mode→Category (string); add int RouteType=3 (before City)
│   └── (JsonOptions.cs — locate; verify JsonStringEnumConverter has no other enum consumer before touching)
│
├── Server/
│   ├── ChefKnifeStudios.MartaJazz.Server.WebAPI/
│   │   ├── GtfsStatic/GtfsStaticLoader.cs        # extend CityStaticEntry w/ RouteTypeCategories; ClassifyCategory(); ParseRouteMetadata tuple → (string Category, int RouteType); BuildLineStringFeature emits "category"+"routeType"
│   │   ├── appsettings.json                      # add RouteTypeCategories to the ttc entry (§4.1)
│   │   └── (appsettings.Development.json has 4 cities — no ttc — nothing to add there today)
│   └── ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/
│       └── Worker.cs                             # _routeMode/modeMap Dictionary<string,TransitMode>→<string,string>; join-failure fallbacks TransitMode.Bus→"unknown" (§3.6); tuple/param retypes threaded through
│
├── Client/
│   ├── ChefKnifeStudios.MartaJazz.Client.Shared/
│   │   ├── ViewModels/RouteFilterViewModel.cs    # RouteItem.Mode→Category(string)+RouteType(int); _railVehicleIds→_vehicleCategory dict; ActiveBusCount/ActiveRailCount→ActiveCountsByCategory ([ObservableProperty], reassigned each recompute); add CategoryOrder; SelectAll/ClearSelection/HasSelectionFor(string)
│   │   ├── Components/RouteFilters.razor(.cs/.css) # @if pair → @foreach over CategoryOrder; route-filters__section + data-category; light+dark CSS
│   │   ├── Components/TransitRunningLabel.razor    # 2 rows → loop; broaden PropertyChanged filter → nameof(ActiveCountsByCategory); RunningNoun() helper; [data-category] icon CSS light+dark
│   │   ├── Resources/RouteFilterResources.resx    # remove Rail/Buses/NumTrainsRunning/NumBusesRunning; add rail/bus/streetcar labels + RunningNoun_* + VehiclesRunningTemplate
│   │   └── wwwroot/js/{map-interop.js, vehicle-animator.js}  # transitMode→category; 'Rail'→['downcase',...] 'rail'; fallback 'bus'→'unknown'
│   └── ChefKnifeStudios.MartaJazz.Client.WebApp/
│       └── Pages/TransitMap.razor.cs             # r.TransitMode.ToString().ToLowerInvariant() → r.Category
│
tests/  (as located under the WebAPI.Tests project)
├── GtfsStaticLoaderTests.cs                      # retype fixture tuples + TransitMode literals → (string Category,int RouteType); add TTC-shaped + unmapped-value cases
└── EventEnvelopeMessagePackTests.cs              # Key(10) round-trip: TransitMode.Rail arg → string category
```

**Structure Decision**: Web application (constitution's decoupled 3-unit architecture + `Shared` contracts). No new projects, directories, or dependencies. Every change is an edit to an existing file in one of the four affected projects (`Shared`, `Server.WebAPI`, `Server.TransitDataWorker`, `Client.Shared`) plus one `Client.WebApp` file and the two `Client.Shared/wwwroot/js` interop files. The exact real paths above were verified by glob (note: real prefix is `src/ChefKnifeStudios.MartaJazz.*`, not the design doc's abbreviated `src/Shared/...`).

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| New resx keys are **EN-only** (`.es` not added), a partial deviation from Principle XII's "English and Spanish MUST both be supported" | Matches the project's established, constitution-tolerated deferral of Spanish for recent UI features (015 route-filter-ui, 016 settings-blade); the strings being replaced (`Rail`/`Buses`/`NumTrainsRunning`/`NumBusesRunning`) were themselves EN-only, so this is not a *regression* in localization coverage | Authoring full `.es` translations now would expand scope beyond the feature's intent (config-driven categories) and diverge from how every recent sibling feature shipped; the `.resx` mechanism is used correctly (single file, keyed lookup, no inline copy) so Spanish can be added later with zero structural change |
| Breaking MessagePack wire change with **no dual-field transition** (`Key(10)` retyped in place) | Adding `Category` alongside a deprecated `TransitMode` would double that slot's payload during rollout (undoing feature 040's thinning) and leave temporary fallback code to remember and remove | A coordinated atomic deploy is the project's *existing* discipline for `RouteNearestPointBatchEvent` wire changes (see `project_signalr_wire_deploy_constraint` / feature 040 precedent), so this is the established pattern, not added complexity |
