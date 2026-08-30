# Incident Report: Missing TransitJazz Tones

**Incident date:** 2026-08-28 (original outage)

**Related recurrence:** 2026-08-30

**Investigation date:** 2026-08-29 to 2026-08-30

**Environment:** Production (`deployment_environment_name=prod`)

**Affected service:** `marta-jazz-dev-ca-server` / `transitjazz-transit-worker`

**Status:** **Resolved, with follow-ups.** The original route-index outage recovered automatically. The Aug 30 recurrence is now fully characterized: it was caused by revision `0000165`'s startup route-index load failing (confirmed from logs), and it **recovered in approximately 45 seconds** rather than persisting. The recovery design remains unsafe in the worst case and is addressed by [ROUTE_INDEX_READINESS_DESIGN_DOCUMENT.md](../ROUTE_INDEX_READINESS_DESIGN_DOCUMENT.md). **The Philadelphia zero-tone condition is closed as not-a-defect** — Philadelphia emits tones normally. Denver has a genuine zero-vehicle condition, tracked in [2026-08-30-denver-no-tones.md](2026-08-30-denver-no-tones.md).

**Last updated:** 2026-08-30, ~18:30 UTC, from centralized logs (feature 054).

> **Update — 2026-08-30 evening.** Three findings supersede parts of this report as originally written:
>
> 1. **The Aug 30 trigger is confirmed.** It was the startup route-index load on revision `0000165`,
>    failing with `HttpRequestException` while the replica was still `ReplicaUnhealthy`. The five
>    candidate variants listed under "Most likely" are resolved: variants 1 and 3 are both correct and
>    are the same condition. See "Aug 30 trigger — confirmed" below.
> 2. **The Aug 30 recurrence recovered quickly.** All seven cities were processing vehicles ~45 s after
>    `WorkerStarted`. Customer impact for the recurrence is under a minute, not the ~24 h of Aug 28.
> 3. **Philadelphia is not broken.** It emits tones on a normal cadence; the apparent zeros were
>    duplicate-feed ticks. P1-6 is closed and the reconciliation investigation is retargeted at Denver.

## Executive summary

TransitJazz emitted no tones for any configured city for most of Aug 28, 2026. The worker was not stopped: it continued completing its approximately 10-second cycles and successfully fetching city input. The failure was in the in-memory route index required for spatial reconciliation.

Read-only Azure Monitor evidence identified a separate occurrence on Aug 30, 2026. Between 15:22:44 and 15:22:45 UTC, all seven cities emitted paired `RouteIndexUnavailable` and `CityCycleAnomaly` warning records with `Outcome=Failed` and `ReasonCode=ROUTE_INDEX_UNAVAILABLE`; each had zero processed vehicles and tones, and publishing was not attempted. The timestamps are two days after the original outage and therefore represent a recurrence of the failure mode, not an extension of the Aug 28 incident.

The strongest causal sequence is:

1. Container App revisions `0000153` and `0000154` were created on Aug 27 at approximately 8:25 PM and 8:29 PM Eastern, replacing the prior worker instance.
2. The new worker process started without a populated route index. Its startup route-shape load either failed, returned no usable data, or failed while parsing/building the index.
3. The worker exhausted its five startup attempts, continued polling with an empty index, and skipped reconciliation for every city.
4. The background route-index refresh succeeded approximately 24 hours later, at 8:31 PM Aug 28. All cities became healthy and six cities immediately resumed emitting tones.

~~The exact startup HTTP failure has not been confirmed from logs.~~ **Confirmed on 2026-08-30.** The startup failure was captured directly on revision `0000165` at 15:54:59.984 UTC — `Failed to initialize route index (attempt 1/5); exception type HttpRequestException` — paired 100 ms earlier with the server-side `GtfsEndpoints: GTFS Static data not yet loaded.` The worker requests route shapes from its own public ingress FQDN for data held by a sibling object in the same process. See "Aug 30 trigger — confirmed".

Grafana metrics establish the empty-index condition and its timing; the Azure Container App activity/revision history establishes the preceding rollout.

**Two corrections to this summary, from the 2026-08-30 evening pass:** step 4's 24-hour refresh is what recovered Aug 28, but revision `0000165` recovered in **~45 seconds** by some other path, so a day-long outage is this defect's worst case rather than its normal behavior. And Philadelphia, described elsewhere in this report as emitting zero tones, **is healthy** — that finding is retracted.

## Customer impact

- All seven configured cities had zero tone output from the beginning of the Aug 28 Eastern-time day through approximately 8:30 PM.
- This represents roughly 20.5 hours of missing tone output within the queried day, and likely approximately 24 hours from the Aug 27 revision rollout until route-index recovery.
- The worker continued accepting/processing input and reporting liveness, so this was a silent degradation rather than a complete service outage.
- After route-index recovery, Atlanta, Boston, Denver, New York City, Toronto, and Washington, DC emitted tones.
- ~~Philadelphia continued to emit zero tones through at least 8:41 AM Aug 29 despite healthy input and vehicle processing.~~ **Retracted.** Philadelphia emits tones normally; the observed zeros were duplicate-feed ticks sampled at dashboard resolution. See "Philadelphia follow-up".
- On Aug 30, the centralized logs recorded the same route-index failure signature across all seven cities. **Now bounded:** revision `0000164` was in a continuous outage from at least 15:15 to 15:54:55 UTC (~40 minutes observed, start not established); revision `0000165` reproduced the startup failure and recovered ~45 seconds later. Aug 30 customer impact is therefore the `0000164` window plus under a minute on `0000165`.
- **Denver** currently processes zero vehicles on every tick despite successful fetches, and emits no tones. Tracked separately.

## Timeline

All times below are Eastern Daylight Time unless noted otherwise.

| Time | Evidence | Interpretation |
|---|---|---|
| Aug 27, 8:25:23 PM | Azure portal shows revision `0000153` created. | Revision rollout activity begins. |
| Aug 27, 8:29:15 PM | Azure portal shows revision `0000154` created. | The instance that later carried the incident is created. |
| Aug 27, approximately 8:30 PM onward | Grafana shows the new worker instance with route-index values of zero. | The new process is polling but has no route geometry loaded. |
| Aug 28, 12:00 AM–8:30 PM | All cities: route index `0`, trigger-point cache `0`, health `0`, tones `0`. | Reconciliation is skipped for every city. |
| Aug 28, 12:00 AM–8:30 PM | Worker cycle age remains approximately 3–9 seconds; input fetch success is `1` for all cities. | Worker liveness and city feed fetches remain healthy. |
| Aug 28, 8:31 PM | All route indexes and trigger-point caches become populated simultaneously; all city health values become `1`. | Route-index refresh succeeds. |
| Aug 28, 8:31 PM onward | Six cities produce nonzero tones. | Normal reconciliation resumes for those cities. |
| Aug 29, 8:41 AM | Philadelphia still reports zero tones while processing 351 vehicles and remaining healthy. | Separate Philadelphia-specific issue remains. |
| Aug 29, 8:59:39 AM | Azure portal shows a later revision `0000155` created. | Subsequent deployments should be checked for recurrence of the same startup condition. |
| Aug 30, 15:22:44.870–15:22:45.321 UTC (11:22:44–11:22:45 AM EDT) | Azure `ContainerAppConsoleLogs` contains seven `RouteIndexUnavailable` warnings and seven paired `CityCycleAnomaly` warnings, all with `ROUTE_INDEX_UNAVAILABLE`. | New all-city recurrence signature; each city reports zero vehicles and tones with no publish attempt. |
| Aug 30, 15:21:11–15:39:21 UTC (11:21–11:39 AM EDT) | A bounded read-only search found no `WorkerCycleRecovered` or successful `CityCycleAnomaly` marker after the failure records. | Recovery was not confirmed in the queried window; the recurrence duration remains unknown. |
| Aug 30, 15:15–15:54:55 UTC | Revision `0000164` emitted `route index is not ready, skipping tick.` continuously for all seven cities. | The 15:22 records above are one sampled minute inside a continuous multi-hour outage on `0000164`, not an isolated cycle. |
| Aug 30, 15:54:34.925 UTC | `RevisionCreation` — `0000165` created. | Deployment begins during the investigation. |
| Aug 30, 15:54:54.785–15:54:56.686 UTC | `ContainerStarted`, then `ReplicaUnhealthy` twice, bracketing `WorkerStarted` at 15:54:56.586. | The worker's startup route-shape call is issued while ingress is not yet serving. |
| Aug 30, 15:54:59.883 UTC | `GtfsEndpoints: GTFS Static data not yet loaded.` (Warning) | Server side: the static-ready key is absent. |
| Aug 30, 15:54:59.984 UTC | `Failed to initialize route index (attempt 1/5); exception type HttpRequestException.` (Error) | Worker side, 100 ms later: the same event from the other end of a self-directed HTTP call. **This is the confirmed Aug 30 trigger.** |
| Aug 30, 15:55:31.151 UTC | `WorkerStopped` (`0000164`). | The prior revision finishes draining. |
| Aug 30, 15:55:41.985–15:55:43.273 UTC | All seven cities emit `CityCycleAnomaly` with nonzero `VehiclesProcessed` (atlanta 180, washington-dc 632, boston 365, new-york-city 2,084, toronto 970, philadelphia 341). | **Route index populated ~45 s after worker start.** Reason `ALL_CROSSINGS_SUPPRESSED` is the expected cold-start first-seen signature. |
| Aug 30, 15:55–18:24 UTC | No `RouteIndexUnavailable` event in 2.5 hours of continuous operation on `0000165`. | The recurrence is over; the route index remained healthy. |
| Aug 30, 18:20–18:24 UTC | Philadelphia emits 624 tones over 8 productive cycles; Toronto 1,191; Denver 0 with zero vehicles processed. | Philadelphia is healthy. Denver is the remaining zero-output city. |

## Aug 30 recurrence evidence

The recurrence was queried read-only from workspace `dd9c8c7e-dae8-410b-b876-2cee18c7ad2c`, table `ContainerAppConsoleLogs`, using the approved Basic Logs Search path. The primary bounded window was `2026-08-30T15:21:11Z` through `2026-08-30T15:36:11Z`; the recovery-marker check extended through `2026-08-30T15:39:21Z`.

The error-indicator query returned 14 records: one `RouteIndexUnavailable` and one `CityCycleAnomaly` record for each of the seven configured cities. All records shared cycle ID `f4b406afb53546ddb8b1fd9e7206578e` and reported:

- `Outcome=Failed` and `ReasonCode=ROUTE_INDEX_UNAVAILABLE`.
- `VehiclesProcessed=0`, `TonesEmitted=0`, and `PublishAttempted=false`.
- `LogLevel=Warning`; a separate bounded query for explicit `LogLevel=Error` records returned no rows.

The recovery-marker query returned only the seven `RouteIndexUnavailable` records; it found no `WorkerCycleRecovered` record or successful `CityCycleAnomaly` record in the extended window. This is evidence that recovery was not observed during that query, not proof that recovery did not occur, because structured-event coalescing and ingestion delay can hide a transition.

The query used for the recovery check was:

~~~kusto
ContainerAppConsoleLogs
| where TimeGenerated between (datetime(2026-08-30T15:21:11Z) .. datetime(2026-08-30T15:39:21Z))
| where Log contains "RouteIndexUnavailable" or Log contains "WorkerCycleRecovered" or (Log contains "CityCycleAnomaly" and Log contains "Succeeded")
| project TimeGenerated, Log
| order by TimeGenerated desc
| take 50
~~~

These records do not include a deployment revision, HTTP status, exception type, or route-shape endpoint identity. They therefore do not establish whether the Aug 30 occurrence began during startup, a scheduled refresh, or another route-index load attempt.

## Aug 30 trigger — confirmed

*Added 2026-08-30 evening. This section resolves the "Most likely" candidate list below.*

The trigger was captured directly during the investigation, because revision `0000165` rolled out
while queries were running and reproduced the failure in full. Two log lines 100 ms apart are the
same event seen from both ends of a self-directed HTTP call:

| Time (UTC) | Revision | Level | Message |
|---|---|---|---|
| 15:54:54.785 | 0000165 | System | `ContainerStarted` |
| 15:54:55.681 | 0000165 | System | **`ReplicaUnhealthy`** |
| 15:54:56.586 | 0000165 | Information | `WorkerStarted` |
| 15:54:56.686 | 0000165 | System | **`ReplicaUnhealthy`** |
| 15:54:59.883 | 0000165 | Warning | **`GtfsEndpoints: GTFS Static data not yet loaded.`** |
| 15:54:59.984 | 0000165 | Error | **`Failed to initialize route index (attempt 1/5); exception type HttpRequestException.`** |

The mechanism is that `GtfsStaticLoader` and `Worker` are both `BackgroundService`s in the *same*
process sharing an in-process singleton repository, yet `Worker` fetches route shapes by issuing an
HTTPS request to its own **public external ingress FQDN**. At startup that request leaves the
container while the replica is still `ReplicaUnhealthy` and fails at the transport layer; had it been
routed, the same process would have answered `503`, because `GtfsStaticLoader` has not finished
downloading seven cities' GTFS zips.

This resolves the five-variant list under "Most likely": **variants 1 and 3 are both correct and are
the same underlying condition**, reached over a network path that did not need to exist. Variants 2,
4 and 5 are not supported.

The full analysis and the proposed fix are in
[ROUTE_INDEX_READINESS_DESIGN_DOCUMENT.md](../ROUTE_INDEX_READINESS_DESIGN_DOCUMENT.md).

### Recovery: ~45 seconds, not 24 hours

Critically, `0000165` **recovered on its own**. Between `WorkerStarted` at `15:54:56.586Z` and
`18:24:51Z` — nearly 2.5 hours — there is **no `RouteIndexUnavailable` event**. All seven cities were
processing vehicles by `15:55:43Z`.

This bounds the Aug 30 customer impact at well under a minute and closes P0-5. It also means the
24-hour `PeriodicTimer` that explains the Aug 28 recovery does **not** explain this one; the recovery
path is only partly understood, and per-attempt load telemetry is needed to settle it. Aug 28's
day-long outage should be treated as the worst case of this defect rather than its normal behavior.

## Grafana evidence

Source dashboard: [Worker App Metrics](https://gallantpuffin3113.grafana.net/d/transitjazz-worker-overview/worker-app-metrics)

Dashboard UID: `transitjazz-worker-overview`

The dashboard was **retitled to "Worker App Metrics" on 2026-08-30**, after this investigation. The UID is unchanged, so links keyed on it keep resolving; only the URL slug moved from `transitjazz-worker-overview` to `worker-app-metrics`. Grafana redirects an old slug to the current one as long as the UID matches, so pre-rename links in older notes are not broken.

The committed dashboard at `observability/grafana/dashboards/transitjazz-worker-overview.json` has been updated to match (`"title": "Worker App Metrics"`). The **filename** still reflects the old slug; it is cosmetic, since Grafana keys on the `uid` inside the file rather than on the filename.

Prometheus datasource: `grafanacloud-prom`

The dashboard variable `transit_city` was evaluated across all seven configured cities:

`atlanta`, `boston`, `denver`, `new-york-city`, `philadelphia`, `toronto`, and `washington-dc`.

### Main output panel

Panel: **Tones emitted**

Dashboard PromQL:

~~~promql
{__name__="transitjazz_worker_city_tones_emitted", "transit_city"=~"$transit_city"}
~~~

The Aug 28 query window was `2026-08-28T00:00:00-04:00` through `2026-08-29T00:00:00-04:00`, sampled at 15-minute resolution. A one-minute-resolution query was used around the recovery point.

At 8:31 PM, the first sampled post-recovery tone values were:

| City | Tones emitted | Vehicles processed |
|---|---:|---:|
| Atlanta | 6 | 204 |
| Boston | 41 | 430 |
| Denver | 99 | 453 |
| New York City | 49 | 2,443 |
| Philadelphia | 0 | 382 |
| Toronto | 159 | 988 |
| Washington, DC | 74 | 677 |

These are per-cycle gauge samples, not daily totals.

### Route-index and health signals

Supporting queries were:

~~~promql
{__name__="transitjazz_worker_city_route_index"}
{__name__="transitjazz_worker_city_route_trigger_point_cache"}
{__name__="transitjazz_worker_city_healthy_ratio"}
~~~

All seven cities changed from zero to populated values at 8:31 PM:

| City | Route index | Trigger-point cache |
|---|---:|---:|
| Atlanta | 172 | 172 |
| Boston | 246 | 246 |
| Denver | 139 | 139 |
| New York City | 452 | 398 |
| Philadelphia | 167 | 167 |
| Toronto | 235 | 235 |
| Washington, DC | 140 | 140 |

The simultaneous transition across all cities rules out seven independent transit-feed failures.

### Signals that did not indicate the cause

~~~promql
time() - {__name__="transitjazz_worker_last_cycled_seconds"}
{__name__="transitjazz_worker_city_input_fetch_ok_ratio"}
{__name__="transitjazz_worker_city_input_records_valid"}
{__name__="transitjazz_worker_city_input_source_failures"}
~~~

- Worker and city cycles continued throughout the outage.
- Input fetch success remained `1` for every city.
- Valid input records continued arriving for every city.
- Atlanta had one isolated source-failure sample; the other cities had none. This cannot explain the all-city output loss.
- Worker/city cycle-error rate queries returned no error series for the incident window.

## Revision and restart analysis

The Azure portal screenshot supplied during the investigation shows the prior revision sequence and dates:

- `0000152`: Aug 25, 6:19:29 PM
- `0000153`: Aug 27, 8:25:23 PM
- `0000154`: Aug 27, 8:29:15 PM

Grafana instance labels show that revision `0000154` was still the same worker instance before and after the Aug 28 8:31 PM recovery. Its worker cycle counter advanced continuously, and its cycle age stayed healthy. Therefore, the 8:31 PM recovery was not caused by a restart.

The Aug 30 recurrence cannot yet be tied to a specific revision or deployment: the observed structured event state did not include a deployment revision, and no revision/activity query was performed as part of this bounded log check.

The evidence supports a restart or revision replacement at the *start* of the incident, followed by a failed startup route-index load and a successful 24-hour refresh.

A restart by itself is not sufficient to cause the incident. If `/gtfs/routes/shapes` had been available and successfully processed during startup, the new instance should have loaded the index and continued emitting tones normally.

## Code-path correlation

The current worker implementation matches the observed behavior:

- `InitializeRouteIndexAsync` attempts to fetch `/gtfs/routes/shapes` five times and then allows the worker to continue if initialization still fails.
- During city processing, an absent route index causes the city tick to be skipped and produces an unhealthy result with zero vehicles and zero tones.
- `RefreshRouteIndexAsync` uses a 24-hour timer. An empty index can therefore remain empty for almost a full day.

Relevant implementation: [Worker.cs](../../src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs)

## Root cause assessment

### Confirmed

- A new Container App revision/worker instance preceded the incident.
- The worker remained live and continued fetching input.
- The route index and trigger-point caches were empty for the affected period.
- Reconciliation resumed for six cities immediately when those caches populated.
- The recovery occurred in the existing instance, not through a restart.
- A separate Aug 30 occurrence reproduced the all-city `ROUTE_INDEX_UNAVAILABLE` failure signature in centralized logs.

### Aug 30 recurrence assessment

*Revised 2026-08-30 evening; the original bullets are retained with corrections.*

- The Aug 30 records are a new occurrence, not a continuation of the Aug 28 outage. **Confirmed.**
- The recurrence has the same broad route-index signature as the original incident and is distinct from the Philadelphia-only zero-tone condition. **Partly superseded:** the route-index signature is confirmed, but there is no Philadelphia zero-tone condition — see "Philadelphia follow-up".
- ~~The logs confirm the affected cycle, but do not yet establish how long the route index remained unavailable, whether tones were absent outside that cycle, or whether the route index subsequently recovered.~~ **Now established.** The 15:22 records were inside a continuous outage on revision `0000164` running from at least 15:15 to 15:54:55. `0000164` never recovered; it was replaced. Revision `0000165` reproduced the startup failure and then **recovered in ~45 seconds**.
- ~~The same unsafe empty-index recovery design remains a plausible common mechanism, but the Aug 30 records alone do not prove the same startup or refresh trigger.~~ **Trigger proven** for `0000165`: the startup route-shape load, failing with `HttpRequestException` against the worker's own ingress FQDN.
- **New:** the 45-second recovery is not explained by the 24-hour refresh timer that explains Aug 28. How the index repopulated so quickly is an open question requiring per-attempt load telemetry.

### Most likely

> **Resolved for Aug 30 — see "Aug 30 trigger — confirmed" above.** Variants 1 and 3 are **both**
> correct and are the same condition: the worker calls its own public ingress FQDN for data held by a
> sibling object in the same process, so at startup it fails at the transport layer while the replica
> is `ReplicaUnhealthy`, and would otherwise be answered `503` by itself. Variants 2, 4 and 5 are not
> supported by evidence. The Aug 28 occurrence was not captured directly, but the mechanism is
> deterministic and fires on every deployment, so the same trigger is the strong presumption.

The startup request to the route-shape API was unavailable or unusable while revision `0000154` was coming up. Likely variants include:

1. WebAPI or the route-shape endpoint was not ready when the worker started. — **Confirmed**
2. A transient HTTP timeout, 5xx, DNS, or network error occurred. — *Not supported*
3. The endpoint returned an empty route-shape response during deployment or data loading. — **Confirmed** (same condition as 1)
4. The response could not be deserialized or converted into the route index. — *Not supported*
5. The new revision used an invalid or temporarily unavailable service URL. — *Not supported*

### Not supported by the evidence

- A worker crash or sustained worker stall.
- A broad GTFS-RT input outage.
- A failure in Grafana metric collection alone; the route-index, health, input, and output signals changed coherently.
- A restart at the recovery time.

## Philadelphia follow-up — closed, not a defect

*Revised 2026-08-30 evening. **The original conclusion in this section was wrong.***

This section previously reported that Philadelphia "processed 351-382 vehicles while emitting zero
tones" and called for a reconciliation investigation. Direct structured-log evidence from
`18:20:00Z`-`18:24:00Z` shows Philadelphia emitting tones normally:

| Cycle (UTC) | Vehicles processed | Tones emitted | Published |
|---|---:|---:|---|
| 18:20:11 | 351 | 83 | yes (~11 KB) |
| 18:21:11 | 349 | 112 | yes |
| 18:22:11 | 349 | 56 | yes |
| 18:23:11 | 347 | 102 | yes |
| 18:23:41 | 348 | 68 | yes |

**624 tones across 8 productive cycles in 4 minutes**, publishing successfully each time.

### Why it looked broken

Philadelphia's cycles alternate on a strict 20-second period between a productive tick and a
`DUPLICATE_FEED` tick that emits nothing:

```
18:20:11  Succeeded  veh=351  tones=83   feed freshness ~7s
18:20:21  Failed     veh=351  tones=0    feed freshness ~17s
18:20:41  Succeeded  veh=349  tones=76   feed freshness ~7s
18:20:51  Failed     veh=349  tones=0    feed freshness ~17s
```

The worker ticks every 10 seconds (`WorkerOptions.CycleIntervalSeconds = 10`) while SEPTA publishes
roughly every 20. Every second tick re-reads a feed whose header timestamp has not advanced,
correctly identifies it as a duplicate, and emits nothing. Feed freshness alternating between ~7 s
and ~17 s — exactly one tick apart — is the fingerprint.

Toronto, Denver and intermittently Atlanta show the same alternation. **It is not
Philadelphia-specific and it is not a reconciliation fault.** The original observation was a
dashboard sample landing on a duplicate tick, at a 15-minute resolution that cannot distinguish
"zero at this instant" from "zero always."

### Two real issues this exposes

1. **`DUPLICATE_FEED` is logged at `Warning` in steady state.** In the window examined, 25 of 52
   `CityCycleAnomaly` rows were `Outcome=Failed` `DUPLICATE_FEED` — the most common line in the
   operational log describes correct behavior. It should be `Information`, or the tick should skip
   early when the feed header timestamp has not advanced, avoiding the redundant fetch entirely.
2. **`Outcome` must be read, not just `EventName`.** A `Succeeded` `CityCycleAnomaly` is a *recovery
   marker*, not a failure: `Worker.cs:408` emits one per missing-tone reason when the classifier
   returns `null`, which happens as soon as `TonesEmitted > 0` (`CityAnomalyClassifier.cs:19`). Its
   `ReasonCode` names the reason being **cleared**. Any alert counting `CityCycleAnomaly` rows
   without filtering `Outcome=Failed` will double-count and invert the meaning of half of them.

### Retarget: Denver

Denver now holds the genuine zero-tone condition. It shows the same 20-second alternation but with
`VehiclesProcessed = 0` on **every** tick, while emitting no `CityInputFailed`, `CityInputEmpty`, or
`CityInputPartial` event in the surrounding 24 minutes — its fetch succeeds and returns a feed
yielding zero usable vehicle records. That is a feed-content or parsing condition, unrelated to the
route index.

The investigation this section originally proposed — `skippedNoJoinKey`, `skippedUnknownRoute`,
vehicle state counts, feed route IDs versus index keys, along-route movement versus trigger
spacing — should be run against **Denver**, and is tracked in
[2026-08-30-denver-no-tones.md](2026-08-30-denver-no-tones.md).

## Corrective actions

*Dispositions revised 2026-08-30 evening. The design and implementation plan for items P0-1 through
P1-5 now live in
[ROUTE_INDEX_READINESS_DESIGN_DOCUMENT.md](../ROUTE_INDEX_READINESS_DESIGN_DOCUMENT.md), which
supersedes this list.*

### Priority 0

1. Replace the one-time five-attempt startup behavior with continuous bounded exponential backoff whenever the route index is empty. — **Superseded.** The root cause is that the worker fetches route shapes from its own process over public HTTPS; the design document's R1 deletes the call rather than retrying it. Retry tuning becomes mandatory only if R1 is rejected.
2. Alert when route-index size is zero while worker cycles and input fetches remain healthy. — **Open**, adopted as R5. Cannot be authored from the current checkout (no Grafana access); needs someone with dashboard permissions.
3. Do not treat a live worker with an empty route index as healthy city processing. — **Open**, adopted as R3 (readiness probe, not liveness).
4. Add tests proving that an empty or failed startup response recovers within minutes after the endpoint becomes available. — **Open**, adopted.
5. Treat the Aug 30 recurrence as unresolved until route-index recovery is observed and the affected customer impact is bounded. — **Closed.** Recovery observed at ~15:55:43 UTC; impact bounded at ~45 seconds for `0000165`, plus the `0000164` window.

### Priority 1

1. Add explicit route-index load state, attempt count, last-success timestamp, and failure classification metrics. — **Open**, adopted as R4, and now the **highest-value next step**: it is what would explain how `0000165` recovered in 45 seconds, which in turn calibrates how urgent the rest of this list is.
2. Add a readiness/dependency check for `/gtfs/routes/shapes` during Container App rollout. — **Open**, adopted as R3 at the data layer rather than the HTTP layer.
3. Correlate worker startup with WebAPI availability and deployment events. — **Closed.** They are the same process; that is the defect.
4. Preserve structured route-shape load errors in the operational log path. — **Open**, adopted as R4.
5. Verify revision `0000155` and all future revisions immediately after deployment for route-index readiness. — **Open**, to be automated by R3's readiness gate rather than performed by hand.
6. Continue the separate Philadelphia investigation using detailed reconciliation telemetry. — **Closed — not a defect.** Retargeted at Denver; see [2026-08-30-denver-no-tones.md](2026-08-30-denver-no-tones.md).
7. Correlate the Aug 30 `15:22:44-15:22:45Z` failure window with revision, route-shape endpoint, and refresh activity to identify the triggering load attempt. — **Closed.** The 15:22 records belong to revision `0000164`; the triggering load attempt was captured directly on `0000165` at 15:54:59.984 UTC.

### New, arising from this update

1. **Demote `DUPLICATE_FEED` from `Warning`.** It is the most frequent line in the steady-state log and describes correct behavior — 25 of 52 anomaly rows in a 4-minute window. Better still, skip the tick early when the feed header timestamp has not advanced, avoiding the redundant fetch.
2. **Reconcile worker tick cadence with feed publish cadence.** The 10-second tick does redundant work against ~20-second feeds for at least Philadelphia, Toronto and Denver. Consider a per-city interval or an early-skip on unchanged feed timestamps.
3. **Document the `Outcome=Succeeded` recovery-marker semantics.** A `Succeeded` `CityCycleAnomaly` reports a reason being *cleared*. Alerts and dashboards must filter `Outcome=Failed`; this is an easy and consequential misreading.
4. **Bound the recovery-marker fan-out.** `Worker.cs:411` iterates every `StructuredLogReasonCode` on each productive tick, relying on `EmitRecovery` to suppress all but the active one. Emit for the active reason instead.
5. **Investigate Denver's zero-vehicle condition.** Successful fetches yielding zero usable records, with no input-failure event.

## Verification query

The central diagnostic condition for this incident is:

~~~promql
({__name__="transitjazz_worker_city_route_index"} == 0)
and on (transit_city)
({__name__="transitjazz_worker_city_input_fetch_ok_ratio"} == 1)
and on (transit_city)
({__name__="transitjazz_worker_city_healthy_ratio"} == 0)
~~~

The desired post-fix behavior is that this condition either never persists beyond the startup retry budget or generates an immediate operational alert rather than silently continuing for 24 hours.

*Added 2026-08-30 evening.* This PromQL remains the right condition for the **route-index** fault, but
note it could not be executed during either investigation — no Grafana tool is registered and no
Grafana CLI is on `PATH` in this checkout, so all quantitative findings above come from
`ContainerAppConsoleLogs` and from source. The expression should be validated by someone with Grafana
access before an alert is built on it.

For the log path, the equivalent bounded check — used to establish the recovery in this
update — is:

~~~kusto
ContainerAppConsoleLogs
| where TimeGenerated between (datetime(2026-08-30T15:54:56Z) .. datetime(2026-08-30T16:20:00Z))
| extend S = parse_json(Log).State
| extend EventName = tostring(S.EventName), City = tostring(S.City),
         ReasonCode = tostring(S.ReasonCode), Outcome = tostring(S.Outcome),
         Tones = toint(S.TonesEmitted), Veh = toint(S.VehiclesProcessed)
| where EventName in ('WorkerStarted', 'RouteIndexUnavailable', 'CityCycleAnomaly')
| project TimeGenerated, EventName, City, ReasonCode, Outcome, Tones, Veh
| order by TimeGenerated asc
| take 100
~~~

Two cautions when interpreting the result, both from findings in this update:

- **Filter `Outcome=Failed`** for failure counts. `Succeeded` rows are recovery markers naming the
  reason being cleared.
- **Do not read a single zero-tone tick as a fault.** Cities on ~20-second feeds legitimately emit
  zero on alternating ticks; evaluate at least four cycles per city, or exclude
  `ReasonCode == 'DUPLICATE_FEED'`.

Note the metrics path cannot see the duplicate-feed cadence at all — `tones_emitted` merely
alternates, which reads as ordinary sparsity at dashboard resolution. That is precisely how the
Philadelphia misdiagnosis happened.
