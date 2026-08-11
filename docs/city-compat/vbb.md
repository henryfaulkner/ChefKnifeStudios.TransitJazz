# GTFS Compatibility Report — VBB (Verkehrsverbund Berlin-Brandenburg) (Berlin-Brandenburg, Germany)

> ## 15/100 — Not Viable
> Bus: 0/70 (blocked — no live feed measured) · Rail: 20/20 (blocked-path desk check) · Credential: 0/10 (blocked)
> Ceiling applied: NO-USABLE-FEED, capped at 15
> No usable vehicle-positions data is currently being produced by VBB's otherwise-standard,
> keyless GTFS-RT feed — a confirmed, ongoing source-side outage with no stated ETA, not a
> credential problem. Computed per `aggregate-score-formula.md`; the rail component reflects
> a strict `route_type=1` desk check on the static data (0 routes) — see the quirk note below
> for why that undercounts VBB's real rail network.

**Evaluated:** 2026-08-11

## Feed health

| | |
|---|---|
| **Blocking classification** | **NO-USABLE-FEED** — see the sub-reason table this template opens with |
| Static GTFS URL | `https://unternehmen.vbb.de/gtfs` — verified live, HTTP 200, ~80.8 MB zipped, extracted to routes/trips/shapes/stops/etc. |
| GTFS-RT vehicle positions (buses) | Reachable, standard protobuf, keyless — decoded to a bare 15-byte `FeedHeader` (version `2.0`, timestamp `0`) with **zero `FeedEntity` records**, confirmed on two independent fetches ~5s apart. VBB's own feed-status page states plainly: *"Due to problems with the data source behind this feed, the feed has been lacking some data since 2026-06-04 16:00. We currently don't have an estimation for when it will be available again."* As of this evaluation that is a **68+ day, VBB-acknowledged, open-ended outage** — not a transient time-of-day gap, so a further retry within this run would not change the classification. |
| Rail realtime (trains) | **N/A — 0 `route_type=1` routes in static** (strict definition; see the quirk note in the Static GTFS section — VBB's real rail network is tagged with GTFS *extended* route_type codes, not `1`). No separate rail-specific realtime API was found; when the combined feed is healthy, rail vehicles are expected to ride the same endpoint as buses under different `route_id` values. |

Searched VBB's own open-data portal (`unternehmen.vbb.de/digitale-services/datensaetze`), the
GTFS-RT feed's own status page (`production.gtfsrt.vbb.de`), and the GitHub project backing
it (`OpenDataVBB/gtfs-rt-feed`, linked from that status page) — all three converge on the
same single, keyless, protobuf endpoint at `https://production.gtfsrt.vbb.de/data` as VBB's
sole official realtime source (a `staging.gtfsrt.vbb.de/data` mirror exists for developer
testing only and is not used here). The format and access are both fine — the blocker is
that VBB's own upstream data pipeline feeding this endpoint is currently broken, a
structurally different problem from every other BLOCKED report in this series so far (all of
which were credential- or format-blocked, never "reachable but genuinely empty").

## Static GTFS (verified by direct parse)

| | |
|---|---|
| Routes | 1,231 total, 1,231 have shapes (100%) |
| `route_type=3` (bus) | 35 routes — plain rider-facing `route_short_name` values (e.g. `686`, `338`, `670`, `688`); these carry the *basic* GTFS bus code, distinct from the 1,014 routes below tagged with VBB's own extended bus code |
| `route_type=1` (rail) | 0 routes — none tagged with the basic heavy-rail code (see quirk note below) |

**Quirk — VBB uses the GTFS *extended* (hierarchical) `route_type` vocabulary almost
exclusively**, not the basic 0–7 codes every other agency in this series has used:
`700`=bus (1,014 routes, incl. night `N`-prefixed lines), `900`=tram (48, e.g. Berlin's
numbered tram network `1`–`M17`), `400`=U-Bahn/urban railway (10 lines, `U1`–`U9`),
`109`=S-Bahn/suburban railway (49 route entries across the `S1`–`S9`/`S25`/`S41`/`S42`/`S45`
family), `106`=regional rail (15, `RBxx`), `100`=mainline railway/Regional-Express (53,
`RExx`/`RBxx`), and `1000`=ferry (7, `Fxx`). Only 35 routes carry the basic code `3`. This
means the platform's default rail classifier (`GtfsStaticLoader.ClassifyCategory`'s
no-config fallback, which recognizes only the literal strings `0`/`1`/`2`) would
misclassify essentially all of VBB's rail as bus unless a per-city `RouteTypeCategories`
config entry is added mapping `400`/`109`/`106`/`100`→`rail` (and, mirroring this report
series' existing TTC streetcar precedent, likely `900`→`streetcar`) — the same config-only
mechanism TTC and WMATA already use, not new code, but a materially larger mapping (7
distinct codes) than any other agency evaluated so far. Separately, `route_id` values are
compound strings like `17526_400` rather than the plain rider-facing name (`U9`) — whether
a live GTFS-RT feed would publish `route_id`s in this compound form or the plain form is
unconfirmed without a live sample.

The downloaded zip also had 1 stray leading byte and one corrupted internal file
(`stop_times.txt`, unused by this evaluation — bad CRC); `routes.txt`/`trips.txt`/
`shapes.txt` parsed cleanly and their counts are internally consistent (1,231 routes sum
exactly across all 8 `route_type` values), so this doesn't affect this report's numbers, but
a future onboarding pass should re-verify a fresh download isn't corrupted.

## Vehicle positions / route ID alignment (buses)

**Not assessable without a live feed.** VBB's GTFS-RT endpoint is reachable, keyless, and
standard protobuf, but currently publishes zero `FeedEntity` records due to the confirmed,
ongoing source-side outage described above. Field completeness (route_id %, lat/lon %,
speed/bearing %) cannot be measured until VBB restores upstream data to this feed.

## Verdict

- **Buses: INCOMPATIBLE (NO-USABLE-FEED)** — feed is reachable, keyless, and standard-format, but has published zero vehicle entities for 68+ days per VBB's own status page, with no ETA.

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **UNASSESSED** — no feed reachable with usable entities to measure |
| Route ID alignment (buses) | **N/A** — nothing to align against |
| Rail line alignment | **N/A** — 0 `route_type=1` routes in static (see quirk note — VBB's real rail network is tagged with extended codes `400`/`109`/`106`/`100` instead) |
| Blocking classification | **NO-USABLE-FEED** |

**Bottom line:** VBB's static schedule data is clean, keyless, current (updated twice
weekly), and — on access alone — the friendliest realtime setup seen in this series so far
(a single keyless standard-protobuf endpoint with a generous 60 req/min limit and no
developer-registration flow at all). But it is currently producing zero vehicles because
VBB's own upstream data pipeline has been broken for over two months as of this evaluation,
with no stated ETA — a "the door is unlocked but the room is empty" case, distinct from
every other BLOCKED report in this series (all previously credential- or format-blocked).
No bespoke adapter is anticipated once VBB's data returns, but this run cannot predict when
that will happen. Separately and regardless of the outage, VBB's near-universal use of the
GTFS extended `route_type` vocabulary means a future onboarding pass will need a
non-trivial (7-code) `RouteTypeCategories` config entry to correctly separate its
substantial U-Bahn/S-Bahn/regional-rail network from its bus network — still config-only,
just a bigger mapping than any agency evaluated so far.

## Adding VBB as a data source

- **Static GTFS zip:** `https://unternehmen.vbb.de/gtfs` — canonical, always resolves to the current file; no auth required. The zip downloaded during this run had a minor internal corruption (1 stray leading byte, bad CRC on `stop_times.txt`, a file this report doesn't need) — re-verify a fresh download before onboarding.
- **Bus realtime:** `https://production.gtfsrt.vbb.de/data` — no GTFS-RT format/access problem exists; it currently produces zero live vehicles due to an ongoing, VBB-acknowledged source outage (since 2026-06-04, no ETA). Once VBB's upstream data recovers, this needs re-evaluation with a live sample, not new code — the format and access are already standard and keyless.
- **Rail realtime:** n/a — no separate rail API found; when healthy, rail vehicles are expected to ride the same combined GTFS-RT feed as buses (VBB's own docs don't split the feed by vehicle mode).
- **Auth:** None. The feed is keyless up to 60 requests/minute; VBB asks for (does not enforce) an informative User-Agent header and a reverse-DNS record.
- **Config entry vs. new code:** Ambiguous until the feed recovers — the access/format side looks config-only (a standard `CityConfig` entry, no bespoke `ITransitCity`), but this report cannot confirm bus/rail compatibility since no live sample was obtainable. Re-run this evaluation once VBB's status page no longer reports the outage.
- **Effort scope:** Blocked on an external, VBB-side data recovery — not a key, not new code. Once resolved, expected effort is "config-only, but with an unusually large `RouteTypeCategories` mapping" per the quirk noted above.

## Open items for a follow-up pass

- Re-check `https://production.gtfsrt.vbb.de/` (or subscribe to the Atom/RSS status feed it links) for the outage to clear, then re-run this evaluation with a live sample.
- Once live, measure route_id / lat-lon / speed / bearing completeness the same way other agencies were measured, and confirm whether the RT feed's `route_id` values are the plain rider-facing name (`U9`) or the static zip's compound form (`17526_400`).
- Draft the `RouteTypeCategories` mapping for VBB's 7 non-basic-bus extended codes (`400`/`109`/`106`/`100`→`rail`, `900`→`streetcar` per the TTC precedent, `1000`→ferry/uncategorized) ahead of any future onboarding pass.
- Re-verify the static zip downloads without corruption before onboarding (this run's download had 1 stray leading byte).
