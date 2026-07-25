# GTFS Compatibility Report — SEPTA (Southeastern Pennsylvania Transportation Authority) (Philadelphia, Pennsylvania)

> ## 92.0/100 — Drop-in
> Bus: 70.0/70 · Rail: 12/20 · Credential: 10/10
> Config-only: buses, trolleys, and the Norristown High Speed Line snap with zero
> transform on a single keyless feed; the Broad Street Subway and Market-Frankford Line
> ride the same feed and index but showed no live vehicles at two separate fetches.
> Computed per `aggregate-score-formula.md`; every component above is a real measurement
> or fixed categorical lookup, never a guess.

**Evaluated:** 2026-07-25

## Feed health

| | |
|---|---|
| GTFS-RT URL | `https://www3.septa.org/gtfsrt/septa-pa-us/Vehicle/rtVehiclePosition.pb` |
| Static GTFS URL | `https://www3.septa.org/developer/gtfs_public.zip` |
| RT feed size | 32,657 bytes  •  Header ts: `0` (normal — per-vehicle timestamps present at 100%) |
| Static routes | 147 routes / 145 with shapes / 2 without |

> **Feed source note:** `gtfs_public.zip` is a **container of two nested zips** —
> `google_bus.zip` (bus, trolley, trolleybus, and Norristown High Speed Line) and
> `google_rail.zip` (Regional Rail, `route_type=2`, not evaluated here — it isn't heavy
> rail and the worker's rail path only targets `route_type=1`). This two-tier zip
> structure is unlike every other agency evaluated so far and would need unzip-of-unzip
> handling in `GtfsStaticLoader.cs`, not a plain single-level `Expand-Archive`. Sibling
> GTFS-RT endpoints exist but are NOT vehicle-positions: `…/Trip/rtTripUpdates.pb` (trip
> updates), `…/Service/rtServiceAlerts.pb` (service alerts).

## Vehicle positions (GTFS-RT)

| | |
|---|---|
| Total / vehicle entities | 448 / 448 |
| With `route_id` | **448 (100%)** |
| Without `route_id` | **0 (0%)** |
| lat/lon present | **100%** |
| speed present | 0% (optional — degrades gracefully; absent on every vehicle in this feed, unlike MARTA's partial coverage) |
| bearing present | 100% |
| vehicle.timestamp | 100% |

This feed carries buses, trackless trolleys (`route_type=11`), streetcars (`route_type=0`,
5 routes `T1`–`T5`), and the Norristown High Speed Line (`route_type=1`, key `M1`) — all
118 distinct live route IDs matched their static counterpart with zero route-less
vehicles. **All 448 live vehicles are route-attributed**, denser than TTC's post-filter
count and with no `skippedNoRouteId` loss at all.

## Route ID alignment (buses + trolleys + streetcars + NHSL)

| | |
|---|---|
| RT distinct route IDs | 118 |
| Static index keys (`route_short_name ?? route_id`) | 147 |
| **Matched (as-is)** | **118 (100%)** |
| Unmatched RT IDs | none |
| Static-only keys | 29 (off-peak/inactive/owl routes, e.g. `B1 OWL`, `L1 OWL`, `D1 Bus`, `WTR Bus` — plus the 4 heavy-rail keys with no live vehicles this pass, see Rail below) |
| Fixable via existing normalizer? | No transform needed — verbatim match. |

**RT `route_id` is a plain rider-facing string (numeric or short alpha code); static
`route_short_name` is identical — no transform needed.** Every live vehicle resolved
cleanly; the 29 static-only keys are inactive/owl-service variants and the two Broad
Street short-turn/Market-Frankford keys discussed below, not a format mismatch.

**Unmatched-route runtime behavior:** not applicable here — 100% of live vehicles
resolved, so no vehicle in this snapshot fell to the platform's `"unknown"` category.

## Rail (heavy rail / `route_type=1`)

| | |
|---|---|
| Static rail routes | 5 — keys `B1`, `B2`, `B3`, `L1`, `M1` |
| Rail realtime API | Not a separate feed — rail vehicles ride the **same** GTFS-RT bus feed as everything else, under the same `route_id` scheme (no separate URL, no separate auth) |
| Live trains available | 1 of 5 rail keys observed live (`M1`, the Norristown High Speed Line) across two fetches ~minutes apart; `B1`/`B2`/`B3`/`L1` (Broad Street Subway + Market-Frankford Line) showed zero live vehicles both times |
| Live-position check | PASS for `M1` (100% lat/lon, consistent single-vehicle presence both fetches) — N/A for `B1`/`B2`/`B3`/`L1` (no vehicles to check) |
| LINE ↔ static match | `M1`: 100% (verbatim). `B1`/`B2`/`B3`/`L1`: not measurable — zero live entities under those IDs at either fetch |
| Integration mechanism this would need | Config-only route-ID remap is not even needed — rail already shares the bus feed's exact ID scheme (`M1` snaps today with zero code or config change). Whether Broad Street/Market-Frankford ever populate this same feed at a different time of day, or require SEPTA's newer `ng-realtime.septa.org` platform (no documented public API found), is unresolved by this pass. |

The Norristown High Speed Line is functionally a drop-in rail route today: it lives in
the identical GTFS-RT feed as the buses, under `route_id=M1`, matching the static key
verbatim, with 100% lat/lon across both fetches — no adapter, no remap, nothing beyond
what the generic `GtfsRtCity` path already does. The Broad Street Subway and
Market-Frankford Line are the open question: their static geometry is present
(`route_type=1`, shapes attached) and their `route_id`s already exist in the same index
namespace, but this pass observed **zero** live vehicles under those IDs on two separate
fetches (not a single fluke read). A `WebSearch` surfaced SEPTA's newer public-facing
tracker at `ng-realtime.septa.org` claiming live subway GPS, but no documented machine-
readable API endpoint was found there to fetch or decode — reverse-engineering an
undocumented private endpoint is out of scope for this run. This is genuinely
**PARTIALLY COMPATIBLE**, not N/A: unlike TTC's subway (which has no public live feed at
all), SEPTA's classic GTFS-RT feed is structurally capable of carrying `B1/B2/B3/L1` (it
already carries `M1`) — it just didn't emit them at either checked moment.

## Verdict

- **Buses + trolleys + streetcars + NHSL: COMPATIBLE** — 100% alignment, zero transform, keyless, zero route-less vehicles
- **Rail (Broad St + Market-Frankford): PARTIALLY COMPATIBLE** — same feed, same ID scheme, zero live vehicles observed at 2 fetches; NHSL (`M1`) on the same static rail axis is fully live and COMPATIBLE today

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **PASS** — 100% lat/lon, 100% route_id, 100% per-vehicle timestamp |
| Route ID alignment | **PASS (100%)** — zero unmatched RT ids |
| Rail line alignment | **PARTIAL** — `M1` PASS (100%, live), `B1`/`B2`/`B3`/`L1` unconfirmed (zero live vehicles both fetches, no separate feed to blame) |

**Bottom line:** SEPTA is a strong candidate — keyless, standard protobuf, 448 live
route-attributed vehicles, 100% alignment, zero route-less vehicles, and one rail route
(`M1`) already riding the feed cleanly with no extra work. The only open question is
whether the Broad Street Subway / Market-Frankford Line ever populate this same feed
(time-of-day gap, headway-based operation without individual GPS broadcast, or a
genuinely separate undocumented platform) — worth a follow-up fetch at a different
time of day before concluding those two lines need a bespoke adapter or are simply
absent from this feed by design.

## Adding SEPTA as a data source

- **Static GTFS zip:** `https://www3.septa.org/developer/gtfs_public.zip`
  - Note: this is a **zip-of-zips** — the top-level download contains `google_bus.zip` and
    `google_rail.zip` (Regional Rail, `route_type=2`, unused by this evaluation); the
    bus/trolley/streetcar/NHSL data needed here is inside `google_bus.zip`, one extraction
    level deeper than every other agency evaluated so far. `GtfsStaticLoader.cs` would need
    a nested-extract step, not a plain single `Expand-Archive`.
- **GTFS-RT vehicle positions:** `https://www3.septa.org/gtfsrt/septa-pa-us/Vehicle/rtVehiclePosition.pb`
  - Sibling feeds (unused by this worker): `…/Trip/rtTripUpdates.pb` (trip updates), `…/Service/rtServiceAlerts.pb` (service alerts)
- **Rail realtime API:** n/a as a separate feed — `M1` (NHSL) already arrives on the bus GTFS-RT feed above; `B1`/`B2`/`B3`/`L1` did not appear live in this feed during this pass and no separate documented rail API was found (SEPTA's `ng-realtime.septa.org` shows live subway tracking on its website but publishes no documented public API)
- **Auth:** **None** for either feed.
- **Route ID transform needed (buses):** none — RT `route_id` matches static `route_short_name` verbatim.
- **Rail line transform needed:** none for `M1` — verbatim match, already live on the same feed. `B1`/`B2`/`B3`/`L1` are n/a to transform since no live vehicles were observed under those IDs to transform in the first place.
- **Config entry (generic city path):** this is a config-only city — a `CityConfig` entry (`Cities:` array in both the Worker's and WebAPI's `appsettings.json`, byte-identical) with `GtfsRtUrls`, `StaticZipUrls`, `EmitsTelemetry: true`. No `ApiKeyEnvVar`/`ApiKeyQueryParam`, `RouteIdNormalization`, or `RailRouteIdMap` needed — everything matches verbatim. The nested-zip static structure is the one config/loader wrinkle worth flagging to whoever onboards this.
- **Optional follow-up:** re-fetch the GTFS-RT feed at a different time of day (e.g. weekday rush vs. this pass's timing) to confirm whether `B1`/`B2`/`B3`/`L1` ever populate this feed, before concluding subway needs a bespoke adapter that may not actually be necessary.
