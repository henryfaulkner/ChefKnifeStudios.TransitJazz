# Route Index Readiness — Design Document

**Status:** Proposed — *revised 2026-08-30 ~18:30 UTC after a second evidence pass*
**Date:** 2026-08-30
**Author:** Investigation follow-up to [2026-08-28-missing-tones.md](incident%20reports/2026-08-28-missing-tones.md)
**Affected component:** `marta-jazz-dev-ca-server` (WebAPI + TransitDataWorker, single container)
**Supersedes:** the Priority 0 / Priority 1 corrective-action list in the incident report

> **Revision note (2026-08-30, later same day).** A follow-up read of centralized logs covering the
> three hours *after* this document was first written materially changes two of its claims and
> strengthens a third. Revision `0000165` **recovered on its own in about 45 seconds** — the 24-hour
> outage did not repeat — and **Philadelphia is emitting tones**, so §7's premise no longer holds.
> The startup defect in §3.1 is unchanged and still worth fixing, but its *observed* severity on
> `0000165` was seconds, not a day. See **§2.5** and **§7** for the corrected record.
>
> **Second revision note (same evening).** `ContainerAppSystemLogs` now explains *how* `0000165`
> recovered: ingress traffic shifted to the new revision at `15:55:00.431Z`, **about one second
> after** the worker's first route-index attempt failed at `15:54:59.984Z`. A later attempt in the
> existing five-attempt budget then succeeded. This **strengthens** the case for R1 rather than
> weakening it — the margin was roughly one second, so the retry budget is *marginal*, not adequate.
> §2.6 replaces the "mechanism not yet identified" language in §2.5 and §10.4.

---

## 1. Executive summary

The incident report identified the *symptom* correctly — an empty in-memory route index causes every
city tick to be skipped while the worker stays live — but listed the trigger as unconfirmed, offering
five candidate variants. This investigation **confirmed the actual mechanism from centralized logs**
and found the failure is not transient or environmental. It is a deterministic startup-ordering
defect that fires on **every deployment**, and a third occurrence was observed live during this
investigation.

*Revised:* the startup **failure** does fire on every deployment — this is confirmed twice over. What
does **not** follow, and what the first pass of this document wrongly generalized from Aug 28, is that
the failure necessarily costs 24 hours. On revision `0000165` the index populated about 45 seconds
after `WorkerStarted`, well inside the same cold start. The defect is real and the retry budget is
still structurally wrong; the day-long outage is its *worst* case, not its normal one (§2.5).

The root cause is a **self-referential HTTP dependency**. `GtfsStaticLoader` and `Worker` are two
`BackgroundService`s registered in the *same process*, sharing an in-process singleton
`InMemoryKeyValueRepository`. Yet `Worker` obtains route shapes by issuing an HTTPS request to its
own public ingress FQDN. At startup that request leaves the container, traverses Azure Container
Apps ingress while the replica is still `ReplicaUnhealthy`, and fails — or, once routed, is answered
`503` by the very process making the call, because `GtfsStaticLoader` has not finished downloading
seven cities' GTFS zips yet. The worker's five-attempt budget expires in roughly 30 seconds; the
static load takes far longer; the only retry after that is a **24-hour** timer.

The fix is to delete the network hop. The data is already in the same address space.

### Confirmed causal chain

1. A new revision is created and its replica starts.
2. `Worker.ExecuteAsync` calls `InitializeRouteIndexAsync`, which `GET`s `/gtfs/routes/shapes`
   against the **external** `WebApi:BaseUrl` FQDN.
3. Concurrently, `GtfsStaticLoader.ExecuteAsync` begins downloading and parsing GTFS static zips for
   all seven cities. Until it completes, it has not written `__gtfs_static_ready__`.
4. The call fails: either at the transport layer (replica not yet healthy → `HttpRequestException`)
   or at the application layer (`GtfsEndpoints` reads the missing ready-key and returns `503`).
5. Five attempts with `2^n` backoff exhaust in about 30 seconds. The worker logs a warning and
   **continues running with an empty index**.
6. Every city tick is skipped for up to 24 hours, until `RefreshRouteIndexAsync` fires.

Steps 4 and 5 are not a race that is *sometimes* lost. The worker's *first* attempts fail every time.

**Corrected:** step 6 is where the original analysis over-reached. On `0000165` the index was
populated ~45 s after `WorkerStarted`, not 24 h. §2.6 identifies the mechanism: **ingress became
ready roughly one second after the first attempt failed**, so a later attempt in the same
five-attempt budget succeeded.

The race is therefore winnable — `0000165` won it — but by a margin of about one second. That makes
the budget **marginal rather than structurally hopeless**, which is arguably the worse property: the
same code path produced a 45-second blip on Aug 30 and a ~24-hour outage on Aug 28, with the outcome
decided by image-pull and health-check timing that nothing in the system controls. R1 removes the
race rather than betting on it.

---

## 2. Evidence

All log evidence is read-only from Log Analytics workspace `dd9c8c7e-dae8-410b-b876-2cee18c7ad2c`,
tables `ContainerAppConsoleLogs` / `ContainerAppSystemLogs`, via the project's bounded query helper.
Note that `ContainerAppConsoleLogs` is a **Basic Logs** table: the standard `/query` API rejects it,
and the `/search` path must be used.

### 2.1 The trigger, captured directly

Revision `0000165` rolled out during this investigation and reproduced the failure in full:

| Time (UTC) | Revision | Level | Message |
|---|---|---|---|
| 15:54:34.925 | 0000164 | System | `RevisionCreation` — creating `0000165` |
| 15:54:54.785 | 0000165 | System | `ContainerStarted` |
| 15:54:55.681 | 0000165 | System | **`ReplicaUnhealthy`** |
| 15:54:56.586 | 0000165 | Information | `WorkerStarted` |
| 15:54:56.686 | 0000165 | System | **`ReplicaUnhealthy`** |
| 15:54:59.883 | 0000165 | Warning | **`GtfsEndpoints: GTFS Static data not yet loaded.`** |
| 15:54:59.984 | 0000165 | Error | **`Failed to initialize route index (attempt 1/5); exception type HttpRequestException.`** |

The two lines 100 ms apart at 15:54:59 are the same event seen from both ends of the loop: the
server side logging that its ready-key is absent, and the worker side recording the failed call. The
incident report could not distinguish among its five candidate causes; this pair shows that variants
1 ("WebAPI not ready") and 3 ("empty response during data loading") are **both** true and are the
same underlying condition, reached over an unnecessary network path.

`ReplicaUnhealthy` firing at 15:54:55 and 15:54:56 — bracketing the worker's call — confirms the
request is issued while the replica is not yet serving, which is why the transport fails outright
rather than returning the `503` the endpoint intended.

### 2.2 This is the third occurrence, not the second

The incident report treats Aug 28 as the original and Aug 30 15:22 as a recurrence. Revision
`0000164` was still emitting `route index is not ready, skipping tick.` for all seven cities
continuously from at least 15:15 through 15:54:55, when it was replaced. Revision `0000165` then
failed identically. The Aug 30 15:22 records the report cites are one sampled minute inside a
continuous multi-hour outage, not an isolated cycle.

The report's open question "whether the route index subsequently recovered" is answered **for
revision `0000164`**: it did not. It was still empty 32 minutes later, and revision `0000165` began
with the same failing startup call.

> **Superseded in part.** The sentence above is correct about `0000164`. The first draft of this
> document extended it to `0000165` by implication; that extension is **wrong**. `0000165` recovered.
> See §2.5.

### 2.3 Scope of impact per occurrence

Every occurrence affects all seven cities simultaneously (`atlanta`, `boston`, `denver`,
`new-york-city`, `philadelphia`, `toronto`, `washington-dc`) with `VehiclesProcessed=0`,
`TonesEmitted=0`, `PublishAttempted=false`. Because `minReplicas: 1` and `maxReplicas: 1`, there is
no second replica to serve the request or to mask the outage.

### 2.4 Why Grafana could not answer this

The Grafana integration is **not configured in this checkout** — no registered Grafana tool and no
Grafana CLI on `PATH`. The dashboard referenced by the incident report is committed at
`observability/grafana/dashboards/transitjazz-worker-overview.json`, and its PromQL is reproduced in
the report, but no live query could be run here. Every claim above therefore rests on Azure Monitor
logs and on source, not on metrics.

The live dashboard is now titled **[Worker App Metrics](https://gallantpuffin3113.grafana.net/d/transitjazz-worker-overview/worker-app-metrics)**
(retitled 2026-08-30; UID `transitjazz-worker-overview` unchanged, slug moved to `worker-app-metrics`).
The committed JSON has been updated to match. Its **filename** still reflects the old slug, which is
cosmetic — Grafana keys on the `uid` field inside the file, not on the filename.

This is itself a finding: the metrics path shows *that* the index is zero, but it never carried the
exception type or the `503`. The confirmation required the log path added in feature 054. The
diagnosis was previously blocked for two days for want of one log line that already existed.

*Re-checked on the second pass and still true.* No Grafana tool is registered in this environment and
no Grafana CLI is on `PATH`, so every quantitative claim in §2.5 likewise comes from
`ContainerAppConsoleLogs` and from source, never from metrics. This has a practical consequence for
§5.5: the PromQL alert proposed there **cannot be validated from this checkout**. It should be
authored and verified by someone with Grafana access before it is relied upon. Note also that the
`DUPLICATE_FEED` finding in §2.5 is invisible to the metrics path entirely — `tones_emitted` simply
alternates between zero and nonzero, which looks like ordinary sparsity on a 15-minute-resolution
panel. Only the per-cycle `ReasonCode` and `FeedFreshnessSeconds` fields make the cadence mismatch
legible, and those exist only in logs.

### 2.5 Revision `0000165` recovered in ~45 seconds — and the failure mode has moved

A second evidence pass covering `15:54:00Z` through `18:24:00Z` — the three hours after §2.1 — was run
against the same workspace and table. It changes the picture materially.

**The route index populated almost immediately.** Between `WorkerStarted` at `15:54:56.586Z` and
`18:24:51Z` there is **not one `RouteIndexUnavailable` event**. The last one belongs to `0000164`.
The first cycle showing real work is at `15:55:41Z`:

| Time (UTC) | Event | Detail |
|---|---|---|
| 15:54:56.586 | `WorkerStarted` | `0000165` begins |
| 15:54:59.984 | route-index load failure | attempt 1/5, `HttpRequestException` (§2.1) |
| 15:55:31.151 | `WorkerStopped` | `0000164` finishes draining |
| 15:55:41.985 | `CityCycleAnomaly` | atlanta, `ALL_CROSSINGS_SUPPRESSED`, **180 vehicles processed** |
| 15:55:42–43 | `CityCycleAnomaly` × 6 | all remaining cities processing vehicles |

By `15:55:43Z` all seven cities were reconciling against a populated index — about **45 seconds**
after the worker started, and roughly **44 seconds** after the retry loop's first failure. The
`ALL_CROSSINGS_SUPPRESSED` on those first ticks is the expected cold-start signature: every vehicle
is first-seen, so there is no prior position to measure a crossing against.

This does **not** exonerate the design. The startup call still failed, still traversed ingress to
reach a sibling object, and still burned its budget on a dependency it did not need. R1 remains
correct. The claim that a failed startup *costs a day* is the worst case rather than the rule.

**§2.6 identifies why it recovered, and the answer cuts against leniency:** ingress went live 447 ms
after the first attempt failed, so the budget succeeded by a hair. R1's priority therefore rests on
§3.1's architectural argument *and* on §2.6's timing margin — not on outage duration, which is the
one variable this defect does not control.

**The dominant failure mode is now `DUPLICATE_FEED`, not `ROUTE_INDEX_UNAVAILABLE`.** Over
`18:20:00Z`–`18:24:00Z` (21 worker cycles), grouping every `CityCycleAnomaly`:

| City | Reason | Outcome | Rows | Tones | Vehicles |
|---|---|---|---:|---:|---:|
| atlanta | `NO_CROSSINGS` | Failed | 1 | 0 | 184 |
| atlanta | `NO_CROSSINGS` | Succeeded | 1 | 74 | 184 |
| denver | `DUPLICATE_FEED` | Failed | 8 | 0 | 0 |
| denver | `NO_CROSSINGS` | Failed | 8 | 0 | 0 |
| philadelphia | `DUPLICATE_FEED` | Failed | 8 | 0 | 2,788 |
| philadelphia | `DUPLICATE_FEED` | Succeeded | 8 | **624** | 2,788 |
| toronto | `DUPLICATE_FEED` | Failed | 9 | 0 | 9,598 |
| toronto | `DUPLICATE_FEED` | Succeeded | 8 | **1,191** | 8,529 |
| toronto | `NO_CROSSINGS` | Succeeded | 1 | 76 | 1,069 |

`boston`, `new-york-city` and `washington-dc` produce **no anomaly rows at all** in this window — the
worker only emits `CityCycleAnomaly` when a reason classifies, so their absence is the healthy case.

**Reading these rows correctly requires one subtlety.** An `Outcome=Succeeded` `CityCycleAnomaly` is
*not* a failure. `Worker.cs:408` takes the `else` branch when `CityAnomalyClassifier.Classify`
returns `null` — which it does as soon as `TonesEmitted > 0` (`CityAnomalyClassifier.cs:19`) — and
emits a **recovery marker** for each missing-tone reason, deduplicated by `EmitRecovery` to whichever
reason was actually active. So `philadelphia / DUPLICATE_FEED / Succeeded / 624 tones` means
"Philadelphia was in `DUPLICATE_FEED` and has just come out of it," and the `ReasonCode` on a
`Succeeded` row names the reason being *cleared*, not one being reported. Any alert or dashboard
built on these events **must filter on `Outcome=Failed`**; counting `CityCycleAnomaly` rows alone
will roughly double-count and invert the meaning of half of them.

**The `DUPLICATE_FEED` alternation is a cadence mismatch, not a fault.** Philadelphia's rows alternate
with strict regularity, one failed and one succeeded tick each 20 seconds:

```
18:20:11  Succeeded  veh=351  tones=83   fresh≈7s
18:20:21  Failed     veh=351  tones=0    fresh≈17s
18:20:41  Succeeded  veh=349  tones=76   fresh≈7s
18:20:51  Failed     veh=349  tones=0    fresh≈17s
```

The worker ticks every 10 s (`WorkerOptions.CycleIntervalSeconds = 10`) while these agencies publish
roughly every 20 s. Every second tick therefore re-reads a feed whose header timestamp has not
advanced, is correctly identified as a duplicate, and emits nothing. Feed freshness alternating
between ~7 s and ~17 s — exactly one 10 s tick apart — is the fingerprint. This is the polling loop
working as designed against a slower publisher; the *only* real cost is that it is logged at
`Warning` on every other tick for every affected city.

**Denver is the genuine open problem.** It shows the same 20-second alternation but with
`VehiclesProcessed = 0` on every tick, while emitting **no** `CityInputFailed`, `CityInputEmpty`, or
`CityInputPartial` events in the surrounding 24 minutes. Its fetch succeeds and returns a feed that
yields zero usable vehicle records. That is a feed-content or parse condition specific to Denver,
unrelated to the route index, and it now owns the zero-tone slot that §7 assigned to Philadelphia.
A separate incident report already exists at
[2026-08-30-denver-no-tones.md](incident%20reports/2026-08-30-denver-no-tones.md); this finding
belongs to it.

### 2.6 How `0000165` recovered: ingress arrived one second too late

`ContainerAppSystemLogs` for `15:50:00Z`–`18:30:00Z` answers the question §2.5 left open, and rules
out the most obvious alternative explanation.

**It was not another deployment.** `0000165` is the last revision created in the window. After
`15:55:31Z` the system log contains nothing but half-hourly Key Vault syncs — no `RevisionCreation`,
no container restart, no image pull, no replica reschedule. The process that failed at startup is the
same process that recovered.

**It was the retry budget, winning by about one second:**

| Time (UTC) | Source | Event |
|---|---|---|
| 15:54:34.925 | System | `Creating a new revision: …--0000165` |
| 15:54:42.823 / 15:54:44.318 | System | first two containers created and started |
| 15:54:54.117 | System | pulling image `…server.webapi:60e52e0…` |
| 15:54:54.659 | System | image pulled in 541 ms |
| 15:54:54.785 | System | **container started** (WebAPI) |
| 15:54:55.681 | System | `ReplicaUnhealthy` |
| 15:54:56.586 | App | `WorkerStarted` |
| 15:54:56.686 | System | `ReplicaUnhealthy` |
| 15:54:59.984 | App | **route-index attempt 1/5 fails** — `HttpRequestException` |
| **15:55:00.431** | System | **`Setting traffic weight of '100%'` for `0000165`** — ingress live |
| 15:55:31.141 | System | `Stopping container server` (`0000164` drains) |
| 15:55:41.985 | App | all seven cities processing vehicles |

The worker's first attempt failed at `15:54:59.984Z`. Ingress went live at `15:55:00.431Z` — **447
milliseconds later**. A subsequent attempt in the same five-attempt exponential backoff then found a
route and succeeded, and the index was serving all seven cities by `15:55:41Z`.

**Why this strengthens R1 rather than weakening it.** The natural reading of §2.5 in isolation is
"the retry budget worked, so this is less urgent." The opposite is true. The budget did not
comfortably cover the wait; it happened to still have attempts left when readiness arrived, by a
margin under half a second. Everything that decides the outcome is outside the application's
control — image pull time (541 ms here, but unbounded on a cold registry cache), health-check
scheduling, and how long `GtfsStaticLoader` takes to parse seven agencies' zips. Shift any of them
and the same code produces Aug 28's day-long outage instead.

So the two occurrences are not different bugs, and not a bug that "sometimes" fires. They are the
same race, decided differently by infrastructure timing. §1's original claim that the worker "loses
this race every time" is falsified — but the corrected statement is worse for the design, not better:
**the outcome is nondeterministic, and the failure mode when it loses is silent and day-long.**

**Two incidental findings from the same log.** The replica runs **multiple containers** (created at
`15:54:42`, `15:54:44`, and `15:54:54`), so the worker's self-directed HTTP call must wait for the
whole replica to pass health checks, not just its own container — which is why the timing is this
tight. And traffic weight is set to `100%` for `0000164` three times at `15:54:36–37Z` before
switching to `0000165` at `15:55:00Z`, confirming §3.4's account of single-revision mode: the old
revision serves until the new one is ready, then is deactivated, with no gate that considers whether
the new one can actually emit a tone.

---

## 3. Root cause analysis

### 3.1 The primary defect: an HTTP call to itself

`src/Server/.../WebAPI/Program.cs` registers both services into one host:

```csharp
builder.Services.AddSingleton(typeof(IKeyValueRepository<>), typeof(InMemoryKeyValueRepository<>));  // :209
builder.Services.AddHostedService<GtfsStaticLoader>();                                               // :212
builder.Services.AddHostedService<Worker>();                                                         // :259
```

`GtfsStaticLoader` writes route shapes into that singleton. `Worker` needs exactly those shapes. But
`Worker` reads them like a remote client:

```csharp
var client = httpClientFactory.CreateClient("RouteShapeApi");
var response = await client.GetAsync("/gtfs/routes/shapes", ct);
```

with a base address of `WebApi:BaseUrl` —
`https://marta-jazz-dev-ca-server.jollytree-dd5ca774.eastus2.azurecontainerapps.io`, the **public
external FQDN**. A pointer dereference has been implemented as a round trip through the public
internet, TLS, and a load balancer.

Every property of the incident follows from this single choice:

- It fails during startup because ingress is not ready, though the data path never left the process.
- It is answered `503` by the caller's own process, because the loader is a sibling, not an upstream.
- It cannot be fixed by retry tuning alone, because the wait is bounded by GTFS parse time.
- It is invisible to a readiness probe, because the process *is* healthy — only its data is missing.

### 3.2 The amplifier: a 24-hour retry interval as the only recovery

`InitializeRouteIndexAsync` exhausts five attempts and returns. From that moment the sole remaining
path to a populated index is `RefreshRouteIndexAsync`:

```csharp
using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
```

The `PeriodicTimer` is created *after* startup failed, so the first tick is a full 24 hours later.
This converts a several-second startup ordering problem into a day-long silent outage. The Aug 28
recovery at 8:31 PM — with no restart, all seven cities populating simultaneously — is exactly this
timer firing.

> **Qualified by §2.5–2.6.** This is the correct reading of Aug 28, and the 24-hour timer remains a
> real hazard worth removing under R2. But it is *not* what happened on `0000165`: per §2.6 the
> startup retry budget succeeded once ingress went live ~0.4 s after its first failure, so the
> 24-hour timer was never reached.
>
> The amplifier is therefore a **latent** risk — it fires only when the startup budget is exhausted,
> which happened on Aug 28 and did not on Aug 30. That does not make it less worth removing: it is
> precisely what converts a lost race into a day-long outage instead of a retry.

### 3.3 The concealment: an empty index reports as healthy

The worker keeps cycling, keeps fetching every city's GTFS-RT feed successfully, and keeps reporting
liveness. `transitjazz_worker_city_input_fetch_ok_ratio` stays at `1`. Nothing in the liveness or
readiness surface distinguishes "processing normally" from "discarding all input". The product
outputs nothing while every infrastructure signal is green.

### 3.4 Contributing: no deployment gate

`minReplicas: 1, maxReplicas: 1` with `activeRevisionsMode: 'Single'` means the old revision is
deactivated as the new one starts. There is no health gate that considers route-index readiness, so
a revision that can never emit a tone is promoted to 100% traffic and the working revision is torn
down.

**Confirmed directly in `ContainerAppSystemLogs` (§2.6).** The promotion sequence is visible in full:
traffic weight is held at `100%` for `0000164` through `15:54:37Z`, shifts to `0000165` at
`15:55:00.431Z`, and `0000164` is stopped at `15:55:31.141Z`. The only condition gating that shift is
the replica's health probe — which passes while the route index is empty (§3.3), because the process
is healthy and only its data is missing.

This also exposes the ordering that makes the defect self-inflicted: **the worker's route-index call
is issued at `15:54:56Z`, four seconds before its own revision receives traffic.** The endpoint it
calls cannot answer until the revision it is running in is routable. R3's readiness probe closes this
by making route-index readiness a precondition of promotion; R1 makes the question moot by removing
the dependency on ingress entirely.

---

## 4. Design goals

| # | Goal | Rationale |
|---|---|---|
| G1 | The worker must never fetch its own process's data over HTTP | Removes the entire failure class |
| G2 | A cold start must reach first tone as soon as static data is parsed, never later | Bounds recovery by real work, not by a timer |
| G3 | An empty route index must never be reported as healthy | Ends silent degradation |
| G4 | Recovery must not depend on a human or a redeploy | Aug 28 recovered by luck of a 24 h timer |
| G5 | The failure must be visible within minutes | Two days elapsed before diagnosis |
| G6 | No change to wire format, SignalR contract, or client | Keeps this deployable on its own |

### Non-goals

- Splitting the worker into its own container app. That would make the HTTP call legitimate but
  reintroduces the ordering problem across a real network boundary, and contradicts the current
  single-container topology. Out of scope; see §8.
- Persisting the route index to durable storage across restarts. Attractive, larger change; §8.
- The Philadelphia zero-tone condition. Genuinely separate; §7.

---

## 5. Proposed design

### 5.1 R1 — Replace the self-call with a direct in-process read *(primary fix)*

Introduce an interface owned by the WebAPI side and consumed by the worker, so the worker reads
shapes from the same singleton the loader writes to.

```csharp
public interface IRouteShapeSource
{
    /// Completes when static data is loaded and shapes are available.
    /// Never returns an empty collection: it waits instead.
    Task<IReadOnlyList<RouteShapeFeature>> GetAllShapesAsync(CancellationToken ct);

    /// Signalled every time the loader completes a refresh cycle.
    Task WaitForNextRefreshAsync(CancellationToken ct);
}
```

Backed by the existing `IKeyValueRepository<string>` singleton plus a `TaskCompletionSource` that
`GtfsStaticLoader` completes when it sets `ReadyKey`. `InitializeRouteIndexAsync` becomes:

```csharp
var shapes = await routeShapeSource.GetAllShapesAsync(ct);   // waits, does not poll or fail
(_routeIndex, _routeMode, _routeCumDist, _routeTriggerPoints) = BuildRouteIndex(shapes);
```

This deletes the retry loop, the backoff, the base-address configuration, the `503` path, the TLS
handshake, and the JSON serialize/deserialize round trip that currently exists solely to move data
between two objects in one process. The worker starts producing tones the instant the loader
finishes — which is the earliest moment it is physically possible to do so.

Note this also removes a real cost: the current path serializes the full shape catalogue for all
seven cities to JSON, ships it over TLS, and deserializes it, on every startup and every refresh.

### 5.2 R2 — Make the refresh timer a subscription, not a poll

Replace the 24-hour `PeriodicTimer` in `RefreshRouteIndexAsync` with a wait on
`WaitForNextRefreshAsync`. `GtfsStaticLoader` already refreshes on its own configured
`Gtfs:StaticRefreshHours` cadence and already skips the swap when an upstream fetch yields zero
routes. Two independent 24-hour timers polling the same data become one timer and one subscriber,
and the worker's index can no longer drift stale relative to the loader's.

### 5.3 R3 — Treat an empty index as unhealthy *(defense in depth)*

Even with R1, add a health check so this class of fault can never again be silent:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<RouteIndexHealthCheck>("route-index", tags: ["ready"]);
```

Reporting `Unhealthy` while the index is empty and `Healthy` once populated. Wire it to a
Container Apps **readiness** probe, not a liveness probe — an empty index must withhold traffic and
block revision promotion, but must not kill a process that is legitimately still loading. Allow a
generous `initialDelaySeconds` / `failureThreshold` sized above worst-case GTFS parse time so a
normal cold start is never restarted mid-load.

This directly implements incident-report items P0-2, P0-3 and P1-2, and gives the deployment gate
that §3.4 identifies as missing.

### 5.4 R4 — Emit the structured events the log path already defines

`StructuredLogEventName` already carries `RouteIndexUnavailable` and `WorkerCycleRecovered`, and
`ROUTE_INDEX_UNAVAILABLE` is already a defined reason code. What is missing is the load-attempt
detail. Add, on the route-index load path:

- `RouteIndexLoadFailed` — with `ExceptionType` and attempt number (already logged as free text;
  promote it to a structured event so it is queryable by field, not by `contains`).
- `RouteIndexLoaded` — with city count, route count, and duration.
- `DeploymentRevision` on both, populated from `CONTAINER_APP_REVISION`.

The report's P1-7 asks to correlate a failure window with revision and endpoint. That correlation was
impossible during this investigation precisely because the structured records carried no revision.
`RevisionName` happened to be available as a table column here, but the event payload should not
depend on that.

Also **de-duplicate the per-city skip warning.** Seven cities × one warning per 10-second tick is
~60 warnings/minute of identical text — 42 of the 43 rows in one query window were this line. It
buries the one row that matters (§2.1) and inflates ingestion cost. Log it once per transition into
the unavailable state plus a periodic summary, not once per city per tick.

**This generalizes beyond the route-index case, and §2.5 makes it more urgent, not less.** With the
index healthy, the steady-state log is now dominated by `DUPLICATE_FEED` warnings: in the 18:20–18:24
window, 25 of 52 `CityCycleAnomaly` rows were `Outcome=Failed` `DUPLICATE_FEED` — a permanent,
expected consequence of polling a 20-second feed every 10 seconds. Emitting these at `Warning` in
perpetuity means the operational log's most common line describes correct behavior.

Two changes follow:

- **`DUPLICATE_FEED` should not be a `Warning`.** A tick that correctly declines to re-process an
  unchanged feed is a normal outcome. Demote it to `Information` (or suppress it when the previous
  tick for that city already reported it), reserving `Warning` for reasons that indicate something
  is actually wrong.
- **The recovery-marker fan-out should be bounded.** `Worker.cs:411` loops over every value of
  `StructuredLogReasonCode`, emitting a `Succeeded` `CityCycleAnomaly` per missing-tone reason on
  each productive tick; `EmitRecovery` suppresses all but the active one, so the observable cost is
  small today, but the shape is fragile — adding a reason code silently adds emit calls to the hot
  path. Emit recovery for the reason that was actually active instead of iterating the enum.

A better long-run fix for the underlying cause is to make the tick cadence adaptive per city — skip
work when the feed header timestamp has not advanced — so the duplicate work is never done rather
than done and then logged. That is a worker-scheduling change beyond this document's scope, but it is
the change that would remove both the wasted fetch and the log line.

### 5.5 R5 — Alert on the condition

Add an alert on the report's own verification expression:

```promql
({__name__="transitjazz_worker_city_route_index"} == 0)
  and on (transit_city) ({__name__="transitjazz_worker_city_input_fetch_ok_ratio"} == 1)
```

firing after ~5 minutes sustained. The semantics — "the worker is healthy and receiving data but
producing nothing" — are exactly the silent-degradation signature, and no existing alert covers it.

Pair it with a log-based alert on `RouteIndexLoadFailed` so the two observability paths corroborate
rather than requiring a human to join them by hand, as was necessary here.

---

## 6. Implementation plan

Ordered so that each step is independently shippable and the highest-value change lands first.

| Step | Change | Files | Risk |
|---|---|---|---|
| 1 | `IRouteShapeSource` + in-process implementation; loader signals readiness | `WebAPI/GtfsStatic/` | Low |
| 2 | `InitializeRouteIndexAsync` awaits the source; delete retry loop and `RouteShapeApi` client | `Worker.cs`, `TransitDataWorker/Program.cs` | Low |
| 3 | `RefreshRouteIndexAsync` subscribes instead of polling | `Worker.cs` | Low |
| 4 | `RouteIndexHealthCheck` + readiness probe in bicep | `WebAPI/`, `bicep/modules/containerApp.bicep` | Medium — probe tuning |
| 5 | Structured load events + revision tag; de-duplicate skip warning | `Logging/`, `Worker.cs` | Low |
| 6 | Grafana alert rules | `observability/grafana/` | Low |

Steps 1–3 are the fix. Steps 4–6 ensure that if something in this class recurs, it is loud.

> **Ordering note (revised after §2.6).** An earlier draft suggested promoting step 5 ahead of step 1,
> on the grounds that load-attempt telemetry was needed to explain the 45-second recovery. §2.6
> answered that from system logs instead, so **that argument no longer applies and the original
> ordering stands**: steps 1–3 first.
>
> Step 5 still carries independent value — it supplies the per-attempt timing and the GTFS parse
> duration that §10.1 needs before step 4's probe can be tuned, and it makes the load path queryable
> by field rather than by `contains`. But it is no longer a prerequisite for deciding whether steps
> 1–3 are worth doing. §2.6 settles that: they are.

### Testing

Per the project's `util-testing` conventions, and covering incident-report item P0-4:

- **Startup ordering:** worker constructed while the loader has not completed → it waits, does not
  fail, and builds the index as soon as the loader signals. This is the regression test for the
  exact defect; it must fail against today's code.
- **Slow load:** loader takes materially longer than the old 30-second budget → worker still
  succeeds. Encodes the fact that the old budget was structurally too short.
- **Refresh propagation:** loader publishes a second generation → worker's index reflects it without
  waiting 24 hours.
- **Zero-route upstream:** loader keeps last-good data → worker's populated index is never replaced
  by an empty one. Guards the existing FR-005 behavior.
- **Health check:** `Unhealthy` while empty, `Healthy` once populated.

### Verification after deploy

The condition in §5.5 must be false within minutes of a cold start. Concretely, on the next
revision: `WorkerStarted` → `RouteIndexLoaded` within one static-load duration, no
`RouteIndexLoadFailed`, and nonzero `transitjazz_worker_city_tones_emitted` for all cities except
any covered by §7.

A live all-city view over the last three hours, with the `transit_city` variable set to all seven and
a 10-second refresh:

```
https://gallantpuffin3113.grafana.net/d/transitjazz-worker-overview/worker-app-metrics?from=now-3h&to=now&timezone=utc&var-transit_city=$__all&refresh=10s
```

Three refinements from §2.5, since a naive check will now produce false alarms:

- **Expect `ALL_CROSSINGS_SUPPRESSED` on the first productive tick of every city.** Every vehicle is
  first-seen at cold start, so there is no prior position to measure against. Observed on all seven
  cities at `15:55:41–43Z`. It clears on the following tick and is not a regression.
- **Do not treat zero tones on a single tick as failure.** Cities on ~20 s feeds legitimately emit
  zero on alternating ticks. Verify against a window of at least four cycles per city, or filter to
  `ReasonCode != DUPLICATE_FEED`.
- **Expect Denver silent, not Philadelphia.** Per the corrected §7.

A concrete post-deploy check, matching what was run for §2.5 — all seven cities appearing with
`VehiclesProcessed > 0` within ~60 s of `WorkerStarted`, and no `RouteIndexUnavailable` thereafter:

```kusto
ContainerAppConsoleLogs
| where TimeGenerated between (datetime(<start>) .. datetime(<start+15m>))
| extend S = parse_json(Log).State
| extend EventName = tostring(S.EventName), City = tostring(S.City),
         ReasonCode = tostring(S.ReasonCode), Outcome = tostring(S.Outcome),
         Tones = toint(S.TonesEmitted), Veh = toint(S.VehiclesProcessed)
| where EventName in ('WorkerStarted', 'RouteIndexUnavailable', 'CityCycleAnomaly')
| project TimeGenerated, EventName, City, ReasonCode, Outcome, Tones, Veh
| order by TimeGenerated asc
| take 100
```

---

## 7. Philadelphia — resolved; Denver is the open zero-tone case

**This section's original premise is falsified.** It stated that Philadelphia "processed 351–382
vehicles while emitting zero tones" and warned that a post-fix verification run would show
Philadelphia still silent. Direct log evidence from `18:20:00Z`–`18:24:00Z` shows the opposite:

| Cycle (UTC) | Vehicles | Tones |
|---|---:|---:|
| 18:20:11 | 351 | 83 |
| 18:21:11 | 349 | 112 |
| 18:22:11 | 349 | 56 |
| 18:23:11 | 347 | 102 |
| 18:23:41 | 348 | 68 |

Philadelphia emitted **624 tones across eight productive cycles** in four minutes, publishing
successfully each time (`PublishAttempted=true`, `PublishSucceeded=true`, `BatchWireBytes≈11 KB`). Its
zero-tone ticks are the intervening `DUPLICATE_FEED` cycles explained in §2.5 — the 10 s worker tick
against a ~20 s publisher — and Toronto, Denver and (intermittently) Atlanta show the same
alternation. It is not Philadelphia-specific and it is not a reconciliation fault.

The Aug 28–29 observation that prompted this section was almost certainly the same alternation
sampled at a moment that happened to land on a duplicate tick, on a dashboard whose 15-minute
resolution cannot distinguish "zero this instant" from "zero always."

**The reconciliation investigation this section called for is therefore not needed for
Philadelphia.** The `skippedNoJoinKey` / `skippedUnknownRoute` / trigger-spacing questions should be
retargeted at **Denver**, which is the city now showing genuine zero output: same 20-second
alternation, but `VehiclesProcessed = 0` on *every* tick, with no `CityInputFailed`,
`CityInputEmpty`, or `CityInputPartial` event in the surrounding 24 minutes. Denver's fetch succeeds
and returns a feed from which zero usable vehicle records are extracted — a feed-content or parsing
condition, tracked separately in
[2026-08-30-denver-no-tones.md](incident%20reports/2026-08-30-denver-no-tones.md).

Retained from the original section, corrected: a post-fix verification run will show **six** cities
producing tones and **Denver** silent. That is expected and is not evidence that this fix failed.

---

## 8. Alternatives considered

**Increase the retry budget / add continuous backoff.** This is the incident report's P0-1. It would
have prevented the 24-hour outage, and it is strictly better than today. But it treats a call that
should not exist as a call that should be retried harder: the worker would keep polling its own
public FQDN, still traversing ingress and TLS to read a local dictionary, and the correct retry
duration would remain an unbounded guess against GTFS parse time. R1 removes the question. If R1 is
rejected, R2's continuous backoff becomes mandatory rather than optional.

**Order the hosted services so the loader completes first.** `IHostedService` instances start
sequentially, so making `GtfsStaticLoader` block in `StartAsync` until loaded would work. Rejected:
it delays the entire host — including ingress readiness and the SignalR hub — behind a multi-minute
download, and any upstream GTFS outage would prevent the app from starting at all. R1 achieves the
ordering without coupling host startup to an external download.

**Split the worker into its own container app.** Makes the HTTP call architecturally honest and
allows independent scaling. Rejected for now: it reintroduces the same startup race across a real
network boundary — where it would be genuinely hard rather than trivially avoidable — and expands a
bug fix into a topology migration. Worth revisiting on its own merits, at which point
`IRouteShapeSource` becomes the seam where an HTTP implementation is substituted.

**Persist the route index to blob storage.** Would let a cold start recover from last-good data even
during an upstream GTFS outage. Complementary rather than alternative, and a reasonable follow-up
once R1 lands.

---

## 9. Corrective-action mapping

How this design relates to the incident report's list:

| Report item | Disposition |
|---|---|
| P0-1 continuous bounded backoff | **Superseded** by R1 — the call is deleted. Becomes mandatory only if R1 is rejected. §2.6 sharpens this: the existing budget won by ~0.4 s on `0000165`, so "make the budget bigger" is a bet on infrastructure timing, not a fix. |
| P0-2 alert on zero index while healthy | **Adopted** as R5 |
| P0-3 empty index is not healthy | **Adopted** as R3 |
| P0-4 tests for startup-failure recovery | **Adopted**, §6 |
| P0-5 treat Aug 30 as unresolved | **Resolved** — `0000164` never recovered and `0000165` reproduced the startup failure (§2.1–2.2), but `0000165` recovered in ~45 s via its own retry budget once ingress went live (§2.6). Not a redeployment. Customer impact of the recurrence is bounded at well under a minute. |
| P1-1 load state / attempt / failure metrics | **Adopted** as R4 |
| P1-2 readiness check for `/gtfs/routes/shapes` | **Adopted** as R3, at the data layer rather than the HTTP layer |
| P1-3 correlate startup with WebAPI availability | **Resolved** — they are the same process; §3.1 |
| P1-4 preserve structured load errors | **Adopted** as R4 |
| P1-5 verify each revision post-deploy | **Adopted** as R3's readiness gate, automated rather than manual |
| P1-6 Philadelphia investigation | **Closed — not a defect.** Philadelphia emits tones normally (624 in 4 min); the observed zeros were `DUPLICATE_FEED` ticks. Retarget the reconciliation investigation at **Denver**. §7 |
| P1-7 correlate Aug 30 window with revision | **Done**, §2.1; revision tagging added in R4. Note `RevisionName` is available as a table column, which is how §2.1 and §2.5 attribute events to `0000164`/`0000165`; R4 still wants it in the payload. |

---

## 10. Open questions

1. **Probe tuning (R3).** What is the true worst-case GTFS static load duration across all seven
   cities, including a slow upstream? The readiness probe's failure threshold must exceed it, and
   `RouteIndexLoaded` duration from R4 supplies the measurement. Ship R4 before tightening R3.
2. **Refresh cadence.** `Gtfs:StaticRefreshHours` and the worker's 24-hour timer are configured
   independently today. R2 collapses them; confirm the loader's cadence is the intended one.
3. **`WebApi:BaseUrl` after R1.** Is it still needed for any other consumer, or can the setting and
   the `RouteShapeApi` client registration be removed outright?
4. ~~**What actually repopulated the index on `0000165`?**~~ **Answered — see §2.6.** Ingress shifted
   traffic to `0000165` at `15:55:00.431Z`, 447 ms after the first attempt failed; a later attempt in
   the same five-attempt budget then succeeded. Not a redeployment: no revision was created after
   `0000165`. The retry budget is *marginal*, not adequate — which argues **for** R1, since the
   margin is set by image-pull and health-check timing that the application does not control.

   The residual question is narrower: **how much slack does the budget actually have?** Five attempts
   with `2^n` backoff is ~30 s; ingress arrived at ~4 s. That looks comfortable until GTFS parse time
   is included, since the endpoint returns `503` until the loader finishes. R4's `RouteIndexLoaded`
   duration is what measures it — and it is the same measurement §10.1 needs for probe tuning.
5. **Worker tick cadence versus feed publish cadence.** §2.5 shows the 10 s tick doing redundant work
   against ~20 s feeds for at least Philadelphia, Toronto and Denver. Should `CycleIntervalSeconds`
   become per-city, or should the tick skip early when the feed header timestamp has not advanced?
   The latter is strictly better — it avoids the fetch as well as the processing — but it needs the
   feed timestamp before the fetch completes, which not every adapter exposes.
6. **Is Denver's zero-vehicle condition new?** It shows a successful fetch yielding zero usable
   records with no input-failure event. Whether this predates `0000165` or arrived with it is not
   established here and would narrow the search considerably.
