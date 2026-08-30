# GTFS Compatibility Report — TransLink (South Coast British Columbia Transportation Authority) (Vancouver, British Columbia)

> ## 0/100 — Not Viable
> Bus: 0/70 (blocked — no live feed measured) · Rail: 0/20 · Credential: 0/10 (blocked)
> Ceiling applied: KEY-GATED, capped at 40
> Feed format is standard, consumable GTFS-realtime protobuf; the only blocker is a
> registered API key. Once obtained, this is a config-only `ApiKeyEnvVar`/
> `ApiKeyQueryParam` addition — no new code. The 0 raw score reflects that no live bus
> feed and no rail line-code desk-check were measurable in this run, not integration
> difficulty; static GTFS itself is clean and keyless.

**Evaluated:** 2026-08-02

## Feed health

| | |
|---|---|
| **Blocking classification** | **KEY-GATED** — see the sub-reason table this template opens with |
| Static GTFS URL | `https://gtfs-static.translink.ca/gtfs/google_transit.zip` — verified live, HTTP 200, ~15.5 MiB (16,266,193 bytes) zipped, flat single-level zip (no nested zip-of-zips) |
| GTFS-RT vehicle positions (buses) | Exists as standard GTFS-RT protobuf but requires a registered API key as a mandatory query parameter (`?apikey=`) on every call; key acquisition is out of scope for this run. The platform's generic city path already supports a config-only key once obtained. |
| Rail realtime (trains) | Same feed and same blocker as buses — SkyTrain (Canada Line, Millennium Line, Expo Line) has no separate rail API; it would ride the identical key-gated GTFS-RT stream under its own `route_id` values. |

Searched TransLink's own developer portal (`translink.ca/.../app-developer-resources/gtfs`),
the Mobility Database, and a targeted web search; all three point to the same canonical
endpoint, `https://gtfsapi.translink.ca/v3/gtfsposition?apikey=[ApiKey]`, and TransLink's own
documentation states the `apikey` parameter is required. An unauthenticated request to
`https://gtfsapi.translink.ca/v3/gtfsposition` (no `apikey` param) was made directly in this
run and returned `HTTP 403` with an empty body — a concrete, reproducible confirmation of the
key gate, not just documentation.

## Static GTFS (verified by direct parse)

| | |
|---|---|
| Routes | 244 total, 244 have shapes (0 without) |
| `route_type=3` (bus) | 238 routes — `route_short_name` is a plain rider-facing string (e.g. `256`, `033`, `609`); `route_id` is a separate internal numeric ID (e.g. `10232`) that does not match `route_short_name` |
| `route_type=1` (rail) | 3 routes — keys `13686` (Canada Line), `30052` (Millennium Line), `30053` (Expo Line); `route_short_name` is empty for all three, so the join key falls back to the internal numeric `route_id` |

Two other non-bus, non-rail categories exist in static and are out of this report's scope:
`route_type=2` (West Coast Express commuter rail, 1 route) and `route_type=4` (SeaBus
passenger ferry, 1 route). A `route_type=715` entry (HandyDART, 1 route) is an
extended/non-standard GTFS route type outside the platform's bus/rail split entirely.

## Vehicle positions / route ID alignment (buses)

**Not assessable without a live feed.** The GTFS-RT vehicle-positions endpoint requires a
registered API key that isn't already available in this environment, so no live sample could
be pulled. Field completeness (route_id %, lat/lon %, speed/bearing %) cannot be measured
until a key is obtained and a live sample is pulled.

## Verdict

- **Buses: INCOMPATIBLE (KEY-GATED)** — standard GTFS-RT protobuf feed exists but is gated behind a registered API key not available in this environment; config-only fix once a key exists.
- **Rail: INCOMPATIBLE (KEY-GATED)** — SkyTrain rides the same key-gated feed as buses; no independent blocking reason, but also not independently assessable.

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **UNASSESSED** — no feed reachable without a registered API key |
| Route ID alignment (buses) | **N/A** — nothing to align against |
| Rail line alignment | **UNASSESSED** — SkyTrain's `route_id` values (`13686`/`30052`/`30053`) are known from static, but whether the RT feed reports the same numeric IDs can't be confirmed without the gated feed |
| Blocking classification | **KEY-GATED** |

**Bottom line:** TransLink publishes a standard, protobuf-format GTFS-RT vehicle-positions
feed — the shape the platform's `GtfsRtCity` path already expects — but every call requires a
registered API key, confirmed directly in this run (`HTTP 403` on an unauthenticated request).
Static GTFS is clean, keyless, and fully shape-covered (244/244 routes), including three
`route_type=1` SkyTrain lines that ride the identical feed under their own numeric IDs — a
config-only `RailRouteIdMap` remap is the plausible mechanism if those IDs turn out to differ
from static's, but that can't be confirmed without a live sample. Once an API key is obtained,
onboarding TransLink is expected to be config-only (no new `ITransitCity` code) unless a live
sample later reveals a route-ID mismatch needing one of the existing normalizer transforms.

## Adding TransLink as a data source

- **Static GTFS zip:** `https://gtfs-static.translink.ca/gtfs/google_transit.zip` — no auth required, drop-in as a config-only `CityConfig` entry.
- **Bus realtime:** `https://gtfsapi.translink.ca/v3/gtfsposition?apikey=[ApiKey]` — registered API key required; once obtained, this is a config-only `CityConfig` entry (`ApiKeyEnvVar`/`ApiKeyQueryParam`) — no new code.
- **Rail realtime:** same endpoint as bus realtime (`gtfsapi.translink.ca/v3/gtfsposition`) — SkyTrain has no separate rail API; if its RT `route_id` values differ from static's numeric IDs, a config-only `RailRouteIdMap` entry would resolve it, no bespoke adapter anticipated.
- **Auth:** A TransLink-issued API key (developer account registration) is required for the single GTFS-RT feed covering both buses and rail. Per repo precedent, store it via `ApiKeyEnvVar`, never committed to source.
- **Config entry vs. new code:** Config-only once a key is obtained — a `CityConfig` entry with `ApiKeyEnvVar`/`ApiKeyQueryParam` set, no new `ITransitCity` implementation anticipated.
- **Effort scope:** Config-only once an API key is obtained; no adapter code anticipated independent of the key.

## Open items for a follow-up pass

- Register for a TransLink developer API key and re-run the STAGE 4 evaluation against `https://gtfsapi.translink.ca/v3/gtfsposition` with the key applied.
- Once a live sample is available, confirm whether SkyTrain's RT `route_id` values match static's numeric IDs (`13686`/`30052`/`30053`) verbatim or need a `RailRouteIdMap` remap.
- If a live sample shows unmatched bus RT route IDs, check them against the platform's `RouteIdNormalizer` transforms before assuming new code is needed.
