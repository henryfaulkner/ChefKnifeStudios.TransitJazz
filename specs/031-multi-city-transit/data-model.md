# Phase 1 Data Model: Multi-City Transit Targets

Entities derived from the spec's Key Entities and the design doc's keying/config decisions
(Q2, Q4, Q7, Q8). Grounded against current source types.

## ITransitCity (strategy)

The anti-drift contract. One implementation per city; the worker loop never branches on `Name`.

| Member | Type | Notes |
|---|---|---|
| `Name` | `string` | SignalR group key, KV-store prefix, URL param, telemetry partition. **Lowercase, stable** (e.g. `marta`, `wmata`). |
| `FetchVehiclesAsync(CancellationToken)` | `Task<FeedMessage>` | Returns the city's COMPLETE, NORMALIZED live feed: bus + rail already merged, `route_id`s already remapped to match the static index. The loop never knows how it was assembled. |
| `EmitsTelemetry` | `bool` | Capability flag. `true` for MARTA, `false` otherwise. Loop gates `PostEvent` on this — never on a city name. |

**Implementations:**
- `GtfsRtCity` — generic, config-driven. Fetches one-or-more GTFS-RT protobuf URLs, applies optional `RailRouteIdMap`, merges, returns. Covers WMATA + any standard agency with zero new code.
- `MartaCity` — bespoke. Fetches bus protobuf + composes the (formerly global) `IRailRealtimeAdapter` JSON rail call internally; merges; returns.

## CityConfig (binds `Cities:` array, Q8)

| Field | Type | Required | Notes |
|---|---|---|---|
| `Name` | `string` | yes | Stable lowercase identifier. |
| `GtfsRtUrls` | `string[]` | yes | One (bus only) or more (bus + rail) GTFS-RT protobuf endpoints. |
| `StaticZipUrls` | `string[]` | yes | One or more GTFS static zips for this city. |
| `RailRealtime` | object `{ BaseUrl, Enabled }` | no | Bespoke JSON rail config (MARTA). Presence/handling is the named impl's concern. |
| `RailRouteIdMap` | `Dictionary<string,string>` | no | e.g. `{ "BLUE":"B", ... }` — remaps rail `route_id`s to the static index (WMATA). |
| `ApiKeyEnvVar` | `string` | no | **Name of an env var** holding the feed API key. The key value is a Container Apps secret, never committed (Principle II, FR-014). |
| `EmitsTelemetry` | `bool` | yes | Maps to `ITransitCity.EmitsTelemetry`. |

**Registry resolution (Q8)**: for each entry, if a named `ITransitCity` is registered for `Name`, use it (passing the config); else instantiate `GtfsRtCity` from config. No code per standard city.

**Validation rules:**
- `Name` non-empty, lowercase, unique across the array.
- At least one `GtfsRtUrls` and one `StaticZipUrls`.
- If `ApiKeyEnvVar` set, the referenced env var SHOULD be present at startup; absence is logged and the city's fetch degrades to best-effort (per-city try/catch isolates failure — FR-009).

## Route (scoped) — keying

The pair **`(city, routeId)`** is the universal key (Q4). `route_short_name` is never assumed
globally unique.

| Surface | Before | After |
|---|---|---|
| Worker index | `IReadOnlyDictionary<string routeId, RoutePoint[]>` | `Dictionary<string city, IReadOnlyDictionary<string routeId, RoutePoint[]>>` |
| Server KV store | key = bare `routeId` | key = `{city}:{routeId}` (e.g. `marta:1`, `wmata:B`) |
| Shapes endpoint | `/gtfs/routes/shapes` (all) | `/gtfs/routes/shapes?city={city}` (scoped) |

## RouteShapeProperties (Shared — additive change)

Current record: `RouteShapeProperties(string RouteId, string? RouteShortName, string? Color, string? TextColor, TransitMode Mode = Bus)`.

**Add** `string? City` (nullable for back-compat with any cached/serialized data; populated by the
loader). Flows: `GtfsStaticLoader` sets it → worker partitions the shapes response into per-city
indexes without N HTTP calls → client `RouteFilterViewModel` consumes it.

## Vehicle State (scoped)

`Worker._vehicleStateCache` (`ConcurrentDictionary<string vehicleId, VehicleState>`) and the
server-side `LastBatchCache` per-vehicle upsert map both become **per-city** so identical vehicle
IDs across cities never collide (FR-011).

| Surface | Before | After |
|---|---|---|
| Worker vehicle state | one `ConcurrentDictionary<vehicleId, VehicleState>` | per-city (e.g. keyed by city, or `(city, vehicleId)`) |
| `ILastBatchCache` | `Current` / `Set(batch)` | `Current(string city)` / `Set(string city, batch)`, backed by `Dictionary<string, LastBatchCache>` |

## Transport: PublishBatch / EventEnvelope

- `ITransitHubPublisher.PublishBatchAsync(string city, List<EventEnvelope> batch, CancellationToken)` — **add `city`**.
- `SignalRHubPublisher` calls `InvokeAsync("PublishBatch", city, batch, ct)`.
- `WorkerTransitHub.PublishBatch(string city, List<EventEnvelope> batch)` → `_lastBatchCache.Set(city, batch)` → `Clients.Group(city).SendAsync("ReceiveBatch", batch)`.
- `EventEnvelope` is **unchanged** (city is a routing key, not domain data — Q2).

## Client city

| Item | Value |
|---|---|
| Source of truth | URL / query param (`?city=wmata` or path segment), default `marta` (Q5) |
| Used by | `SignalRNotificationService.JoinCity(city)` after connect; `?city=` on shape fetch |
| Unknown city | falls back to default `marta` (FR-004) |

## State transitions

None of these entities are stateful workflows. The only lifecycle note: a client connection
transitions `connected → JoinCity(city) → immediate cache replay of Current(city) → live group
broadcasts`.
