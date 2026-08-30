# GTFS Compatibility Report — OC Transpo (Ottawa, Ontario)

> ## 20/100 — Not Viable
> Bus: 0/70 (blocked — no live feed measured) · Rail: 20/20 · Credential: 0/10 (blocked)
> Ceiling applied: KEY-GATED, capped at 40
> Static data is clean and keyless, and there is no heavy rail to separately assess, but
> the only realtime vehicle-positions feed requires a registered API key this run does not
> have — nothing can be measured on the bus axis until one is obtained.

**Evaluated:** 2026-08-04

## Feed health

| | |
|---|---|
| **Blocking classification** | **KEY-GATED** |
| Static GTFS URL | `https://oct-gtfs-emasagcnfmcgeham.z01.azurefd.net/public-access/GTFSExport.zip` — verified live, HTTP 200, ~52.1 MiB zipped |
| GTFS-RT vehicle positions (buses) | Exists as standard GTFS-RT protobuf but requires a registered API key not already available in the environment; key acquisition is out of scope for this run. The platform's generic city path already supports a config-only key once obtained. |
| Rail realtime (trains) | N/A — OC Transpo has no `route_type=1` routes in static; the O-Train (Lines 1, 2, 4) is tagged `route_type=0` (light rail/tram), same category TTC's streetcars already ride cleanly with no separate rail mechanism. |

OC Transpo's own Developers page and Developer Documentation point to an Azure API
Management portal (`nextrip-public-api.developer.azure-api.net`) where "registered
developers [can] use the GTFS-RT data for both non-commercial and commercial purposes" —
access requires signing up for a subscription key. A direct, unauthenticated request in
this run against the published vehicle-positions endpoint confirms the gate:

```
GET https://nextrip-public-api.azure-api.net/octranspo/gtfs-rt-vp/beta/v1/VehiclePositions
→ HTTP 401
{ "statusCode": 401, "message": "Access denied due to missing subscription key. Make sure
  to include subscription key when making requests to an API." }
```

The endpoint path (`gtfs-rt-vp`) and error format are standard Azure APIM self-serve
behavior — no CAPTCHA or manual approval step is implied, but this run does not attempt
registration per the skill's auth boundary. The historic `octranspo1.com/files/
google_transit.zip` static URL referenced in older third-party docs is dead (404); the
current live static mirror is the Azure Front Door URL above, found via the agency's
current developer-documentation page.

## Static GTFS (verified by direct parse)

| | |
|---|---|
| Routes | 125 total, 125 have shapes |
| `route_type=3` (bus) | 122 routes — plain numeric `route_id`/`route_short_name` (e.g. `1`, `95`, `197`), verbatim rider-facing strings |
| `route_type=1` (rail) | 0 routes — none |
| `route_type=0` (light rail) | 3 routes — O-Train Line 1 (`1-350`, Blair ↔ Tunney's Pasture), Line 2 (`2-354`, Bayview ↔ Limebank), Line 4 (`4-354`, South Keys ↔ Airport); short names `1`/`2`/`4` do not collide with any bus route_short_name |

## Vehicle positions / route ID alignment (buses)

**Not assessable without a live feed.** The vehicle-positions endpoint 401s without a
subscription key (see Feed health above). Field completeness (route_id %, lat/lon %,
speed/bearing %) and route ID alignment against the 125-route static index cannot be
measured until a key is obtained and a live sample is pulled.

## Verdict

- **Buses: INCOMPATIBLE (KEY-GATED)** — standard GTFS-RT protobuf exists but 401s without a registered subscription key; config-only fix once a key exists.
- **Rail: N/A** — no `route_type=1` routes exist; the O-Train's `route_type=0` lines would ride the same mechanism buses do (no separate rail integration), mirroring TTC's streetcars.

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **UNASSESSED** — no feed reachable without a subscription key |
| Route ID alignment (buses) | **N/A** — nothing to align against |
| Rail line alignment | **N/A** — no route_type=1 routes to align |
| Blocking classification | **KEY-GATED** |

**Bottom line:** Static GTFS is clean, keyless, flat-zip, and 100% shape-covered across
all 125 routes, with plain numeric route IDs and no rail-integration question to resolve
(the O-Train rides the light-rail category, not the heavy-rail one this platform treats
specially). The sole blocker is the vehicle-positions feed sitting behind an Azure APIM
subscription key that isn't already available in this environment. Once a key is
obtained, this is expected to be a config-only onboarding — no new `ITransitCity`
implementation or route-ID transform anticipated — but that can't be confirmed without a
live sample to measure alignment and field completeness against.

## Adding OC Transpo as a data source

- **Static GTFS zip:** `https://oct-gtfs-emasagcnfmcgeham.z01.azurefd.net/public-access/GTFSExport.zip` — no auth required, drop-in as a config-only `CityConfig` entry.
- **Bus realtime:** `https://nextrip-public-api.azure-api.net/octranspo/gtfs-rt-vp/beta/v1/VehiclePositions` — registered API key needed (Azure APIM subscription via `nextrip-public-api.developer.azure-api.net`); once obtained, this is a config-only `CityConfig` entry (`ApiKeyEnvVar`/`ApiKeyQueryParam`) — no new code.
- **Rail realtime:** n/a — no rail to onboard as a distinct mechanism; the O-Train rides the same bus feed once a key exists.
- **Auth:** one Azure APIM subscription key for the vehicle-positions endpoint; per repo precedent this must be stored via an environment variable / secret, never committed.
- **Config entry vs. new code:** config-only — a `CityConfig` entry once a key exists, no new `ITransitCity` implementation needed.
- **Effort scope:** config-only once a key is obtained.

## Open items for a follow-up pass

- Register for an OC Transpo / Azure APIM developer subscription key for the
  `gtfs-rt-vp` vehicle-positions product.
- Once a key exists, pull a live sample and measure route_id / lat-lon / speed / bearing
  completeness and route ID alignment against the 125-route static index the same way
  other agencies were measured.
