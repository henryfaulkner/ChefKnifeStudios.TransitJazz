# Design Document: Atlanta Rail Recovery and Available Statistics

**Status:** Proposed; guides end-to-end implementation.  
**Date:** 2026-11-07  
**Related:** [Production investigation](ATLANTA_DATA_SOURCE_INVESTIGATION.md), [stats overlay design](STATS_OVERLAY_DESIGN_DOCUMENT.md), [reported vehicles design](REPORTED_VEHICLES_DESIGN_DOCUMENT.md)

## Outcome

Restore Atlanta rail ingestion using MARTA's replacement endpoint and an existing
valid API key. Independently, make the Stats overlay display measurements from
available sources when another source fails. **Missing rail must never hide valid
bus statistics.** The same rule applies when buses fail and rail remains available.

Example: buses contribute 120 km while rail is unavailable. Display **120 km** with
**Partial data — bus observations available; rail unavailable**. Plot the available
bus distance and activity. Rail distance remains unavailable, never zero. A bus-only
selection describes bus coverage; a city-wide selection describes partial Atlanta
coverage. Recovery of rail is not a prerequisite for delivering this behavior.

This document specifies application, persistence, API, UI, testing, and operational
work. It does not authorize deployment, secret changes, or rewriting historical data.
The six endpoint edits prepared during investigation are local changes, not proof
of production recovery.

## Confirmed failure chain

The November 7 investigation establishes:

1. Production repeatedly logs MARTA rail `HttpRequestException`; buses continue
   processing and publishing successfully.
2. The old and replacement rail URLs both reject keyless requests with HTTP 400,
   `Invalid API Key`. An authenticated replacement response has not been validated.
3. The deployed Container App has no rail key environment setting. Committed
   settings contain no key. This does not establish whether the operator possesses
   an existing valid key elsewhere.
4. `MartaCity` combines bus/rail results into `CityFetchResult.PartialFailure`.
   `Worker.cs` accepts activity only for combined `Success` or `Empty` outcomes.
5. `RouteHourStatisticsAccumulator` retains some movement counters but skips valid
   activity samples under that gate. It propagates incomplete reasons across the
   hour. The query then requires every city/hour row to be Complete.
6. `StatsOverlayQuery` excludes the entire partial cohort, including bus rows.
   The API returned `partial_cohort` for all 24 completed hours despite successful
   route-hour persistence. Reported-vehicle counts already support partial evidence.

Changing the URL alone cannot resolve authentication or the statistics policy.
Removing the SQL Complete filter alone cannot restore discarded activity samples
and could expose misleading zeros or unsafe historical rows.

## Scope and decisions

- Keep the existing adapters, processing pipeline, accumulators, PostgreSQL tables,
  stats endpoint, overlay, and hourly refresh. No new service or runtime package.
- Separate **valid measurements** from **complete source coverage**. Partial is a
  coverage state, not a blanket prohibition on presenting measurements.
- Attribute source evidence to the routes it supports. For Atlanta, explicitly map
  `marta-bus` to static bus routes and `marta-rail` to the four rail routes. Category
  alone is not a universal source identity; do not apply this mapping to other cities.
- Preserve current behavior for cities until their adapters provide the new source
  evidence. Their fully healthy existing hours continue to work.
- Preserve immutable history, conflict quarantine, ordinal route keys, bounded
  queues, memory limits, UTC hour identities, and local-day/DST window rules.
- Do not weaken the definition of an existing v1 Complete hour. Introduce versioned
  evidence for partial contributions, with conservative legacy reads.
- Keep feedback within Stats. Do not add global warnings or change live music and
  SignalR payloads for this feature.

## 1. Recover the rail source

Use this BaseUrl in both hosts and both environments:

```text
https://developerservices.itsmarta.com/mcsmrail/itsmarta/railrealtimearrivals/developerservices/traindata
```

The active endpoint/key binding is the named source section
`TransitSources:atlanta:RailRealtime`; the production environment setting for the
key is `TransitSources__atlanta__RailRealtime__ApiKey`. Reference an existing
managed secret rather than placing its value in appsettings, source, reports,
shell history, or deployment output. The city's enabled flag and reported-source
identity policy remain under its `Cities[]` entry; endpoint binding is independent
of that array's order.

Validate using the application's HTTP client and DTO behavior:

- Successful authenticated HTTP response containing a JSON array compatible with
  `RailArrivalDto`, including `TRAIN_ID`, `LINE`, `LATITUDE`, `LONGITUDE`, and
  `IS_REALTIME`. Verify value types, realtime filtering, coordinate ranges, route
  alignment, deduplication, and event-time semantics against a redacted live sample.
- Distinguish valid empty/schedule-only results from HTTP, authentication, timeout,
  or parse failure. A valid empty source can establish observed zero; a failed
  source cannot. Trains filtered for unusable positions must not establish complete
  positioned-vehicle coverage.
- The existing key continues to work according to MARTA's email, but verify it;
  no fallback to the retired endpoint and no assumption of keyless support.
- Invalid or missing credentials fail the rail source promptly and leave bus work
  intact. Cancellation remains cancellation. Sanitize URLs and exceptions; never
  log the query string containing the key.

Keep `MartaCity` and the separately registered `RailRealtimeAdapter` consistent.
Determine the production call path before changing either; avoid introducing a
second rail poll. Update the source `skills/mj-gtfs/SKILL.md` and synchronize its
generated copies through the repository skill-sync workflow during implementation.

## 2. Carry source evidence through capture

Extend `CityFetchResult` with bounded source evidence independent of whether
reported-vehicle capture is enabled. Reuse existing source keys, not URLs or vehicle
IDs. Do not make statistics correctness depend on the reported-vehicle feature flag.

Each expected source supplies a stable key, outcome (successful, successful empty,
failed, or intentionally disabled), receipt time, and bounded failure classification.
Preserve the expected source set and route dependency mapping throughout an hour.
Unknown provenance prevents a completeness claim; it must not be inferred from
the presence of other source records.

Keep combined fetch outcome for existing monitoring. At the capture boundary,
derive validity separately for each route from its source outcomes, usable route
index, and successful processing. A failed sibling source does not invalidate it.
For routes with overlapping feeds, define the expected dependency set explicitly,
deduplicate through existing identity rules, and retain partial coverage when only
some dependencies succeed. Do not claim full coverage from one successful replica
unless the adapter's source contract establishes it as a complete substitute.

The route accumulator must:

1. Count activity samples and identities only when the route's contributing source
   evidence and processing are valid. Successful empty polls increment valid sample
   denominators with a zero numerator. Failed polls increment neither.
2. Retain accepted movement intervals from valid observations. Preserve teleport,
   stale, nonpositive delta, transfer, and gap checks. A source outage must not create
   a long movement interval across missing observations after recovery.
3. Track source continuity and start/end boundaries independently, so bus polling
   cannot prove rail continuity. A disabled expected source is an explicit coverage
   limitation; removing it from configuration does not manufacture complete history.
4. Separate publication success from movement/activity validity. Publication failure
   invalidates crossing-publication measures but does not erase valid movement or
   activity. Preserve existing publication diagnostics and delivery semantics.
5. Keep partial source coverage separate from lost evidence: identity overflow,
   invalid route catalogs, processing exceptions, capture loss, and conflicts remain
   exclusions for the affected measures. If impact cannot be localized safely,
   exclude the affected cohort rather than guessing which rows survived.

Update category capture to use the same validity decisions. The overlay currently
derives its figures from route-hour cohorts; do not silently substitute the category
tables and create inconsistent populations. Category history continues its own
minute/hour reconciliation rules.

## 3. Version and retain trustworthy partial measurements

Introduce `observed-city-route-hour-statistics-v2` for newly captured route hours.
Extend the existing route-hour model and atomic envelope, rather than creating a
parallel statistics store. Persist enough evidence to classify each row's distance,
activity, and publication independently:

| Evidence | Purpose |
| --- | --- |
| Expected and contributing source keys, source-set fingerprint | Identify unavailable dependencies without guessing from missing rows |
| Per-source successful/failed/empty poll counts, boundary and maximum-gap evidence | Establish source coverage independently of sibling feeds |
| Distance/activity/publication validity flags and bounded reasons | Distinguish safe partial measurements from lost or invalid measurements |
| Existing valid activity samples, activity sum, distinct population, movement counters | Retain the actual numerators and denominators |
| Existing catalog/run/cadence, conflict, and retention evidence | Preserve identity and cohort integrity |

Implement the bounded source summary as a versioned JSONB value on each route-hour
row, with scalar measure-validity columns used by SQL. Keep only route-relevant
sources; cap keys and counts using the configured source inventory. Include all new
fields in validation, immutable payload equality, conflict detection, and migration
tests. Do not persist per-poll records or vehicle lists for this change.

Complete still requires proven continuity and healthy evidence for every expected
dependency of that row. Partial rows may carry usable measures. NoData, null, and
observed zero retain distinct meanings. A bus row can be Complete while rail rows
are NoData; their city aggregate remains Partial.

**Legacy policy:** keep admitting healthy v1 Complete cohorts under existing checks.
Do not admit v1 Partial rows through a relaxed filter: their activity samples and
source evidence may already be missing. Expose those exclusions in coverage. Do not
backfill, relabel, or upgrade old rows. Combine admitted v1 and v2 hour contributions
only for unchanged formulas, units, and cadence; disclose definition transitions.
An unsupported version or incompatible formula remains excluded. Exclude conflicting
or mixed-run city/hour cohorts conservatively as today; a simple source outage is
not an integrity conflict.

## 4. Query available measurements

Refactor `StatsOverlayQuery` into two decisions: cohort identity/integrity validation
and per-row/per-measure contribution eligibility. Preserve structural cohort checks;
replace the requirement that every row be Complete with the v2 evidence rules above.
Source failure in rail is not an integrity failure in bus rows.

For each requested window:

- Sum accepted distance across eligible rows, including safe v2 Partial rows.
  Return a number whenever at least one valid observation contributes. Do not scale
  a partial distance to estimate an unobserved full day or city.
- Calculate bus/category/route totals independently. Missing rail does not remove
  bus totals, chart points, or route options. Selection uses historical route/source
  membership; a bus-only selection can be Complete even while Atlanta is Partial.
- Track `ContributingHours` separately from `FullyCoveredHours`. A partially observed
  hour counts once as contributing and zero times as fully covered. Do not add
  category hour counts to obtain city hours. Distinguish expected, observed, and
  unavailable route hours; a retained NoData row is not an available measurement.
- Keep `ExpectedRouteHours` unknown when historical membership cannot be established.
  A current route catalog or expected source list does not prove past membership.
- Continue summing distinct population per route/hour only where that population
  is intact. Do not describe the sum as a window-wide unique vehicle count.
- Preserve route-vehicle-hour and active-clock-hour definitions for admitted hour
  measures, and label ratios based on Partial hours as observed ratios. Require
  distance and its denominator to use the same contributing route/hour set.
- Compute activity means as `sum(activity numerator) / sum(valid sample denominator)`
  within a coherent population. Failed-source polls are not zero samples. At city
  or category level, unequal route sample counts cannot be replaced by a shared
  maximum/minimum denominator or averages of route means. Emit the derived aggregate
  only when aligned sample support is proven by capture; otherwise leave that ratio
  null with an explicit reason while retaining distance and valid route bubbles.
- If a bubble lacks a valid axis or size, omit that bubble with its reason in the
  accessible data table. One unavailable derived metric cannot blank other charts.
- All-cities scope sums available city contributions and labels missing city/source
  coverage. Preserve each city's local-day boundaries and completed-hour cutoff.

Category share percentages use **available distance** as the denominator. Bus can
therefore show 100% of available distance while rail is missing; the chart must say
so explicitly. A zero available-distance denominator produces null percentages,
not NaN or an invented equal split. Rail remains listed as unavailable.

Timeline buckets contain observed distance increments. Accumulate only those
increments, annotate partial intervals, and leave wholly unavailable buckets as
gaps. Never connect a gap in a way that implies measured movement or reset a missing
bucket to zero. Category sums and the displayed total must use the same admitted
rows and window.

## 5. API and overlay contract

Keep `POST /stats/overlay/query`, request shapes, scope, time windows, and refresh
cadence. Add source coverage metadata and measure-specific availability to the shared
response. Extend the bounded reason vocabulary for source failure, disabled source,
source recovery, invalid measure evidence, and unaligned activity samples. Recovery
is coverage metadata, not a reason to emit a global notification.

Because v1 clients validate evidence and may assume partial city cohorts contain no
measures, introduce response contract `stats-overlay-v2`. During rollout, the server
must support an explicit v2 opt-in request header (proposed name
`X-TransitJazz-Stats-Contract`) and retain v1 semantics for absent/v1 headers. Include
the requested contract in cache keys and `Vary`; do not serve a v2 cached response to
a v1 client. Update `StatsEndpointsService`, `StatsSnapshotPolicy`, fixtures, and
contract tests before the client opts in. An unsupported contract returns a clear
client error; it must not silently change semantics.

The overlay behavior is:

| Source condition | Distance and charts | Coverage text |
| --- | --- | --- |
| Bus and rail healthy | Available measures from both | Complete when window continuity also passes |
| Bus healthy, rail failed | Bus distance, bus timeline and valid bus bubbles | Partial data; rail unavailable |
| Rail healthy, bus failed | Rail distance, rail timeline and valid rail bubbles | Partial data; bus unavailable |
| Both failed for entire window | Null distance and unavailable charts | No observations available for this period |
| Source fails during an hour | Retain valid observed measures | Partial data for affected source/time |
| Successful empty feeds with proven coverage | Observed zeros; no invalid derived ratios | Complete zero only when full coverage is proven |
| A source recovers during the hour | Retain valid pre/post-recovery observations | Hour remains Partial; subsequent healthy hours may be Complete |

Show partial status beside distance cards and affected chart headings, not solely
in a tooltip or transient toast. Show source names and covered periods in the existing
details/data tables. Retain unavailable categories in the legend or table. Use
localized resource keys, existing units, accessible descriptions, theme colors, and
mobile layouts. Illustrative copy: **Observed distance: 120 km** and
**Partial data — rail unavailable. Percentages use available distance.**

Update previous-day fallback, selection changes, and snapshot acceptance so a valid
Partial snapshot is accepted and replaces old selection data correctly. Do not
retain yesterday's value or an older Complete snapshot merely because today's data
is Partial. Cancellation and out-of-order results still cannot replace current data.
Reported-vehicle counts remain city-wide and independently qualified as currently
designed; do not derive them from positioned route activity.

## Implementation sequence

| Stage | Work and completion evidence |
| --- | --- |
| A. Rail integration | Verify new endpoint and existing key; finish configuration and operator instructions; adapter HTTP/DTO tests pass |
| B. Evidence and schema | Add bounded source evidence, route dependencies, v2 model/migration and immutable validation; loss/zero/version tests pass |
| C. Worker capture | Remove combined-fetch gating for independent valid measures; carry source continuity through route and category capture; integration fixture preserves bus samples during rail outage |
| D. Query and API | Admit safe partial contributions; preserve integrity exclusions; add v2 opt-in and coverage; database and contract tests pass |
| E. Overlay | Accept v2 Partial snapshots; render numbers/charts/copy/tables and unavailable rail correctly; client and browser checks pass |
| F. Release | Apply additive schema, deploy compatible server, then client; verify controlled outage and real recovery before declaring completion |

Stages B–E must work against deterministic feed fixtures without requiring a live
MARTA key. Stage A can ship earlier as a small operational recovery, but does not
satisfy the partial-statistics requirement.

Primary implementation files, relative to their projects:

- Worker: `Cities/MartaCity.cs`, `Metrics/CityFetchResult.cs`, `Worker.cs`,
  `RailRealtime/{RailRealtimeAdapter,RailArrivalDto}.cs`,
  `Statistics/{RouteHourStatisticsCapture,RouteHourStatisticsAccumulator,FinalizedRouteHourStatisticsBatch,CategoryStatisticsAccumulator}.cs`.
- Data: route-hour entity/configuration/migration, `CityRouteHourStatisticsStore`,
  and `Statistics/StatsOverlayQuery.cs`.
- WebAPI: route-hour writer, `Statistics/StatsOverlayService.cs`, endpoint contract
  handling/cache, and production/development appsettings.
- Shared/client: `Stats/StatsOverlayResponse.cs`, `StatsEndpointsService`,
  `StatsSnapshotPolicy`, overlay projection/components, chart models, and resources.

## Required verification

Use existing xUnit projects and database integration fixtures; no new test framework.
Write behavior tests before changing the gates. The core regression starts with a
successful bus response and failed rail response, advances through a closed UTC hour,
persists the envelope, queries v2, and asserts non-null bus distance, retained valid
activity samples, Partial city coverage, and null rail measures.

| Test group | Required cases |
| --- | --- |
| Feed | Valid key/header construction without leaking key; HTTP 400/401/403/500, timeout, cancellation, malformed/null JSON, successful empty, schedule-only, invalid coordinates and duplicate trains |
| Capture | Bus-only/rail-only/both successful; both failed; intermittent outage; source disabled/config changes; independent continuity; valid zero samples; outage recovery cannot bridge invalid movement gaps |
| Integrity | Overflow, queue loss, catalog change, unknown provenance, processing failure, publish-only failure, conflict, mixed run, unsupported version/cadence; unaffected evidence survives only when validity is proven |
| Persistence | Additive migration, retry equality including evidence, atomic envelope, mismatched payload quarantine, v1 read compatibility, v2 Partial validation |
| SQL/API | Partial bus rows admitted despite rail NoData; missing-source zero prohibited; compatible mixed-version windows; filtered bus/rail/both; all-cities; unequal sample denominators; invalid derived axes do not hide distance |
| Arithmetic | Category/total/timeline reconciliation, available-distance percentages, zero denominators, weighted means, route-hour versus distinct window populations, integer/decimal bounds |
| Time | Mid-hour failure/recovery, first full healthy hour, startup/shutdown fragments, UTC/local midnight, DST, 24 completed hours, ingestion grace, previous-day fallback |
| Client | v1/v2 negotiation/cache isolation, Partial snapshot acceptance, route changes and stale responses, unavailable categories, accessible data tables, metric/imperial, light/dark, mobile |

Release evidence must include a browser view showing nonzero bus statistics with
rail deliberately unavailable in a controlled environment, and a subsequent view
showing both sources after recovery. Use fixtures or a test-only source stub; do not
disable production rail simply to generate a screenshot. Run the existing worker,
WebAPI/Data integration, shared-contract, and client tests affected by the changes,
plus Release builds. Record exact commands, results, environment, revision, and UTC
windows in the implementation handoff. Unrun authenticated or browser checks stay
explicitly outstanding.

## Deployment, observability, and rollback

1. Verify access to the existing MARTA key and validate an authenticated response
   without exposing credentials. Inspect current deployment overrides so the new
   settings take effect.
2. Apply and verify the additive migration before v2 writes. Keep current v1 reads
   available, then deploy the dual-contract server and v2 writer. Check secret-backed
   configuration on the serving revision, not only a local settings file.
3. Deploy the v2 client after server/database verification. Keep hourly refresh and
   cache isolation. Inspect the actual API response and browser presentation.
4. Observe source-specific success/failure, contributing versus complete hours,
   writer outcomes/conflicts, and query exclusions. Use bounded source keys and
   reason codes with existing structured logging; no endpoint URLs or API keys in
   metrics. Do not introduce per-vehicle log volume.
5. Verify trains return and rail failures stop. A recovery hour can remain Partial;
   wait for the next fully observed closed UTC hour plus ingestion grace and the
   scheduled stats refresh before expecting Complete coverage. The last-24 window
   can remain Partial until older outage hours leave it.
6. For rollback, stop v2 writes before reverting a reader that cannot consume them;
   verify the selected previous deployment can safely exclude unknown definitions.
   Retain additive columns and immutable captured history. Roll the client back
   through v1 negotiation if necessary. Do not restore the retired rail URL or
   manufacture complete historical hours as a recovery step.

## Completion criteria

The work is complete when authenticated rail ingestion works on the serving
revision, and the end-to-end outage fixture plus browser verification prove that
available bus statistics remain visible without rail. Missing observations stay
null, totals reconcile to the admitted evidence, partial coverage is clear, legacy
history is interpreted conservatively, and existing healthy-city behavior passes
regression checks. URL migration alone is not completion.
