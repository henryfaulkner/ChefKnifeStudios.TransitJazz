# GTFS Compatibility Report — ATM (Azienda Trasporti Milanesi) (Milan, Italy)

> ## 0/100 — Not Viable
> Bus: 0/70 (blocked — no live feed measured) · Rail: 0/20 (blocked) · Credential: 0/10 (blocked)
> Ceiling applied: NO-USABLE-FEED, capped at 15
> No realtime feed of any format — GTFS-RT or otherwise — is publicly published for ATM;
> even a hypothetical clean rail desk-check couldn't be run because no rail API
> documentation was found either. Computed per `aggregate-score-formula.md`.

**Evaluated:** 2026-08-13

## Feed health

| | |
|---|---|
| **Blocking classification** | **NO-USABLE-FEED** — see the sub-reason table this template opens with |
| Static GTFS URL | `https://dati.comune.milano.it/gtfs.zip` — verified live, HTTP 200, ~55.4 MB zipped |
| GTFS-RT vehicle positions (buses) | Does not exist. No `.pb` protobuf feed is published for ATM buses/trams. |
| Rail realtime (trains) | Does not exist. No `.pb` protobuf feed and no separate rail-specific API documentation were found for the Metro (route_type=1). |

Searched the agency's own developer surface (AMAT — the Comune di Milano's mobility agency
that publishes ATM's GTFS — has no realtime section on its site), the full Comune di Milano
open-data "Trasporti" catalog (256 datasets, none live), Transitland's operator/feed pages
(exactly one feed listed for ATM: the static GTFS), and MobilityDatabase (no GTFS-RT entry
surfaced for Milan). No GTFS-RT gateway of any kind — vehicle-positions, trip-updates, or
alerts — is exposed. The only "real-time" surface that exists is ATM's in-app arrival
predictions, backed by an undocumented internal service (community name "GiroMilano") that
several third-party GitHub projects have reverse-engineered for next-departure countdowns;
per the skill's non-goals this is not something to build against — it is unofficial,
undocumented, and (like `mvv.md`'s finding on the unofficial MVG API) not a vehicle-position
source in any case, so it wouldn't unblock this evaluation even if used.

## Static GTFS (verified by direct parse)

| | |
|---|---|
| Routes | 166 total, 166 have shapes |
| `route_type=3` (bus) | 144 routes — `route_id`/`route_short_name` are plain rider-facing numeric strings (e.g. `121`, `165`) |
| `route_type=1` (rail) | 5 routes — `1, 2, 3, 4, 5` (Milan Metro lines M1–M5) |

One quirk worth flagging for any future onboarding pass: ATM also runs 17 `route_type=0`
(tram) routes, and five of them — `T1`/`T2`/`T3`/`T4`/`T5` — carry the identical rider-facing
`route_short_name` values `1`–`5` as the five Metro lines. The worker keys its route index by
`routeShortName` (falling back to `routeId`), so a single unqualified live feed carrying both
trams and Metro under these short names would collide; a real onboarding pass would need to
key rail off `route_id` (`M1`–`M5`) rather than `route_short_name` to avoid mis-snapping tram
vehicles onto Metro shapes or vice versa. This is a static-data observation only — no live
feed exists to actually exercise it.

## Vehicle positions / route ID alignment (buses)

**Not assessable without a live feed.** No GTFS-RT vehicle-positions endpoint of any kind is
published for ATM — not standard protobuf, not a proprietary equivalent. Field completeness
(route_id %, lat/lon %, speed/bearing %) cannot be measured until ATM (or AMAT on its behalf)
publishes one.

## Verdict

- **Buses: INCOMPATIBLE (NO-USABLE-FEED)** — no GTFS-RT or proprietary vehicle-position feed of any kind is published.
- **Rail: INCOMPATIBLE (NO-USABLE-FEED)** — Metro (M1–M5) has clean static geometry but no live position source was found, official or otherwise.

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **UNASSESSED** — no feed reachable at all, buses or rail |
| Route ID alignment (buses) | **N/A** — nothing to align against |
| Rail line alignment | **UNASSESSED** — no rail API documentation was found to desk-check against `static.rail_index_keys` |
| Blocking classification | **NO-USABLE-FEED** |

**Bottom line:** Static GTFS is clean, keyless, and complete — 166/166 routes carry shapes,
including all 5 Metro lines. But there is no realtime layer to build on: no GTFS-RT
vehicle-positions feed exists for buses or trams, and no separate rail-realtime API (official
or documented) exists for the Metro either — this is a structurally different problem from a
credential gate, closer to `tfl.md`'s and `idfm.md`'s findings than to the KEY-GATED cases in
this series. Onboarding ATM today would require a net-new `ITransitCity` implementation built
against whatever internal service ATM's own app uses, none of which is public or documented,
plus reverse-engineering effort this skill is explicitly barred from doing (no CAPTCHA
solving, no unofficial API scraping presented as a data source). This is materially more
effort than any KEY-GATED report in this series.

## Adding ATM as a data source

- **Static GTFS zip:** `https://dati.comune.milano.it/gtfs.zip` — no auth required, drop-in as a config-only `CityConfig` entry for the static side alone.
- **Bus realtime:** none found — would need a net-new `ITransitCity` implementation normalizing whatever internal feed ATM's own app consumes into the platform's feed format, mirroring the one bespoke city implementation that already does this for a different agency. No such feed is publicly documented today.
- **Rail realtime:** none found — same treatment as bus realtime; no config-only `RailRouteIdMap` path exists because there is no live feed of any kind to remap from.
- **Auth:** Not applicable — there is no discovered realtime endpoint to authenticate against. The static zip needs none.
- **Config entry vs. new code:** NO-USABLE-FEED — requires a new bespoke `ITransitCity` implementation; there is no existing config-only path for this feed shape (or evidence one currently exists to reverse-engineer safely).
- **Effort scope:** new adapter code independent of any key — and, unlike the KEY-GATED cases in this series, there is no known credential that would even unlock one.

## Open items for a follow-up pass

- Watch AMAT/Comune di Milano's open-data portal and MobilityDatabase for a future GTFS-RT publication — ATM has not announced one as of this evaluation.
- If ATM ever documents its internal "GiroMilano" arrivals service as a public API, re-evaluate whether it (or a companion feed) carries actual vehicle positions, not just stop-level predictions.
- Note the `route_short_name` collision between trams (`T1`–`T5`) and Metro (`M1`–`M5`) for whoever eventually onboards ATM — rail keying should use `route_id`, not `route_short_name`.
