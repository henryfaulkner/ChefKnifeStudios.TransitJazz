# GTFS Compatibility Report — STM (Société de transport de Montréal) (Montréal, Québec)

> ## 0/100 — Not Viable
> Bus: 0/70 (blocked — no live feed measured) · Rail: 0/20 · Credential: 0/10 (blocked)
> Ceiling applied: KEY-GATED, capped at 40
> Static data is clean and complete (231/231 routes with shapes, verbatim numeric route
> IDs, including the 4 Métro lines), but the sole realtime vehicle-positions endpoint
> requires a registered STM developer-portal API key not already available in this
> environment, and no published rail line-code documentation exists to desk-check
> independently. Computed per `aggregate-score-formula.md`; every component above is a
> real measurement or fixed categorical lookup, never a guess.

**Evaluated:** 2026-08-03

## Feed health

| | |
|---|---|
| **Blocking classification** | **KEY-GATED** — see the sub-reason table this template opens with |
| Static GTFS URL | `https://www.stm.info/sites/default/files/gtfs/gtfs_stm.zip` — verified live, HTTP 200, ~59.9 MB zipped, flat single-level zip (no unzip-of-unzip handling needed) |
| GTFS-RT vehicle positions (buses) | Exists as standard GTFS-RT protobuf (`https://api.stm.info/pub/od/gtfs-rt/ic/v2/vehiclePositions`) but requires a registered API key issued through STM's developer portal (`portail.developpeurs.stm.info/apihub`) as a mandatory request header; key acquisition is out of scope for this run. The platform's generic city path already supports a config-only key once obtained. |
| Rail realtime (trains) | Not independently confirmed — STM's real-time program is branded "iBUS" (bus-only) in its own project documentation, and no separate proprietary rail-realtime API or line-code reference was found during this run's search. If Métro vehicles are present at all, they would most likely ride the same key-gated `vehiclePositions` endpoint above under some route-ID scheme — not assessable without a key. |

Searched STM's own developer portal (`www.stm.info/en/about/developers`, `.../available-data-description`), a Ville de Montréal open-data mirror (`donnees.montreal.ca`), and Transitland's feed listing; all confirm the same static zip URL and the same GTFS-Realtime v2 gateway. An unauthenticated request made directly against the vehicle-positions endpoint in this run returned:

```
GET https://api.stm.info/pub/od/gtfs-rt/ic/v2/vehiclePositions
HTTP/1.1 400
Content-Type: text/plain;charset=UTF-8

Invalid API Key
```

This confirms the feed is reachable and speaks the expected protocol, but rejects any request lacking a valid, registered key — a clean KEY-GATED signal, not a dead or unpublished endpoint.

## Static GTFS (verified by direct parse)

| | |
|---|---|
| Routes | 231 total, 231 have shapes (100%) |
| `route_type=3` (bus) | 227 routes — `route_id` and `route_short_name` are identical plain rider-facing numeric strings (e.g. `10`, `11`, `12`) |
| `route_type=1` (rail) | 4 routes — keys `1`, `2`, `4`, `5` (Montréal Métro Green/Orange/Yellow/Blue lines; STM's own rider-facing line numbers, not translated or prefixed in any way) |

No off-nominal quirks found in `routes.txt`/`trips.txt` — every route resolves to exactly one `shape_id` with points present, and `route_id == route_short_name` for both buses and Métro lines, so no join-key ambiguity is anticipated once a live feed can be measured.

## Vehicle positions / route ID alignment (buses)

**Not assessable without a live feed.** The only vehicle-positions endpoint STM publishes, `api.stm.info/pub/od/gtfs-rt/ic/v2/vehiclePositions`, rejected this run's unauthenticated request with `400 Invalid API Key`. Field completeness (route_id %, lat/lon %, speed/bearing %) cannot be measured until a key is obtained via STM's developer portal and a live sample is pulled.

## Verdict

- **Buses: INCOMPATIBLE (KEY-GATED)** — standard GTFS-RT protobuf feed exists and responds, but every request requires a registered STM API key not available in this environment; config-only fix once a key exists.
- **Rail: INCOMPATIBLE (KEY-GATED)** — same endpoint is the only plausible carrier for any live Métro positions and is equally key-gated; no independent rail-realtime API or line-code documentation was found to desk-check instead.

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **UNASSESSED** — no feed reachable without a registered STM API key |
| Route ID alignment (buses) | **N/A** — nothing to align against |
| Rail line alignment | **UNASSESSED** — no published rail-specific API/line-code documentation exists to desk-check; static Métro keys (`1`/`2`/`4`/`5`) are known but have nothing live to compare against |
| Blocking classification | **KEY-GATED** |

**Bottom line:** Static GTFS is clean and complete — 231/231 routes have shapes, and both the 227 bus routes and the 4 Métro lines use plain, verbatim rider-facing route IDs with no transform anticipated. The sole blocker is STM's real-time vehicle-positions gateway, which is standard GTFS-RT protobuf but requires a registered developer-portal API key this run does not have (confirmed live via a `400 Invalid API Key` response). This is a config-only fix once a key is obtained — no new code — mirroring `trimet.md`/`mts.md`/`translink.md`'s precedent. Whether Métro vehicles are even present in this feed, and under what route-ID scheme, cannot be determined until that key exists.

## Adding STM as a data source

- **Static GTFS zip:** `https://www.stm.info/sites/default/files/gtfs/gtfs_stm.zip` — no auth required, drop-in as a config-only `CityConfig` entry.
- **Bus realtime:** `https://api.stm.info/pub/od/gtfs-rt/ic/v2/vehiclePositions` — registered API key required (issued via `portail.developpeurs.stm.info/apihub`, "Données Ouverte iBUS - GTFS-Realtime (v2.0)" API product); once obtained, this is a config-only `CityConfig` entry (`ApiKeyEnvVar`/`ApiKeyQueryParam`) — no new code.
- **Rail realtime:** unknown — no separate rail-realtime API was found; if Métro vehicles turn out to ride the same `vehiclePositions` feed under distinct route IDs, a config-only `RailRouteIdMap` entry would likely suffice instead of a bespoke adapter, but this cannot be confirmed without a key.
- **Auth:** A key registered through STM's developer portal (create an application, attach the GTFS-Realtime v2 API product, retrieve the key from Authentication & Credentials) is required for the vehicle-positions endpoint. Per repo precedent, any key must be stored via `ApiKeyEnvVar`/secrets, never committed to source.
- **Config entry vs. new code:** Config-only once a key is obtained — a `CityConfig` entry with `ApiKeyEnvVar`/`ApiKeyQueryParam` set; no new `ITransitCity` implementation anticipated based on what's observable today.
- **Effort scope:** Config-only once an API key is obtained; no adapter code anticipated independent of the key, pending confirmation of how (or whether) Métro vehicles appear in the feed.

## Open items for a follow-up pass

- Register an application at `portail.developpeurs.stm.info/apihub` for the GTFS-Realtime v2.0 API product and obtain an API key.
- Once a key exists, pull a live sample of `vehiclePositions` and measure route_id/lat-lon/speed/bearing completeness the same way other agencies were measured, and check the platform's `RouteIdNormalizer` transforms against any unmatched RT route IDs before assuming new code is needed.
- Confirm whether Métro (route_id `1`/`2`/`4`/`5`) vehicles appear in the same `vehiclePositions` feed as buses, or whether STM publishes no live Métro positions at all (plausible, given the "iBUS" bus-only branding of STM's real-time program) — this determines whether rail needs a `RailRouteIdMap` entry or is simply N/A.
