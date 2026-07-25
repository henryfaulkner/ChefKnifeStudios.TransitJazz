# GTFS Compatibility Report — SFMTA (San Francisco Municipal Transportation Agency) (San Francisco, California)

> ## 0/100 — Not Viable
> Bus: 0/70 (blocked — no live feed measured) · Rail: 0/20 · Credential: 0/10 (blocked)
> Ceiling applied: KEY-GATED, capped at 40
> Feed format is standard, consumable GTFS-realtime protobuf (confirmed via the Mobility
> Database catalog); the only blocker is a registered 511.org API key this run doesn't
> have. Once obtained, this is a config-only `ApiKeyEnvVar`/`ApiKeyQueryParam` addition —
> no new code. The 0 raw score reflects that no live bus feed and no static-desk rail
> check were measurable in this run, not integration difficulty.

**Evaluated:** 2026-07-25

## Feed health

| | |
|---|---|
| **Blocking classification** | **KEY-GATED** — see the sub-reason table this template opens with |
| Static GTFS URL | `https://www.sfmta.com/reports/gtfs-transit-data` (also mirrored on DataSF, dataset `dni7-qpv3`, and cataloged as `mdb-50` on the Mobility Database) — not verified in this run (see note below) |
| GTFS-RT vehicle positions (buses) | Exists as standard GTFS-RT protobuf (`http://api.511.org/transit/vehiclepositions?agency=SF`) but requires a registered 511.org Open Data API key (`api_key` query parameter) on every call; key acquisition is out of scope for this run. The platform's generic city path already supports a config-only key once obtained. |
| Rail realtime (trains) | N/A — could not be desk-checked; static zip wasn't reachable in this run to confirm SFMTA has zero `route_type=1` routes (Muni Metro light rail and historic streetcars/cable cars are `route_type=0`/`5`; San Francisco's heavy rail, BART, is a legally separate authority out of scope for this report) |

SFMTA does not publish its own standalone keyless vehicle-positions feed — Muni bus and
rail vehicle positions are served through **511.org**, the Bay Area's regional realtime
API operated by the Metropolitan Transportation Commission, with `agency=SF` selecting
SFMTA specifically. The Mobility Database's GitHub-hosted catalog (fetched directly,
`mdb_source_id` 1843) confirms this is a registered `gtfs-rt` source with `entity_type:
["vp"]` — i.e. genuinely vehicle-positions, not trip-updates/alerts — gated by
`authentication_type: 1` (API key) with registration at `https://511.org/open-data/token`
and a data-use license at `https://511.org/sites/default/files/pdfs/511_Data_Agreement_Final.pdf`.
Separately, this run's own network egress policy denied outbound connections to
`sfmta.com`, `data.sfgov.org`, `mobilitydatabase.org`, and `api.511.org` directly (each
came back "policy denial" at the CONNECT level, confirmed via the proxy's status
endpoint) — the Mobility Database facts above were instead obtained from that project's
public GitHub mirror (`raw.githubusercontent.com`), which this run could reach.

## Static GTFS (verified by direct parse)

Not assessed — static GTFS zip was not reachable (this run's network egress policy
blocked outbound connections to `sfmta.com` and `data.sfgov.org`; third-party listings
describe the current file as `SFMTA_GTFS_20260606_20260828v3.zip`, requiring acceptance
of SFMTA's Transit Data License Agreement per SFMTA's own developer-resources page).

## Vehicle positions / route ID alignment (buses)

**Not assessable without a live feed.** The GTFS-RT vehicle-positions endpoint requires a
registered 511.org API key that isn't already available in this environment, so no live
sample could be pulled. Field completeness (route_id %, lat/lon %, speed/bearing %)
cannot be measured until a key is obtained and a live sample is pulled.

## Verdict

- **Buses: INCOMPATIBLE (KEY-GATED)** — standard GTFS-RT protobuf feed exists (confirmed via the Mobility Database catalog) but is gated behind a registered 511.org API key not available in this environment; config-only fix once a key exists.
- **Rail: N/A** — SFMTA/Muni has no known `route_type=1` (heavy rail/subway) service of its own (BART, which does, is a separate authority); not independently confirmed via static parse in this run.

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **UNASSESSED** — no feed reachable without a registered 511.org API key |
| Route ID alignment (buses) | **N/A** — nothing to align against |
| Rail line alignment | **UNASSESSED** — static zip not reachable in this run |
| Blocking classification | **KEY-GATED** |

**Bottom line:** SFMTA's vehicles are exposed through a standard, protobuf-format
GTFS-RT vehicle-positions feed — the shape the platform's `GtfsRtCity` path already
expects — but it rides the region-wide 511.org gateway, and every call requires a
registered API key that isn't available in this environment. Static GTFS is documented
as a public download (behind a click-through license agreement, not an API key), but
this run's network policy blocked both `sfmta.com` and `data.sfgov.org` before that
could be independently verified. Once a 511.org key is obtained, onboarding SFMTA is
expected to be config-only (no new `ITransitCity` code) unless a live sample reveals a
route-ID mismatch needing one of the existing normalizer transforms, or the static parse
turns up an unexpected `route_type=1` route.

## Adding SFMTA as a data source

- **Static GTFS zip:** `https://www.sfmta.com/reports/gtfs-transit-data` (mirrored on DataSF as dataset `dni7-qpv3`) — documented as a public download behind SFMTA's Transit Data License Agreement (click-through, not an API key); not independently verified in this run (this run's network policy blocked both hosts).
- **Bus realtime:** `http://api.511.org/transit/vehiclepositions?agency=SF` — registered 511.org API key required as a query parameter (`api_key`) on every call; once obtained, this is a config-only `CityConfig` entry (`ApiKeyEnvVar`/`ApiKeyQueryParam`) — no new code.
- **Rail realtime:** n/a — SFMTA has no known public live heavy-rail (`route_type=1`) position feed to onboard; BART, which does run heavy rail in the region, is a separate authority and out of scope for this report.
- **Auth:** A 511.org Open Data API key (free registration at `https://511.org/open-data/token`) is required for the GTFS-RT feed; the same key also covers 511.org's aggregated static-feed endpoint if that route is preferred over SFMTA's direct download. Per repo precedent, store it via `ApiKeyEnvVar`, never committed to source.
- **Config entry vs. new code:** Config-only once a key is obtained — a `CityConfig` entry with `ApiKeyEnvVar`/`ApiKeyQueryParam` set, no new `ITransitCity` implementation anticipated.
- **Effort scope:** Config-only once a 511.org API key is obtained; no adapter code anticipated independent of the key.

## Open items for a follow-up pass

- Register for a 511.org Open Data API key (`https://511.org/open-data/token`) and re-run the STAGE 4 evaluation against `http://api.511.org/transit/vehiclepositions?agency=SF` with the key applied.
- Re-verify the static GTFS zip is reachable (from an environment with network access to `sfmta.com`/`data.sfgov.org`) and parse it directly to confirm route_id format and that `route_type=1` is genuinely absent (public documentation suggests Muni Metro/streetcars/cable cars are `route_type=0`/`5`, not heavy rail).
- If a live sample later shows unmatched RT route IDs, check them against the platform's `RouteIdNormalizer` transforms before assuming new code is needed.
