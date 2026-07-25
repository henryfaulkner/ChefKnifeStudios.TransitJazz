# GTFS Compatibility Report — TTC (Toronto, Ontario)

**Evaluated:** 2026-07-14

## Feed health

| | |
|---|---|
| GTFS-RT URL | `https://bustime.ttc.ca/gtfsrt/vehicles` |
| Static GTFS URL | `https://ckan0.cf.opendata.inter.prod-toronto.ca/dataset/7795b45e-e65a-4465-81fc-c36b9dfff169/resource/cfb6b2b8-6191-41e3-bda1-b175c51148cb/download/TTC Routes and Schedules Data.zip` |
| RT feed size | 93,433 bytes  •  Header ts: `0` (normal — per-vehicle timestamps present at 100%) |
| Static routes | 233 routes / 225 with shapes / 8 without |

> **Feed source note:** `bustime.ttc.ca/gtfsrt/*` is a standard GTFS-RT protobuf gateway
> (UMO/NextBus-backed). An earlier web search claimed TTC has "no standard GTFS-RT feed" —
> that is **outdated**. The `/vehicles`, `/trips`, `/alerts` endpoints are standard protobuf
> and decode cleanly with the MARTA field layout (position at field 2). The static zip is
> served from Toronto's Open Data (CKAN) portal, keyless.

## Vehicle positions (GTFS-RT)

| | |
|---|---|
| Total / vehicle entities | 1,426 / 1,426 |
| With `route_id` | **916 (64.2%)** |
| Without `route_id` | **510 (35.8%)** ← out-of-service / deadheading; skipped as `skippedNoRouteId` |
| lat/lon present | **100%** |
| speed present | 38.2% (optional — degrades gracefully) |
| bearing present | 99.5% |
| vehicle.timestamp | 100% |

This is a **surface-only** feed: buses + streetcars. It carries **no subway vehicle
positions** (see Rail below). Even after dropping the 510 route-less vehicles, **~916 live,
route-attributed surface vehicles** remain to drive the soundscape — denser than MARTA.

## Route ID alignment (buses + streetcars)

| | |
|---|---|
| RT distinct route IDs | 165 |
| Static index keys (`route_short_name ?? route_id`) | 233 |
| **Matched (as-is)** | **164 (99.4%)** |
| Unmatched RT IDs | `600` |
| Static-only keys | 69 (off-peak/inactive routes + the 3 subway lines with no surface RT) |

**RT `route_id` is a plain integer string; static `route_short_name` is the same integer
string — no transform needed.** The lone unmatched RT id `600` does not exist in the public
static schedule (TTC-internal special / community service, e.g. Wheel-Trans). Those vehicles
are silently counted `skippedUnknownRoute` — harmless, one route out of 165 (0.6%).

## Rail (heavy rail / `route_type=1`)

| | |
|---|---|
| Static rail routes | 3 — keys `1`, `2`, `4` (Line 1 Yonge-University / Line 2 Bloor-Danforth / Line 4 Sheppard) |
| Rail realtime API | **Not provided — TTC publishes no public live subway vehicle-position feed** |
| Live trains available | 0 (no feed exists) |
| LINE ↔ static match | N/A |

> Subway **geometry** is present in the static zip (`route_type=1`, with shapes), so the
> lines would load and could be drawn — but there is no live train-position source to
> animate them. **Rail is effectively N/A**, not merely "unassessed": the feed doesn't
> exist publicly, so no `RailRealtimeAdapter` work is possible or warranted right now.

### Streetcars are `route_type=0` (tram), not rail

TTC's route_type breakdown is **3 subway (`1`) + 210 bus (`3`) + 20 streetcar (`0`)**. The
worker's loader classifies only `route_type=1` as `TransitMode.Rail`; **everything else,
including `route_type=0` streetcars, is treated as Bus.** So Toronto's iconic 500-series
streetcars (501 Queen, 504 King, 505 Dundas, 506 Carlton, 510 Spadina, 512 St Clair, …)
load and snap correctly but ride the **bus instrument palette** unless dedicated tram
handling is added later. Functionally fine; sonically a design choice to revisit.

## Verdict

- **Buses (+ streetcars): COMPATIBLE** — 99.4% alignment, zero transform, keyless
- **Rail: N/A** — subway geometry exists in static, but no public live train feed to drive it

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **PASS** — 100% lat/lon, 100% per-vehicle timestamp |
| Route ID alignment | **PASS (99.4%)** — sole miss is non-scheduled route `600` |
| Rail line alignment | **N/A** — no live subway feed |

**Bottom line:** TTC is one of the stronger candidates — keyless, standard protobuf,
~916 live route-attributed surface vehicles, clean 99.4% alignment with no route-ID
transform. Two caveats, both cosmetic: (1) no subway sonification because no public live
feed exists, and (2) streetcars voice on the bus palette (`route_type=0` → Bus). The 35.8%
route-less vehicles are normal out-of-service/deadhead traffic and are skipped as any
agency's would be.

## Adding TTC as a data source

- **Static GTFS zip:** `https://ckan0.cf.opendata.inter.prod-toronto.ca/dataset/7795b45e-e65a-4465-81fc-c36b9dfff169/resource/cfb6b2b8-6191-41e3-bda1-b175c51148cb/download/TTC Routes and Schedules Data.zip`
  - Note: the URL contains a space (`…/download/TTC Routes and Schedules Data.zip`) — URL-encode or quote when wiring it up. Consider mirroring/pinning; CKAN resource IDs can rotate on schedule updates.
- **GTFS-RT vehicle positions:** `https://bustime.ttc.ca/gtfsrt/vehicles`
  - Sibling feeds (unused by this worker): `…/gtfsrt/trips` (trip updates), `…/gtfsrt/alerts` (service alerts)
- **Rail realtime API:** n/a — TTC has no public live subway position feed
- **Auth:** **None** for either feed.
- **Route ID transform:** none — RT `route_id` matches static `route_short_name` verbatim.
- **`GtfsStaticLoader.cs`:** point `GtfsStaticUrl` at the TTC zip (or make it per-city, per
  the multi-city pattern). Existing `route_short_name ?? route_id` index key works as-is.
- **`Worker.cs`:** point `_gtfsRtUrl` at the TTC `/vehicles` endpoint. **No rail merge / no
  `RailRealtimeAdapter` needed** (no rail feed).
- **Optional follow-up:** if streetcar-specific voicing is desired, extend the loader's
  mode classification to map `route_type=0` (tram) to a distinct `TransitMode` instead of
  folding it into Bus.
