# Stats Overlay: Research and Decisions

**Date**: 2026-10-11  
**Inputs**: [specification](spec.md), [Stats design](../../docs/STATS_OVERLAY_DESIGN_DOCUMENT.md), [chart foundation](../../docs/PAX_BLAZOR_CHARTJS_COMPONENTS_DESIGN_DOCUMENT.md), constitution v3.3.2, and the current repository.  
**Outcome**: Product and architecture questions are resolved for task generation. Prototypes, database execution, retained-data inspection and load measurements remain explicit implementation/release gates; they are not claimed as completed research experiments.

## 1. Evidence source, identity and actual readiness

**Decision**: Read only `public.city_route_hour_statistics` through a dedicated bounded Data query. Use ordinal `(CitySlug, RouteJoinKey)` identity and the published definition `observed-city-route-hour-statistics-v1`. Keep existing capture, city/category history and internal SQL interfaces intact.

**Rationale**: `Server.Data/Models/CityRouteHourStatistic.cs` already contains accepted distance, exact hourly route membership, sample counts, historical metadata, status, boundary evidence and quarantine. Its configuration uses PostgreSQL `C` collation for route keys and has the `(city_slug, hour_start_utc, route_join_key)` index. City/category tables cannot supply missing distance or recover city-unique vehicle unions. The actual Atlanta slug is `atlanta` (`CityNames.Marta`), so the source document's `marta` request example must not enter the public contract.

History defaults are disabled with dry run enabled. The existence of entities, migrations and deterministic checks does not establish production retention. The 057 plan explicitly leaves disposable PostgreSQL, load, production observation and analyst validation pending. Implementation must inspect actual retaining cities, coverage and cutoff-to-retention lag before exposure.

**Alternatives considered**: Live-feed distance, category-history fallback, current-catalog membership, new capture/storage, and exposing the unrestricted analyst SQL. Each would change evidence or exceed this release's scope.

## 2. Reporting windows, cadence and coverage

**Decision**: Capture one `TimeProvider` instant for a cold snapshot; use `H = floorUtcHour(asOfUtc - 2 minutes)`. Today follows each configured city's actual local midnight and next midnight; last 24 is `[H-24h,H)`. Only fully contained UTC hours contribute. Use each city's resolved `RouteHours.GapLimitFor(city)` as the supported cadence policy.

**Rationale**: The seven configured cities use `America/New_York`, `America/Toronto`, or `America/Denver`; their ordinary midnight offsets are whole hours, but DST gives 23/25-hour days. Compute actual boundaries from IANA zones and retain UTC interval identity/offset. Future fractional-offset zones require boundary exclusions rather than proration. The two-minute allowance is a proposed read policy, not proof that a cohort was retained; quantify writer/finalizer lag and acceptance around 01:02, 14:01 and 14:02.

Validate the entire retained city/hour cohort before applying route filters: supported definition/cadence, Complete status, no conflict or catalog change, one capture run, shared eligible samples and intact row invariants. Route fingerprints identify individual routes and need not be equal across routes. No manifest is added; the existing atomic writer is the source of intact retained membership.

Filtered requests use the union of each route's eligible rows. A selected route absent from a healthy cohort stays unknown; other routes contribute. Unfiltered requests use each actual retained cohort. Missing whole cohorts remain visible on an expected hour grid even with no discoverable routes, and their historical route-hour denominator stays null. Current capture disablement does not suppress previously retained eligible evidence; `history_disabled` explains unavailable evidence where relevant.

**Alternatives considered**: UTC days for every city, rolling-to-the-second totals, blending cadence policies, intersection of selected-route periods, and requiring a complete window before showing useful subtotals. These conflict with confirmed behavior or retained resolution.

## 3. Arithmetic and visual gaps

**Decision**: PostgreSQL numeric/server decimal arithmetic owns distance sums, weighted denominator sums, category shares and known cumulative prefixes. The client only aligns evidence and formats presentation. City activity samples are counted once per city/hour; city active clock hours are counted once when any route is active. X retains route-vehicle-hour counting in both modes.

**Rationale**: `DistinctActiveVehicleCount` is route/hour membership, not a city or period union. `ActiveVehicleCountSum > 0` defines an active hour, including stationary vehicles. Common `ValidActiveSampleCount` supports a city sample denominator once per eligible hour. Averaging displayed ratios would bias results.

Carry `hourlyKnownKilometers`, `cumulativeObservedKilometers` and `plotCumulativeKilometers` separately. An excluded city/hour or a missing selected route nulls every category's plotted stack, while known contributions still increase cards and later prefixes. A fully covered later point resumes the known subtotal. A midnight origin is a visual reference only. Entirely unavailable prefixes remain null; a known zero remains zero.

**Alternatives considered**: Zero filling, interpolating gaps, subtracting plotted cumulative values to recover increments, discarding useful partial selection rows, or pre-stacking then enabling chart stacking. These produce unsupported measurements or duplicate arithmetic.

## 4. One database snapshot and bounded execution

**Decision**: Implement one parameterized PostgreSQL statement/CTE pipeline returning bounded aggregate result groups for all requested cities/windows, including route-option and cohort-validation evidence. Avoid per-chart, per-route and per-city queries. If implementation requires multiple statements, use one short read-only REPEATABLE READ transaction over all groups. The default is a single statement.

**Rationale**: A default READ COMMITTED transaction gives successive SELECTs different snapshots; coverage could authorize a later, quarantined numerator. One SELECT is already a consistent snapshot. PostgreSQL documents both behaviors in [transaction isolation](https://www.postgresql.org/docs/current/transaction-iso.html). Aggregate inside PostgreSQL and retain only bounded result groups in .NET; do not materialize the full All-city route/hour cube. Query using the existing city/hour/route index first.

The five-second budget starts when a cold read is admitted and includes connection acquisition, SQL, projection and size validation; caller/session cancellation propagates. Set command/server statement timeout within that remaining budget, use no row/advisory locks and no unbounded retries/queues. Schema/connection/deadline failures become safe 503 responses. No migration is assumed; an index is justified only by representative EXPLAIN/load evidence and follows schema-before-code deployment.

**Alternatives considered**: Independent cache/query results per panel or assembling All from city caches, tracked EF entities, raw-row client aggregation, a materialized view, and an extra analytics service. They weaken consistency or add unnecessary retained state.

## 5. Public limits, cache and trusted rate limiting

**Decision**: Use read-only POST `/stats/overlay/query`, with explicit `city`/`all` scope, no user date/zone/metric expression and public configured city membership. Enforce 4 MiB request body, 512 UTF-8 bytes/key, 4,096 selected/returned historical keys per city, 32 cities, 32 categories and 8 MiB uncompressed successful response. Fail safely rather than truncate. An unknown valid key stays selected and unavailable.

Cache only successful unfiltered city/All snapshots for an absolute 60 seconds in a dedicated size-limited cache: at most 32 city slots and one All slot, initial 64 MiB object-memory budget. Replace each scope slot at cutoff/date/policy/config-set changes, evict expiration and reject over-budget admission. Account for object memory as well as serialized bytes. Coalesce only these bounded in-flight scope fills. A canceled waiter does not cancel another waiter's shared fill; the fill has its own deadline. Filtered requests are uncached. The All response is one snapshot, never stitched from cached cities. `generatedAtUtc` stays the original snapshot instant; conflict changes may be delayed by at most TTL. POST responses use `Cache-Control: no-store`.

Use an endpoint-specific token bucket with initial capacity 10, replenishment one token/second (60/minute sustained) and no request queue; document that a burst can exceed a fixed 60-request window. Use a separate per-instance limit of four cold database fills, with immediate safe busy rejection. Cache hits do not consume a cold slot. Confirm the Azure ingress/forwarded-header trust configuration before partitioning by client IP; arbitrary forwarded headers cannot select buckets. Per-instance limits scale with replica count and require load evidence. Existing WebAPI has no rate-limiter or forwarded-header trust pipeline to inherit.

**Rationale**: Arbitrary route-subset caching creates uncontrolled key cardinality. A shared cache with a size limit can break other consumers because every entry must provide a size; Microsoft recommends a dedicated cache in [memory caching guidance](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/memory?view=aspnetcore-10.0). ASP.NET supports token and concurrency limiters, bounded queues and Retry-After in [rate limiting middleware](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0). Trusted ingress behavior is an operational gate, not an assumed client identity guarantee.

**Alternatives considered**: Output caching every POST/subset, Redis, an unbounded wait queue, trusting all X-Forwarded-For values, and top-N routes. None is needed to meet the bounded v1 contract.

## 6. Typed transport and client session ownership

**Decision**: Add a narrow opt-in metadata-preserving POST operation to existing `HttpService` and a Stats endpoint service. Return status, safe ProblemDetails code and Retry-After alongside `Result<T>`; dispose responses and propagate caller cancellation. Existing typed POST callers keep their current signatures.

**Rationale**: Current transport loses status/retry information and catches cancellation. A second HttpClient factory would duplicate established configuration. Global `JsonSettings` omits null properties, so Stats DTO nullable evidence fields must explicitly opt into writing JSON null (without changing existing callers). `StatsOverlay` owns session/generation, selection, one request/timer and an atomic accepted snapshot. Routine same-scope refresh retains evidence; scope changes enter Loading. Theme/language changes reuse evidence. Parent TransitMap forwards visibility because the current visibility interop has one callback.

Selection debounce is 200 ms. Failure cooldown begins at 60 seconds, grows to five minutes and honors a longer server delay. New selections/retry/visibility restoration respect active cooldown; cancellation/generation checks stop obsolete responses and post-disposal writes.

**Alternatives considered**: A second global application store, a second visibility registration, relying on cancellation alone, or requerying on theme changes. They duplicate ownership or permit races.

## 7. Shared route presentation and audio

**Decision**: Extract `RouteFilterList` as an immutable-data presentational button grid. Keep existing `RouteFilters` as the adapter to the map's `IRouteFilterViewModel`; Stats supplies independent historical options and an ordinal selected-key set. No feed subscription or map callbacks enter the list's Stats use.

Stats is hosted in TransitMap and opens from either welcome or map. Silent entry never calls the audio-enable callback, changes unlock or awaits geometry/SignalR. After unlock, its audio control uses existing settings and `AudioSettingChangedEventArgs`. Synchronize AudioFab with those events; its current empty Dispose/event behavior does not supply cross-control synchronization.

**Rationale**: A second RouteFilters instance would inject the same live selection view model. Historical options come from either reporting window, including zero/ineligible rows and removed routes; current-catalog-only options are excluded. The grouped scrolling selector has no search. Accessible buttons/select-category/clear-category behavior belongs in one presentation component.

**Alternatives considered**: Reinstantiating the live view model, sharing mutable `RouteItem` instances, copying markup, independent Stats audio preferences or restoring the pre-open preference on close. They violate selection/audio ownership.

## 8. Chart foundation and package boundary

**Decision**: Implement the proposed chart foundation inside `Client.Shared/Components/Charts`, with app-owned pie/bubble/numeric-series models, a shared ChartFrame and internal ChartCanvasHost. Pin Pax 0.9.1 plus self-hosted Chart.js 4.5.1 for the fixed-model prototype. Upstream package metadata confirms net10.0 and `Microsoft.AspNetCore.Components.Web >=10.0.9`; align direct client ASP.NET Core/Blazor references to a compatible exact 10.0 patch. Keep package types inside chart mapping/host and bootstrap registration. No plugin/date adapter is required. Revalidate the pinned combination at implementation time.

One mutable Pax config belongs to each mounted canvas. Initialization stages the latest pending model; one update path synchronizes datasets/options/theme and disposal. Chart kind changes deliberately remount. The package's `SetDatasetsSmooth` update is void/event-driven, so the host serializes app updates rather than awaiting a fictitious async API. Use the package's native-delegating `Legend.OnClickEvent`, `IsDatasetVisible` and `SetDatasetVisibility` bridge for city-keyed visible state/keyboard controls initially; no extra visibility JS is assumed. Source smooth updates retain/reorder dataset objects by ID, supporting hidden-state preservation as an inference that still needs browser proof. Local trusted named callbacks are limited to formatting actually required; API text is always data.

The package catches JS initialization failure and raises only its success event. Add a bounded host initialization deadline and canvas-unavailable presentation rather than treating an app try/catch as complete failure detection. Text, metric evidence and tables remain Ready and usable. Reconcile late initialization/disposal events against the host generation.

Use linear elapsed-hour X, raw category cumulative contributions stacked once, no gap bridging/smoothing, square-root bubble radius `24*sqrt(mean/max)` with disclosed 3 px minimum, and stable keyed styling. Native All legends and accessible visibility controls share city-keyed state; default Chart.js toggling remains authoritative and any adapter reapplies state during updates. Pie/category-stack toggling stays disabled.

**Rationale**: Pie, bubble and numeric-X have different evidence/table shapes. Browser initialization and hidden-state preservation cannot be proven by mapping tests. There is no existing chart package/components/asset to reuse; the foundation is delivery work. Static text/tables remain available on chart failure. Candidate API names are not assumed implementation signatures.

**Alternatives considered**: A generic dashboard/chart registry, arbitrary package-options parameters, runtime CDN, a date adapter, data-label plugin or jittered coordinates. These add unnecessary policy/dependency surface.

## 9. Shared Stats/Info browser history

**Decision**: One page-lifetime OverlayNavigationService and isolated module own namespaced same-URL entries for both surfaces. TransitMap owns Stats; existing MainLayout owns InfoFab/SettingsBlade. Connect those owners through the coordinator rather than adding Stats state to MainLayout. Prefer framework-owned same-URL `NavigationManager` pushes/replacements with namespaced `HistoryEntryState`; the module observes traversal/ownership and supports focus. Preserve unrelated user state, URL/hash and canonical city. Blazor owns `_index` and `userState`; raw pushState cloning the current framework index is not an accepted implementation assumption.

Track session, unique entry ID, surface and originating city, plus explicit-close traversal acknowledgement. Base open pushes once, replacement replaces, Back closes, valid Forward adopts without push and creates a fresh Stats session. Explicit close dismisses immediately and traverses Back only when current entry is demonstrably owned; repeated close/popstate are idempotent. Retain page-session ownership after content unmount. Reload invalidates only old owned markers via replaceState, never startup Back; disposal/unrelated navigation/city reload never traverses unconditionally.

**Rationale**: Current InfoFab has independent FAB/body-button/Escape paths. Browser history entries must coexist with Blazor navigation indexes. The [Blazor 10.0.9 navigation source](https://github.com/dotnet/aspnetcore/blob/v10.0.9/src/Components/Web.JS/src/Services/NavigationManager.ts) demonstrates framework-owned state, and the [History API](https://developer.mozilla.org/en-US/docs/Web/API/History/pushState) defines session entries. Target-browser prototypes must prove same-URL framework navigation does not reload/remount map/audio, preserves unrelated user state, and provides correct traversal ordering, Forward, reload and focus. If the preferred seam fails, a bounded framework-aware adapter must meet the same contract before integration.

**Alternatives considered**: Separate popstate handlers, persisting Stats selections/audio, history.back on Dispose, URL hash changes or restoring old overlays after full reload. They can consume unrelated navigation or violate fresh-session behavior.

## 10. Localization, focus and staged release

**Decision**: Add Stats/chart resources to the existing RouteFilterResources store and add its missing Spanish satellite resource. Existing settings have no language property/selector, Program only calls AddLocalization and document lang is fixed to English. Deliver the smallest shared language setting/event, persisted English/Spanish selection, culture initialization/runtime application and document-lang update needed for this feature; verify WASM globalization/satellite payload and Spanish formatting. Use named full-screen dialog semantics, one focus owner, background inertness, persistent controls and reachable tables. Prove MatBlazor modal behavior; use one small focus helper if needed. Both themes use existing palettes; 100 ms entrance and immediate dismissal respect reduced motion.

Use independent server `StatsOverlay.Enabled=false` and one existing-style client feature flag for both launchers, default false. Resolve read options centrally without changing HistoricalStatistics controls. Missing dependencies yield safe unavailable service; actual missing retention is a 200 availability state. Rollout follows validated history, bounded API, then client launchers; rollback hides launchers/disables reads while keeping capture/history.

**Rationale**: The source refers to an existing `.es.resx`, but only the neutral file exists. MainLayout/TransitMap/modal coordination and current settings/culture support need explicit integration. Accessible text and tables are required evidence even if canvas rendering fails.

**Alternatives considered**: Another resource file/store, hardcoded JS copy, assuming library focus support, one combined capture/read flag, or exposure before measurement. They conflict with the constitution or operational contract.

## 11. Success-criterion interpretations and engineering gates

**Decision**: Preserve SC-011's 5% limit as the existing total history overhead budget: compare a matched workload with history capture plus Stats load against the equivalent baseline without added history/reporting work. Also report the incremental Stats-on versus Stats-off delta with capture held constant. This is not permission to add a second 5% allowance. Record device/network, dashboard concurrency, city/route cardinality, replica count, warmup, samples and p95 calculation before acceptance.

**Rationale**: 057 SC-004 explicitly sets at most 5% enabled-capture p95 increase and the source Stats design says to stay within that existing budget. If matched baselines cannot be reproduced or already exceed it, release remains gated and the concrete result needs review.

**Decision**: Keep SC-012's five-visitor review with at least four correctly explaining scope, zero/partial/unavailable, completed-hour freshness and each-city-local-day All semantics. This is the specification's measurable acceptance assumption, rather than a threshold recorded in the source design. Use visible copy/table interactions and record results before exposure.

**Remaining gates**: Fixed-model pie/bubble/stacked/unstacked browser prototype; package/asset compatibility and local-only chart loading; shared history/focus/cancellation prototype; disposable PostgreSQL correctness/concurrent quarantine/query plans; actual retained coverage/lag for every configured city; cap/memory/response-size/max-cardinality checks; latency and total live-processing budget; English/Spanish/keyboard/screen-reader/mobile/theme review; deployment/rollback handoff. All are implementation work with required evidence. No unresolved product clarification or constitution exception remains.

## Primary chart/client references verified during research

- [Pax 0.9.1 NuGet metadata](https://www.nuget.org/packages/pax.BlazorChartJs/0.9.1): pinned package/framework minimum and tested Chart.js pair.
- [Pax component lifecycle](https://github.com/ipax77/pax.BlazorChartJs/blob/v0.9.1/src/pax.BlazorChartJs/ChartComponent.razor.cs) and [dataset API](https://github.com/ipax77/pax.BlazorChartJs/blob/v0.9.1/src/pax.BlazorChartJs/ChartJsConfig/ChartJsConfig.Datasets.cs): successful initialization, event-driven smooth updates and failure limitation.
- [Native legend event bridge](https://github.com/ipax77/pax.BlazorChartJs/blob/v0.9.1/src/pax.BlazorChartJs/TypeScript/chartEvents.ts), [visibility commands](https://github.com/ipax77/pax.BlazorChartJs/blob/v0.9.1/src/pax.BlazorChartJs/TypeScript/chartCommands.ts), and [dataset synchronization](https://github.com/ipax77/pax.BlazorChartJs/blob/v0.9.1/src/pax.BlazorChartJs/TypeScript/chartDatasets.ts): package-backed visibility path and explicitly inferred persistence.
- [Chart.js bubble data](https://www.chartjs.org/docs/latest/charts/bubble.html), [line gaps](https://www.chartjs.org/docs/latest/charts/line.html), [linear axes](https://www.chartjs.org/docs/latest/axes/cartesian/linear.html), and [accessibility](https://www.chartjs.org/docs/latest/general/accessibility.html): pixel radius, numeric-X/gaps and canvas limitations.
- [HistoryEntryState API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.components.navigationoptions.historyentrystate?view=aspnetcore-10.0) and [Blazor localization](https://learn.microsoft.com/en-us/aspnet/core/blazor/globalization-localization?view=aspnetcore-10.0): framework history seam and culture/globalization requirements. Their application to the existing shell remains a prototype/implementation gate.
