# GTFS Compatibility Report — MTS (San Diego Metropolitan Transit System) (San Diego, California)

> ## 0/100 — Not Viable
> Bus: 0/70 (blocked — no live feed measured) · Rail: 0/20 · Credential: 0/10 (blocked)
> Ceiling applied: KEY-GATED, capped at 40
> Feed format is standard, consumable GTFS-realtime protobuf (a OneBusAway-hosted
> endpoint); the only blocker is a registered API key this run doesn't have and cannot
> acquire (5-7 business day manual request form). Once obtained, this is a config-only
> `ApiKeyEnvVar`/`ApiKeyQueryParam` addition — no new code. The 0 raw score reflects that
> no live feed and no separate rail-API desk-check were measurable in this run, not
> integration difficulty.

**Evaluated:** 2026-08-02

## Feed health

| | |
|---|---|
| **Blocking classification** | **KEY-GATED** — see the sub-reason table this template opens with |
| Static GTFS URL | `https://www.sdmts.com/google_transit_files/google_transit.zip` — verified live, HTTP 200 (following a 301 redirect from the documented `http://` URL), 4,531,175 bytes zipped |
| GTFS-RT vehicle positions (buses) | Exists as standard GTFS-RT protobuf (`https://realtime.sdmts.com/api/api/gtfs_realtime/vehicle-positions-for-agency/MTS.pb`) but requires a `key` query parameter obtained via a manual developer-registration form ("expected fulfillment within 5 to 7 business days" per MTS's own developer page); key acquisition is out of scope for this run. An unauthenticated request returns HTTP 500 (a generic Tomcat error page, not a decodable feed). The platform's generic city path already supports a config-only key once obtained. |
| Rail realtime (trains) | N/A as a separate blocking reason — San Diego Trolley (route_type=0) and the Bayfront Silver streetcar line have no distinct realtime feed; they are ordinary routes in the same MTS.pb endpoint above and are gated by the identical key requirement. |

Searched MTS's own developer portal (`sdmts.com/business-center/app-developers`), which
documents the feed via a OneBusAway-based gateway, plus a targeted web search that
confirmed the same canonical static and realtime URLs via Transitland's `f-mts~rt~onebusaway`
listing. No API key is already available in this environment, and this run does not attempt
the registration form (per the skill's auth boundary — no account creation, no waiting on
manual approval).

## Static GTFS (verified by direct parse)

| | |
|---|---|
| Routes | 105 total, 104 have shapes |
| `route_type=3` (bus) | 98 routes — `route_id` and `route_short_name` both match plain rider-facing route numbers (e.g. `1`, `2`, `3`, … `18`) |
| `route_type=0` (light rail / streetcar) | 6 routes — `Blue`, `Copper`, `Green`, `Orange`, `Silver`, plus a temporary `MTG Event Line` special-event route, all keyed by `route_short_name` |

<!-- Per GtfsStaticLoader.cs's ClassifyCategory (route_type 0, 1, AND 2 are all Rail absent
     a per-city override), MTS's 6 route_type=0 Trolley/streetcar routes fall on the
     platform's Rail axis, not Bus — the BLOCKED template's usual "route_type=1" framing is
     adapted here since MTS has zero route_type=1/2 routes and all its rail service is
     route_type=0. -->

No `route_type=1` or `route_type=2` routes exist — MTS's entire rail-adjacent service (San
Diego Trolley's Blue/Orange/Green/Copper/Silver lines) is `route_type=0`, all with clean
plain-string `route_short_name` line names, no numeric prefix scheme like RTD's.

## Rail line-key alignment

Not applicable — no separate, non-GTFS-RT rail API exists to desk-check against. MTS
publishes Trolley/streetcar vehicles through the same `MTS.pb` OneBusAway gateway as buses,
so the Trolley's realtime data is blocked by the identical key requirement rather than a
distinct API with its own documented line codes.

## Vehicle positions / route ID alignment (buses)

**Not assessable without a live feed.** The GTFS-RT vehicle-positions endpoint requires a
registered API key that isn't already available in this environment, so no live sample
could be pulled — an unauthenticated request returns HTTP 500 rather than a decodable
protobuf response. Field completeness (route_id %, lat/lon %, speed/bearing %) cannot be
measured until a key is obtained and a live sample is pulled.

## Verdict

- **Buses: INCOMPATIBLE (KEY-GATED)** — standard GTFS-RT protobuf feed exists but is gated behind a manually-issued API key not available in this environment; config-only fix once a key exists
- **Rail: INCOMPATIBLE (KEY-GATED)** — Trolley/streetcar vehicles ride the same key-gated `MTS.pb` feed as buses; no independent blocking reason

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **UNASSESSED** — no feed reachable without a registered API key |
| Route ID alignment (buses) | **N/A** — nothing to align against |
| Rail line alignment | **N/A** — no separate rail API; blocked by the same key gate as buses |
| Blocking classification | **KEY-GATED** |

**Bottom line:** MTS's static GTFS is clean, keyless, and fully parseable (105 routes, 104
with shapes, plain rider-facing IDs for both bus and Trolley routes). The sole blocker is
the realtime feed's registered API key, requested via a manual form with a stated 5-7
business-day turnaround — structurally identical to TriMet's AppID gate (`trimet.md`).
Once a key is obtained, onboarding is expected to be config-only (a `CityConfig` entry with
`ApiKeyEnvVar`/`ApiKeyQueryParam`) unless a live sample later reveals a route-ID mismatch
needing one of the platform's three existing normalizer transforms.

## Adding MTS as a data source

- **Static GTFS zip:** `https://www.sdmts.com/google_transit_files/google_transit.zip` — no auth required, drop-in as a config-only `CityConfig` entry once the RT key exists; note the documented URL is `http://` and 301-redirects to `https://` — either works with a client that follows redirects.
- **Bus realtime:** `https://realtime.sdmts.com/api/api/gtfs_realtime/vehicle-positions-for-agency/MTS.pb?key=<KEY>` — registered API key needed (query param `key`); once obtained, this is a config-only `CityConfig` entry (`ApiKeyEnvVar`/`ApiKeyQueryParam`) — no new code.
- **Rail realtime:** n/a — Trolley/streetcar vehicles arrive on the same `MTS.pb` feed under ordinary `route_type=0` route IDs; a config-only `RailRouteIdMap` entry would only be needed if a live sample later shows a route-ID scheme mismatch (unverifiable without the key).
- **Auth:** An MTS-issued API key (free but manual developer registration form, ~5-7 business days) is required for the GTFS-RT feed; per repo precedent, store it via `ApiKeyEnvVar`, never committed to source. Static GTFS needs no credential.
- **Config entry vs. new code:** Config-only once a key is obtained — a `CityConfig` entry with `ApiKeyEnvVar`/`ApiKeyQueryParam` set, no new `ITransitCity` implementation anticipated.
- **Effort scope:** Config-only once an API key is obtained; no adapter code anticipated independent of the key.

## Open items for a follow-up pass

- Submit MTS's developer API key request form and re-run the STAGE 4 evaluation against `MTS.pb` with the key applied once it arrives.
- Once a live sample is available, check for a Trolley live vehicle under a plain `Blue`/`Green`/`Orange`/`Copper`/`Silver` route_id to confirm it matches static verbatim (no RailRouteIdMap expected, but unverified).
- If a live sample later shows unmatched RT route IDs, check them against the platform's `RouteIdNormalizer` transforms before assuming new code is needed.
