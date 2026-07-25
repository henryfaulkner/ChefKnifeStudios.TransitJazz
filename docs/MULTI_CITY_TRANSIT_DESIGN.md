# Multi-City Transit Targets — Feature Specification & Decision Record

**Status:** Implementation complete (2026-06-27)
**Author:** Henry Faulkner
**Date:** 2026-06-26
**Source:** `grill-me` design interview (8 questions resolved)
**Reference city for examples:** WMATA (Washington DC) — see
[`WMATA_GTFS_COMPATIBILITY.md`](./WMATA_GTFS_COMPATIBILITY.md)

---

## 1. Goal

Extend TransitJazz from a single hardcoded agency (MARTA / Atlanta) to **N transit
cities**, such that:

- **The common case is free.** Adding a city whose feeds are standard GTFS-RT protobuf
  (e.g. WMATA buses) requires a **config entry + a secret — no C# code**.
- **The exceptional case is isolated.** A city with a bespoke feed (e.g. MARTA's
  proprietary JSON rail API) requires **exactly one new class**, and that class touches
  nothing else.
- **There is no drift.** Adding cities must never edit the worker's processing loop, the
  SignalR hub, the client, or any other city's code. The shared pipeline is written once.
- **Cloud cost scales sub-linearly.** N cities run in **one worker process / one Azure
  Container App**, not N containers.

This document is the authoritative specification. Each decision below is numbered to its
originating design question (Q1–Q8) and states the choice, the rejected alternatives, and
the rationale.

---

## 2. Current Architecture (single-city baseline)

Grounded against current source, the MARTA-only flow is:

```
┌────────────────────┐   InvokeAsync("PublishBatch", batch)   ┌─────────────────────────┐
│  TransitDataWorker │ ─────────────────────────────────────► │ WorkerTransitHub        │
│  (BackgroundService│   (worker is a SignalR *client*)        │  - caches batch         │
│   SignalR client)  │                                         │  - relays to TransitHub │
└────────────────────┘                                         └───────────┬─────────────┘
        │ fetches                                                          │ Clients.All
        │   • _gtfsRtUrl (hardcoded MARTA bus protobuf)                    │ .SendAsync(
        │   • IRailRealtimeAdapter (MARTA JSON → GTFS-RT entities)         │   "ReceiveBatch")
        │ snaps against _routeIndex (one flat dict, route_short_name key)  ▼
        │ pulls index from WebAPI GET /gtfs/routes/shapes        ┌─────────────────────────┐
        ▼                                                        │ TransitHub (clients)    │
   PostEvent → Logging/ (Parquet → Azure Blob, MARTA telemetry) └───────────┬─────────────┘
                                                                            │ "ReceiveBatch"
                                                                            ▼
                                                                  ┌─────────────────────┐
                                                                  │ Blazor WASM client  │
                                                                  │  /hubs/transit      │
                                                                  └─────────────────────┘
```

Route shapes are served by the WebAPI from an in-memory `IKeyValueRepository<string>`,
seeded by `GtfsStaticLoader` from **one hardcoded MARTA static zip**.

**The load-bearing single-city assumptions:**

| Assumption | Location | Why it breaks under N cities |
|---|---|---|
| `Clients.All` broadcasts to everyone | `WorkerTransitHub.PublishBatch` | A DC user would receive Atlanta's 762 buses. |
| One flat route index keyed by `route_short_name` | `Worker.BuildRouteIndex` | Route `1` (ATL) collides with route `1` (DC); rail letters `B/G/R` collide. |
| One vehicle cache dictionary | `LastBatchCache._vehicles` | DC trains and ATL buses share one vehicle map. |
| Hardcoded `_gtfsRtUrl` + single `IRailRealtimeAdapter` singleton | `Worker` / `Program.cs` | One city's feed shape baked into the worker. |
| KV store keyed by bare `routeId`; one static zip | `GtfsStaticLoader`, `GtfsEndpoints` | Same key collision on the server side. |
| Client has no city concept | `SignalRNotificationService` | Client can't scope what it receives or fetches. |

---

## 3. Design Spine — The Anti-Drift Mechanism

> **Decision (Q3):** The mechanism that prevents drift is an **`ITransitCity` strategy
> interface — one implementation per city. The worker loop NEVER branches on a city
> name.** Process/deployment topology is a *separate, reversible* decision layered on top.

Drift comes from per-city logic living in branches (`if (city == "marta") … else if
(city == "wmata") …`), **not** from how many processes run. Copying the whole worker into
N containers would make drift *worse* — the snapping logic forks N times and diverges
silently. The fix is one contract the compiler enforces:

```csharp
public interface ITransitCity
{
    /// SignalR group key, KV-store prefix, URL param, telemetry partition. Lowercase, stable.
    string Name { get; }

    /// Returns this city's COMPLETE, NORMALIZED live feed: bus + rail already merged,
    /// route_ids already remapped to match the static route index. The loop never knows
    /// how this was assembled (one feed? three? a JSON adapter? a color map?).
    Task<FeedMessage> FetchVehiclesAsync(CancellationToken ct);

    /// Capability flag — does this city emit snap/lerp/cycle telemetry? (MARTA-only today.)
    bool EmitsTelemetry { get; }
}
```

The worker loop, **city-agnostic forever**:

```csharp
foreach (var city in _cities)            // injected IEnumerable<ITransitCity>
{
    try
    {
        var feed  = await city.FetchVehiclesAsync(ct);
        var index = _routeIndex[city.Name];                 // per-city index (Q4)
        var batch = Reconcile(feed, index, city.EmitsTelemetry);
        await _publisher.PublishBatchAsync(city.Name, batch, ct);   // city param (Q2)
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "City {City} tick failed; other cities unaffected", city.Name);
    }
}
```

The loop branches only on **what a city declares** (`EmitsTelemetry`) or **what a city
returns** (a normalized `FeedMessage`). It never names a city. Adding a city cannot edit
this loop.

---

## 4. Decision Record

### Q1 — Client fan-out: per-city SignalR Group

**Decision:** Each client joins a **SignalR Group named for its city**. The relay sends to
`Clients.Group(city)` instead of `Clients.All`.

**Rejected:**
- *One hub endpoint per city* (`/hubs/transit/wmata`): more boilerplate, no gain — SignalR
  groups exist for exactly this routing.
- *Client-side filtering* (send all, drop others): ships every city's bandwidth to every
  browser; wastes the WASM client's CPU; does not scale.

**Rationale:** Groups are the textbook mechanism and the only option that stops cross-city
bandwidth **at the server**. One hub, one process, one routing key.

**Implementation:**
- `TransitHub` gains `JoinCity(string city)` → `Groups.AddToGroupAsync(Context.ConnectionId, city)`.
- `WorkerTransitHub.PublishBatch` relays `_clientHub.Clients.Group(city).SendAsync("ReceiveBatch", batch)`.

---

### Q2 — City tag transport: a `PublishBatch` parameter

**Decision:** The city travels as a **method parameter** on the publish call:
`PublishBatch(string city, List<EventEnvelope> batch)`. `EventEnvelope` stays city-free.

**Rejected:**
- *Field on `EventEnvelope`*: per-vehicle redundancy (every envelope in a batch shares the
  city), bloats the wire, and city is a property of the *producing worker*, not of an event.
- *New `CityBatch` wrapper record*: more type churn through client handler, cache, and tests
  for no benefit over a parameter.

**Rationale:** City is a **transport routing key**, not domain data. One string on the
existing invoke.

**Implementation — per-city cache (important nuance):** `LastBatchCache` is **not** a
"last batch" store — it is a **per-vehicle upsert map** (`Dictionary<vehicleId, record>`)
used to replay current vehicle positions to late-joining clients. Under N cities this map
must be **scoped per city**, or DC trains and ATL buses share one vehicle dictionary.

- `ILastBatchCache` becomes keyed by city: `Set(string city, batch)` / `Current(string city)`,
  backed by `Dictionary<string, LastBatchCache>` (or the inner dict keyed by `(city, vehicleId)`).
- On `JoinCity(city)`, the hub immediately sends that client `cache.Current(city)` so it sees
  vehicles within milliseconds instead of waiting up to 10 s for the next worker tick.

---

### Q3 — Worker topology: one process, config-driven city loop

**Decision:** **One worker process** iterates all registered `ITransitCity` instances on
the existing 10-second tick, **with per-city try/catch fault isolation**. (Mechanism = the
`ITransitCity` interface of §3; this Q fixes the *deployment* shape.)

**Rejected:**
- *N hosted `Worker` instances in one process*: same container, more ceremony, N SignalR
  connections, no payoff.
- *One container per city*: N× idle cost, N× cold starts, N× scaling config — and worse,
  without the §3 interface it forks the shared logic. YAGNI: no city needs independent
  scaling or deploy cadence today.

**Rationale:** Per-city work is I/O-bound HTTP every 10 s (small protobuf payloads,
sequential `await`s). One process handles a dozen cities trivially. Per-city try/catch
gives most of the fault isolation of separate containers at none of the cost.

**Reversibility (explicit):** Promoting one heavy city to its own container later is a
**config split, not a rewrite** — the same `ITransitCity` classes are registered, and each
container filters to its city: `_cities.Where(c => c.Name == Environment.GetEnvironmentVariable("CITY"))`.
The interface (§3) makes A and C the *same code* with a different filter and replica count.

---

### Q4 — Keying: `(city, routeJoinKey)` is universal

**Decision:** The pair **`(city, routeJoinKey)`** is the real key everywhere in the Worker's
spatial index. `route_short_name` is **never** assumed globally unique again.

- **Worker index:** `Dictionary<string /*city*/, IReadOnlyDictionary<string /*routeJoinKey*/, RoutePoint[]>>`,
  **owned by the loop** (not by `ITransitCity` — index-building is shared mechanics
  parameterized by city; `ITransitCity` stays focused on live-vehicle fetch+normalize).
- **Server KV store:** keys become **`{city}:{routeId}`** (e.g. `marta:1`, `wmata:B`) — this store
  uses the true GTFS static `route_id`, distinct from the Worker's `routeJoinKey`.
- **Shape contract:** add a **`City` property to `RouteShapeProperties`** so the worker can
  partition the `/gtfs/routes/shapes` response into per-city indexes without N HTTP calls.
- **Static load:** one `GtfsStaticLoader` **loops the city registry**, seeding `{city}:{routeId}`.

**Rejected:** *Index owned by each `ITransitCity`* — would push the shared WebAPI round-trip
and index-build into every city class, duplicating wiring around shared infra.

**Free consequence (client):** The client already fetches shapes to render pills + map
lines. It now fetches only its joined city's shapes via the same **`?city=` filter** on
`/gtfs/routes/shapes`; the new `RouteShapeProperties.City` flows naturally into the client's
`RouteFilterViewModel`. No new decision — flagged so it doesn't surprise implementers.

---

### Q5 — Client city source of truth: URL param, default MARTA

**Decision:** The client reads its city from the **URL / query param** (`?city=wmata` or a
path segment `/wmata`), defaulting to **`marta`** when absent. This single value feeds both
the SignalR `JoinCity` call and the `?city=` shape fetch.

**Rejected:**
- *Settings-blade selector*: the 016 Settings Blade is pure-boolean reflection; a dropdown
  breaks that model (this is exactly why 016 deferred the Language selector). More UI work.
- *Geolocation*: a trap for a portfolio/demo app whose point is showing DC **and** Atlanta
  to a viewer sitting anywhere. Permission friction, fragile, no out-of-region browsing.

**Rationale:** Laziest thing that fully works — one query-string read at startup, every city
becomes a shareable/bookmarkable link, zero new UI. A city *switcher* can layer on later via
navigation; the URL stays the source of truth.

---

### Q6 — Telemetry: keep MARTA-only via a capability flag

**Decision:** Telemetry (the `Logging/` Parquet → Azure Blob sidecar, features 013/014)
**stays, but emits for MARTA only**, expressed as `ITransitCity.EmitsTelemetry`
(`true` for MARTA, `false` for all others).

**Rejected:**
- *Remove entirely*: it is still actively used via the `mj-data-explorer` skill to study
  worker behavior; deleting it loses a working diagnostic workflow.
- *Make it city-aware (per-city `city` column + partition)*: most work and most cloud cost
  (N× blob writes/storage) for instrumentation we don't need replicated.

**Rationale:** Keep the diagnostic that earns its keep; do not pay to expand it. **Critically,
this respects the §3 anti-drift rule** — the `PostEvent` call sites check the *declared
capability* `city.EmitsTelemetry`, never a city name. No per-city blob-write multiplier is
introduced.

---

### Q7 — Rail strategy (keystone): the city returns a fully-normalized feed

**Decision:** `ITransitCity.FetchVehiclesAsync` returns a **complete, normalized, merged
`FeedMessage`** — bus + rail already combined, `route_id`s already remapped to match the
static index. Every city-shaped difference is **sealed inside the city implementation**.

**Rejected:** *Loop orchestrates, city supplies parts* (`BusUrl`, `RailAdapter?`,
`RailRouteMap?`) — re-grows city-shaped knowledge in the loop (the exact branching being
eliminated, merely relocated).

**Rationale:** This is the payoff of the whole pattern. The only thing that varies between
cities — how you obtain and normalize live vehicles — is fully encapsulated; the identical
part (snap → batch → publish to group) is the loop. Concretely:

| City | Inside `FetchVehiclesAsync` |
|---|---|
| **MARTA** | Fetch bus protobuf + call the **JSON `RailRealtimeAdapter`** (now an internal detail of `MartaCity`, no longer a global singleton); merge; return. |
| **WMATA** | Fetch bus protobuf + rail protobuf; apply the **6-entry `BLUE→B` route_id map**; merge; return. No adapter needed. |
| **No-rail city** | Fetch one feed; return. |

The loop never asks "does this city have rail?". The existing global `IRailRealtimeAdapter`
DI registration is **retired** and composed into `MartaCity`.

---

### Q8 — Config, registration & secrets: `Cities:` array + generic class + named exceptions

**Decision:** A pragmatic blend leaning config-first.

- Flat `Marta:` config → a **`Cities:` array** the registry loops.
- The registry instantiates a **generic config-driven `GtfsRtCity : ITransitCity`** by
  default. This covers WMATA buses and any future standard-GTFS-RT agency **with zero new
  code**; WMATA's 6-entry rail map rides as **optional `RailRouteIdMap` config**.
- **Named `ITransitCity` implementations only for genuinely bespoke feeds** — e.g.
  `MartaCity` for the JSON rail API. The registry uses a named impl when one is registered
  for that city name, else the generic `GtfsRtCity`.

**Secrets:** WMATA's `api_key` is a **Container Apps secret referenced by env-var name**
(`ApiKeyEnvVar` in config), **never committed to `appsettings.json`** — consistent with the
feature-013 `DefaultAzureCredential` no-committed-key stance.

**Rationale — the sustainability test, answered:**

| Adding a city like… | Cost |
|---|---|
| WMATA (standard GTFS-RT) | **1 config entry + 1 CA secret. Zero C#.** |
| MARTA (bespoke JSON rail) | **1 isolated `ITransitCity` class.** |
| Either | **0 changes** to the loop, hub, client, or any other city. |

**Config shape:**

```jsonc
"Cities": [
  {
    "Name": "marta",
    "GtfsRtUrls": [ "https://gtfs-rt.itsmarta.com/.../vehiclepositions.pb" ],
    "RailRealtime": { "BaseUrl": "https://developerservices.itsmarta.com:18096/...", "Enabled": true },
    "StaticZipUrls": [ "https://.../marta-gtfs.zip" ],
    "EmitsTelemetry": true
    // resolved to the named MartaCity impl (bespoke JSON rail)
  },
  {
    "Name": "wmata",
    "GtfsRtUrls": [
      "https://api.wmata.com/gtfs/bus-gtfsrt-vehiclepositions.pb",
      "https://api.wmata.com/gtfs/rail-gtfsrt-vehiclepositions.pb"
    ],
    "StaticZipUrls": [
      "https://api.wmata.com/gtfs/bus-gtfs-static.zip",
      "https://api.wmata.com/gtfs/rail-gtfs-static.zip"
    ],
    "ApiKeyEnvVar": "WMATA_API_KEY",
    "RailRouteIdMap": { "BLUE":"B","GREEN":"G","ORANGE":"O","RED":"R","SILVER":"S","YELLOW":"Y" },
    "EmitsTelemetry": false
    // resolved to the generic GtfsRtCity — no code
  }
]
```

**Deploy:** stays **one Azure Container App / one worker process** (per Q3). Adding a city
adds config + (if keyed) a secret; it does not add infrastructure.

---

## 5. Affected Components (implementation map)

| Layer | File / area | Change |
|---|---|---|
| **Shared** | `GtfsData/RouteShapeFeature.cs` (`RouteShapeProperties`) | Add `string City`. |
| **Shared** | `ITransitHubPublisher` | `PublishBatchAsync(string city, …)`. |
| **Worker** | new `Cities/ITransitCity.cs`, `GtfsRtCity.cs`, `MartaCity.cs` | Strategy interface + generic impl + MARTA bespoke impl. |
| **Worker** | new `Cities/CityConfig.cs` + registry in `Program.cs` | Bind `Cities:`; register named impls else generic. |
| **Worker** | `Worker.cs` | Loop over `IEnumerable<ITransitCity>`; per-city try/catch; `_routeIndex` → per-city; `PostEvent` gated by `city.EmitsTelemetry`; retire hardcoded `_gtfsRtUrl` + global rail adapter. |
| **Worker** | `SignalRHubPublisher.cs` | Forward `city` to `InvokeAsync("PublishBatch", city, batch)`. |
| **WebAPI** | `SignalR/TransitHub.cs` | Add `JoinCity(string)`; on join, replay `cache.Current(city)`. |
| **WebAPI** | `SignalR/WorkerTransitHub.cs` | `PublishBatch(city, batch)` → cache per city → `Clients.Group(city).SendAsync(...)`. |
| **WebAPI** | `SignalR/ILastBatchCache.cs` | Key cache by city. |
| **WebAPI** | `GtfsStatic/GtfsStaticLoader.cs` | Loop city registry; load multi-zip per city; seed `{city}:{routeId}`; set `City` on shapes. |
| **WebAPI** | `EndpointGroups/GtfsEndpoints.cs` | `/gtfs/routes/shapes` accepts `?city=`; keys are `{city}:{routeId}`. |
| **Client** | `SignalRNotificationService.cs` | Read city from URL; call `JoinCity(city)` after connect. |
| **Client** | shape-fetch service + `RouteFilterViewModel` | Pass `?city=`; consume `RouteShapeProperties.City`. |
| **Config** | worker `appsettings*.json` | Replace flat `Marta:` with `Cities:` array. WMATA key via CA secret / env var. |

---

## 6. Constitution / Principle Notes

- **Principle VI (GTFS ID Mapping):** `(city, routeJoinKey)` keying generalizes the existing
  `route_short_name` join (see `RouteShapeProperties.JoinKey`); per-city `RailRouteIdMap` is
  config-declared, not branched.
- **Principle VII (no re-fetch of static data):** unchanged — client still re-adds layers
  after style load and never re-fetches; per-city shapes fetched once at init.
- **Principle XII (i18n):** city names are stable lowercase keys, not display strings; any
  city *label* shown in UI goes through `IStringLocalizer` (EN-only this pass, consistent
  with 015–017).
- **Security (feature-013 precedent):** no agency key committed; WMATA `api_key` lives in a
  Container Apps secret referenced by env-var name.

---

## 7. Out of Scope / Deferred

- City **switcher UI** (URL stays the source of truth; navigate to switch).
- Per-city **container isolation** (reversible via `.Where(name == CITY)` filter when a city
  needs independent scaling — not now).
- **Telemetry for non-MARTA cities** (deliberately not expanded).
- Spanish/`.es` localization for any new city labels (deferred, consistent with 015–017).

---

## 8. Suggested Implementation Order

1. **Pure refactor on MARTA, no behavior change:** introduce `ITransitCity` + `MartaCity`
   (wrapping today's bus URL + JSON rail adapter), loop-over-one-city, per-city index/cache,
   `city` param threaded through publisher → hub → client `JoinCity`. Ship; verify MARTA is
   identical end-to-end. This proves the pattern without a second city's variables.
2. **Add WMATA as config only:** `Cities:` entry + `GtfsRtCity` + `RailRouteIdMap` + CA
   secret + multi-zip in `GtfsStaticLoader`. The proof the pattern holds with zero new
   processing code.
