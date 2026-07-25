# GTFS Compatibility Report — MBTA (Boston, Massachusetts)

**Evaluated:** 2026-06-28

## Feed health

| | |
|---|---|
| GTFS-RT URL | `https://cdn.mbta.com/realtime/VehiclePositions.pb` |
| Static GTFS URL | `https://cdn.mbta.com/MBTA_GTFS.zip` |
| RT feed size | 36,941 bytes  •  Header ts: `0` (normal — MBTA, like MARTA, doesn't set it) |
| Static routes | 403 routes / 373 with shapes / 30 without |

## Vehicle positions (GTFS-RT)

| | |
|---|---|
| Total / vehicle entities | 311 / 311 |
| With `route_id` | **311 (100%)** ← every vehicle carries one |
| Without `route_id` | 0 |
| lat/lon present | **100%** |
| speed present | 11.6% (optional — degrades gracefully) |
| bearing present | 88.4% |
| vehicle.timestamp | 100% |

The single `.pb` feed carries **all modes at once**: 89 bus + 9 commuter rail + 5 light
rail + 3 heavy rail in the sampled snapshot. A full pass found **32 live heavy-rail trains**
(Red/Orange/Blue) with valid positions.

## Route ID alignment (buses + everything)

| | |
|---|---|
| RT distinct route IDs | 106 |
| Static index keys (`route_short_name ?? route_id`) | 220 |
| **Matched (as-is)** | **95 (89.6%)** |
| Unmatched RT IDs | `Green-B/C/D/E`, `741`, `742`, `743`, `749`, `751`, `441442`, `Shuttle-Generic` |

**The 11 mismatches are 100% systematic, and there's a one-line fix.** MBTA populates
`route_short_name` with *rider-facing* names that differ from the stable `route_id` the RT
feed broadcasts:

| RT `route_id` | static `route_short_name` (current index key) | what it is |
|---|---|---|
| `Green-B/C/D/E` | `B / C / D / E` | Green Line light rail |
| `741/742/743/749/751` | `SL1 / SL2 / SL3 / SL5 / SL4` | Silver Line BRT |
| `441442` | `441/442` | combined bus route |
| `Shuttle-Generic` | `Shuttle` | replacement shuttle |

**Keying the route index by `route_id` instead of `route_short_name` gives 100% alignment
(106/106) — verified.** Every RT route ID exists verbatim as a static `route_id`.

## Rail (heavy rail / `route_type=1`)

| | |
|---|---|
| Static rail routes | 3 — keys `Red`, `Orange`, `Blue` |
| Rail realtime API | **Not needed** — heavy-rail positions are in the standard `.pb` feed |
| Live trains in `.pb` | 32 (Red/Orange/Blue), all with live lat/lon |
| LINE ↔ static match | `Red/Orange/Blue` align verbatim (no `route_short_name` collision) |

> This is the big structural difference from MARTA: **MBTA needs no separate
> `RailRealtimeAdapter`.** Trains flow through the exact same GTFS-RT bus path. The
> MARTA-specific JSON rail adapter is irrelevant here.

## Verdict

- **Buses: COMPATIBLE** (PARTIAL as-is at 89.6%, but a trivial index-key change → 100%)
- **Rail: COMPATIBLE** — heavy rail rides the same protobuf, no new adapter

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **PASS** — 100% / 100% |
| Route ID alignment | **PARTIAL (89.6%) → fixable to 100%** |
| Rail line alignment | **PASS** |

**Bottom line:** MBTA is one of the cleanest agencies you could add — all modes in a single
keyless protobuf, every vehicle has route_id and position. The only work is a one-line change
to key the route index by `route_id` instead of `route_short_name` (or fall back to `route_id`
when the RT id isn't found), which lifts match from 89.6% to 100% and incidentally avoids
needing MARTA's rail adapter entirely.

## Adding MBTA as a data source

- **Static GTFS zip:** `https://cdn.mbta.com/MBTA_GTFS.zip`
- **GTFS-RT vehicle positions:** `https://cdn.mbta.com/realtime/VehiclePositions.pb`
- **Rail realtime API:** n/a — heavy rail is in the `.pb`
- **Auth:** **None.** The `cdn.mbta.com` `.pb`/zip endpoints are public and keyless. (The
  `42aa019b…` key is for the V3 JSON API at `api-v3.mbta.com` — not used by this worker,
  though it would lift rate limits if you ever switched to the JSON realtime endpoints.)
- **Route ID transform:** none if you key the index by `route_id`; otherwise the
  Green/Silver/combined/shuttle routes are skipped.
- **`GtfsStaticLoader.cs`:** point `GtfsStaticUrl` at the MBTA zip (or make it per-city, per
  the multi-city pattern); **change the index key to `routeId` (or add `routeId` fallback).**
  Rail loads automatically via `route_type`.
- **`Worker.cs`:** point `_gtfsRtUrl` at the MBTA `.pb`. **No rail merge / no
  `RailRealtimeAdapter` needed.**
