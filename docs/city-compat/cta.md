# GTFS Compatibility Report — CTA (Chicago Transit Authority)

**Evaluated:** 2026-07-25

## Feed health

| | |
|---|---|
| Static GTFS URL | `https://www.transitchicago.com/downloads/sch_data/google_transit.zip` — verified live, HTTP 200, 68 MB zipped / ~400 MB unzipped |
| GTFS-RT vehicle positions (buses) | **Does not exist.** No `.pb` protobuf feed is published for CTA buses |
| Rail realtime (trains) | **Does not exist as GTFS-RT either.** No `.pb` feed for the 'L' |

CTA is confirmed absent from the Mobility Database registry's realtime listings (the
successor to TransitFeeds, which lists CTA's static feed but zero GTFS-RT feeds for it) and
CTA's own developer docs only document two **proprietary legacy APIs**: **Bus Tracker** and
**Train Tracker**, both XML/JSON over HTTP, both requiring a registered API key:

```
GET http://www.ctabustracker.com/bustime/api/v2/getvehicles?rt=22&format=json
→ 200 OK: {"bustime-response":{"error":[{"msg":"No API access key supplied"}]}}
```

This is a structurally different problem than every other agency evaluated so far (MARTA,
MBTA): there the question was route-ID alignment or field completeness within a real
GTFS-RT feed. Here, **there is no protobuf feed for the worker's decoder to read at all** —
neither keyed nor keyless.

## Static GTFS (verified by direct parse)

| | |
|---|---|
| Routes | 133 total, all 133 have shapes |
| `route_type=3` (bus) | 125 routes — `route_id` and `route_short_name` match plain rider-facing strings (`1`, `2`, `X4`, `N5`, …) |
| `route_type=1` (rail) | 8 routes — `Red`, `P`, `Y`, `Blue`, `Pink`, `G`, `Org`, `Brn` |

All 8 rail routes have `route_short_name = ""` (empty string, not missing). The worker's
loader already normalizes empty string → `null` before the `RouteShortName ?? RouteId`
fallback (`GtfsStaticLoader.cs`, `ParseRouteMetadata`), so this resolves cleanly to
`route_id` as the index key with **no code change needed** for that part specifically.

## Rail line-key alignment (the one clean result)

CTA's Train Tracker API route parameter (`rt=`) uses exactly these codes: **Red, Brn, Blue,
G, Org, Pink, P, Y** — identical, verbatim, to the static `route_id` values above. If a
CTA-specific rail adapter were built, line-key alignment would be a **100% match**, same
pattern as MARTA's `LINE=RED` ↔ static `RED`, zero transform required.

## Vehicle positions / route ID alignment (buses)

**Not assessable without a live feed.** No GTFS-RT `.pb` exists to decode, and the Bus
Tracker API requires a registered developer key that wasn't available for this pass. Field
completeness (route_id %, lat/lon %, speed/bearing %) can't be measured until a key is
obtained and a live `getvehicles` sample is pulled.

## Verdict

- **Buses: INCOMPATIBLE** (as GTFS-RT) — no protobuf feed exists; the worker's bus path
  assumes real GTFS-RT and has nothing to decode
- **Rail: INCOMPATIBLE** (as GTFS-RT) — same story; but line-key alignment would be trivial
  once an adapter exists

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **UNASSESSED** — no feed reachable without an API key |
| Route ID alignment (buses) | **N/A** — nothing to align against |
| Rail line alignment | **Would PASS (100%, zero transform)** — verified from static + published line codes |

**Bottom line:** CTA's static data is clean and its rail line codes already match what a
future adapter would need, but neither buses nor trains are reachable through the worker's
current GTFS-RT decode path. CTA needs two new bespoke protocol adapters (mirroring
`RailRealtimeAdapter`, but for Bus Tracker and Train Tracker's XML/JSON) plus two registered
API keys — not a config swap.

## Adding CTA as a data source

- **Static GTFS zip:** `https://www.transitchicago.com/downloads/sch_data/google_transit.zip`
  — no auth required, drop-in for `GtfsStaticLoader.cs`.
- **Bus realtime:** `http://www.ctabustracker.com/bustime/api/v2/getvehicles` (Bus Tracker
  API, XML or JSON) — **requires a registered API key** (separate developer application).
  No GTFS-RT equivalent exists; needs a **net-new bus adapter** normalizing Bus Tracker's
  response shape into the `FeedMessage`-like structure the worker consumes today.
- **Rail realtime:** Train Tracker API (XML/JSON) — **requires its own, separately
  registered API key**. Route parameter values (`Red/Brn/Blue/G/Org/Pink/P/Y`) already equal
  static `route_id`s, so the line-key mapping step is free. Needs a **net-new rail adapter**
  mirroring `RailRealtime/RailRealtimeAdapter.cs`, but response field names, the "live
  position" contract (one coord per train, like MARTA's `IS_REALTIME` + coord-dedup checks),
  and freshness semantics all need verification against a real Train Tracker sample — not
  yet obtained.
- **Auth:** two separate API keys (Bus Tracker, Train Tracker), provisioned independently
  through CTA's developer portal. Store both via env/secrets, never committed, per the
  existing MARTA rail key precedent.
- **`GtfsStaticLoader.cs`:** point at the CTA zip — straightforward config change; rail
  routes load automatically via `route_type=1`.
- **`Worker.cs`:** the bus merge point (`_gtfsRtUrl`) has no CTA equivalent to point at;
  requires new adapter wiring, not a URL swap. Same for the rail merge.
- **Effort scope:** notably larger than a config-only onboarding (MBTA) or a rail-only new
  adapter (a hypothetical MARTA-shaped agency) — this is two new protocol adapters against
  undocumented-here JSON/XML schemas, gated on obtaining both API keys.

## Open items for a follow-up pass

- Obtain a CTA Bus Tracker + Train Tracker developer API key.
- Pull a live `getvehicles` sample; measure `route_id`/lat-lon/speed/bearing completeness
  the same way MBTA/MARTA were measured.
- Pull a live Train Tracker sample; confirm field names for lat/lon/line/run-number and
  whether the "one coordinate per train" live-position contract holds.
