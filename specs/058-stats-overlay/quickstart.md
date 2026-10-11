# Stats Overlay Implementation and Release Quickstart

**Status**: Planning instructions. Stats code and the commands specific to it become available after implementation. This planning stage has not built, tested, queried operator data or deployed the feature.

## Prerequisites and contract review

Use the existing .NET10 repository/Azure configuration. Read [plan](plan.md), [research](research.md), [data model](data-model.md), [API](contracts/stats-overlay-api-v1.md), [read semantics](contracts/stats-overlay-read-v1.md) and [UI/history](contracts/stats-overlay-ui-v1.md) before implementation. Preserve AGENTS.md's prohibition on commits and the user's existing design/assets changes.

Confirm existing route-hour schema/migration and configuration independently of whether capture is currently enabled. Never infer production retention from source defaults. Use the configured seven canonical cities/zones, `observed-city-route-hour-statistics-v1` and resolved RouteHours cadence. The Stats example city is `atlanta`, not `marta`.

For correctness checks, provision an explicitly disposable PostgreSQL admin database and use the existing `ROUTE_HOUR_HISTORY_DISPOSABLE_CONNECTION` opt-in pattern; fixtures create their own disposable child databases. Operator production/development history is not a disposable test target. Use fixture cohorts rather than modifying real retained observations to simulate conflicts/missing data.

## First implementation gates

1. Freeze independently calculated positive/zero/partial/transfer/unknown/category-change fixtures and DTO null/reason contracts.
2. Pin Pax 0.9.1, self-host Chart.js 4.5.1/license and align client direct Blazor package references to compatible 10.0.9. Render fixed pie/bubble/stacked/unstacked forms before network coupling. Prove lifecycle/native visibility/DST/table/failure behavior in real browsers.
3. Prototype shared Stats/Info framework-owned same-URL history and full-screen modal focus before integrating dashboard requests. Exercise Back/Forward/explicit/repeated close/replacement/reload/foreign city and silent welcome access. Prove no map/audio remount.
4. Implement one bounded parameterized snapshot read with all-cohort validation before filtering, then API/cache/admission/transport. Validate against disposable PostgreSQL and independent expected amounts; add no index unless measured plans require it.
5. Integrate independent historical route grid, the two modes/all panels, culture/audio synchronization and one visible refresh session. Accessibility/coverage/failure copy accompanies each increment.

These steps establish local behavior; they do not authorize public exposure before the later gates.

## Local configuration after implementation

Keep committed API `StatsOverlay:Enabled=false` and client `AppSettings:FeatureFlags:StatsOverlay=false`. For an explicitly configured local fixture/development instance, enable the API through a local environment/config override and set the **same** client flag for welcome/map launchers. Reuse existing TransitJazzDB configuration without putting any connection string/secret in WASM. Enabling Stats must not alter HistoricalStatistics.Enabled, DryRun, Cities or category settings.

The API must start safely when Stats is disabled without a configured DB; enabled reads with missing dependencies return safe 503. Existing valid retained history stays readable when capture is currently disabled. Missing/partial history returns 200 with honest evidence states.

Run WebAPI and WebApp in separate PowerShell terminals from the repository root after implementation/configuration:

```powershell
dotnet run --project ./src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/ChefKnifeStudios.TransitJazz.Server.WebAPI.csproj
```

```powershell
dotnet run --project ./src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/ChefKnifeStudios.TransitJazz.Client.WebApp.csproj
```

Current local HTTPS profiles use API 52834/client 52832. Ensure the development named API base URL points to that API through existing local configuration. Start the client welcome screen; View statistics must work before audio unlock/map/feed readiness.

Example read once the endpoint exists and local exposure is enabled:

```powershell
$statsRequest = @{ scope = 'city'; citySlug = 'atlanta'; routeJoinKeys = @() } | ConvertTo-Json -Compress
Invoke-RestMethod -Method Post -Uri https://localhost:52834/stats/overlay/query -ContentType application/json -Body $statsRequest
```

Inspect generatedAtUtc/cutoff/window/date/coverage, not only plotted values. Test 200 pending/unavailable separately from 503 service unavailable. Verify explicit JSON nulls, no individual/internal identifiers, case-distinct route keys and safe unknown selection.

## Focused validation after implementation

Use meaningful tests in existing projects; no new framework. The following commands are future implementation checks and have not been executed by planning:

```powershell
dotnet test ./src/ChefKnifeStudios.TransitJazz.Shared.Tests/ChefKnifeStudios.TransitJazz.Shared.Tests.csproj --filter FullyQualifiedName~Stats
dotnet test ./src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ChefKnifeStudios.TransitJazz.Client.Shared.Tests.csproj
dotnet test ./src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.csproj --filter FullyQualifiedName~Stats
dotnet test ./src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.csproj --filter FullyQualifiedName~RouteHour
```

Run existing map/selection/audio/history/query regressions appropriate to changed seams; run disposable DB tests with their opt-in connection and disclose skips. Build API/client and publish WASM to validate exact package patches, local static asset URLs, no chart CDN/plugin requests, Spanish satellite/globalization payload and document language:

```powershell
dotnet build ./src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/ChefKnifeStudios.TransitJazz.Server.WebAPI.csproj
dotnet publish ./src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/ChefKnifeStudios.TransitJazz.Client.WebApp.csproj -c Release
```

Independent reconciliation must include 160 km/16-of 20 route-hour fixture, 30/6 X, 30/2 Y, 180/120 mean, city samples once/hour, 100+gap+40=140 cumulative known, whole-stack selected gap retaining amounts, conflict outside filter and All hidden lines still in totals. Browser checks include 4096 route-grid/bubble points, overlaps, blocked chart asset, rapid close/initialization/theme, keyboard/screen reader, 320px/200% zoom, touch, long Spanish strings, both themes/reduced motion and all history/focus paths.

## Evidence required before staged exposure

Record one release handoff with the following evidence and status. A skipped check remains an unresolved release gate.

| Record | Required detail |
| --- | --- |
| Definitions and policies | Contract/observation versions, route-based X/active-hour Y, per-city local-day All, full hours, two-minute grace,60-second TTL/polling, cadence and boundary treatment |
| Actual retained cities | Every configured city: actual persistence mode/earliest requested evidence, representative healthy day/coverage, partial/unavailable/zero/early-morning behavior; no assumed seven-city completeness |
| Retention/freshness | Measured finalizer/writer lag and cutoff behavior around grace boundaries; complete-hour freshness copy |
| SQL correctness | Disposable real-DB fixtures/concurrent quarantine consistency and representative EXPLAIN; existing index or documented migration dependency |
| Capacity/load | Typical and 4,096-route/32-city/32-category scope, worst historical union, peak memory/cache object sizing, raw/compressed response bytes and safe-cap failure |
| Latency/capture | Device/network/concurrency/replicas/cardinality/p95 samples; warm open/cold/prepared/deadline targets; combined history+Stats <=5% total matched-load budget and incremental Stats delta; no read-induced blockage/loss |
| Ingress/admission | Trusted forwarded-client provenance, token burst/refill, four cold reads/instance, no queue, per-replica scaling and Retry-After |
| Browser/user review | Chart/history/modal/accessibility/culture/audio evidence; at least four of five visitors explain scope/evidence/freshness/All days |
| Safe operations | Disabled/no DB/schema/rate/deadline/cap responses, bounded telemetry, refresh/cooldown, API/client flag values, rollback and explicitly pending checks |

The 5% ceiling is the existing total history overhead budget, not another 5% for Stats. Compare equivalent matched workload with combined history/reporting against baseline without that added work, and report Stats-on/off with capture fixed. If evidence fails or cannot be produced, keep exposure disabled and review the concrete result rather than changing confirmed behavior silently.

## Deploy and rollback

Use existing GitHub Actions WASM/server artifacts and normal operator deployment. Validate/apply the existing route-history schema first; any measured index change uses the existing Data migration image before dependent code. Deploy API with bounded reads, then client with one shared launcher flag. Enable only after evidence acceptance; read exposure is independent of capture.

Rollback hides both launchers, disables Stats reads and rolls back its API/client artifacts. Preserve retained data and capture settings. Missing Stats service must leave map/music usable after dismissal. This planning workflow performs no commit, deployment or public enablement.
