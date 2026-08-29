# GTFS Compatibility Report — National Transport Authority / Transport for Ireland (Dublin, Ireland)

> ## 20/100 — Not Viable
> Bus: 0/70 (blocked — no live feed measured) · Rail: 20/20 · Credential: 0/10 (blocked)
> Ceiling applied: KEY-GATED, capped at 40
> A standard GTFS-RT protobuf vehicle-positions endpoint exists and is unified across Dublin's operators, but it requires a registered NTA subscription key not available in this environment; the score is dominated by the unmeasured bus axis, not by any structural incompatibility.

**Evaluated:** 2026-08-29

## Feed health

| | |
|---|---|
| **Blocking classification** | **KEY-GATED** — see the sub-reason table this template opens with |
| Static GTFS URL | `https://www.transportforireland.ie/transitData/Data/GTFS_All.zip` — verified live, HTTP 200, 147,573,628 bytes (~140.7 MB) zipped / ~750 MB unzipped |
| GTFS-RT vehicle positions (buses) | **Exists as standard GTFS-RT protobuf but requires a registered API key not already available in the environment; key acquisition is out of scope for this run. The platform's generic city path already supports a config-only key once obtained.** |
| Rail realtime (trains) | N/A — Transport for Ireland has no `route_type=1` routes in static (Luas is `route_type=0` tram, Irish Rail is `route_type=2` commuter rail) |

Searched the NTA's own developer portal (`developer.nationaltransport.ie`), the Mobility Database (`mdb-2364`, which catalogs the schedule feed only and lists no realtime endpoint), and targeted web search. The canonical realtime gateway is `https://api.nationaltransport.ie/gtfsr/v2/Vehicles`, fronted by Azure API Management. It returns HTTP 401 keyless, and the challenge header names the credential explicitly:

```
GET https://api.nationaltransport.ie/gtfsr/v2/Vehicles
→ HTTP 401
WWW-Authenticate: AzureApiManagementKey realm="https://api.nationaltransport.ie/gtfsr",name="x-api-key",type="header"
```

The 401 (rather than 404) confirms the endpoint exists; the sibling `/gtfsr/v2/TripUpdates` also returns 401, while `/gtfsr/v2/VehiclePositions` and `/gtfsr/v1/Vehicles` return 404 — so `/v2/Vehicles` is the correct and current vehicle-positions path. This is a credential gate on an otherwise standard, consumable protobuf feed — structurally unlike a `cta.md`-style dead end, where no protobuf feed exists for the worker's decoder at all. A third party (`smartcitiestransport.com`) rehosts these feeds keyless, but that is a mirror rather than the agency's canonical source and was not evaluated.

## Static GTFS (verified by direct parse)

| | |
|---|---|
| Routes | 810 total, 809 have shapes |
| `route_type=3` (bus) | 789 routes — `route_short_name` carries the plain rider-facing designator (e.g. `393`, `783`), while `route_id` is a composite internal token (e.g. `026 393 b`) containing embedded spaces |
| `route_type=1` (rail) | 0 routes — none |

The static feed is national in scope, not Dublin-only: it bundles 100+ operators, of which the Dublin urban-core ones are Bus Átha Cliath – Dublin Bus (116 routes), Nitelink/Dublin Bus (12), Go-Ahead Ireland (64), and LUAS (2). Two identifier facts matter for a future onboarding pass. First, the worker's index key is `route_short_name ?? route_id`, and `route_short_name` is populated here, so the index would key on the clean rider-facing names (774 distinct keys across 810 routes) rather than on the space-bearing composite `route_id`. Second, there is no heavy rail at all: Luas is tagged `route_type=0` (tram) and Iarnród Éireann / Irish Rail is `route_type=2` (commuter rail, 19 routes), both of which the platform's static loader classifies as Rail while neither requires a separate rail-realtime adapter — whatever live positions exist for them arrive on the same unified GTFS-R stream as the buses.

## Vehicle positions / route ID alignment (buses)

**Not assessable without a live feed.** The `/gtfsr/v2/Vehicles` endpoint returns HTTP 401 without a registered NTA subscription key, so no protobuf sample could be decoded. Field completeness (route_id %, lat/lon %, speed/bearing %) cannot be measured until a key is obtained and a live sample is pulled. In particular, whether the feed's `trip.route_id` values carry the composite form (`026 393 b`) or the short-name form (`393`) is the single most important unknown — it determines whether alignment is verbatim or needs a transform, and it cannot be inferred from static data alone.

## Verdict

- **Buses: INCOMPATIBLE (KEY-GATED)** — the vehicle-positions endpoint is standard GTFS-RT protobuf and confirmed present (401, not 404), so this is a credential gap rather than a format gap, and is close to a config-only fix once a key exists.
- **Rail: N/A** — no `route_type=1` routes exist in the static feed.

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **UNASSESSED** — no feed reachable without a registered NTA subscription key |
| Route ID alignment (buses) | **N/A** — nothing to align against |
| Rail line alignment | **N/A** |
| Blocking classification | **KEY-GATED** |

**Bottom line:** The static side is clean and rich — 810 routes with 809 carrying shapes, populated `route_short_name` values, and a single unified national zip covering every Dublin operator in one file. The blocker is purely credential: the NTA fronts its GTFS-R feeds with Azure API Management and requires a registered subscription key, which this run does not have and will not attempt to obtain. One caveat keeps this from being a clean "config-only once keyed": NTA's challenge specifies the key as an `x-api-key` **header**, whereas the platform's `GtfsRtCity` currently injects a key only as a query-string parameter (`GtfsRtCity.cs:61`, using `CityConfig.ApiKeyQueryParam`). If Azure APIM's query-parameter form is not accepted for this product, a small additive header-injection option on `CityConfig` would be needed — a few lines in one existing class, not a bespoke `ITransitCity`. Separately, a future onboarding pass would need to decide how to scope the national static feed down to Dublin, since ingesting all 810 national routes is far wider than the city-shaped model the platform uses elsewhere.

## Adding Transport for Ireland as a data source

- **Static GTFS zip:** `https://www.transportforireland.ie/transitData/Data/GTFS_All.zip` — no auth required, drop-in as a config-only CityConfig entry, though at ~140 MB zipped it is by far the largest static feed evaluated so far and is national rather than city-scoped.
- **Bus realtime:** `https://api.nationaltransport.ie/gtfsr/v2/Vehicles` — registered API key needed; once obtained, this is close to a config-only CityConfig entry (`ApiKeyEnvVar`/`ApiKeyQueryParam`), with the one caveat that the documented credential is an `x-api-key` header and the existing code path injects the key as a query parameter only.
- **Rail realtime:** n/a — no `route_type=1` routes to onboard. Luas (`route_type=0`) and Irish Rail (`route_type=2`) are covered by the same unified GTFS-R stream as the buses per NTA's own upgrade announcement, so no `RailRouteIdMap` entry and no bespoke rail adapter would be needed for them.
- **Auth:** one NTA developer-portal subscription key for the GTFS-Realtime product, supplied as `x-api-key`. Per this repo's existing precedent, any such key must be stored via env/secrets (`ApiKeyEnvVar`) and never committed.
- **Config entry vs. new code:** config-only for the static feed and, once a key exists, for the realtime feed as well — no new `ITransitCity` implementation is needed, since this is an ordinary GTFS-RT protobuf feed the generic `GtfsRtCity` path already handles. The only possible code touch is the additive header-key option noted above.
- **Effort scope:** config-only once a key is obtained, plus a possible one-line-class change if header auth proves mandatory.

## Open items for a follow-up pass

- Register at `developer.nationaltransport.ie` and subscribe to the GTFS-Realtime product to obtain an `x-api-key` subscription key.
- Pull a live sample from `/gtfsr/v2/Vehicles` once access exists; measure route_id / lat-lon / speed / bearing completeness the same way other agencies were measured.
- Determine whether the RT feed's `trip.route_id` matches static `route_short_name` (e.g. `393`) or the composite `route_id` (e.g. `026 393 b`) — this decides whether alignment is verbatim or needs a transform not covered by the three existing normalizers.
- Confirm whether Azure APIM accepts the subscription key as a query parameter; if not, add a header-injection option to `CityConfig`/`GtfsRtCity` alongside the existing `ApiKeyQueryParam`.
- Decide how to scope the ~140 MB national static feed to Dublin's urban core (Dublin Bus, Nitelink, Go-Ahead Ireland, Luas — roughly 194 of the 810 routes) rather than ingesting all national operators.
