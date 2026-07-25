# GTFS Compatibility Report — Sound Transit (Seattle, Washington)

> ## 12/100 — Not Viable
> Bus: 0/70 (blocked — no live feed measured) · Rail: 12/20 · Credential: 0/10 (blocked)
> Ceiling applied: KEY-GATED, capped at 40
> Vehicle positions (Link light rail, Sounder commuter rail, ST Express buses) exist only
> behind a OneBusAway API key requested by email with a stated ~20-business-day
> turnaround; key acquisition is out of scope for this run. The only keyless realtime
> source (service alerts) carries no lat/lon and cannot substitute. Computed per
> `aggregate-score-formula.md`; the rail component comes from a real desk-check against
> static + the keyless alerts feed's route identifiers, never a guess.

**Evaluated:** 2026-07-25

> **Why this report exists as a companion to `kcm.md`:** Sound Transit and King County
> Metro are **separate agencies** serving the same Seattle metro. KCM's report (evaluated
> the same day) covers buses and streetcars only — it explicitly does not carry Link
> light rail. This report evaluates Sound Transit, the agency that actually operates
> Link, Sounder commuter rail, and ST Express regional buses, so a reviewer isn't left
> assuming Seattle is "fully covered" by the KCM report alone. **Do not merge the two
> reports' verdicts** — they are independent axes for two independent authorities that
> happen to share one metro area, exactly like WMATA and a hypothetical separate DC
> streetcar operator would be.

## Feed health

| | |
|---|---|
| **Blocking classification** | **KEY-GATED** — see the sub-reason table this template opens with |
| Static GTFS URL | `https://www.soundtransit.org/GTFS-rail/40_gtfs.zip` — verified live, HTTP 200, 1,385,261 bytes zipped |
| GTFS-RT vehicle positions (buses + rail) | Exists as standard GTFS-RT protobuf (served via the OneBusAway API, which "supports trip updates and vehicle positions" for Sound Transit agency ID 40 and all King County Metro routes), but requires a registered API key not already available in the environment. Key acquisition is out of scope for this run — the request process (email `oba_api_key@soundtransit.org`, ~20 business days processing) is a human, out-of-band step this skill must never attempt. The platform's generic city path already supports a config-only key once obtained (`ApiKeyEnvVar`/`ApiKeyQueryParam`). |
| Rail realtime (trains) | Same OneBusAway key-gate as buses — Link light rail (1 Line, 2 Line) and Sounder commuter rail (N Line, S Line) vehicle positions are not published through any separate, keyless channel. |

Searched Sound Transit's own Open Transit Data (OTD) portal (developer resources +
downloads pages), which explicitly names OneBusAway as the sole realtime vehicle-position
source and states the API key requirement plainly, rather than merely omitting a URL. The
**only** keyless realtime Sound Transit publishes directly is GTFS-RT **service alerts**
(`https://s3.amazonaws.com/st-service-alerts-prod/alerts.pb`, protobuf, and a JSON mirror
at `.../alerts_pb.json`) — no lat/lon, alerts-only, cannot substitute for vehicle
positions per the feed-discovery playbook's "trip-updates/alerts only" row. This is
structurally different from KCM's situation (evaluated the same day): KCM has a second,
undocumented-but-real keyless S3 path for actual vehicle positions; Sound Transit's only
keyless S3 path is alerts, not positions.

## Static GTFS (verified by direct parse)

| | |
|---|---|
| Routes | 8 total, all with shapes |
| `route_type=3` (bus) | 3 routes — `1-SHUTTLE`, `2-SHUTTLE`, `TLINE_S` (bus bridges/shuttles substituting for rail segments, not the ST Express regional network itself, which appears to live in the separate consolidated regional GTFS, not this rail-specific zip) |
| `route_type=0` / `route_type=2` (rail) | 5 routes — `100479` (1 Line, Lynnwood–Federal Way, light rail), `2LINE` (2 Line, Lynnwood–Downtown Redmond, light rail), `TLINE` (T Line, Tacoma Dome–St Joseph, light rail), `SNDR_EV` (N Line, Everett–Seattle, Sounder commuter rail), `SNDR_TL` (S Line, Seattle–Tacoma/Lakewood, Sounder commuter rail) |

**Important classifier note:** Link light rail's static `route_type` is **`0`** (tram/light
rail), not `1` (subway/metro) — the report-template's default "Rail (`route_type=1`)"
section header doesn't literally apply. Per this platform's actual classifier
(`GtfsStaticLoader.cs`, confirmed via the `route_type classifier` finding from a prior
evaluation), `route_type` values 0, 1, **and** 2 are **all** treated as `TransitMode.Rail`
— so Link (`0`) and Sounder (`2`) would both render as Rail if a live feed ever existed,
exactly like RTD's light-rail (`0`) and commuter-rail (`2`) lines did in that separate
evaluation. This is a naming-convention footnote, not a compatibility finding.

## Rail line-key alignment (desk-check against the keyless service-alerts feed)

Sound Transit's GTFS-RT **service alerts** feed (the only keyless realtime source
reachable) references `informed_entity.route_id` values `2LINE`, `100479`, `SNDR_TL`, and
`SNDR_EV` in live alert entries fetched during this evaluation — **identical, verbatim,**
to the static `route_id` values above. This is not proof a vehicle-positions feed would
align this cleanly (alerts and positions are different endpoints, potentially maintained
independently), but it is a real signal from a live, keyless fetch that Sound Transit's
`route_id` scheme is internally consistent between at least one realtime channel and
static — **would likely need zero transform** if OneBusAway's vehicle-positions response
uses the same `route_id` values (OneBusAway's REST API conventionally does mirror the
underlying agency's native route IDs).

## Vehicle positions / route ID alignment (buses + rail)

**Not assessable without a live feed.** Vehicle positions for every Sound Transit mode —
Link, Sounder, and ST Express — sit behind the same OneBusAway API key; no keyless
sample could be pulled. Field completeness (route_id %, lat/lon %, speed/bearing %)
cannot be measured until a developer API key is obtained (email request to
`oba_api_key@soundtransit.org`, ~20 business day stated turnaround) and a live sample is
pulled through the authenticated OneBusAway endpoint.

## Verdict

- **Buses (ST Express): INCOMPATIBLE (KEY-GATED)** — OneBusAway vehicle-positions requires a key not available in this run; config-only fix once a key exists
- **Rail: INCOMPATIBLE (KEY-GATED)** — same OneBusAway key-gate covers Link and Sounder; static geometry and a route-id desk-check (via the keyless alerts feed) both look clean, but no live position check was possible

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **UNASSESSED** — no vehicle-positions feed reachable without a OneBusAway key |
| Route ID alignment (buses) | **N/A** — nothing to align against |
| Rail line alignment | **Would PASS (100%, zero transform) — verified from static + the keyless alerts feed's live route_id values, but not from an actual vehicle-positions fetch** |
| Blocking classification | **KEY-GATED** |

**Bottom line:** Sound Transit's data is clean where it's reachable — static GTFS is
public and well-formed, and even the one keyless realtime channel (service alerts) uses
`route_id` values that match static verbatim, a good sign for what a vehicle-positions
feed would look like. But the actual vehicle-position data — the one thing this platform
needs — sits entirely behind a human-mediated OneBusAway API key request with a ~20
business day turnaround, which this run correctly does not attempt. This is a
**config-only fix once a key is obtained**: no new code, no bespoke adapter, just an
`ApiKeyEnvVar`/`ApiKeyQueryParam` CityConfig entry and (per the static parse above) likely
zero route-ID transform. Seattle is genuinely bus-only *for now* via King County Metro
(`kcm.md`, evaluated the same day, 100/100 Drop-in) — Link/Sounder/ST Express onboarding
is real, believed-low-effort work gated purely on obtaining that key, not on any
structural incompatibility.

## Adding Sound Transit as a data source

- **Static GTFS zip:** `https://www.soundtransit.org/GTFS-rail/40_gtfs.zip` — no auth required, drop-in as a config-only `CityConfig` entry. (Sound Transit also publishes per-agency and consolidated regional GTFS zips on the same OTD downloads page; this evaluation used the dedicated rail zip since Link/Sounder is the differentiator from the already-evaluated KCM report.)
- **Bus + rail realtime:** OneBusAway API (`https://developer.onebusaway.org/api/where`) — registered API key needed (email `oba_api_key@soundtransit.org`); once obtained, this is a config-only `CityConfig` entry (`ApiKeyEnvVar`/`ApiKeyQueryParam`) — no new code. Both buses (ST Express) and rail (Link, Sounder) ride the same OneBusAway-mediated source, so one key unlocks both axes.
- **Rail realtime:** same OneBusAway source and same key as bus realtime above — no separate rail-specific credential or endpoint. If a `CityConfig.RailRouteIdMap` ends up needed at all, the alerts-feed desk-check above suggests it would be a no-op (verbatim match), pending confirmation once a key exists.
- **Auth:** one OneBusAway API key covers all realtime data (bus + rail); per this repo's existing precedent, store it via env var / secrets, never commit it. Static GTFS needs no credential.
- **Config entry vs. new code:** config-only once a key exists — a `CityConfig` entry (`Cities:` array in both the Worker's and WebAPI's `appsettings.json`, byte-identical) with `GtfsRtUrls` pointed at the OneBusAway vehicle-positions endpoint, `ApiKeyEnvVar`/`ApiKeyQueryParam`, and `StaticZipUrls` pointed at the rail zip above. No new `ITransitCity` implementation needed — this fits the same generic `GtfsRtCity` path every other config-only city uses.
- **Effort scope:** config-only once a key is obtained — no new adapter code independent of the key, unlike a NO-USABLE-FEED case.

## Open items for a follow-up pass

- Request a Sound Transit OneBusAway API key via `oba_api_key@soundtransit.org` (stated ~20 business day turnaround — plan around this lead time, it is the single blocking dependency).
- Once a key exists, pull a live OneBusAway vehicle-positions sample and measure route_id/lat-lon/speed/bearing completeness the same way KCM and other agencies were measured, and confirm the `route_id` scheme actually matches static verbatim in the positions response (not just in the alerts feed checked here).
- Decide whether to onboard Sound Transit as a fully separate `CityConfig` entry (distinct map pin, own picker button) or attempt to merge Link/Sounder into the existing KCM entry as a second `GtfsRtUrls` source — the `NymtaCity`/`BusGtfsRtUrls` precedent in this codebase already supports a two-feed city if merging is preferred over a second Seattle-area entry.
