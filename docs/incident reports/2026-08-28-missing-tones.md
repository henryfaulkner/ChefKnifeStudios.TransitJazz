# Incident Report: Missing TransitJazz Tones

**Incident date:** 2026-08-28 (original outage)

**Related recurrence:** 2026-08-30

**Investigation date:** 2026-08-29 to 2026-08-30

**Environment:** Production (`deployment_environment_name=prod`)

**Affected service:** `marta-jazz-dev-ca-server` / `transitjazz-transit-worker`

**Status:** The original route-index outage recovered automatically; a same-signature recurrence was observed on Aug 30 with recovery not yet confirmed. The recovery design remains unsafe, and Philadelphia has a separate unresolved zero-tone condition.

## Executive summary

TransitJazz emitted no tones for any configured city for most of Aug 28, 2026. The worker was not stopped: it continued completing its approximately 10-second cycles and successfully fetching city input. The failure was in the in-memory route index required for spatial reconciliation.

Read-only Azure Monitor evidence identified a separate occurrence on Aug 30, 2026. Between 15:22:44 and 15:22:45 UTC, all seven cities emitted paired `RouteIndexUnavailable` and `CityCycleAnomaly` warning records with `Outcome=Failed` and `ReasonCode=ROUTE_INDEX_UNAVAILABLE`; each had zero processed vehicles and tones, and publishing was not attempted. The timestamps are two days after the original outage and therefore represent a recurrence of the failure mode, not an extension of the Aug 28 incident.

The strongest causal sequence is:

1. Container App revisions `0000153` and `0000154` were created on Aug 27 at approximately 8:25 PM and 8:29 PM Eastern, replacing the prior worker instance.
2. The new worker process started without a populated route index. Its startup route-shape load either failed, returned no usable data, or failed while parsing/building the index.
3. The worker exhausted its five startup attempts, continued polling with an empty index, and skipped reconciliation for every city.
4. The background route-index refresh succeeded approximately 24 hours later, at 8:31 PM Aug 28. All cities became healthy and six cities immediately resumed emitting tones.

The exact startup HTTP failure has not been confirmed from logs. Grafana metrics establish the empty-index condition and its timing; the Azure Container App activity/revision history establishes the preceding rollout.

## Customer impact

- All seven configured cities had zero tone output from the beginning of the Aug 28 Eastern-time day through approximately 8:30 PM.
- This represents roughly 20.5 hours of missing tone output within the queried day, and likely approximately 24 hours from the Aug 27 revision rollout until route-index recovery.
- The worker continued accepting/processing input and reporting liveness, so this was a silent degradation rather than a complete service outage.
- After route-index recovery, Atlanta, Boston, Denver, New York City, Toronto, and Washington, DC emitted tones.
- Philadelphia continued to emit zero tones through at least 8:41 AM Aug 29 despite healthy input and vehicle processing. This is treated as a separate reconciliation/eligibility issue, not part of the broad route-index outage.
- On Aug 30, the centralized logs recorded the same route-index failure signature across all seven cities. This confirms an application-level recurrence, but the available records establish only the affected cycle, not the duration or customer impact of the recurrence.

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

## Grafana evidence

Source dashboard: [TransitJazz Worker Overview](https://gallantpuffin3113.grafana.net/d/transitjazz-worker-overview/transitjazz-worker-overview)

Dashboard UID: `transitjazz-worker-overview`

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

- The Aug 30 records are a new occurrence, not a continuation of the Aug 28 outage: the original route index recovered on Aug 28 and the new records are timestamped two days later.
- The recurrence has the same broad route-index signature as the original incident and is distinct from the Philadelphia-only zero-tone condition.
- The logs confirm the affected cycle, but do not yet establish how long the route index remained unavailable, whether tones were absent outside that cycle, or whether the route index subsequently recovered.
- The same unsafe empty-index recovery design remains a plausible common mechanism, but the Aug 30 records alone do not prove the same startup or refresh trigger.

### Most likely

The startup request to the route-shape API was unavailable or unusable while revision `0000154` was coming up. Likely variants include:

1. WebAPI or the route-shape endpoint was not ready when the worker started.
2. A transient HTTP timeout, 5xx, DNS, or network error occurred.
3. The endpoint returned an empty route-shape response during deployment or data loading.
4. The response could not be deserialized or converted into the route index.
5. The new revision used an invalid or temporarily unavailable service URL.

### Not supported by the evidence

- A worker crash or sustained worker stall.
- A broad GTFS-RT input outage.
- A failure in Grafana metric collection alone; the route-index, health, input, and output signals changed coherently.
- A restart at the recovery time.

## Philadelphia follow-up

Philadelphia remained healthy after route-index recovery and continued processing vehicles. Its route index and trigger-point cache were populated, input fetches succeeded, and published batch bytes were nonzero. However, its tone gauge remained zero.

The dashboard exposes only suppression counters, not the detailed reconciliation breakdown. Philadelphia showed occasional no-distance suppressions but no meaningful first-seen, teleport, or transfer suppression pattern. The next investigation should inspect structured worker telemetry/logs for:

- `skippedNoJoinKey`
- `skippedUnknownRoute`
- moved, unchanged, stationary, and stale vehicle counts
- route IDs from the Philadelphia feed versus route-index keys
- along-route distance movement relative to trigger-point spacing

This is a separate data/reconciliation investigation from the all-city route-index outage.

## Corrective actions

### Priority 0

1. Replace the one-time five-attempt startup behavior with continuous bounded exponential backoff whenever the route index is empty.
2. Alert when route-index size is zero while worker cycles and input fetches remain healthy.
3. Do not treat a live worker with an empty route index as healthy city processing.
4. Add tests proving that an empty or failed startup response recovers within minutes after the endpoint becomes available.
5. Treat the Aug 30 recurrence as unresolved until route-index recovery is observed and the affected customer impact is bounded.

### Priority 1

1. Add explicit route-index load state, attempt count, last-success timestamp, and failure classification metrics.
2. Add a readiness/dependency check for `/gtfs/routes/shapes` during Container App rollout.
3. Correlate worker startup with WebAPI availability and deployment events.
4. Preserve structured route-shape load errors in the operational log path.
5. Verify revision `0000155` and all future revisions immediately after deployment for route-index readiness.
6. Continue the separate Philadelphia investigation using detailed reconciliation telemetry.
7. Correlate the Aug 30 `15:22:44–15:22:45Z` failure window with revision, route-shape endpoint, and refresh activity to identify the triggering load attempt.

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
