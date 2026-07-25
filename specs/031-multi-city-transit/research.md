# Phase 0 Research: Multi-City Transit Targets

All open questions were resolved in the source design interview
(`docs/MULTI_CITY_TRANSIT_DESIGN.md`, Q1–Q8). This file consolidates those decisions in
Decision / Rationale / Alternatives form and records the source-grounding checks done during
planning. **No NEEDS CLARIFICATION remain.**

## Source-grounding checks (planning-time verification)

Verified against current `main`/branch source so the plan reflects code, not just the doc:

- `WorkerTransitHub.PublishBatch(List<EventEnvelope>)` uses `Clients.All.SendAsync("ReceiveBatch", …)` and `_lastBatchCache.Set(batch)` — confirmed single-city fan-out + un-keyed cache.
- `LastBatchCache` is a per-vehicle upsert map (`Dictionary<vehicleId, RouteNearestPointRecord>`), **not** a last-batch store — confirms Q2's nuance: it must be keyed per city.
- `TransitHub` is an empty `Hub` — `JoinCity` is a clean add.
- `Worker.cs` holds a single `_routeIndex` (`IReadOnlyDictionary<string, RoutePoint[]>`), a hardcoded `_gtfsRtUrl`, and an injected `IRailRealtimeAdapter` singleton merged inline each tick — confirms the three single-city assumptions to retire.
- `ITransitHubPublisher.PublishBatchAsync(List<EventEnvelope>, CancellationToken)` and `SignalRHubPublisher` call `InvokeAsync("PublishBatch", batch)` — confirms the city param must thread through both.
- `RouteShapeProperties` is a record with `RouteId, RouteShortName, Color, TextColor, Mode` — adding `City` is an additive record member.
- `GtfsEndpoints` exposes `GetRouteShape`/`GetAllRouteShapes`/`GetAllRoutes` over `IKeyValueRepository<string>` keyed by bare `routeId` — confirms KV keys + endpoint need `?city=` and `{city}:{routeId}`.
- Client `SignalRNotificationService` only listens for `ReceiveBatch`; it never calls a hub method — `JoinCity` after `StartAsync` is the insertion point. Namespace root is `ChefKnifeStudios.MartaJazz`.

## Q1 — Client fan-out: per-city SignalR Group

- **Decision**: Each client joins a SignalR Group named for its city; the relay sends to `Clients.Group(city)` instead of `Clients.All`.
- **Rationale**: Groups are the textbook routing mechanism and the only option that stops cross-city bandwidth **at the server**. One hub, one process, one routing key.
- **Alternatives**: One hub endpoint per city (`/hubs/transit/wmata`) — more boilerplate, no gain. Client-side filtering (send all, drop others) — ships every city's bandwidth to every browser, wastes WASM CPU, does not scale.

## Q2 — City tag transport: a `PublishBatch` parameter

- **Decision**: City travels as a method parameter: `PublishBatch(string city, List<EventEnvelope> batch)`. `EventEnvelope` stays city-free.
- **Rationale**: City is a transport routing key, not domain data. One string on the existing invoke.
- **Cache nuance**: `LastBatchCache` is a per-vehicle upsert map; under N cities it MUST be scoped per city (`Set(city, batch)` / `Current(city)`), backed by `Dictionary<string, LastBatchCache>` (or inner key `(city, vehicleId)`). On `JoinCity(city)`, the hub immediately replays `cache.Current(city)` so the client sees vehicles within ms (FR-012/SC-007).
- **Alternatives**: Field on `EventEnvelope` — per-vehicle redundancy, bloats the wire, city belongs to the producer not the event. New `CityBatch` wrapper record — type churn through client handler/cache/tests for no benefit.

## Q3 — Worker topology: one process, config-driven city loop

- **Decision**: One worker process iterates all registered `ITransitCity` instances on the existing 10-second tick, with **per-city try/catch fault isolation**.
- **Rationale**: Per-city work is I/O-bound HTTP every 10 s; one process handles a dozen cities trivially. Per-city try/catch gives most of the fault isolation of separate containers at none of the cost (FR-009/SC-005).
- **Reversibility**: Promoting a heavy city to its own container later is a config split, not a rewrite — same `ITransitCity` classes registered, each container filters `_cities.Where(c => c.Name == Environment.GetEnvironmentVariable("CITY"))`.
- **Alternatives**: N hosted `Worker` instances in one process — same container, more ceremony, N SignalR connections, no payoff. One container per city — N× idle cost/cold starts/scaling config and, without the §3 interface, forks shared logic. YAGNI: no city needs independent scaling today.

## Q4 — Keying: `(city, routeId)` is universal

- **Decision**: The pair `(city, routeId)` is the real key everywhere; `route_short_name` is never assumed globally unique again.
  - Worker index: `Dictionary<string city, IReadOnlyDictionary<string routeId, RoutePoint[]>>`, owned by the loop (not by `ITransitCity`).
  - Server KV store: keys `{city}:{routeId}` (e.g. `marta:1`, `wmata:B`).
  - Shape contract: add `City` to `RouteShapeProperties` so the worker partitions the `/gtfs/routes/shapes` response into per-city indexes without N HTTP calls.
  - Static load: `GtfsStaticLoader` loops the city registry, seeding `{city}:{routeId}`.
- **Rationale**: Route names collide across cities (route `1` ATL vs `1` DC; rail letters B/G/R). Index-building is shared mechanics parameterized by city, so the loop owns it; `ITransitCity` stays focused on live-vehicle fetch+normalize.
- **Free consequence (client)**: client fetches only its joined city's shapes via `?city=`; `RouteShapeProperties.City` flows into `RouteFilterViewModel`.
- **Alternatives**: Index owned by each `ITransitCity` — would push the shared WebAPI round-trip and index-build into every city class, duplicating wiring around shared infra.

## Q5 — Client city source of truth: URL param, default MARTA

- **Decision**: Client reads its city from the URL/query param (`?city=wmata` or path `/wmata`), defaulting to `marta` when absent. This single value feeds both `JoinCity` and the `?city=` shape fetch.
- **Rationale**: Laziest thing that fully works — one query-string read at startup; every city becomes a shareable/bookmarkable link; zero new UI. A switcher can layer on later; the URL stays the source of truth.
- **Alternatives**: Settings-blade selector — breaks the 016 pure-boolean reflection model (why 016 deferred Language); more UI. Geolocation — permission friction, fragile, blocks out-of-region browsing (the whole point is showing DC + Atlanta to anyone).
- **Fallback (FR-004)**: unknown/unconfigured city falls back to the default rather than blanking.

## Q6 — Telemetry: keep MARTA-only via a capability flag

- **Decision**: Telemetry (Logging/ Parquet → Azure Blob, features 013/014) stays but emits for MARTA only, expressed as `ITransitCity.EmitsTelemetry` (`true` MARTA, `false` others).
- **Rationale**: Keep the diagnostic that earns its keep (used via `mj-data-explorer`); don't pay to expand it. `PostEvent` call sites check the declared capability, never a city name (anti-drift §3). No per-city blob-write multiplier.
- **Alternatives**: Remove entirely — loses a working diagnostic workflow. Make it city-aware (per-city column/partition) — most work + most cloud cost (N× blob writes) for instrumentation we don't need replicated.

## Q7 — Rail strategy (keystone): city returns a fully-normalized feed

- **Decision**: `ITransitCity.FetchVehiclesAsync` returns a complete, normalized, merged `FeedMessage` — bus + rail already combined, `route_id`s already remapped to match the static index. Every city-shaped difference is sealed inside the city implementation.
- **Rationale**: The only thing that varies between cities — how you obtain and normalize live vehicles — is fully encapsulated; the identical part (snap → batch → publish to group) is the loop. The loop never asks "does this city have rail?".
  - MARTA: bus protobuf + JSON `RailRealtimeAdapter` (now internal to `MartaCity`, no longer a global singleton); merge; return.
  - WMATA: bus protobuf + rail protobuf; apply the 6-entry `BLUE→B` route_id map; merge; return. No adapter.
  - No-rail city: fetch one feed; return.
- **Alternatives**: Loop orchestrates, city supplies parts (`BusUrl`, `RailAdapter?`, `RailRouteMap?`) — re-grows city-shaped knowledge in the loop (the exact branching being eliminated, merely relocated).
- The existing global `IRailRealtimeAdapter` DI registration is retired and composed into `MartaCity`.

## Q8 — Config, registration & secrets: `Cities:` array + generic class + named exceptions

- **Decision**: Config-first blend. Flat `Marta:` → a `Cities:` array the registry loops. The registry instantiates a generic config-driven `GtfsRtCity : ITransitCity` by default (covers WMATA buses and any standard GTFS-RT agency, zero new code; WMATA's 6-entry rail map rides as optional `RailRouteIdMap` config). Named `ITransitCity` implementations only for genuinely bespoke feeds (e.g. `MartaCity` for the JSON rail API). Registry uses a named impl when one is registered for that city name, else `GtfsRtCity`.
- **Secrets**: WMATA `api_key` is a Container Apps secret referenced by env-var name (`ApiKeyEnvVar` in config), never committed to `appsettings.json` — consistent with feature-013 `DefaultAzureCredential` no-committed-key stance (Principle II).
- **Rationale (sustainability test)**: standard GTFS-RT city = 1 config entry + 1 CA secret, zero C#; bespoke city = 1 isolated `ITransitCity` class; either = 0 changes to loop/hub/client/other cities.
- **Deploy**: stays one Azure Container App / one worker process (Q3).

## Resolved unknowns summary

| Unknown | Resolution |
|---|---|
| How clients receive only their city | SignalR Groups, `Clients.Group(city)` (Q1) |
| How city is tagged on the wire | `PublishBatch(string city, …)` parameter (Q2) |
| Process/deploy topology | One process, config-driven loop, per-city try/catch (Q3) |
| Collision-proof keying | `(city, routeId)` everywhere; KV `{city}:{routeId}` (Q4) |
| Client city source | URL/query param, default `marta` (Q5) |
| Telemetry scope | MARTA-only via `EmitsTelemetry` capability (Q6) |
| Rail/bespoke-feed handling | City returns fully-normalized merged `FeedMessage` (Q7) |
| Add-a-city cost / secrets | `Cities:` array + generic `GtfsRtCity` + named impls; key via CA secret (Q8) |
