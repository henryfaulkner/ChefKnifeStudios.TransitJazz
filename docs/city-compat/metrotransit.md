# GTFS Compatibility Report — Metro Transit (Minneapolis–St. Paul, Minnesota)

> ## 40/100 — Adapter Needed
> Bus: 10/70 · Rail: 20/20 · Credential: 10/10
> Route IDs and static data are a clean, verbatim, zero-transform match — but ~42% of the
> live bus fleet publishes an explicit `lat=0/lon=0` position instead of a real GPS fix,
> failing the required-fields gate (needs ≥90% lat/lon) and capping the bus score at
> 10/70 regardless of the otherwise-perfect route alignment. Computed per
> `aggregate-score-formula.md`; every component above is a real measurement or fixed
> categorical lookup, never a guess.

**Evaluated:** 2026-08-01

## Feed health

| | |
|---|---|
| GTFS-RT URL | `https://svc.metrotransit.org/mtgtfs/vehiclepositions.pb` |
| Static GTFS URL | `https://svc.metrotransit.org/mtgtfs/gtfs.zip` |
| RT feed size | 55,447 bytes  •  Header ts: `0 (normal — per-vehicle timestamps carried instead)` |
| Static routes | 128 routes / 128 with shapes / 0 without |

> **Feed source note:** Both URLs are Metro Transit's own canonical `svc.metrotransit.org`
> endpoints (confirmed independently via Transitland's feed listing), keyless, and a flat
> single-level zip — no CKAN-style rotating resource IDs, no zip-of-zips. Sibling GTFS-RT
> endpoints `.../mtgtfs/tripupdates.pb` and `.../mtgtfs/alerts.pb` exist but are NOT
> vehicle-positions. The RT feed was fetched twice, 20 seconds apart, to rule out a
> momentary fluke in the lat/lon finding below — both fetches (630 and 611 vehicle
> entities) agreed closely (58.4% and 59.6% lat/lon present), confirming this is a stable
> feed characteristic, not a transient blip.

## Vehicle positions (GTFS-RT)

| | |
|---|---|
| Total / vehicle entities | 630 / 630 |
| With `route_id` | **630 (100%)** |
| Without `route_id` | **0 (0%)** |
| lat/lon present | **58.4%** |
| speed present | 31.7% (optional — degrades gracefully) |
| bearing present | 44.8% |
| vehicle.timestamp | 58.4% |

This single feed carries buses (`route_type=3`, 622 of 630 live vehicles) and light rail
(`route_type=0`, 8 of 630) together. The lat/lon gap is **entirely on the bus side**: bus
vehicles report a real position only 57.9% of the time (360 of 622), while every one of
the 8 light-rail vehicles observed had a valid, non-zero position (100%). The missing
360 aren't absent fields — each one explicitly encodes `latitude=0.0, longitude=0.0` as a
well-formed `Position` submessage (verified via raw field inspection), not a decode
artifact or an alternate proto field layout.

## Route ID alignment (buses + light rail)

| | |
|---|---|
| RT distinct route IDs | 69 |
| Static index keys (`route_short_name ?? route_id`) | 128 |
| **Matched (as-is)** | **69 (100%)** |
| Unmatched RT IDs | none |
| Static-only keys | 59 (routes with no live vehicle in this snapshot, e.g. `113`, `114`, `120`, `121`, `122`, `123`, `124`, `125`, `134`, `156` — plausibly peak/school-only or currently-inactive routes, not a matching defect since every RT-observed ID matched) |
| Fixable via existing normalizer? | No transform needed — verbatim match. |

Every route_id the live feed emitted was already present in static, byte-for-byte —
including the 3 light-rail route_ids (`901`, `902`, `906`), which use the same plain
numeric scheme as the bus routes rather than a separate letter/line-name convention like
RTD's. No `RouteIdNormalizer` transform, `RailRouteIdMap`, or other config-level ID fixup
is needed anywhere in this feed.

**Unmatched-route runtime behavior:** N/A here — every live route_id resolved cleanly, so
no vehicle would fall into the platform's `"unknown"` category on route grounds.

## Rail (`route_type=0` light rail)

<!-- Metro Transit has zero route_type=1 or route_type=2 routes; all 3 of its rail
     routes are route_type=0 (light rail). Per GtfsStaticLoader.cs's classifier
     (route_type 0, 1, AND 2 are all Rail), this is the platform's Rail axis, not Bus —
     included here despite the section header's usual route_type=1 framing, mirroring
     rtd.md's precedent for a route_type=0/2-only rail authority. -->

| | |
|---|---|
| Static rail routes | 3 — keys `901`, `902`, `906` (`route_short_name` is blank for all three; the index key falls back to `route_id`) |
| Rail realtime API | Not a separate feed — light rail rides the **same** keyless GTFS-RT feed as the buses, under its own plain `route_id`s |
| Live trains available | 8 (first fetch) / 7 (second fetch), 20 seconds apart — all on `902` (METRO Green Line) |
| Live-position check | PASS — 100% lat/lon on every rail vehicle in both fetches, distinct real coordinates tracing the Green Line's Minneapolis–St. Paul corridor (lat ~44.95–44.98, lon ~-93.09 to -93.28) |
| LINE ↔ static match | 100%, zero transform — RT `route_id` already equals the static `route_id` verbatim |
| Integration mechanism this would need | **None** — RT route_id already matches static verbatim; no `CityConfig.RailRouteIdMap` entry is even needed (a simpler case than RTD's 8-entry remap or WMATA's Metro-line map). |

Light rail's own live data is clean and unaffected by the bus-side zero-position issue
above — the 8/8 and 7/7 rail vehicles observed across both fetches all had real GPS
fixes. The one caveat: only `902` (Green Line) had any live vehicles in either fetch,
both taken on a Saturday evening (~17:32 local, per `vehicle.timestamp`); `901` (Blue
Line) and `906` (Airport Shuttle) showed zero live vehicles at check time. That's most
plausibly a scheduling/frequency artifact of this particular snapshot window, not a
feed defect — Blue Line vehicles that WERE reporting would ride the identical verbatim
route_id match `902` already demonstrated.

`906` ("Airport Shuttle," MSP Terminal 1↔Terminal 2) is tagged `route_type=0` (light
rail) in static despite functioning as a short people-mover, not a conventional
light-rail line — a naming/classification quirk worth noting, not a compatibility issue.

## Verdict

- **Buses: PARTIALLY COMPATIBLE** — 100% route ID alignment, but only 57.9% of bus
  vehicles report a genuine (non-zero) live position.
- **Rail: COMPATIBLE** — verbatim route_id match, 100% live position on every observed
  vehicle, though only 1 of the 3 static light-rail routes had active vehicles during
  either check window.

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **FAIL** — route_id is 100%, but lat/lon is only 58.4% overall (57.9% for buses specifically), below the ≥90% usability gate |
| Route ID alignment | **PASS** (100% match, zero unmatched RT IDs) |
| Rail line alignment | **PASS** (100%, zero transform) — but low sample coverage, only 1 of 3 lines observed live |

**Bottom line:** Metro Transit's static data and route-ID scheme are about as clean as a
drop-in gets — buses and light rail share one keyless feed under IDs that match static
verbatim, with zero `CityConfig` transforms of any kind needed for route resolution. What
blocks a clean score is a real, stable data-quality trait of the live feed itself: roughly
42% of the live bus fleet (confirmed consistent across two independent fetches) reports
an explicit `(0,0)` position rather than omitting the field. Because this platform's
`RouteSnapper.FindNearest` (`ChefKnifeStudios.TransitJazz.Shared/Geospatial/RouteSnapper.cs`)
has no maximum-distance threshold, a `(0,0)` vehicle would not be filtered — it would
silently "snap" to whichever route point is nearest in raw lat/lon space and render at an
effectively arbitrary point along its route, a real map-accuracy defect rather than a
crash. This isn't fixable by any existing `CityConfig` knob (`ApiKeyEnvVar`,
`RouteIdNormalization`, `RailRouteIdMap`) — it's missing GPS data upstream, which no
route-ID transform or rail adapter can restore. Light rail (route_type=0) is unaffected
and clean.

## Adding Metro Transit as a data source

- **Static GTFS zip:** `https://svc.metrotransit.org/mtgtfs/gtfs.zip` — flat single-level
  zip, keyless, no rotating resource IDs.
- **GTFS-RT vehicle positions:** `https://svc.metrotransit.org/mtgtfs/vehiclepositions.pb`
  — sibling `tripupdates.pb` and `alerts.pb` exist but are not vehicle-positions and were
  not evaluated.
- **Rail realtime API:** n/a — light rail rides the same GTFS-RT feed as buses, not a
  separate API.
- **Auth:** None for either feed — both returned HTTP 200 with no query params or headers
  across three separate fetches (one static, two RT).
- **Route ID transform needed (buses):** none — RT `route_id` matches static verbatim
  (100%, zero unmatched IDs).
- **Rail line transform needed:** none — RT `route_id` matches static `route_id`
  verbatim for the one line observed live.
- **Config entry (generic city path):** this is a config-only city — a `CityConfig`
  entry (`Cities:` array in both the Worker's and WebAPI's `appsettings.json`,
  byte-identical) with `GtfsRtUrls`, `StaticZipUrls`, `EmitsTelemetry: true`. No
  `RouteIdNormalization`, `RailRouteIdMap`, or `ApiKeyEnvVar`/`ApiKeyQueryParam` needed.
- **Optional follow-up:** Before onboarding, consider whether `Worker.cs`'s spatial
  reconciliation should gain a platform-wide guard against `(0,0)`-style non-positions
  (skip vehicles whose position is exactly `0,0`, mirroring the existing
  `skippedNoRouteId`/`skippedUnknownRoute` counters) — without one, ~40% of this
  authority's live bus markers would render at an arbitrary snapped point rather than
  being cleanly dropped. This is a platform-level fix, not Metro-Transit-specific, and
  out of scope for this compatibility evaluation. Also worth a longer live sample to
  confirm `901`/`906` do carry real positions during their own active service windows.
