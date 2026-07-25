# GTFS Static Polling & Caching — Design Spec

## 1. Problem & context

GTFS **static** data (route shapes, short names, colors, transit mode) is loaded
**once at WebAPI startup** by `GtfsStaticLoader` (`IHostedService`) into an
in-memory `IKeyValueRepository<string>`, keyed `"{city}:{routeId}"` with a
`__gtfs_static_ready__` readiness sentinel. The `/gtfs/*` endpoints serve
exclusively from this cache. There is no refresh: stale or partially-failed loads
persist until the process restarts, and every consumer is implicitly beholden to
the upstream feed being healthy at the one moment we start.

## 2. Goals

- Periodically re-poll upstream GTFS static zip feeds and refresh the cache, on
  **our** cadence — so the app serves from cache and upstream transit providers
  see at most one fetch per city per interval.
- Move scaling/availability onto our side: clients hit our cache, never the
  provider; provider provisioning no longer gates us.
- Refresh **without serving interruptions** and **without losing last-good data**
  when an upstream fetch fails.

## 3. Non-goals

- No change to client, shared, or worker projects (server-only).
- No change to the GTFS parsing, route simplification, GeoJSON shape, cache
  abstraction, or serving endpoints.
- No new persistence/distributed cache — in-memory remains the store.
- Realtime GTFS-RT (the `Worker`) is out of scope; this is static only.
- **Not** in scope: changing the `Worker`'s route-index refresh cadence to track
  the new static-refresh cadence. See §9 — this mismatch is an accepted decision,
  not an oversight.

## 4. Functional requirements

- **FR-001** The WebAPI MUST re-fetch each configured city's `StaticZipUrls` on a
  recurring interval and refresh the cache from the result.
- **FR-002** The refresh interval MUST be configurable via
  `Gtfs:StaticRefreshHours` (a number, hours). Default when absent/invalid: 24.
  Initial deployed value: 6.
- **FR-003** Initial load behavior MUST be unchanged: data loads at startup and
  the `__gtfs_static_ready__` sentinel gates the endpoints (503 until first load).
- **FR-004** On a successful refresh for a city, routes that no longer exist
  upstream MUST be removed from the cache for that city's `"{city}:"` prefix
  (no stale-key accumulation).
- **FR-005** If a city's refresh produces **zero** routes (fetch error, empty/bad
  zip), the cache for that city MUST be left untouched — previous good data keeps
  serving (last-good). Other cities still refresh independently.
- **FR-006** A failing or throwing refresh tick MUST NOT stop the recurring loop;
  it is logged and the next tick proceeds.
- **FR-007** Endpoints MUST continue serving with no 503 blip while a refresh
  swaps a city's keys.
- **FR-008** The readiness sentinel MUST be set only after the first cycle that
  stores ≥1 route; subsequent cycles only refresh.

## 5. Design

Convert `GtfsStaticLoader` from `IHostedService` to `BackgroundService`. Reuse the
periodic-loop + per-city try/catch pattern already established in
`Worker.ExecuteAsync` / `Worker.RefreshRouteIndexAsync`
(`src\Server\...TransitDataWorker\Worker.cs`).

```
ExecuteAsync(ct):
  interval = config "Gtfs:StaticRefreshHours" (default 24, guard <=0 -> 24) hours
  timer = new PeriodicTimer(interval)
  do:
    try: RefreshAllCitiesAsync(ct)
    catch: log, continue
  while await timer.WaitForNextTickAsync(ct)

RefreshAllCitiesAsync(ct):
  for each city in LoadCityEntries():
    fresh = BuildCityShapeSet(city, ct)   // existing fetch+parse+Simplify+GeoJSON,
                                          // accumulated into Dictionary<key,geojson>
    if fresh.Count == 0: continue          // FR-005 last-good
    await ReconcileCityAsync(city.Name, fresh, ct)
    anyStored = true
  if anyStored and sentinel not set: SetAsync(ReadyKey, "ready")  // FR-008

ReconcileCityAsync(cityName, fresh, ct):
  foreach (k,v) in fresh: SetAsync(k, v)                 // upsert
  all = GetAllAsync()
  foreach key in all where key startsWith "{cityName}:" and key not in fresh:
    DeleteAsync(key)                                     // FR-004 prune vanished
```

The reconcile is upsert-then-prune (not a destructive clear-then-fill), so readers
always see either old-complete or new-complete data — never an empty window (FR-007).
The sentinel key is not city-prefixed, so prune never touches it.

### Reused unchanged
- `IKeyValueRepository<string>` + `InMemoryKeyValueRepository`
  (`GetAllAsync`/`SetAsync`/`DeleteAsync` already exist).
- `EndpointGroups\GtfsEndpoints.cs` serving + sentinel gating.
- All GTFS parsing/simplify/GeoJSON helpers in `GtfsStaticLoader`.
- `Cities[].StaticZipUrls` / `ApiKeyEnvVar` config shape.

## 6. Configuration

`appsettings.json` (prod) and `appsettings.Development.json`:
```json
"Gtfs": { "StaticRefreshHours": 6 }
```
Code default is 24 if the key is absent; deployed initial value is 6.

## 7. Edge cases

- Interval ≤ 0 or unparseable → fall back to 24h.
- Partial multi-zip city (one of several zips fails) → existing per-zip try/catch
  already merges what succeeded; `fresh` is non-empty so the city still refreshes
  with available data.
- All cities fail on first ever run → sentinel never set → endpoints 503 (correct;
  no bad data served).
- Concurrent reads during reconcile → safe: `ConcurrentDictionary` upserts/deletes
  are atomic per key; readers see a consistent superset/subset, never empty.

## 8. Accepted cadence mismatch: static refresh vs. worker route index

The `TransitDataWorker` builds an in-memory route index **derived from** this same
GTFS static data (it fetches `/gtfs/routes/shapes` from the WebAPI) and refreshes
that index on its **own** independent timer — every **24h**
(`RefreshRouteIndexAsync`, `Worker.cs:610-612`). This feature sets the WebAPI's
static refresh to **6h**.

**Consequence:** after a static refresh changes route shapes/IDs, the WebAPI's
served data updates within ≤6h, but the worker's snapping index can lag by up to
**24h** behind it. During that window the worker may snap vehicles using stale
geometry, or skip/mis-attribute a route that was just added/removed upstream
(counted as `BusesSkippedUnknownRoute` in telemetry).

**Decision — accept the mismatch for now.** GTFS static changes land on
infrequent service-change boundaries (weeks apart), so a sub-day lag in the worker
index is operationally harmless and self-heals at the next 24h tick. Tightening it
is deliberately deferred, not forgotten.

**If it ever needs fixing**, the cheapest options (in increasing effort):
1. Lower the worker's `RefreshRouteIndexAsync` interval (or bind it to the same
   `Gtfs:StaticRefreshHours` config) so both poll on the same cadence.
2. Have the WebAPI signal "static changed" (e.g. bump a version/etag the worker
   already polls) so the worker refreshes on change rather than on a fixed timer.

Either is a follow-up; **neither is implemented here**, and the in-process `Worker`
running inside the WebAPI host shares this same 24h cadence.

## 9. Verification

1. Build the WebAPI project.
2. Cold start → logs show per-city load + sentinel; `/gtfs/debug/keys` and
   `/gtfs/routes?city=marta` return data.
3. Set `Gtfs:StaticRefreshHours` ~ `0.02` (≈72s) in dev → a second load log line
   appears with no restart; endpoint serves throughout (no 503).
4. Point a city's `StaticZipUrls` at a bad URL → fetch error logged, that city's
   prior keys remain (last-good), other cities still refresh.
5. Revert dev interval to 6h.
