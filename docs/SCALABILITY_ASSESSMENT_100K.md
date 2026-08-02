# Scalability Assessment — 100,000 Concurrent Users

**Date:** 2026-07-25
**Scope:** `bicep/` infrastructure, `Server.WebAPI` (API + SignalR hub), `Server.TransitDataWorker`
**Target evaluated against:** 100,000 concurrent users
**Status:** Assessment only — no code changes made.

---

## Verdict

**The system cannot support 100,000 concurrent users. It is architecturally capped at roughly 1,000–5,000, and the binding constraint is a hard ceiling, not a tuning problem.**

The single fact that decides this: `bicep/main.bicep:214-215` sets `minReplicas: 1, maxReplicas: 1`. There is exactly one container, with `cpu: '0.5'`, `memory: '1Gi'`. Every user's SignalR connection, every REST call, the GTFS static loader, the telemetry parquet writer, and the transit data worker all live in that one half-core process. 100k users is ~200,000 concurrent users per CPU core.

Worse, `maxReplicas: 1` is not an oversight you can simply raise. It is load-bearing — the architecture *requires* it (see Finding 2).

### Caveat on the numbers

The ~200 KB/batch figure used throughout is an **estimate** derived from the 5 MB `MaximumReceiveMessageSize` ceiling (`Program.cs:50`) and known fleet sizes — it is **not a measurement**. Before committing to a design, measure actual MessagePack batch bytes per city from the telemetry pipeline. That single number sets tile granularity for Finding 1 and determines whether viewport scoping is merely important or existential.

---

## Findings, ranked by impact

### 1 — Viewport-scoped fan-out groups (replace per-city groups)

**Impact: decides whether 100k is affordable at all.**

Fan-out bandwidth is the dominant cost and constraint of the entire system, and nothing in the current design addresses it.

NYMTA carries ~5,000 vehicles. Feature 040 field-thinning + MessagePack shrank the batch, but assume a conservative ~200 KB per batch for a large city. Every 10 seconds, each connected client in that city receives one batch:

- 100k users × 200 KB = **20 GB per 10s tick = 16 Gbps sustained egress**
- Even at an optimistic 50 KB/batch: **4 Gbps**

Azure egress alone at 16 Gbps runs into six figures per month. No amount of replica scaling fixes this.

The cause is group granularity. `TransitHub.JoinCity` (`TransitHub.cs:23`) subscribes a connection to an entire city:

```csharp
await Groups.AddToGroupAsync(Context.ConnectionId, city);
```

A user looking at three blocks of Denver receives all of Denver regardless of zoom. (They do not receive NYC — groups are per-city — but the per-city granularity is still far coarser than the viewport.)

**Fix:** clients subscribe to a geohash/tile group derived from visible map bounds, not a city. Cuts payload 10–100× depending on zoom. Every other cost on this list scales down with it, which is why it ranks first.

**Touches:** `TransitHub.cs`, `WorkerTransitHub.PublishBatch` (`WorkerTransitHub.cs:27`), client join call, `LastBatchCache` keying.

---

### 2 — Extract Worker into its own Container App

**Impact: unblocks all horizontal scaling. Nothing else deploys past 1 replica until this is done.**

`WebAPI/Program.cs:161` registers the transit data worker as a hosted service inside the same process that serves the hub:

```csharp
builder.Services.AddHostedService<Worker>();
```

`Worker.ExecuteAsync` (`Worker.cs:51`) runs a 10-second `PeriodicTimer` polling every configured agency's GTFS-RT feed — 7 cities in `appsettings.json` (marta, wmata, mbta, nymta, ttc, septa, rtd).

Scale to N replicas and you get:

- N independent workers, each polling every agency feed every 10s
- N divergent crossing-baseline states — `_vehicleStateCaches` and `_crossingBaselines` are in-process `Dictionary`/`ConcurrentDictionary` fields (`Worker.cs:27`, `Worker.cs:39`) with no distributed coordination
- Every client hearing every tone N times
- Public agency feeds (MARTA/MBTA/SEPTA) hammered at N× rate — a realistic path to an IP ban before scale is ever reached

**This is the root cause of the `maxReplicas: 1` ceiling.** Worker must become its own single-replica Container App, or a leader-elected singleton, before the API can scale horizontally at all.

---

### 3 — Azure SignalR Service (Default mode)

**Impact: removes the connection ceiling.**

Two independent problems:

**No backplane.** `WorkerTransitHub.PublishBatch` (`WorkerTransitHub.cs:27`) calls:

```csharp
await _clientHub.Clients.Group(city).SendAsync(HubMethods.ReceiveBatch, batch);
```

`Clients.Group` only reaches connections held by *this* process. No Redis backplane and no Azure SignalR Service is registered in `Program.cs`. Split clients across replicas and each replica's users only hear batches from a worker that connected to that same replica.

The Bicep compensates with `stickySessions: { affinity: 'sticky' }` (`containerApp.bicep:67-69`), which correctly preserves WebSocket connection continuity but does nothing for cross-replica fan-out. Sticky sessions also actively harm scale-out: they pin load rather than distributing it.

**Per-connection memory.** ASP.NET Core SignalR is roughly 20–80 KB per WebSocket connection (buffers, transport, connection context). At 100k connections that is **2–8 GB just for idle connections**, against `memory: '1Gi'` (`main.bicep:213`). The container OOMs in the low thousands. Kestrel's default connection limit and the 0.5-CPU TLS handshake cost bite well before that.

**Fix:** Azure SignalR Service in **Default mode** — the ASP.NET Core SDK offloads connections entirely, removing connection load from the container and making `stickySessions` unnecessary.

**Sizing note:** Standard tier is 1,000 connections/unit × 100 units max = exactly 100k per resource. That is the absolute ceiling with zero headroom. Plan for Premium or multiple resources.

---

### 4 — Cache `GetAllRouteShapes` / move it to CDN

**Impact: prevents cold-start collapse.**

Every client calls this on startup (`ApplicationViewModel.cs:135`). The handler (`GtfsEndpoints.cs:100-106`) reads the entire in-memory KV store and **JSON-deserializes every shape blob into objects on every request**, then re-serializes them to the response:

```csharp
var features = allShapesResult.Value
    .Where(kvp => kvp.Key != GtfsStaticLoader.ReadyKey && ...)
    .Select(kvp => JsonSerializer.Deserialize<RouteShapeFeature>(kvp.Value, Shared.JsonOptions.Get()))
    .Where(f => f is not null)
    .ToList();
```

No result caching, no `ETag`, no output caching. This is full route geometry for a city — megabytes. At 100k users starting up, or on any deploy that reconnects the fleet, this endpoint alone performs 100k full deserialize+serialize cycles of multi-MB payloads on 0.5 CPU. It will not respond.

The data refreshes once every 24 hours (`Worker.cs:644`), so it has no business touching the API on a per-user basis.

**Fix:** precompute the serialized response once at load time and serve cached bytes with `ETag`/`Cache-Control`; better still, move static shape data to blob storage/CDN entirely. `GetAllRoutes` (`GtfsEndpoints.cs:166-173`) has the identical pattern and needs the same treatment.

---

### 5 — Right-size the container, add autoscale rules and health probes

**Impact: the actual capacity dial — useless before Finding 2.**

Current (`main.bicep:212-215`, `containerApp.bicep:105-108`):

```
cpu: '0.5'   memory: '1Gi'   minReplicas: 1   maxReplicas: 1
```

The `scale` block has **no `rules` at all** — even with `maxReplicas` raised there is no KEDA trigger to scale on.

There are also **no `probes` defined** in `containerApp.bicep` — no liveness, readiness, or startup probe. Container Apps will route traffic to a replica still building its route index (`Worker.cs:292`, up to 5 retries with exponential backoff) and returning 503s.

**Target:** ~2 CPU / 4 GB across 20–50 replicas, KEDA autoscale on concurrent connections or CPU, plus readiness gating on route-index readiness.

---

### 6 — Fix `LastBatchCache` lock contention

**Impact: survives connection storms.**

`LastBatchCache` (`ILastBatchCache.cs:21`) guards all state with a single `lock (_gate)` covering **all cities**. Every `JoinCity` (`TransitHub.cs:24`) takes it; every worker `Set` takes it.

Inside the lock, `Current()` does LINQ `Where`/`OrderBy`/`ToList` over all recent crossings (`ILastBatchCache.cs:80-105`), and `Set` performs a full rebuild of the position envelope list (`ILastBatchCache.cs:156-164`).

With NYMTA's ~5,000-vehicle fleet, every join allocates and sorts a fresh snapshot **under a process-wide mutex**. A connection storm — a deploy reconnecting 100k clients at once — serializes every join behind that one lock while the worker is simultaneously blocked trying to `Set`. This is thundering-herd deadlock-by-contention.

**Fix:** per-city locking at minimum, plus a pre-serialized cached snapshot rather than per-join recomputation.

Ranked sixth rather than higher because Finding 1 shrinks the snapshots and Finding 3 spreads the joins across the SignalR service, softening this considerably.

---

### 7 — Static Web App off the Free SKU

**Impact: hard outage, trivial fix.**

`staticWebApp.bicep:32`:

```bicep
sku: {
  name: 'Free'
  tier: 'Free'
}
```

Free tier: 100 GB/month bandwidth cap, no SLA. A Blazor WASM payload is several MB — 100k users exhaust 100 GB within the first few thousand sessions and the app stops serving.

**Fix:** Standard tier, plus CDN in front of the WASM bundle.

---

### 8 — Turn on logging

**Impact: you currently cannot diagnose any of the above.**

`containerAppsEnvironment.bicep:15` declares `logAnalyticsCustomerId` with a default of `''`, and `main.bicep` never passes it. The module therefore resolves:

```bicep
appLogsConfiguration: empty(logAnalyticsCustomerId) ? null : { ... }
```

to `null` — **container logs go nowhere.** At any scale this is flying blind; at 100k users it is untenable, and it is a prerequisite for validating every other fix on this list.

**Fix:** provision a Log Analytics workspace, wire it through `main.bicep`, add Application Insights, and emit connection-count metrics.

---

### 9 — Multi-region and zone redundancy

**Impact: real, but premature.**

`containerAppsEnvironment.bicep:33` sets `zoneRedundant: false`, and everything deploys to a single `eastus2` region (`main.bicep:23`). No AZ failover, and every user worldwide crosses the internet to Virginia.

100k concurrent implies global distribution, so this becomes necessary — but it is a later-stage concern than the correctness blockers above.

---

### 10 — CORS and detailed errors

**Impact: hygiene, not scale.**

- `containerApp.bicep:73-74` combines `allowedHeaders: ['*']` with `allowCredentials: true`. Browsers reject this per the CORS spec, and it is a security smell.
- This duplicates an app-level policy (`Program.cs:33-42`) that lists only `localhost` origins. Two CORS layers with different rules; the ingress one is what actually applies in production. Worth reconciling.
- `Program.cs:46` sets `EnableDetailedErrors = true`, leaking server exception detail to all clients in production.

Fix whenever next in these files.

---

## Summary table

| Area | Now | Required |
|---|---|---|
| Fan-out scope | Whole city | Viewport/tile-scoped groups |
| Worker | Hosted in API, 1 replica | Separate Container App, single/leader-elected |
| SignalR | In-process groups | Azure SignalR Service (Default mode), Premium |
| Route shapes | Per-request deserialize | Precomputed bytes + CDN/blob, ETag |
| API replicas | 1 (0.5 CPU / 1 GB) | 20–50+ replicas, 2 CPU / 4 GB, KEDA autoscale |
| Snapshot cache | One global lock, recomputed per join | Per-city locks, pre-serialized |
| Client host | SWA Free | SWA Standard + CDN |
| Observability | Logs to nowhere | Log Analytics + App Insights + connection metrics |
| Region | Single, no AZ | Multi-region, zone-redundant |

---

## Sequencing

1. **Findings 2 and 3 are the gate.** Nothing deploys past 1 replica without both.
2. **Finding 1** is the highest-value change but lands cleanest after them.
3. **Findings 4 and 7** are small and independent — do them opportunistically.
4. **Finding 8** before starting to measure anything.

## What is already in good shape

The path to scale exists, and several prior decisions help:

- The app is **stateless per-connection** apart from `LastBatchCache`.
- The wire format is **already MessagePack** (`Program.cs:55`, `SignalRHubPublisher.cs:42`) with field-thinning from feature 040.
- The **city-group concept generalizes cleanly to viewport tiles** — Finding 1 is an extension of an existing mechanism, not a rewrite.
- `SignalRHubPublisher` already has **unbounded reconnect with capped backoff** (`SignalRHubPublisher.cs:133-140`) and single-flight reconnect gating.
- Worker uses **`RecyclableList`/ArrayPool** on the per-tick hot path (`Worker.cs:374`) to avoid LOH pressure.
- ACR pull, telemetry blob access, and storage RBAC all use **managed identity** — no committed secrets in the Bicep.

## Next action

Measure actual MessagePack batch bytes per city from the telemetry pipeline. That number sets tile granularity for Finding 1 and converts the estimates in this document into a real capacity and cost model.
