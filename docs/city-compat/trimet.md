# GTFS Compatibility Report — TriMet (Portland, Oregon)

> ## 0/100 — Not Viable
> Bus: 0/70 (blocked — no live feed measured) · Rail: 0/20 · Credential: 0/10 (blocked)
> Ceiling applied: KEY-GATED, capped at 40
> Feed format is standard, consumable GTFS-realtime protobuf; the only blocker is a
> registered AppID this run doesn't have. Once obtained, this is a config-only
> `ApiKeyEnvVar`/`ApiKeyQueryParam` addition — no new code. The 0 raw score reflects that
> no live bus feed and no static-desk rail check were measurable in this run, not
> integration difficulty.

**Evaluated:** 2026-07-25

## Feed health

| | |
|---|---|
| **Blocking classification** | **KEY-GATED** — see the sub-reason table this template opens with |
| Static GTFS URL | `http://developer.trimet.org/schedule/gtfs.zip` — not verified in this run (see note below) |
| GTFS-RT vehicle positions (buses) | Exists as standard GTFS-RT protobuf (`http://developer.trimet.org/ws/V1/VehiclePositions`) but requires a registered AppID as a mandatory parameter on every TriMet web-service call; key acquisition is out of scope for this run. The platform's generic city path already supports a config-only key once obtained. |
| Rail realtime (trains) | N/A — could not be desk-checked; static zip wasn't reachable in this run to confirm TriMet has zero `route_type=1` routes (MAX light rail and Portland Streetcar are `route_type=0`; WES Commuter Rail, if present, is `route_type=2`, outside this report's `route_type=1` scope) |

Searched TriMet's own developer portal (`developer.trimet.org`), the Mobility Database, and
a targeted web search; all three confirm the same canonical URLs and the AppID requirement
stated in TriMet's own documentation. Separately, every fetch path available in this run
(a direct HTTPS request and the environment's web-fetch tool) received an HTTP 403 from
`developer.trimet.org` itself — this looks like bot/WAF protection on the agency's site
rather than the resource being unpublished or offline; the Mobility Database and Transitland
both list an active TriMet regional GTFS feed as of July 2026, and Transitland separately
mirrors TriMet's realtime feed (Onestop ID `f-trimet~rt`) behind Transitland's own API key,
which is a different credential than TriMet's own AppID and doesn't change the
classification.

## Static GTFS (verified by direct parse)

Not assessed — static GTFS zip was not reachable (HTTP 403 returned by
`developer.trimet.org` to every fetch path available in this run; third-party listings
describe it as a combined regional feed for TriMet, Portland Streetcar, and Portland Aerial
Tram, last updated July 5, 2026).

## Vehicle positions / route ID alignment (buses)

**Not assessable without a live feed.** The GTFS-RT vehicle-positions endpoint requires a
registered TriMet AppID that isn't already available in this environment, so no live sample
could be pulled. Field completeness (route_id %, lat/lon %, speed/bearing %) cannot be
measured until a key is obtained and a live sample is pulled.

## Verdict

- **Buses: INCOMPATIBLE (KEY-GATED)** — standard GTFS-RT protobuf feed exists but is gated behind a registered AppID not available in this environment; config-only fix once a key exists.
- **Rail: N/A** — TriMet has no known `route_type=1` (heavy rail/subway) service; not independently confirmed via static parse in this run.

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **UNASSESSED** — no feed reachable without a registered AppID |
| Route ID alignment (buses) | **N/A** — nothing to align against |
| Rail line alignment | **UNASSESSED** — static zip not reachable in this run |
| Blocking classification | **KEY-GATED** |

**Bottom line:** TriMet publishes a standard, protobuf-format GTFS-RT vehicle-positions
feed — the shape the platform's `GtfsRtCity` path already expects — but every call requires
a registered AppID, and none is available in this environment. Static GTFS is documented as
a public, keyless download, but this run's fetch tools were blocked by the agency's site
(HTTP 403) before that could be independently verified. Once an AppID is obtained, onboarding
TriMet is expected to be config-only (no new `ITransitCity` code) unless a live sample later
reveals a route-ID mismatch needing one of the existing normalizer transforms.

## Adding TriMet as a data source

- **Static GTFS zip:** `http://developer.trimet.org/schedule/gtfs.zip` — documented as keyless in TriMet's own developer resources; not independently verified in this run (site returned HTTP 403 to this run's fetch tools).
- **Bus realtime:** `http://developer.trimet.org/ws/V1/VehiclePositions` — registered AppID required as a parameter on every call; once obtained, this is a config-only `CityConfig` entry (`ApiKeyEnvVar`/`ApiKeyQueryParam`) — no new code.
- **Rail realtime:** n/a — TriMet has no known public live heavy-rail (`route_type=1`) position feed to onboard.
- **Auth:** A TriMet-issued AppID (free developer registration) is required for both the GTFS-RT feed and the legacy Vehicle Locations web service. Per repo precedent, store it via `ApiKeyEnvVar`, never committed to source.
- **Config entry vs. new code:** Config-only once a key is obtained — a `CityConfig` entry with `ApiKeyEnvVar`/`ApiKeyQueryParam` set, no new `ITransitCity` implementation anticipated.
- **Effort scope:** Config-only once an AppID is obtained; no adapter code anticipated independent of the key.

## Open items for a follow-up pass

- Register for a TriMet developer AppID and re-run the STAGE 4 evaluation against `http://developer.trimet.org/ws/V1/VehiclePositions` with the key applied.
- Re-verify the static GTFS zip is reachable and parse it directly (this run's fetch tools were blocked by the agency's site) to confirm route_id format and the `route_type=1` rail count.
- If a live sample later shows unmatched RT route IDs, check them against the platform's `RouteIdNormalizer` transforms before assuming new code is needed.
