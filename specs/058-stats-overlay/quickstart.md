# Stats Overlay implementation quickstart

**Navigation simplification (2026-10-12)**: Overlay navigation is withdrawn at the user's request. Stats uses a local visibility flag and the browser's native dialog; Info keeps its existing local open/close behavior. No shared coordinator, history entries/traversals, Info/Stats switching, modal lifecycle manager or special settings replacement is required. Earlier navigation prototype tasks and evidence are historical.

**Scope update (2026-10-12)**: The user has assessed the feature and confirmed it works. Remove the temporary chart prototype and browser-history diagnostic UI. Ship English only: no language selector, saved language preference, runtime language switching or Spanish satellite resource. Earlier Spanish/prototype validation records describe the prior implementation.

**Status:** Local implementation is available. Both checked-in exposure defaults remain disabled. No production database was inspected and no deployment or public enablement occurred. See [validation.md](validation.md) for test/browser evidence and [release-handoff.md](release-handoff.md) for artifacts, measurements, and pending gates.

## Contract and data behavior

Read the [plan](plan.md), [research](research.md), [data model](data-model.md), [API contract](contracts/stats-overlay-api-v1.md), [read semantics](contracts/stats-overlay-read-v1.md), and [UI/history contract](contracts/stats-overlay-ui-v1.md) before changing behavior.

- Existing schema dependency: `20261010151240_CreateCityRouteHourStatistics`, table `public.city_route_hour_statistics`, metric definition `observed-city-route-hour-statistics-v1`, and existing `(city_slug, hour_start_utc, route_join_key)` index. Do not infer retention, persistence mode, or actual city coverage from source configuration.
- Configured city identities: `atlanta`, `washington-dc`, `boston`, `new-york-city`, `toronto`, `philadelphia`, and `denver`. `marta` is not a public Stats city. Eastern cities use `America/New_York`; Toronto uses `America/Toronto`; Denver uses `America/Denver`. Current configured cadence is 30 seconds for each city.
- A single server instant defines each response. The read cutoff is the UTC hour floor of that instant minus two minutes. Today uses each city's local calendar day; Last 24 Completed Hours is a separate UTC window. All compares each configured city's own local day.
- Zero is reported only with eligible coverage. Partial totals retain known contributions; unknown evidence is not estimated. Future plot values are null. Selected route coverage preserves expected and available route-hours independently.
- Bubble X is kilometers per route-vehicle-hour; Y is kilometers per active clock-hour; marker area represents weighted mean active vehicles. Counts are summed before division. City activity samples count once per city/hour.
- API `StatsOverlay.Enabled=false` and client `AppSettings.FeatureFlags.StatsOverlay=false` are the checked-in defaults. Capture settings remain separate. Valid retained data can be read when capture is off, but actual history availability is an operator evidence gate.

## Run locally

Run the API and client in separate PowerShell terminals. Existing local HTTPS profiles use API port `52834` and client port `52832`; configure the development API base URL through existing local settings. Never put an API/database connection string or secret in WASM settings.

```powershell
dotnet run --project ./src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/ChefKnifeStudios.TransitJazz.Server.WebAPI.csproj
```

```powershell
dotnet run --project ./src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/ChefKnifeStudios.TransitJazz.Client.WebApp.csproj
```

For local fixture work only, a developer may override the API and client flags in untracked local settings. Do not edit tracked defaults or alter `HistoricalStatistics.Enabled`, dry-run, cities, categories, or capture configuration to enable the dashboard. Disabled API reads return safe `stats_disabled`; enabled reads without a usable dependency return safe `dependency_unavailable`. The welcome Stats entry does not unlock audio; the existing audio gesture and shared mute control remain authoritative.

The city request uses `POST /stats/overlay/query` with JSON such as:

```json
{"scope":"city","citySlug":"atlanta","routeJoinKeys":[]}
```

The server owns time windows and the five-second read budget. Inspect `generatedAtUtc`, `cutoffUtc`, coverage, local dates, and reason codes as well as amounts. A successful pending/unavailable response is evidence state, not a transport error. Never display raw server exception details.

## Run tests

Use an explicitly provisioned disposable PostgreSQL admin database whose name contains `disposable`. The fixture families create and drop randomized child databases; do not point these variables at an operator or production database. Set the variables only in the test process/session and clear them afterward:

```powershell
$env:ROUTE_HOUR_HISTORY_DISPOSABLE_CONNECTION = '<explicit disposable PostgreSQL connection string>'
$env:CITY_CATEGORY_INSIGHTS_DISPOSABLE_CONNECTION = '<explicit disposable PostgreSQL connection string>'
dotnet test ./src/ChefKnifeStudios.TransitJazz.Shared.Tests/ChefKnifeStudios.TransitJazz.Shared.Tests.csproj
dotnet test ./src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ChefKnifeStudios.TransitJazz.Client.Shared.Tests.csproj
dotnet test ./src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.csproj
dotnet test ./src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.csproj
Remove-Item Env:ROUTE_HOUR_HISTORY_DISPOSABLE_CONNECTION
Remove-Item Env:CITY_CATEGORY_INSIGHTS_DISPOSABLE_CONNECTION
```

The latest full regression passed: Shared 38/38, Client.Shared 129/129, WebAPI 291/291, Worker 186/186; all had zero failures or skips. An earlier WebAPI run skipped 52 CityCategory PostgreSQL cases because `CITY_CATEGORY_INSIGHTS_DISPOSABLE_CONNECTION` was unset. They were opt-in disposable-database checks, not operator-host or API-key cases; rerunning with both disposable variables removed all skips. The suite needed a timezone-normalized UTC test assertion and an additive-migration expectation update.

Final local Release API build and trimmed WASM publish succeeded. The publish includes pinned `pax.BlazorChartJs` 0.9.1, local Chart.js 4.5.1 and its license/provenance, local chart callbacks, Spanish satellite and ICU globalization data. Existing client workflows install `wasm-tools` and upload `publish/wwwroot`. The Chart.js request is local; the application retains its existing MapLibre CDN dependency. See `release-handoff.md` for exact artifact hashes and workflow details.

## Limits, errors, and evidence

| Bound | Value |
| --- | ---: |
| Request body | 4 MiB |
| Complete uncompressed response | 8 MiB |
| Route options/selection per city | 4,096 |
| Cities/categories | 32 each |
| UTF-8 route key | 512 bytes |
| Unfiltered successful cache | at most 33 replaceable scope slots; 64 MiB conservative estimated object weight; absolute 60 seconds from snapshot instant |
| Cold reads | Four per API instance; immediate busy response, no queue |
| Read deadline | Five seconds including connection acquisition |
| Client selection debounce / visible refresh | 200 ms / 60 seconds |
| Rate limiter | Burst 10, refill one token/second, no queue; peer IP partition |

Failures are complete and safe: 400 for invalid scope/city/key, 422 for over-cap or oversized complete responses, 429 with `Retry-After` for rate refusal, and 503 for disabled/dependency/timeout/busy conditions. The client localizes finite codes, preserves same-scope stale evidence and its original timestamp, and observes cooldown across scope changes. Do not substitute a new error cause during cooldown.

One synthetic PostgreSQL fixture with 98,304 rows (4,096 routes × 32 categories × 24 hours) completed normal unfiltered and selected reads in 1.975s and 1.647s, respectively, with 4.78 MB/96.5 KB gzip and 6.52 MB/156.3 KB gzip responses. 512-byte-key unions returned complete 422 `response_too_large`; 4,097 routes returned 422 `scope_too_large`. These are single synthetic observations, not production p95, memory acceptance, or matched load. The same query plan ran 1.661–3.354s and selected sequential scans in this fixture, above the 250ms prepared-query target. Full details and raw plans are in `release-handoff.md` and `fixtures/evidence/`.

Browser checks passed for DST axes, chart lifecycle/visibility, delayed/blocked Chart.js fallback tables, language/theme reprojection, modal/history ownership, actual welcome/map navigation, selected/All rendering, and 320px layout. Screen-reader/touch/200%-zoom, reduced-motion runtime, live feed playback, maximum-route render timing, actual retention/finalizer lag, trusted ingress/replicas, 100-sample matched load, visitor review, deployment and withdrawal remain pending. Keep both feature exposure flags false until all applicable G1-G8 gates are accepted. Do not treat disposable PostgreSQL or synthetic load evidence as actual retained-history evidence.

## Release and withdrawal

The release order remains: validate the existing history schema/index dependency; deploy API with reads disabled; deploy client with its shared launcher flag false; accept actual history, accessibility, capacity, visitor and matched-load gates; then have the operator enable bounded reads and the client flag in the approved order. Keep capture settings independent. Use the existing bounded Stats telemetry with worker cycle/write evidence; do not add route/city-array labels or a new monitoring service.

On withdrawal, hide both launchers, disable reads, and restore the recorded API/client artifacts if needed. Preserve retained history and capture/dry-run/city/category settings. Verify map/music after dismissal. See [release-handoff.md](release-handoff.md); this implementation task performs no deployment, public enablement or commit.
