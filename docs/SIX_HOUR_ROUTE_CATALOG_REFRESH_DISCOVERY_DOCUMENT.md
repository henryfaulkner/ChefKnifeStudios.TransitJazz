# Six-hour route-catalog refresh: historical collection degradation

**Discovered:** 2026-10-11  
**Status:** Local correction and regression checks completed in the follow-up below; deployment and event-by-event production correlation remain pending. The original observed database pattern strongly matches scheduled refreshes.  
**Scope:** Category historical collection and the Grafana insights prototype. The original discovery evidence is preserved below, followed by the local implementation and remaining operational checks. No historical repair is included.

## Finding

At discovery, a routine route-catalog reload invalidated category historical capture even when the catalog's relevant contents had not changed. The checked-in application configuration schedules static GTFS refreshes every six hours. Before the local correction, each applied route index incremented a numeric geometry generation and invalidated category capture for every affected city without first comparing catalog content.

This invalidation marks the current category minute and hour as having capture loss, clears movement baselines, and makes the hourly exact vehicle-population denominator unavailable. An otherwise healthy hour consequently becomes Partial and is excluded from definitive Grafana metrics.

The prototype correctly preserves these exclusions as gaps. The collection bug is treating a routine reload as evidence that historical continuity/population integrity has been lost. Relaxing dashboard coverage checks would conceal the problem and admit evidence that the current collector explicitly declares incomplete.

## Investigation context

- Dashboard: [TransitJazz — The City's Rhythm (Prototype)](https://gallantpuffin3113.grafana.net/d/transitjazz-category-insights-lab/transitjazz-e28094-the-city-s-rhythm-prototype?from=now-24h&to=now&timezone=utc&var-city=new-york-city&var-category=$__all&var-route=1&var-route_segment=latest).
- Selections: `city=new-york-city`, all discovered categories (`bus`, `rail`), route key `1`, latest route segment, UTC display.
- Grafana PostgreSQL source: `cg086lcj6z6yob`; database/schema: `transitjazz.public`.
- Database inspection began at `2026-10-11T12:32:13.712291Z` with a rolling 24-hour range. Saved panel SQL and the authoritative coverage recipe were subsequently evaluated against the fixed range `[2026-10-10T12:32:00Z, 2026-10-11T12:32:00Z)`.
- Database evidence was queried through the transitjazz-db helper using read-only Repeatable Read transactions. A separate read-only query through Grafana's configured source confirmed the same problematic category rows.
- Supported category definition: `observed-city-category-statistics-v1`; retained cadence policy: 30 seconds.

The investigation retrieved saved chart configuration and reproduced its SQL results. It did not capture a new rendered dashboard image, inspect production refresh logs, or change Grafana, database, or collection configuration.

## Observed evidence

Both bus and rail have the same excluded category hours:

| UTC hour start | Complete backing minutes | Stored state | Relevant evidence |
| --- | ---: | --- | --- |
| 2026-10-10 14:00 | 59 | Partial, no conflict | Minute 14:15 is Partial; exact hourly population is null |
| 2026-10-10 17:00 | 39 stored covered minutes | Partial, conflict | Retained minute 17:39 and its parent hour are conflicting; this is a separate exclusion requiring investigation |
| 2026-10-10 23:00 | 59 | Partial, no conflict | Minute 23:38 is Partial; exact hourly population is null |
| 2026-10-11 05:00 | 59 | Partial, no conflict | Minute 05:38 is Partial; exact hourly population is null |
| 2026-10-11 11:00 | 58 | Partial, no conflict | Minute 11:38 is Partial; minute 11:59 additionally contains one failed cycle; exact hourly population is null |

The refresh-like minutes at 23:38, 05:38, and 11:38 are exactly six hours apart. Each retains six observed cycles, six eligible activity samples, six publication samples, and zero failed cycles. Their maximum observation gaps are approximately 10.55, 11.33, and 10.56 seconds, respectively—below the 30-second policy. The 23:00 and 05:00 hours each retain 360 observed cycles with no failed cycles, yet are incomplete and lack the exact population denominator.

This is evidence of healthy cycle activity accompanied by historical integrity invalidation, rather than evidence of a two-hour transit or worker outage. It strongly matches the unconditional reload invalidation path below. Production logs are still needed to associate each Partial minute with its exact refresh event and to exclude other calls that mark capture loss.

The 14:15 Partial minute has a similar healthy-cycle signature, but its trigger was not established. The 17:00 conflict and the 11:59 failed cycle must not be attributed to routine refreshes solely because they appear in the same requested range.

For the fixed range, the saved category chart query yields **18 contributing hours out of 23 fully contained requested hours**. Five hours are excluded. The first intersected hour is excluded by the request boundary; the current open hour is not a definitive completed hour.

## Original code path causing the bug

This describes the implementation inspected at discovery, before the follow-up correction.

1. [`appsettings.json`](../src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json) sets `Gtfs:StaticRefreshHours` to `6`. [`GtfsStaticLoader.cs`](../src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/GtfsStatic/GtfsStaticLoader.cs) reads this setting; its fallback default is 24 hours, so six hours is configured behavior rather than a universal hardcoded interval. The deployed effective setting was not independently inspected.
2. [`Worker.cs`](../src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs), `RefreshRouteIndexAsync`, waits for a source refresh and applies the rebuilt index.
3. `ApplyRouteIndex` builds the affected city union, increments `_routeGeometryGenerations[city]`, and calls `_categoryStatisticsCapture?.InvalidateGeometry(city, generation)` for every city. It does not establish that the city's relevant catalog content actually changed before making this call.
4. [`CityCategoryStatisticsCapture.cs`](../src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Statistics/CityCategoryStatisticsCapture.cs), `CityState.InvalidateGeometry`, clears `_movement`, updates the generation, and calls `Accumulator.MarkCaptureLoss()`. `AlignGeneration` also treats a changed numeric generation as capture loss after initialization.
5. [`CategoryStatisticsAccumulator.cs`](../src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Statistics/CategoryStatisticsAccumulator.cs), `MarkCaptureLoss`, sets `QueueLoss=true` on all currently open category minutes and hours. This internal flag also represents capture invalidation; its name alone does not establish an actual queue drop.
6. The minute finalizer treats `QueueLoss` as Partial. The hour finalizer requires 60 Complete minutes and no `QueueLoss`; it returns a null exact population when that flag is set. The resulting degraded evidence is retained.

The numeric generation tracks index replacement, but category capture interprets it as a substantive geometry/integrity change. That mismatch is the defect. Genuine changes must still invalidate incompatible movement baselines and historical comparisons.

## Data corruption and loss semantics

The observed corruption is **semantic degradation of historical evidence**, not demonstrated damage to PostgreSQL pages or arbitrary modification of stored counts.

| Impact | What is established or expected |
| --- | --- |
| Incorrect coverage classification on unchanged reloads | The code can downgrade a healthy current minute/hour solely because an index was replaced. Observed healthy Partial minutes match this mechanism. |
| Lost exact hourly denominator | Observed affected category hours store `distinct_active_vehicle_count=NULL`. Primary movement per observed vehicle-hour cannot be calculated definitively. |
| Reduced coverage count | One invalidated minute prevents the hour from satisfying the required 60 Complete backing minutes. Diagnostics remain stored but cannot contribute to authoritative hour-based measures. |
| Movement undercollection after baseline reset | Clearing movement state means the next observation must reseed its baseline; a movement interval spanning the refresh can be rejected rather than accumulated. This follows from the code, but its magnitude was not measured in this investigation. |
| Ambiguous failure provenance | Category rows do not persist a specific refresh/invalidation reason. Healthy samples and timing suggest the cause, but retained rows alone cannot distinguish every path that marks capture loss. |
| Durable incomplete history | Existing immutable category evidence is not automatically upgraded after later healthy cycles. Fixing future capture does not repair these retained hours or reconstruct their missing exact populations. |

Do not invent the null denominator, replace Partial hours with Complete, fill chart gaps with zero, or estimate missing movement from nearby observations. Stored diagnostic numerators may remain useful, but must retain their incomplete status. No exact movement-loss amount, fleet-population correction, or historical recovery feasibility has been established.

The 17:00 conflict is a distinct immutable-payload conflict/quarantine observation. It does not prove this refresh defect overwrote counts or created conflicting writes; its producer/run/retry history needs separate investigation.

## Effect on Grafana metrics

The saved prototype derives category observations from [`hourly-insights.sql`](../specs/056-city-transit-type-insights/contracts/hourly-insights.sql). Eligibility requires a Complete, nonconflicting hour with an exact population, 60 compatible Complete backing minutes, and reconciled additive totals. The dashboard additionally requires shared eligible hours across the selected categories under the same cadence policy.

Consequently the invalidated hour is removed from distance totals, peak-hour selection, category shares, cumulative curves, hourly movement, hourly percentages, publication-cadence charts, and Movement & Music observations. Totals represent the remaining observed eligible hours; they must not be interpreted as complete travel over the requested elapsed period. Repeated exclusions can also bias local-hour or period comparisons toward the retained hours, although the bias magnitude is unknown.

Charts use hourly timestamps and `spanNulls=false`. For example:

```text
22:00 valid point → 23:00 null → 00:00 valid point
```

The line has a two-hour visual break between valid points even though only one hourly observation is excluded. Cumulative sums resume from retained contributing hours; they do not estimate distance during the excluded hour.

Route history uses a different continuity model and content fingerprints. For New York route `1`, the inspected 23:00 and 05:00 route hours are Complete despite the category exclusions. The 11:00 route hour is Partial. This explains why category and route charts need not have identical gaps and limits the demonstrated blast radius. An all-city impact assessment has not been performed.

## Recommended correction and follow-up

1. Correlate production route-index/static-refresh events with the observed Partial-minute timestamps. Confirm effective refresh configuration, deployed code version, and whether relevant catalog contents were unchanged.
2. Separate routine index replacement from substantive content changes. Preserve category continuity and compatible movement baselines when the relevant city/route content is unchanged; invalidate genuinely changed content and in-flight incompatible observations deliberately.
3. Reuse the existing route-hour catalog fingerprint machinery where its semantics fit category movement, rather than adding a second independent notion of catalog identity. Specify how changes affecting only some routes propagate to category-hour integrity before implementing the correction.
4. Add bounded structured provenance for invalidation so future incomplete hours can be explained without relying on timestamp patterns. Distinguish capture invalidation from real queue admission/write loss.
5. Investigate the 17:00 quarantine and 11:59 failed cycle separately. Neither is proven to share the refresh root cause.
6. After a correction, verify unchanged refreshes retain category minute/hour completeness and exact populations across multiple refresh boundaries. Verify real content changes still reject incompatible movement and do not falsely claim Complete evidence. Compare saved Grafana query results with the authoritative database coverage recipe.
7. Treat historical remediation as a separate decision. Exact populations and rejected movement cannot be recovered from additive category minute counts alone. Any recovery requires independently retained source evidence and an explicit migration/repair design; none is authorized or performed here.

For an unchanged healthy refresh, acceptance means no refresh-only Partial minute, no refresh-only null population, no baseline reseed solely due to an index generation increment, and no resulting exclusion in the category insights query. Actual failures and genuine catalog changes must remain visible as incomplete evidence.

## Reproduction and references

Read-only reproduction:

1. Retrieve dashboard UID `transitjazz-category-insights-lab` with the classic `dashboard.grafana.app/v1beta1` API.
2. Inspect panel 6, **City Score · The distance adds up**, including its saved SQL and `spanNulls=false` setting.
3. Bind city `new-york-city`, categories `bus`/`rail`, and the fixed UTC range above. Execute the saved SQL via the transitjazz-db helper: null values occur at the five excluded hourly keys listed above.
4. Execute [`minute-coverage.sql`](../specs/056-city-transit-type-insights/contracts/minute-coverage.sql) for both categories with the supported definition and the same bounds. Inspect hour exclusions and incomplete backing minutes. Distinguish stored `covered_minutes` from independently recomputed durable evidence, especially for quarantined hours.
5. Inspect route `1` in `public.city_route_hour_statistics` for the same range, checking whole-city/hour conflict before interpreting route measures.

Additional contracts:

- [Category statistics v1](../specs/056-city-transit-type-insights/contracts/observed-category-statistics-v1.md).
- [Route-hour statistics v1](../specs/057-hourly-route-history/contracts/observed-route-hour-statistics-v1.md).
- [Grafana prototype source and semantics](../observability/grafana/insights/README.md).

The original discovery performed no bug fix, database update, dashboard update, historical backfill, or commit. The subsequent local correction is described below.

## Follow-up: 2026-10-11

The local implementation now distinguishes replacement indexes from changed city catalog content. It is uncommitted and has not been deployed. No production data or dashboard was changed.

### Correction and change propagation

- `Worker.ApplyRouteIndex` reuses `RouteHourCatalog.ContentFingerprint` and each entry's `Fingerprint`; no second fingerprint format or runtime dependency was added. Identity includes canonical route keys, final live category classification, contributing static route IDs, and final concatenated ordered geometry. Reordering independent routes or changing display colors preserves identity.
- A comparable unchanged city catalog preserves its numeric generation and all category capture state. This also preserves a cycle in flight during publication. A change in a different city does not invalidate this city's capture.
- A real content change invalidates all currently open category minutes/hours in the changed city, retaining the conservative city-wide integrity rule and null exact populations. Changed, added, and removed routes reset their baselines; unaffected routes retain compatible baselines under the new generation. Source timestamp watermarks survive resets, so stale observations cannot seed fresh movement.
- Null, case-colliding, ambiguously aliased, or inconsistently classified alias identity cannot establish compatibility. Those replacements and city removal reset all city movement baselines and invalidate open category evidence. Repeated unchanged reloads of an unidentifiable catalog remain conservative invalidations.
- Late cycles from an older snapshot cannot roll the category generation backward or clear compatible current baselines. Their observations and completion are rejected. Numeric generations now describe substantive change rather than every refresh.

The category v1 definition, durable coverage checks, immutable rows, and saved dashboard SQL retain their existing semantics. The fix does not reconstruct lost exact populations or rejected movement in retained history.

### Diagnostic provenance

Each city catalog application emits one bounded `RouteCatalogApplied` diagnostic through the existing ILogger stream, outside the catalog lock. Fields include `City`, `ReasonCode`, `GeometryGeneration`, nullable `ChangedRouteCount`, previous/current content fingerprints, and `CategoryCaptureInvalidated` for existing capture state. Reasons are:

| Reason | Meaning |
| --- | --- |
| `CATALOG_INITIALIZED` | First applied catalog; no prior category state invalidated |
| `CATALOG_CONTENT_UNCHANGED` | Comparable identical content; continuity preserved |
| `CATALOG_CONTENT_CHANGED` | Comparable content changed; affected route count is known |
| `CATALOG_IDENTITY_UNAVAILABLE` | Compatibility cannot be established, including city removal |

Queue admission failure has a separate `CategoryStatisticsQueueLoss` event field and `QUEUE_ADMISSION_FAILED` reason. These diagnostics do not modify the centralized `StructuredLogEvent` v1 enum or persist a reason in category rows. Existing writer failure diagnostics remain the evidence for persistence failures. Historical rows still cannot explain their own trigger without external event evidence.

### Validation

The deterministic worker fixture exercised thirteen healthy UTC hours and three unchanged reloads at minute 38, six hours apart. Both bus and rail retained all 26 Complete category hours, 60 Complete backing minutes per hour, exact populations of one and zero respectively, and all 360 accepted bus movement intervals per hour. This is simulated local evidence, not a production soak or a Grafana/database validation.

Additional regressions cover unchanged and changed refreshes during publication, selective baseline preservation, category/static-ID/route-membership/point-order changes, reordered routes and display metadata, final live classification of grouped routes, other-city isolation, city removal, unavailable/ambiguous/inconsistently classified identities, and late old-generation cycles.

| Check | Result |
| --- | --- |
| Category worker/movement regression cases, included in full worker suite | 27 passed |
| Full TransitDataWorker tests | 186 passed |
| Full WebAPI tests | 187 passed; 60 opt-in disposable PostgreSQL tests skipped |
| Server compilation through test builds | Passed; existing package and nullable/analyzer warnings remain |
| `git diff --check` | Passed |

No disposable PostgreSQL instance was configured for the opt-in persistence/query tests. No schema, persistence payload, or analyst query changed. Durable query reconciliation and rendered production dashboard acceptance are still operational checks.

### Production correlation preflight and pending query

The read-only Azure Monitor interface is available and the current Azure CLI identity resolves. Neither `TRANSITJAZZ_LOG_WORKSPACE_ID_PROD` nor `TRANSITJAZZ_LOG_WORKSPACE_ID_DEV` is configured in this session. The logs skill's doctor therefore stops at the approved workspace-resolution layer. No production log query was executed and no refresh timestamp or deployed code/configuration was independently confirmed. The next action is to configure the approved workspace alias through the normal operator process and run the bounded query.

Effective intended context: workspace alias `prod` (ID unresolved), table `ContainerAppConsoleLogs`, UTC range `[2026-10-10T23:37:00Z, 2026-10-10T23:40:00Z)`, limit 100, presentation as a table. This first query uses known message selectors and projects only event classification/time/revision; it does not assume a deployed JSON field layout or return raw log bodies:

```kusto
ContainerAppConsoleLogs
| where TimeGenerated >= datetime(2026-10-10T23:37:00Z)
    and TimeGenerated < datetime(2026-10-10T23:40:00Z)
| where Log contains 'RouteIndexLoaded' or Log contains 'Refreshed route index'
| extend EventSelector = iff(Log contains 'RouteIndexLoaded', 'RouteIndexLoaded', 'Refreshed route index')
| project TimeGenerated, RevisionName, EventSelector
| take 100
```

Repeat with separate narrow ranges around `2026-10-11T05:38:00Z` and `2026-10-11T11:38:00Z`. After checking the actual redacted console JSON layout, project the new `RouteCatalogApplied` fields for post-deployment boundaries and match event times against category-minute evidence. An old `RouteIndexLoaded` event can confirm refresh timing, but does not itself prove unchanged content because the previous implementation did not log content identity. A zero-row result would not prove the absence of refreshes; check retention, ingestion, logger level, and selector/schema compatibility.

Remaining operational work:

1. Confirm the deployed revision and effective six-hour setting; correlate the three original refresh-like minutes with route-index events. Investigate the unestablished 14:15 trigger separately.
2. After deploying the correction, observe multiple six-hour boundaries with `CATALOG_CONTENT_UNCHANGED` and `CategoryCaptureInvalidated=false`. Verify healthy category minutes/hours retain Complete evidence and non-null exact populations, then reconcile the saved Grafana query with `minute-coverage.sql`.
3. Investigate the 17:00 immutable-payload conflict/quarantine and 11:59 failed cycle independently. No evidence in this follow-up attributes them to reloads.
4. Keep historical remediation as a separate design requiring independently retained source evidence. This correction does not upgrade historical Partial rows or fill dashboard gaps.
