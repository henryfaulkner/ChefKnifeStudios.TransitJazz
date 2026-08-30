# GTFS Compatibility Report — Île-de-France Mobilités (IDFM) (Paris, Île-de-France)

> ## 0/100 — Not Viable
> Bus: 0/70 (blocked — no live feed measured) · Rail: 0/20 · Credential: 0/10 (blocked)
> Ceiling applied: NO-USABLE-FEED, capped at 15
> No vehicle-position data of any format — GTFS-RT or otherwise — is published for either
> buses or the Métro; a net-new bespoke adapter would be required regardless of any key,
> and even then there is nothing to adapt to today.

**Evaluated:** 2026-08-06

## Feed health

| | |
|---|---|
| **Blocking classification** | **NO-USABLE-FEED** — see the sub-reason table this template opens with |
| Static GTFS URL | `https://eu.ftp.opendatasoft.com/stif/GTFS/IDFM-gtfs.zip` — verified live, HTTP 200, ~151.9 MB zipped |
| GTFS-RT vehicle positions (buses) | **Does not exist.** No `.pb` protobuf feed is published for IDFM/RATP buses. IDFM's only public real-time offering is SIRI Lite, and its exposed resources are next-departure predictions and traffic messages — not vehicle positions. |
| Rail realtime (trains) | **Does not exist**, for the same reason — the Métro's live GPS positions are never surfaced to external consumers in any format. |

Searched IDFM's own developer portal (PRIM, `prim.iledefrance-mobilites.fr`), the canonical
dataset listing on `transport.data.gouv.fr`, and targeted web search for "IDFM GTFS-RT
vehicle positions" / "SIRI-VM" / "RATP real-time API." IDFM's public catalog for its urban
network dataset lists exactly five non-documentation resources: a static GTFS zip, a static
NeTEx zip, and three SIRI Lite real-time resources — **stop-monitoring** (next-departure
predictions, unitary request), **estimated-timetable** (next-departure predictions, global
query), and **general-message** (traffic disruption text). None of the three carry a
vehicle's lat/lon. SIRI itself defines a fourth real-time service, **Vehicle Monitoring
(SIRI-VM)**, which does carry live GPS — IDFM's own documentation confirms operators
(RATP, Optile, SNCF Transilien) transmit SIRI-VM data *into* the PRIM platform so IDFM can
compute its next-departure predictions — but no SIRI-VM (or equivalent GTFS-RT
`VehiclePositions`) resource is exposed *out* to external API consumers anywhere in the
public catalog. This is the same structural gap as `tfl.md`'s finding: the underlying
system tracks vehicle positions internally, but nothing downstream of it is a consumable
position feed.

The two next-departure endpoints are, separately, also key-gated — confirmed live:

```
$ curl -i https://prim.iledefrance-mobilites.fr/marketplace/stop-monitoring
HTTP/2 401
www-authenticate: Key
{"message":"No API key found in request"}
```

Per the feed-discovery playbook's rule for this exact combination ("only a proprietary
non-GTFS-RT API is published... if it's ALSO key-gated, still classify NO-USABLE-FEED — the
missing protobuf format dominates, since even a key wouldn't make it consumable without new
code"), this stays **NO-USABLE-FEED** rather than KEY-GATED: obtaining a PRIM key would only
unlock arrival-prediction text and disruption messages, neither of which is a vehicle
position — it would not make this feed consumable by the worker's `GtfsRtCity` path at all.

## Static GTFS (verified by direct parse)

| | |
|---|---|
| Routes | 1,923 total, 1,923 have shapes (100%) |
| `route_type=3` (bus) | 1,864 routes — `route_id` values are internal IDFM codes (e.g. `IDFM:C01624`); `route_short_name` carries the rider-facing line number |
| `route_type=1` (rail — Métro) | 16 routes — keys `1, 2, 3, 3B, 4, 5, 6, 7, 7B, 8, 9, 10, 11, 12, 13, 14` (the full Paris Métro network, verbatim line numbers) |

The dataset also carries 17 `route_type=0` (tram) and 24 `route_type=2` (RER/Transilien
commuter rail) routes, both out of this platform's current Bus/Rail(=1) scope. `route_id`
is an opaque IDFM-internal identifier, not the rider-facing line number — a live GTFS-RT
feed's `trip.route_id` would need to be checked against IDFM's own scheme (not necessarily
`route_short_name`) if a position feed is ever published, but this can't be determined
without one.

## Vehicle positions / route ID alignment (buses)

**Not assessable without a live feed.** No GTFS-RT (or SIRI-VM) vehicle-position resource
is published for IDFM's network in any format reachable by this run — see Feed health
above. Field completeness (route_id %, lat/lon %, speed/bearing %) cannot be measured until
IDFM (or RATP directly) publishes an external vehicle-position feed; a registered PRIM key
would not change this, since none of PRIM's exposed real-time resources carry positions.

## Verdict

- **Buses: INCOMPATIBLE (NO-USABLE-FEED)** — no protobuf or SIRI-VM vehicle-position feed of any kind is exposed to external consumers.
- **Rail: INCOMPATIBLE (NO-USABLE-FEED)** — the Métro's live positions have the identical gap as buses; no separate rail-specific position API exists either.

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **UNASSESSED** — no feed reachable without a vehicle-position resource existing at all |
| Route ID alignment (buses) | **N/A** — nothing to align against |
| Rail line alignment | **N/A** — no rail position feed exists to desk-check against the 16 static Métro line keys |
| Blocking classification | **NO-USABLE-FEED** |

**Bottom line:** Static GTFS is clean, keyless, and complete — 1,923 routes, 100% with
shapes, including a fully-enumerated 16-line Métro static network. Everything real-time is
blocked at a more fundamental level than credentials: IDFM's public API surface (PRIM)
exposes only next-departure predictions and traffic messages, never a vehicle's GPS
position, for buses or trains alike. A registered API key would not fix this. Onboarding
IDFM/RATP would require a net-new, bespoke position-tracking source — and today there is no
known public one to adapt to, unlike `cta.md`'s case where at least a proprietary
Bus/Train Tracker API exists to wrap.

## Adding Île-de-France Mobilités as a data source

- **Static GTFS zip:** `https://eu.ftp.opendatasoft.com/stif/GTFS/IDFM-gtfs.zip` — no auth required, drop-in as a config-only `CityConfig` entry for the static side alone.
- **Bus realtime:** none published — no GTFS-RT equivalent exists at all (not merely key-gated); there is no known API to wrap even with a bespoke `ITransitCity` implementation, since IDFM does not expose vehicle positions to any external consumer today.
- **Rail realtime:** none published — same gap as bus realtime; no `RailRouteIdMap` remap is possible without a live feed to remap from.
- **Auth:** PRIM (the developer portal) requires a registered API key for its three exposed real-time resources (stop-monitoring, estimated-timetable, general-message), none of which carry vehicle positions — so a key would need to be provisioned via env/secrets if IDFM's prediction/disruption data were ever wanted for some other purpose, but it does nothing for this platform's live-vehicle use case.
- **Config entry vs. new code:** requires a new bespoke `ITransitCity` implementation if a position source is ever found; there is no existing config-only path for this feed shape, and no such source is known to exist publicly as of this evaluation.
- **Effort scope:** new adapter code independent of any key — and contingent on a vehicle-position data source existing at all, which none currently does.

## Open items for a follow-up pass

- Re-check IDFM's PRIM catalog periodically for a newly-published SIRI-VM or GTFS-RT `VehiclePositions` resource — IDFM's own "Le Lab" blog has publicly discussed rolling out real-time bus position display in its rider-facing app, which implies the underlying data exists internally and could eventually be exposed via PRIM.
- If RATP (rather than IDFM) ever publishes its own independent vehicle-position feed on `data.ratp.fr` (distinct from what IDFM exposes), re-evaluate under that authority instead.
- If a position feed does appear, pull a live sample and measure route_id/lat-lon/speed/bearing completeness the same way other agencies were measured, and check `route_id` alignment against IDFM's internal (`IDFM:Cxxxxx`) scheme rather than assuming `route_short_name` matches verbatim.
