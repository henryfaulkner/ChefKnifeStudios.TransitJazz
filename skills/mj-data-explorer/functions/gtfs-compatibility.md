<!-- last verified: 2026-06-26 -->

# Function: GTFS Compatibility

Evaluate whether a transit agency's GTFS feeds are compatible with the TransitJazz
data worker algorithm. You were routed here because the user wants to assess a new
data source or understand why an existing one is producing skips or mismatches.

Use the `mj-gtfs` skill as your data-fetch tool. It handles all downloading and
decoding — read it before fetching anything.

> **Before fetching:** Use the pure-Python decode path in `mj-gtfs` (no pip required).
> The `pip install gtfs-realtime-bindings` path is blocked in Claude Code auto-mode
> by default and will waste a tool call if attempted first.

## What "compatible" means for the worker

The worker's `ProcessSpatialReconciliationAsync` has two hard dependencies:

1. **GTFS-RT feed** must provide vehicle positions with a non-empty `trip.route_id`
   and valid `position.latitude` / `position.longitude` per entity.
2. **Route ID alignment**: the `route_id` values in the GTFS-RT feed must match the
   keys in the route index, which are built from `routeShortName` (falling back to
   `routeId`) in the static GTFS `routes.txt` + `trips.txt` + `shapes.txt`.

If route IDs don't align, vehicles are silently counted as `skippedUnknownRoute` —
they appear in the feed but never reach the map.

Optional fields (speed, bearing, vehicle.timestamp) degrade gracefully; their absence
does not block the worker but reduces telemetry richness.

### Rail is a third, independent source (since the MARTA rail integration)

A GTFS-RT protobuf feed typically carries **buses only**. Heavy-rail vehicle positions
come from a **separate, agency-specific realtime API** that the worker normalizes through
a `RailRealtimeAdapter` into the same `FeedMessage` shape the bus path uses
(`RailRealtime/RailRealtimeAdapter.cs`, merged in `Worker.cs`). Rail compatibility is
therefore a **separate axis** from bus compatibility — an agency can be bus-compatible,
rail-compatible, both, or neither.

What the worker needs for rail:

- **Static rail routes exist**: the static GTFS has routes with `route_type=1`
  (subway/metro). The loader classifies these as `TransitMode.Rail`; their shapes are
  already in the index, so rail geometry needs no special handling.
- **A live rail position source**: an agency API returning a live position per train.
  MARTA's is JSON with one row **per (train, upcoming-station)**; the adapter:
  1. drops rows with `IS_REALTIME != "true"`,
  2. de-dups to one row per train ID (the live coord is identical across a train's rows —
     a contract guard logs a warning if not),
  3. maps `LINE` → `route_id`, `TRAIN_ID` → vehicle/entity id, `LATITUDE/LONGITUDE` →
     position, `EVENT_TIME` → timestamp.
- **Rail line-key alignment**: each row's `LINE` must match a static rail route's index
  key (`routeShortName ?? routeId`), exactly like bus `route_id` alignment. MARTA's
  `LINE` equals the static `route_short_name` (`RED/GOLD/BLUE/GREEN`) verbatim — zero
  transform. A new agency may differ.

The current adapter is **MARTA-specific** (its JSON shape and field names). Onboarding a
different agency's rail feed means a new adapter unless the feed happens to match MARTA's
schema — call that out in the report rather than implying rail is plug-and-play.

## Method

### Step 1 — Gather inputs

Ask the user for:
- The agency name (for the report header)
- Static GTFS zip URL
- GTFS-RT vehicle positions URL (buses)
- Whether the agency runs **heavy rail** and, if so, a **rail realtime API URL** (trains)
- Auth requirements (if any)

If the user only has some of these URLs, proceed with what's available and flag what's
missing in the report. If they don't mention rail, you can still detect rail routes from
the static zip (`route_type=1`) and then ask whether a rail position feed exists.

### Step 2 — Fetch feeds via mj-gtfs (in parallel), then run combined decode + align

Read `mj-gtfs` first, then:

**2a. Parallel fetch** — issue both downloads in a single parallel tool call:
1. Download the **GTFS-RT protobuf** → `$env:TEMP\gtfs-rt.pb`
2. Download + extract the **static GTFS zip** → `$env:TEMP\gtfs-<agency-slug>\`
   (always use the agency-slug directory — never the shared `gtfs-static` name)
3. If a rail realtime URL was supplied → fetch it too (parallel)

**2b. Combined decode + align** — once both downloads complete, run the **combined
decode + alignment script** from `mj-gtfs` in a **single tool call**. This does RT
decode + static parse + route ID alignment together and emits one JSON blob. Do not
run separate decode scripts and manually copy route ID sets between them.

The combined script skips `shapes.txt` by default (big file, not needed for alignment).
Only run the full static parse (with shapes) if the report needs shape point counts.

After the combined run: if `rt._diag_note` is present, the decoder hit a field-number
mismatch — fix the position field before continuing. Do not proceed with `lat_lon_pct = 0`.

If a rail realtime URL was supplied, decode it separately using the rail section of
`mj-gtfs` (it's a different format — JSON, not protobuf).

If the combined output shows `static.rail_route_count > 0` but no rail realtime URL was
provided, note it as RAIL UNASSESSED and ask for the rail API URL.

### Step 3 — Interpret alignment output

The combined script already computed alignment — read it from `alignment.*`:

- `alignment.match_pct` — the headline number. 100% = COMPATIBLE; <100% = investigate.
- `alignment.unmatched_rt_ids` — vehicles in the RT feed that will be `skippedUnknownRoute`.
  Look for a pattern: prefix (`Green-` → strip it), numeric vs. named, zero-padding.
- `alignment.static_only_sample` — routes in static with no active vehicles (off-peak
  routes, inactive lines) — usually expected, not a problem.

Compute and report:
- What % of RT route IDs have a corresponding static index key (`alignment.match_pct`)
- Any systematic transformation that would fix a mismatch (e.g. strip `Green-` prefix)

### Step 3b — Cross-check rail (only if a rail feed was fetched)

Same alignment question, rail edition: do the rail feed's `LINE` values match the static
**rail** index keys (the `rail_index_keys` from the static block, i.e. `route_type=1`
routes)?

- **Exact match** (MARTA: `LINE=RED` ↔ static `RED`) → train snaps, zero transform.
- **Mismatch** → trains counted as `skippedUnknownRoute`, same failure mode as buses.

Also confirm the rail feed's own health from the **Rail Realtime output block**:
- Live-position check **PASS** (one coord per train) — if FAIL, lat/lon isn't the live
  position and the feed can't drive honest motion.
- Enough realtime trains after the `IS_REALTIME` drop to feel alive (MARTA peaks ~16).

### Step 4 — Emit compatibility report

```
## GTFS Compatibility Report — <Agency Name>
Evaluated: <date>

### Feed health
GTFS-RT URL:        <url>
Static GTFS URL:    <url>
RT feed size:       <N> bytes  |  Header ts: <UTC or "— (0 is normal for some feeds)">
Static routes:      <N> routes / <N> with shapes / <N> total shape points

### Vehicle positions (GTFS-RT)
Total entities:     <N>
Vehicle entities:   <N>
With route_id:      <N> (<pct>%)   ← must be high; blanks = skippedNoRouteId
Without route_id:   <N>
Position fields:    lat/lon present in <pct>% of vehicle entities

Optional fields:
  speed present:    <pct>%
  bearing present:  <pct>%
  vehicle.timestamp present: <pct>%

### Route ID alignment (buses)
RT distinct route IDs:     <N>
Static index keys:         <N>
Matched:                   <N> (<pct>%)
Unmatched RT IDs (sample): <list up to 5>
Unmatched static keys (sample): <list up to 5>
ID format notes:           <e.g. "RT uses plain integers; static uses same — no transform needed">

### Rail (heavy rail / route_type=1) — omit this section if the agency has no rail
Static rail routes:        <rail_route_count> — keys: <rail_index_keys or "none">
Rail realtime API:         <url or "not provided / agency has no rail API">
Realtime trains:           <N> (after dropping <N> IS_REALTIME != "true")
Live-position check:       <PASS / FAIL>
LINE keys seen:            <list>
LINE ↔ static rail match:  <N> (<pct>%)  ← unmatched LINEs become skippedUnknownRoute
Rail line transform:       <none / describe>

### Verdict
Buses: <COMPATIBLE / PARTIALLY COMPATIBLE / INCOMPATIBLE>
Rail:  <COMPATIBLE / PARTIALLY COMPATIBLE / INCOMPATIBLE / N/A — no rail>

Required fields: <PASS / FAIL — explain>
Route ID alignment: <PASS / PARTIAL (<pct>% match) / FAIL — explain mismatch pattern>
Rail line alignment: <PASS / PARTIAL / FAIL / N/A>

<One sentence: what works, what blocks, and whether a simple transform would fix it.>

### Adding this agency
To add <agency> as a data source:
- Static GTFS zip URL: <url>
- GTFS-RT vehicle positions URL (buses): <url>
- Rail realtime API URL (trains): <url or "n/a">
- Auth: <none / describe>
- Route ID transform needed (buses): <none / describe>
- Rail line transform needed: <none / describe / n/a>
- GtfsStaticLoader change: update hardcoded URL or make configurable (rail routes load
  automatically via route_type — no extra static work)
- Worker change (buses): <none if IDs align / describe transform if needed>
- Rail adapter change: <none if agency rail JSON matches MARTA's schema (unlikely) /
  NEW agency-specific adapter mirroring RailRealtimeAdapter — the current one is
  MARTA-JSON-specific>
```

## Interpreting partial compatibility

| Scenario | Verdict | Likely fix |
|----------|---------|-----------|
| All required fields present, route IDs 100% match | COMPATIBLE | No changes needed |
| Required fields present, route IDs partially match | PARTIALLY COMPATIBLE | Investigate ID format; may need a normalization step |
| Missing `position.latitude` / `position.longitude` | INCOMPATIBLE | Feed does not carry vehicle positions |
| >20% of vehicles missing `route_id` | PARTIALLY COMPATIBLE | Vehicles without route ID will be skipped; assess if acceptable |
| Route IDs 0% match | INCOMPATIBLE (likely fixable) | Almost certainly a format mismatch; identify the transform |
| `speed` absent on many vehicles | COMPATIBLE (degraded) | Normal; speed is optional — lerp telemetry will have sparse speed fields |
| `header.timestamp = 0` | COMPATIBLE | Normal for some agencies; not a decode error |
| Static has `route_type=1` routes, no rail feed provided | RAIL UNASSESSED | Rail geometry exists; need the live train API URL to judge rail |
| Rail `LINE` keys match static rail keys | RAIL COMPATIBLE | Trains snap; if JSON schema ≠ MARTA's, still need a new adapter |
| Rail `LINE` keys don't match static rail keys | RAIL PARTIAL | Line-key transform needed (same as bus route-id mismatch) |
| Rail live-position check FAIL | RAIL INCOMPATIBLE | lat/lon isn't the live train position; feed can't drive motion |
| Agency has no `route_type=1` routes | RAIL N/A | No heavy rail; evaluate buses only |

## Wrap-up

After the report, offer one of:
- "The feeds look compatible — here's what you'd need to change to wire it up."
- "There's a route ID mismatch — here's what the transform would look like."
- "A required field is missing — this feed can't drive the worker as-is."
- (rail) "Rail geometry's there but it needs its own adapter — here's the shape of it."

If the user wants to proceed with onboarding the agency, point them to:
- `GtfsStaticLoader.cs` — hardcoded `GtfsStaticUrl` const (currently MARTA's zip).
- `Worker.cs` — hardcoded `_gtfsRtUrl` (bus GTFS-RT) + the rail merge in `ExecuteAsync`.
- For rail: `RailRealtime/` (`RailRealtimeAdapter`, `RailArrivalDto`, `RailRealtimeOptions`)
  + the `Marta:RailRealtime` config (base URL in appsettings, **API key via env/secrets,
  never committed**). A non-MARTA rail feed almost certainly needs a new DTO + adapter,
  since the current one parses MARTA's specific JSON field names.

(Line numbers drift — search by symbol name rather than trusting a line cite.)
