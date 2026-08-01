# GTFS Compatibility Report — Metro Transit (Minneapolis–St. Paul, Minnesota)

> ## 40/100 — Adapter Needed
> Bus: 10/70 · Rail: 20/20 · Credential: 10/10
> Required-fields gate fails: only 57.5% of live vehicle entities carry a usable (non-placeholder) position, well under the 90% gate, even though route-ID alignment is a perfect 100% verbatim match and both feeds are keyless.

**Evaluated:** 2026-08-01

## Feed health

| | |
|---|---|
| GTFS-RT URL | `https://svc.metrotransit.org/mtgtfs/vehiclepositions.pb` |
| Static GTFS URL | `https://svc.metrotransit.org/mtgtfs/gtfs.zip` |
| RT feed size | 56,622 bytes  •  Header ts: `0 (normal — see note)` |
| Static routes | 128 routes / 128 with shapes / 0 without |

Metro Transit's own `linked_datasets.txt` (shipped inside the static zip) documents three sibling GTFS-RT endpoints on the same host: `vehiclepositions.pb` (used here), `tripupdates.pb`, and `alerts.pb` — all three list `authentication_type=0` (keyless). Only `vehiclepositions.pb` was fetched/decoded; the other two carry no lat/lon and are correctly unused.

## Vehicle positions (GTFS-RT)

| | |
|---|---|
| Total / vehicle entities | 642 / 642 |
| With `route_id` | **642 (100.0%)** |
| Without `route_id` | **0 (0.0%)** |
| lat/lon present | **57.5%** |
| speed present | 32.1% (optional — degrades gracefully) |
| bearing present | 46.0% |
| vehicle.timestamp | 57.5% |

This is a bus + light-rail feed: METRO Blue Line, Green Line, and an airport shuttle (all `route_type=0`) ride the identical protobuf stream as the 125 bus routes, under the same plain numeric `route_id` scheme. Of the 642 vehicle entities, all 642 carry a `route_id`, but only 369 (57.5%) carry a real, non-placeholder lat/lon — the other 273 arrive with an *identical* `(0.0, 0.0)` position **and** no `vehicle.timestamp` (verified: it is the exact same 273-entity subset missing both fields, not independent per-field noise). This was confirmed stable, not a one-off snapshot: a second live fetch minutes later measured 58.1% (366/630) — consistent within the feed's normal churn. Wire-level inspection also confirms these are genuinely encoded 32-bit float zeros (`Position.latitude`/`longitude` = `0.0`), not a decoder field-number mismatch (no `_diag_note` was raised, and the same field carries real coordinates on the other 57.5% of entities).

## Route ID alignment (buses + light rail)

| | |
|---|---|
| RT distinct route IDs | 71 |
| Static index keys (`route_short_name ?? route_id`) | 128 |
| **Matched (as-is)** | **71 (100.0%)** |
| Unmatched RT IDs | none |
| Static-only keys | 57 (off-peak/inactive routes at fetch time — sample: `113, 114, 120, 121, 122, 123, 124, 125, 134, 156`) |
| Fixable via existing normalizer? | No transform needed — verbatim match. |

Every distinct `route_id` seen live matched a static key with zero transform, including light-rail route `902` (METRO Green Line). The 57 static-only keys are simply routes with no vehicle currently in the feed (normal off-peak coverage), not a mismatch.

**Unmatched-route runtime behavior:** not applicable here — alignment is 100%, so no vehicle falls into the platform's `"unknown"` category fallback.

<!-- Rail (route_type=1) section omitted: static.rail_route_count (route_type=1) == 0 -->

### Light rail is `route_type=0` (tram), not heavy rail — and needs no rail mechanism at all

Metro Transit's static routes break down as **3 light rail (`0`) + 125 bus (`3`)** — no `route_type=1` (heavy rail) or `route_type=2` (commuter rail; the Northstar commuter line does not appear in this feed at all, consistent with its 2020s service discontinuation). The three `route_type=0` routes are `901` (METRO Blue Line), `902` (METRO Green Line), and `906` (Airport Shuttle). Unlike RTD (which needed an 8-entry `RailRouteIdMap` to reconcile a numeric rail-prefix scheme) or WMATA, these light-rail routes already ride the *same* GTFS-RT feed under the *same* plain numeric `route_id` as buses — `902` matched verbatim in the alignment above with **zero remap**. This mirrors `ttc.md`'s streetcar precedent: `route_type=0` vehicles are functionally part of the bus-shaped path, and true "Rail" (a separate live-position source needing `RailRealtimeAdapter`-style code) simply does not apply to this authority.

## Verdict

- **Buses (+ light rail): PARTIALLY COMPATIBLE** — 100% route-ID alignment, but only 57.5% of live vehicle entities carry a usable position
- **Rail: N/A** — no `route_type=1`/`route_type=2` routes exist; the three `route_type=0` light-rail routes ride the bus feed with zero remap needed

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **FAIL** — 100% have `route_id`, but only 57.5% carry a non-placeholder lat/lon (below the 90% gate); confirmed stable across two independent live fetches |
| Route ID alignment | **PASS (100% match)** — verbatim, zero transform, includes light rail |
| Rail line alignment | **N/A** — no rail section exists |

**Bottom line:** Route identification is as clean as it gets — 100% verbatim `route_id` alignment across buses and light rail, keyless on all three GTFS-RT siblings. But roughly 42.5% of every live snapshot is a consistent subset of vehicles reporting an identical `(0.0, 0.0)` placeholder position (and no timestamp) rather than a real coordinate — likely pull-in/pull-out or GPS-not-yet-acquired vehicles that the feed still includes. `Worker.cs`'s `ProcessSpatialReconciliationAsync` has no guard against this today: it unconditionally calls `RouteSnapper.FindNearest`/`FindNearestInWindow` on whatever lat/lon arrives, so these vehicles would currently snap to a bogus nearest-shape-point near null island rather than being skipped. This is not a per-city config gap — closing it needs one small, shared guard in the worker (skip entities where `lat == 0 && lon == 0`) that would benefit any city hitting the same pattern, not a bespoke Metro Transit adapter.

## Adding Metro Transit as a data source

- **Static GTFS zip:** `https://svc.metrotransit.org/mtgtfs/gtfs.zip` — flat zip, no quirks, ~25MB
- **GTFS-RT vehicle positions:** `https://svc.metrotransit.org/mtgtfs/vehiclepositions.pb` (sibling `tripupdates.pb` and `alerts.pb` exist on the same host but are correctly unused — not vehicle-positions)
- **Rail realtime API:** n/a — light rail rides the same GTFS-RT bus feed under matching `route_id`s; no separate feed exists or is needed
- **Auth:** None for any of the three GTFS-RT feeds or the static zip (confirmed both by successful keyless fetch and by the feed's own `linked_datasets.txt` declaring `authentication_type=0`)
- **Route ID transform needed (buses):** none — RT `route_id` matches static `route_id` verbatim (100%)
- **Rail line transform needed:** n/a — no separate rail feed to integrate; light rail already matches verbatim on the bus feed
- **Config entry (generic city path):** the `CityConfig` registration itself is genuinely zero new city-specific code — a standard `Cities:` entry (`GtfsRtUrls`, `StaticZipUrls`, `EmitsTelemetry: true`; no `RouteIdNormalization`, no `RailRouteIdMap`, no `ApiKeyEnvVar` needed) is all `metro-transit` requires on the config side. The blocker sitting above this score is a **shared worker-level fix** (a null-island position guard), not a per-city adapter — see Bottom line.
- **Optional follow-up:** a `RouteTypeCategories` entry (e.g. `{"0": "light-rail", "3": "bus"}`) purely for category labeling, mirroring TTC's `"streetcar"` treatment — cosmetic, not required for the feed to function once the position guard above lands.
