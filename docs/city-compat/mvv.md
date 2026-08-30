# GTFS Compatibility Report — MVV (Münchner Verkehrs- und Tarifverbund) (Munich, Germany)

> ## 0/100 — Not Viable
> Bus: 0/70 (blocked — no live feed measured) · Rail: 0/20 (no desk check possible) · Credential: 0/10 (blocked)
> Ceiling applied: NO-USABLE-FEED, capped at 15
> No realtime feed of any kind — GTFS-RT or otherwise — is publicly published for MVV's
> network; the only "live" source found is an undocumented, unofficial, reverse-engineered
> departures API with no vehicle positions. Computed per `aggregate-score-formula.md`;
> rail scores 0 rather than the N/A-case 20 because MVV's U-Bahn genuinely carries the
> basic `route_type=1` code (unlike `vbb.md`'s extended-vocabulary case) but there is no
> live feed or published rail API of any kind to desk-check its integration mechanism
> against.

**Evaluated:** 2026-08-12

## Feed health

| | |
|---|---|
| **Blocking classification** | **NO-USABLE-FEED** — see the sub-reason table this template opens with |
| Static GTFS URL | `https://www.mvv-muenchen.de/fileadmin/mediapool/developer/opendata/gesamt_gtfs.zip` — verified live, HTTP 200, ~16.9 MiB (17,679,535 bytes) zipped |
| GTFS-RT vehicle positions (buses) | **Does not exist. No `.pb` protobuf feed of any kind — vehicle-positions, trip-updates, or alerts — is published for MVV.** |
| Rail realtime (trains) | Same finding as buses — no separate rail-specific realtime API is published either, documented or otherwise. |

Searched MVV's own developer page (`mvv-muenchen.de/service-hilfe/mvv-content-fuer-entwickler/`,
redirected from the legacy `/fahrplanauskunft/fuer-entwickler/opendata/` path), the Mobility
Database and Transitland (`transit.land/feeds/f-u281z9-mvv` lists only the static feed, 35
archived static versions, zero GTFS-RT entries), and several targeted `WebSearch` queries
(German and English) for "MVV GTFS-RT", "MVG Echtzeit API Fahrzeugpositionen", and similar —
all converge on the same result: MVV's developer page offers **only** two static GTFS zips
(full-network and regional-bus-only), a handful of CSV reference files, and a **TRIAS**
(VDV-Standard 431) request/response interface listed as "demnächst verfügbar" (coming soon)
— TRIAS is a departure/connection-query standard, not a GTFS-RT-shaped streaming
vehicle-position feed, and isn't live yet regardless. One adjacent but non-usable finding:
Deutsche Bahn's national developer portal (`developer-docs.deutschebahn.com`) does list a
GTFS-RT stream for "DB Regio S-Bahn München," but it carries only delays/cancellations/track
changes (trip-updates/alerts, not vehicle positions), requires a `DB-Api-Key` header
(key-gated, no key already in this environment), and covers only the S-Bahn slice of MVV's
network (buses, tram, and U-Bahn — the bulk of the fleet — aren't DB Regio's to publish at
all). The only thing resembling a "live" API is an **unofficial, reverse-engineered**
MVG endpoint (`mvg.de/api/bgw-pt/v3`, successor to a now-dead `mvg.de/api/fahrinfo`)
documented only by third-party GitHub wrappers (e.g. `mondbaron/mvg`, explicit disclaimer:
*"not an official project from the Münchner Verkehrsgesellschaft"*) — it exposes station
departures/timetables only, no vehicle GPS positions, and per this skill's non-goals is not
something this run attempts to build against regardless.

## Static GTFS (verified by direct parse)

| | |
|---|---|
| Routes | 898 total, **0 have shapes (0%)** |
| `route_type=3` (bus) | 828 routes |
| `route_type=1` (rail — U-Bahn) | 8 routes — `U1`, `U2`, `U3`, `U4`, `U5`, `U6`, `U7`, `U8` |

**Quirk — this static feed carries zero route geometry, in either published variant.**
`shapes.txt` is a header-only, zero-data-row file (`shape_id,shape_pt_lat,shape_pt_lon,
shape_pt_sequence,shape_dist_traveled` and nothing else) in both the full-network
`gesamt_gtfs.zip` (898 routes) and the regional-bus-only `mvv_gtfs.zip` (728 routes)
variants; a direct scan of `trips.txt` confirms 0 of 114,360 trips carry a non-empty
`shape_id`. This is independent of, and compounds, the missing-realtime-feed problem: even
in a hypothetical future where a live vehicle-positions feed appeared, the platform's
`RouteSnapper` would have no shape geometry to snap any MVV vehicle onto without a
separate shapes source. Two other GTFS route_type codes are present but out of this
report's `route_type=1` scope: `route_type=2` (33 routes — S-Bahn + regional/mainline
rail, the same code RTD uses for its commuter rail) and `route_type=0` (29 routes — tram,
the same code TTC's streetcars use, which this platform's default classifier treats as
Bus, not Rail). Both `route_id` and `route_short_name` values are plain, human-legible
strings (e.g. `U6`, `100`) rather than VBB's compound `17526_400`-style IDs — a route-ID
alignment transform would likely be a non-issue if a live feed ever existed, but this
can't be confirmed without one.

## Vehicle positions / route ID alignment (buses)

**Not assessable without a live feed.** No GTFS-RT protobuf endpoint of any kind is
published for MVV's network — not vehicle-positions, not trip-updates, not alerts. Field
completeness (route_id %, lat/lon %, speed/bearing %) cannot be measured until MVV (or a
constituent operator like MVG) publishes one.

## Verdict

- **Buses: INCOMPATIBLE (NO-USABLE-FEED)** — no GTFS-RT feed of any format is published; the only realtime-adjacent source found is an unofficial, undocumented departures API with no vehicle positions.
- **Rail: INCOMPATIBLE (NO-USABLE-FEED)** — U-Bahn is present in static (`route_type=1`, 8 lines) but has no live position source, published or otherwise, to even desk-check an integration mechanism against.

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **UNASSESSED** — no feed reachable to measure |
| Route ID alignment (buses) | **N/A** — nothing to align against |
| Rail line alignment | **UNASSESSED** — no live feed and no published rail API line codes to desk-check |
| Blocking classification | **NO-USABLE-FEED** |

**Bottom line:** MVV's static schedule data is clean, keyless, and current (both zips
verified live), but that's where the good news stops. No realtime feed of any format —
GTFS-RT or proprietary — is published for MVV's bus, tram, U-Bahn, or S-Bahn network; the
one DB-operated GTFS-RT stream that touches MVV's territory (S-Bahn) is both the wrong
feed type (predictions, not positions) and key-gated, and covers only a fraction of the
fleet regardless. Worse, even a hypothetical future live feed would hit a second,
independent blocker: MVV's static GTFS carries zero route shape geometry in either
published variant, so route-snapping has nothing to snap onto. Onboarding MVV would need,
at minimum, a net-new realtime data source (most plausibly by reverse-engineering and
formalizing MVG's undocumented `bgw-pt/v3` API into a bespoke `ITransitCity`
implementation, since no public protobuf equivalent exists) **and** a separate shapes
source to substitute for the missing geometry — this is materially more effort than a
typical BLOCKED finding in this series, which usually blocks on exactly one axis
(credentials or format), not two independent ones.

## Adding MVV as a data source

- **Static GTFS zip:** `https://www.mvv-muenchen.de/fileadmin/mediapool/developer/opendata/gesamt_gtfs.zip` — no auth required, but carries zero shape geometry (see quirk note above); a shapes source would need to come from somewhere else entirely.
- **Bus realtime:** none published — no GTFS-RT equivalent exists; would need a net-new `ITransitCity` implementation normalizing either a formalized version of MVG's unofficial `bgw-pt/v3` departures API (which has no positions today) or some other not-yet-identified source into the platform's feed shape.
- **Rail realtime:** none published — same treatment as bus realtime; no separate rail API exists to check whether a config-only `RailRouteIdMap` would suffice instead.
- **Auth:** No key is required for either static zip. Nothing to provision for realtime because no realtime endpoint of any kind exists yet to gate.
- **Config entry vs. new code:** Requires a new bespoke `ITransitCity` implementation (and a bespoke shapes source); there is no existing config-only path for a network with no live feed and no shape geometry at all.
- **Effort scope:** New adapter code independent of any key acquisition — and a second, unrelated blocker (missing shapes) on top of it.

## Open items for a follow-up pass

- Monitor MVV's developer page for the announced TRIAS (VDV-431) interface going live, and re-check at that time whether MVV or MVG separately ships a GTFS-RT vehicle-positions feed — neither exists as of this evaluation.
- If a future onboarding pass considers formalizing MVG's unofficial `bgw-pt/v3` API, confirm with MVG directly rather than relying on reverse-engineered third-party wrappers, and check whether it (or any successor) ever exposes vehicle GPS coordinates, not just departures.
- Identify an alternate shapes source (e.g. OpenStreetMap-derived route geometry) independent of MVV's own static export, since neither published GTFS variant carries any `shapes.txt` data.
- Re-pull the static zip and re-run the `route_type` breakdown if a live feed ever appears, to confirm route_id values on the RT side match the plain static naming observed here (`U6`, `100`, etc.).
