<!-- last verified: 2026-06-19 -->

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

## Method

### Step 1 — Gather inputs

Ask the user for:
- The agency name (for the report header)
- Static GTFS zip URL
- GTFS-RT vehicle positions URL
- Auth requirements (if any)

If the user only has one of the two URLs, proceed with what's available and flag what's
missing in the report.

### Step 2 — Fetch both feeds via mj-gtfs (in parallel)

Read `mj-gtfs` first, then issue both fetches in a single parallel tool call:
1. Fetch + decode the **GTFS-RT protobuf** → produces the **GTFS-RT output block**
2. Fetch + decode the **static GTFS zip** → produces the **Static shapes output block**

Do not serialize these — they are independent and fetching in parallel saves latency.

After decoding: if `lat_lon_pct` is 0% but `vehicles_with_route_id > 0`, the decoder
hit a field-number mismatch. The output will include `_diag_vp_fields` listing the
actual VehiclePosition field numbers — use those to identify the position field before
continuing. Do not proceed to Step 3 with null positions.

### Step 3 — Cross-check route ID alignment

The critical question: do the GTFS-RT `route_id` values match the static route index keys?

```
RT route IDs (sample):   110, 12, 15, 19, 1, 21, 23, 240, ...
Static index keys:        1, 10, 100, 101, 102, 103, ...
```

- **Exact match**: a RT `route_id` appears verbatim in the static index keys → vehicle will snap
- **Mismatch**: RT uses `"route_110"` but static has `"110"` → `skippedUnknownRoute`
- **Format difference**: numeric vs zero-padded, prefixed, or agency-prefixed IDs are a
  common failure mode

Compute and report:
- What % of RT route IDs have a corresponding static index key
- Any systematic transformation that would fix a mismatch (e.g. strip prefix, parse int)

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

### Route ID alignment
RT distinct route IDs:     <N>
Static index keys:         <N>
Matched:                   <N> (<pct>%)
Unmatched RT IDs (sample): <list up to 5>
Unmatched static keys (sample): <list up to 5>
ID format notes:           <e.g. "RT uses plain integers; static uses same — no transform needed">

### Verdict
<COMPATIBLE / PARTIALLY COMPATIBLE / INCOMPATIBLE>

Required fields: <PASS / FAIL — explain>
Route ID alignment: <PASS / PARTIAL (<pct>% match) / FAIL — explain mismatch pattern>

<One sentence: what works, what blocks, and whether a simple transform would fix it.>

### Adding this agency
To add <agency> as a data source:
- Static GTFS zip URL: <url>
- GTFS-RT vehicle positions URL: <url>
- Auth: <none / describe>
- Route ID transform needed: <none / describe>
- GtfsStaticLoader change: update hardcoded URL or make configurable
- Worker change: <none if IDs align / describe transform if needed>
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

## Wrap-up

After the report, offer one of:
- "The feeds look compatible — here's what you'd need to change to wire it up."
- "There's a route ID mismatch — here's what the transform would look like."
- "A required field is missing — this feed can't drive the worker as-is."

If the user wants to proceed with onboarding the agency, point them to
`GtfsStaticLoader.cs` (hardcoded URL at line 23) and `Worker.cs` (hardcoded RT URL
at line 23) as the two places that need updating.
