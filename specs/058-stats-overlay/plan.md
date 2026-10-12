# Implementation Plan: TransitJazz Stats Overlay

**Audio scope update (2026-10-12)**: Audio preference changes are withdrawn at the user's request. Restore the original AudioFAB settings/event implementation; Stats has no audio preference channel, mute/unmute controls or synth dependency. Earlier shared-audio tasks and evidence are historical.

**Navigation simplification (2026-10-12)**: Overlay navigation is withdrawn at the user's request. Stats uses a local visibility flag and the browser's native dialog; Info keeps its existing local open/close behavior. No shared coordinator, history entries/traversals, Info/Stats switching, modal lifecycle manager or special settings replacement is required. Earlier navigation prototype tasks and evidence are historical.

**Scope update (2026-10-12)**: The user has assessed the feature and confirmed it works. Remove the temporary chart prototype and browser-history diagnostic UI. Ship English only: no language selector, saved language preference, runtime language switching or Spanish satellite resource. Earlier Spanish/prototype validation records describe the prior implementation.

**Branch**: `058-stats-overlay` | **Date**: 2026-10-11 | **Spec**: [spec.md](spec.md)  
**Input**: [Stats design](../../docs/STATS_OVERLAY_DESIGN_DOCUMENT.md), [chart foundation](../../docs/PAX_BLAZOR_CHARTJS_COMPONENTS_DESIGN_DOCUMENT.md), and [057 history contract](../057-hourly-route-history/contracts/observed-route-hour-statistics-v1.md)  
**Status**: Implementation in progress. See [tasks.md](tasks.md) and [validation.md](validation.md) for completed work, current test results, browser evidence, and remaining release gates. Runtime prototypes and disposable PostgreSQL checks have been completed where recorded; actual retained coverage, matched load and visitor acceptance remain release gates.

## Summary

Add a full-screen Stats dialog owned by StatsFab, accessible silently from welcome or during map playback. Current-city mode independently filters four panels by historical routes; All compares every configured public city using its own local day. One bounded public read derives amounts, ratios, coverage and known cumulative prefixes from a consistent snapshot of existing route-hour history. Useful selected-route contributions survive visual gaps.

Deliver the proposed chart foundation, a presentational route grid shared with the map adapter, transport preserving safe error/retry metadata, English presentation and local Stats open/close state. Keep reads and both launchers disabled until recorded correctness, browser, coverage, capacity and performance gates pass.

## Technical Context

**Language/Version**: C#/.NET 10.0, Razor/Blazor WebAssembly, PostgreSQL SQL, ES modules and CSS; align direct client ASP.NET Core/Blazor references to exact compatible 10.0.9 for Pax.  
**Primary Dependencies**: Existing MatBlazor 2.10.0, EF Core 10.0.4/Npgsql EF 10.0.3, HttpClient factory, Result<T>, settings/events/localization, TimeProvider and ASP.NET Core cache/rate limiting/compression. Add exact Pax 0.9.1 and local Chart.js 4.5.1 with license/provenance. No plugin/date adapter/new runtime project.  
**Storage**: Existing `public.city_route_hour_statistics`, read only; no initial migration. Dedicated bounded memory cache and existing browser audio/theme settings. Stats visibility is local UI state.
**Testing**: Existing xUnit Shared, Client.Shared, WebAPI and Worker projects; independent fixture arithmetic/state/transport checks, opt-in disposable PostgreSQL, actual-browser prototypes/accessibility and matched-load evidence. Planning runs no runtime tests.  
**Target Platform**: Existing Azure Static Web App WASM frontend, .NET Linux Azure Container App WebAPI and PostgreSQL; desktop/mobile browsers, keyboard and screen readers.  
**Project Type**: Public bounded aggregate-read API plus modal frontend and reusable chart/route presentation.  
**Performance Goals**: Warm useful open p95 <=2s; cold read p95 <=2s; prepared read p95 <=250ms; safe read completion <=5s; typical compressed city response <1 MiB, absolute uncompressed success <=8 MiB; combined history/reporting worker p95 overhead <=5% under matched load.  
**Constraints**: Complete compatible cohorts only; ordinal city/JoinKey; exact retained membership; route-based X denominator; completed full UTC hours; city-local date/DST; one snapshot; no truncation/proration/extrapolation/individual records; isolated map/audio selection; native modal focus; one client request; no obsolete writes; default-disabled exposure.
**Scale/Scope**: Seven configured cities initially; <=32 cities/categories, <=4,096 historical/selected routes per city, <=512 UTF-8 bytes/key, 4 MiB request. Initial cache <=33 slots/64 MiB, four concurrent cold reads/instance, sustained 60/minute IP reads with burst ten and no queue. Bound-size browser/query performance requires measurement.

## Constitution Check

Evaluated against constitution v3.3.2 before research and after design. Design gates pass without exceptions; runtime acceptance is still gated.

| Principle | Initial / post-design | Response |
| --- | --- | --- |
| I. Decoupled cloud architecture | Pass / Pass | Shared DTOs, Data query, WebAPI policy and existing WASM; no database access in frontend |
| II. No frontend secrets | Pass / Pass | Existing named API transport/public aggregate data; no new credential |
| III. Two-pass processing | Pass / Pass | No capture/publisher changes; bounded read admission and no row/advisory locks |
| IV. OpenTelemetry | Pass / Pass | Existing telemetry; bounded scope/status/reason labels without route arrays |
| V. GitHub Actions | Pass / Pass | Existing client/server artifacts and disabled rollout; any proven index precedes code |
| VI. GTFS identity | Pass / Pass | Ordinal city/JoinKey separate from static RouteId; canonical `atlanta` and retained metadata |
| VII. Cartography | Pass / Pass | Navigation preserves current map, city, layers and basemap |
| VIII-IX. Music/selection | Pass / Pass | Independent Stats state; silent entry preserves lock; existing audio settings/events are untouched by Stats |
| X-XI. Controls/overlays | Pass / Pass | Full-screen reachable controls, native dialog focus, 100 ms entrance/immediate close/reduced motion |
| XII. Localization | Pass / Pass | Existing neutral English resources, fixed English startup culture/document language, no language options |
| XIII. Dark parity | Pass / Pass | Existing palettes, keyed colors, non-color evidence and dataset/options updates |
| User Git instructions | Pass / Pass | Feature branch retained, commit hooks skipped, AGENTS marker update preserves no-commit section |

Shared route extraction preserves existing map behavior. Full-screen text/tables, silent access and local Stats open/close controls are feature work, with no constitution exception.

## Project Structure

### Documentation (this feature)

```text
specs/058-stats-overlay/
  spec.md
  checklists/requirements.md
  plan.md
  research.md
  data-model.md
  quickstart.md
  contracts/
    stats-overlay-api-v1.md
    stats-overlay-read-v1.md
    stats-overlay-ui-v1.md
  tasks.md                           # generated dependency-ordered implementation checklist
```

### Source Code (repository root)

Original planned file map. The current implementation and remaining work are tracked in `tasks.md` and `validation.md`.

```text
src/ChefKnifeStudios.TransitJazz.Shared/
  ApiEndpoints.cs
  Enums/FeatureFlags.cs                         # StatsOverlay flag
  Stats/StatsOverlayRequest.cs
  Stats/StatsOverlayResponse.cs                 # explicit null serialization

src/Server/ChefKnifeStudios.TransitJazz.Server.Data/
  Statistics/StatsOverlayQuery.cs               # parameterized snapshot SQL/projections
  ServiceCollectionExtensions.cs

src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/
  EndpointGroups/StatsEndpoints.cs
  Statistics/StatsOverlayOptions.cs
  Statistics/StatsOverlayService.cs             # windows/cache/fills/admission/policy
  Program.cs
  appsettings.json                             # StatsOverlay.Enabled=false

src/Client/ChefKnifeStudios.TransitJazz.Client.Core/
  Services/HttpService.cs                       # opt-in metadata/cancellation
  Services/EndpointsServices/StatsEndpointsService.cs

src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/
  Components/Stats/StatsOverlay.razor[.cs/.css]
  Components/Stats/StatsOverlayProjection.cs
  Components/AudioUnlockOverlay.razor
  Components/FABs/InfoFab.razor
  Components/RouteFilterList.razor[.css]
  Components/RouteFilters.razor[.cs/.css]
  Components/Charts/
    ChartFrame.razor[.css]
    ChartCanvasHost.razor[.cs]
    ChartPresentation.cs
    SeriesChart.razor[.css]
    SeriesChartModel.cs / SeriesChartConfig.cs
    PieChart.razor / PieChartModel.cs / PieChartConfig.cs
    BubbleChart.razor / BubbleChartModel.cs / BubbleChartConfig.cs
  Models/Settings.cs                           # preserve saved audio/theme
  Resources/RouteFilterResources.resx
  wwwroot/js/stats-dialog.js                    # native showModal/close only
  wwwroot/js/chart-formatters.js               # trusted necessary callbacks
  ChefKnifeStudios.TransitJazz.Client.Shared.csproj # Pax + patch alignment

src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/
  Pages/TransitMap.razor[.cs]
  Layout/MainLayout.razor                       # connect existing Info/settings owner
  Program.cs
  wwwroot/index.html                           # local chart/culture/document lang
  wwwroot/appsettings[.Development].json        # same false launcher flag
  wwwroot/vendor/chartjs/4.5.1/                 # distribution/license/provenance
  ChefKnifeStudios.TransitJazz.Client.WebApp.csproj # patch/globalization

src/ChefKnifeStudios.TransitJazz.Shared.Tests/   # contract/null serialization
src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ # pure chart/Stats/state/transport
src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/ # SQL/API/policy/cache
src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/ # existing regression

.github/workflows/client.yml / client-marta.yml / server.yml
bicep/main.bicep / modules/containerApp.bicep / main.json # only for read tunables if needed
```

**Structure Decision**: Shared owns wire contracts, Data bounded SQL/internal aggregates, WebAPI time/cadence/exposure/cache/error policy, and Client.Core transport. Client.Shared owns charts/route presentation/local Stats session/browser coordination. TransitMap owns the welcome entry/current city/visibility; StatsFab owns Stats visibility; MainLayout keeps the existing independent Info/settings components. No formula registry, extra application store or new runtime/test framework.

## Design Details

### Reporting evidence and consistent reads

The [read contract](contracts/stats-overlay-read-v1.md) defines exact grids/windows/eligibility/arithmetic. Capture one as-of and configured city/cadence set. Today uses as-of local date, never the grace-adjusted cutoff. Validate every retained row in a city/hour before selecting route rows; keep selector metadata unfiltered on filtered reads.

One SQL/CTE statement returns all related result groups. A short read-only REPEATABLE READ transaction is permitted only if implementation needs multiple statements. No per-chart/per-route/per-city queries, tracked entity cube or read-time row/advisory lock. Aggregate in PostgreSQL and assemble one immutable response. All needs city/hour summaries rather than returning route/hour records. Start with the existing `(city_slug,hour_start_utc,route_join_key)` index; justify a migration only with EXPLAIN/load evidence.

A Complete compatible inactive row is zero evidence. Missing selected membership leaves other routes' contributions intact, while the entire category stack gaps. Missing unfiltered cohorts have unknown historical population. Coverage units remain distinct. Latest retained route metadata is deterministic; category amounts follow each row's category. Current capture disablement does not suppress valid older retained evidence.

### Public contract, cache and bounds

The [API contract](contracts/stats-overlay-api-v1.md) specifies read-only POST, configured canonical city and bounded ordinal keys with safe ProblemDetails. Valid unknown routes remain selected/unavailable. Stats nullable fields explicitly override existing global omit-null behavior. UTC timestamps serialize with Z and metrics as numbers/null, never strings/nonfinite coordinates.

Use a dedicated absolute-60-second cache with <=33 slots and initial 64 MiB measured object budget. Scope/date/cutoff/definition/cadence/config identity replaces superseded slots. Only successful unfiltered city/All fills coalesce; filtered reads are uncached. Waiter cancellation cannot cancel another waiter's shared fill; shared fills have their own five-second budget. Failures/expired values are not served silently; All never assembles city caches. Quarantine visibility lag is bounded by TTL.

Endpoint token bucket has burst ten, refill one/second and no queue; partition only on trusted ingress/client provenance. Separate nonblocking four-slot cold-read admission protects DB/capture. Size-check the response before successful headers, with >8 MiB safe failure. Five seconds includes DB acquisition/commands/projection/size validation and flows through cancellation/remaining timeout. Reuse existing compression, no-store, ProblemDetails and bounded telemetry. Rate/cache/capacity defaults are validation targets.

### Chart foundation before API coupling

TransportCategoryColors is the shared palette for running-label dots, category pie slices and cumulative category lines: bus green, rail blue and streetcar red, with lighter dark-mode shades and a neutral fallback. Normalize category-prefixed series keys before selecting colors. Route-specific bubbles/pills retain GTFS colors; unmatched routes use the shared transport palette for the current theme. ChartFrame receives IsDark and applies the same text/surface/grid theme as its canvas so card backgrounds and labels remain readable.

Pin Pax 0.9.1/local Chart.js 4.5.1 and compatible client package patches. Prove fixed pie, bubble, raw stacked category line and unstacked city line before API/UI coupling. Compose typed renderers with ChartFrame/internal ChartCanvasHost: one stable config/canvas, latest pending input, coordinated no-animation updates, deliberate kind remount and generation-aware disposal. Smooth update is void/event-driven; do not invent async APIs.

Use linear numeric X for elapsed hours since each midnight, local-clock/offset city labels and elapsed All labels. Raw category cumulative contributions stack once; null the whole plot stack for missing selected membership; no bridging/smoothing/future estimates. All lines are unstacked/unfilled. Bubble radius is square-root scaled with disclosed 3 px minimum, larger-first order, stable keyed palette and no jitter. Tables use the same complete app model.

Use native-delegating Pax legend/visibility APIs for city-keyed All hidden state and keyboard/Show all. Preserve through refresh/reorder/theme, new cities visible; reset on leaving All/close. Browser proof is required for inferred dataset-object preservation. Bounded initialization failure/deadline presentation keeps text/tables usable. Trusted local callbacks format required tooltip/ticks; package types stay out of feature/wire DTOs.

### Route session, refresh and audio

Route bubbles and filter pills share one cached GTFS route-color resolver and historical/category fallback. GTFS route colors persist through theme changes and size ordering; historical/category fallback colors follow the transport-label theme palette, and All-city bubbles retain the keyed city palette. When route metadata finishes loading, replace the accepted snapshot's presentation locally so pills and bubbles update together without a new Stats request.

Stats wraps the shared route filter in the map's MatAccordion/MatExpansionPanel structure, using the existing RoutesLabel resource and a local expanded flag that starts collapsed. The panel retains selections across collapse/expand and is absent in All mode.

Share one MatBlazor RouteFilterList presentation between map RouteFilters and Stats: compact colored pills, MatSubtitle1 category labels with keyboard-operable MatButton selection, MatIconButton category clearing and MatButton bulk actions. Share CSS and theme handling without separate map/Stats visual variants. Map RouteFilters retains existing live sorting/selection/emphasis/music through its adapter. Stats uses immutable retained items and its own ordinal selected set, cached GTFS route colors with category colors for unmatched historical routes, bulk operations, shared summary and no search; historical availability/category-change notes remain visible below the pills. It mutates no map RouteItem and sends no map hover/selection callbacks. Mode/close/city changes reset Stats; metadata refresh never silently drops a selected key.

Stats owns session/generation, normalized scope, one request/timer and one accepted snapshot. Debounce selection 200 ms. Scope changes enter Loading; routine refresh retains same-scope Ready evidence with refresh/stale status. Check generation/scope/disposal before atomically publishing labels/models/coverage/windows. Parent visibility is forwarded. Cooldown starts at 60 seconds, grows to five minutes or longer Retry-After, and governs retry/selection/visible restoration. Presentation changes reuse evidence.

Welcome Stats has a separate callback/control from gesture-attached enable-audio. It never unlocks/awaits map or feed. Stats has no audio controls, preference subscriptions or synth dependency. AudioFab retains its original settings/event path; opening and closing Stats leave audio lock, playback and preferences unchanged.

### Local visibility, native focus and English presentation

StatsFab owns one local open flag and listens to the existing app event channel for the welcome launcher. StatsOverlay receives IsOpen and an OnClose callback, starts/resets its own Stats session and cancels work when closed. The small `stats-dialog.js` helper calls native showModal/close; the browser supplies focus containment, background inertness and focus restoration. There is no history state, coordinator, traversal acknowledgement or special settings-close path. Info retains its existing independent controls. The full-screen shell keeps one scroll owner, safe-area controls, 100 ms entrance and reduced-motion behavior.

Use the neutral RouteFilterResources for all copy and accessible names. Set the application culture and document language to English at startup; no language preference or controls are available. Preserve saved audio/theme settings without changing the Settings version. Dates use the city zone and numbers use English formatting. Theme changes update datasets/options locally, with text/table alternatives.

## Validation Strategy and Gates

| Gate | Required evidence | Requirements |
| --- | --- | --- |
| G1. Contracts/fixtures | Canonical identities, DTO/null/reason vocabulary and independently calculated healthy/zero/partial/transfer/category-change fixtures | FR-001â€“027; SC-003/004 |
| G2. Chart prototype | Four fixed forms, numeric X/DST, once-only stacking, contributions/tooltips, multiple canvases/pending init/theme/disposal/asset failure, native+keyboard visibility | FR-028â€“035/045â€“047; SC-007/009 |
| G3. Local dialog controls | Stats welcome/map open, Close/Escape/reopen/reload, unchanged URL, native focus/inertness and preserved audio | FR-036-043; SC-005/006 |
| G4. PostgreSQL/API | One-snapshot quarantine/write races, ordinal keys/missing grids/eligibility, bounded SQL/plans, deadline/cancel, caps/errors/cache/coalescing/null serialization | FR-005â€“027/049â€“053; SC-002â€“004/010 |
| G5. Integrated accessibility/state | Map-vs-Stats isolation, silent entry/unchanged audio, cooldown/races/hidden/disposal, tables/warnings, 320px/200%/touch/keyboard/screen reader/English/themes | FR-001â€“004/028â€“050; SC-001/005â€“009/012 |
| G6. Actual history | Every configured city's actual mode/retention, representative healthy day, zero/partial/unavailable/early morning, retention lag and 057 operational dependencies | FR-055; SC-011 |
| G7. Capacity/performance | 4,096 historical union/32 cities/categories, response/object memory/four reads/trusted ingress/replicas, cold/prepared latency and no capture blockage | FR-051/052; SC-001/002/010/011 |
| G8. Release handoff | Definitions/windows/actual cities/coverage/cutoff/load/sizes/errors/ingress, flags/deployment/monitoring/rollback and all skipped gates | FR-054/055; SC-011/012 |

Arithmetic examples: 160 km and 16/20 selected route-hours; X=30/6=5, Y=30/2=15, mean=180/120=1.5; city samples counted once; gap then 140 km resumed subtotal; hidden endpoints still reconcile All. Useful amounts can remain when no point can plot. Zero, unavailable/undefined null, pending and hidden-all are distinct.

Use existing tests by responsibility and independent evidence fixtures. Reuse `ROUTE_HOUR_HISTORY_DISPOSABLE_CONNECTION` provisioned-disposable pattern, never operator data. Build WebAPI/client and publish WASM for package/assets/globalization. Record skipped DB/browser/load checks as release gates. The planning stage itself performed document review only; implementation validation is recorded in `validation.md`.

SC-011 retains the existing total <=5% matched-load history budget, without another 5% reporting allowance. Record total history+Stats versus equivalent baseline without that work, plus Stats-on/off delta with capture fixed; define load/device/network/replicas/cardinality/p95 before running. SC-012 needs four of five visitors to explain scope, zero/partial/unavailable, completed-hour freshness and All local days; this threshold is the spec's explicit acceptance assumption, absent from the source interview.

## Implementation Sequence

1. Freeze contracts/fixtures/options and preflight 057 schema/capture, configured identities/cadence and ingress (G1).
2. Deliver chart and native-dialog controls before feature coupling (G2/G3); include required package/culture/audio seams.
3. Implement bounded SQL/windows/coverage/arithmetic, DTOs/endpoint/cache/admission/typed transport with G4; keep reads disabled.
4. Extract route presentation/map adapter; deliver current-city US1 and independent route US2. Accessibility/localization/failure behavior accompanies each increment.
5. Deliver All US3/native visibility, local open/close US4, accessible evidence US5 and refresh/recovery US6 with one consistent snapshot/local session.
6. Finish G5â€“G8 and operator handoff. Deploy validated existing history dependency, API, then client; expose after recorded acceptance. Rollback hides both launchers/disables reads without deleting history/changing capture.

Task generation was expected to cover shared safety foundations, six story increments and explicit release evidence even when an increment works locally. The generated checklist and implementation status are now recorded in `tasks.md` and `validation.md`.

## Complexity Tracking

No constitution violations. Three finite chart shapes use composition; one query, bounded cache/fill owner and local Stats visibility implement the contracts. No dashboard registry, generic repository, extra application store/query language/cache table/new service/statistics SignalR stream/plugin/backfill is justified.
