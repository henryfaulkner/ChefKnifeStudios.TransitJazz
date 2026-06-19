<!-- last verified: 2026-06-19 -->

# Function: GTFS Compatibility

Evaluate whether a transit agency's GTFS feeds are compatible with the TransitJazz
data worker algorithm. You were routed here because the user wants to assess a new
data source or understand why an existing one is producing skips or mismatches.

Use the `mj-gtfs` skill as your data-fetch tool. It handles all downloading and
decoding — read it before fetching anything.

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

### Step 2 — Fetch both feeds via mj-gtfs

Read and follow `mj-gtfs` to fetch and decode:
1. The static GTFS zip → get the **GTFS-RT output block** and **Static shapes output block**
2. The GTFS-RT protobuf feed → get the same two blocks

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
RT feed size:       <N> bytes  |  Header ts: <UTC or "—">
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

## Wrap-up

After the report, offer one of:
- "The feeds look compatible — here's what you'd need to change to wire it up."
- "There's a route ID mismatch — here's what the transform would look like."
- "A required field is missing — this feed can't drive the worker as-is."

If the user wants to proceed with onboarding the agency, point them to
`GtfsStaticLoader.cs` (hardcoded URL at line 23) and `Worker.cs` (hardcoded RT URL
at line 23) as the two places that need updating.
