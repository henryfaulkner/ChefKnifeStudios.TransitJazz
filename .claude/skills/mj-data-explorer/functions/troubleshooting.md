<!-- last verified: 2026-06-07 -->

# Function: Troubleshooting

Diagnose common TransitJazz problems from telemetry, data-first. You were routed here
because the user is chasing something that looks broken. Stay conversational: confirm
the symptom, form a hypothesis, query, interpret, narrow.

Use [../references/telemetry-query-guide.md](../references/telemetry-query-guide.md)
for how to call the tool and
[../references/telemetry-schema.md](../references/telemetry-schema.md) for columns.
When a symptom involves route IDs or GTFS load state, also consult
[../references/mj-api-query-guide.md](../references/mj-api-query-guide.md) to cross-reference
against the live API.

## Method

1. **Pin the symptom.** Ask one focused question to turn "it's broken" into something
   measurable: which day? a specific bus or route? buses missing from the map, stale
   positions, slow updates, or nothing at all? Default the day to today (UTC) unless
   they say otherwise.
2. **Pick the layer.** Most issues resolve at the `cycle` (system health) layer first;
   drop to `snap`/`lerp` only once you've localized it to per-vehicle behavior.
3. **Query, interpret, narrow.** Run a targeted filter, read the rows back to the user
   in plain language, and propose the next probe. One step at a time.
4. **Conclude.** State what the data shows, the most likely cause, and (if useful) what
   to check in the app/worker. Be honest about what the data can't tell you.

## Symptom → first probe playbook

### "Buses are missing from the map" / fewer buses than expected
Likely route-mapping or skip behavior. Check `cycle`:
- `buses_skipped_unknown_route > 0` — GTFS route-id mapping gap (route in feed not
  recognized). This is the usual culprit.
- `buses_skipped_no_route_id > 0` — feed entries lacking a route id.
- Compare `buses_processed` vs `buses_moved + buses_unchanged + buses_stationary`.
Then drop to `snap` for the affected `route_id` to see individual outcomes.
To confirm whether the missing `route_id` is recognized by the server at all, call
`GET /gtfs/debug/keys` via the API — see
[../references/mj-api-query-guide.md](../references/mj-api-query-guide.md). A 503
from the shapes endpoint also means GTFS data isn't loaded, which explains all
skipped routes at once.

### "Bus positions are stale / not updating"
Feed-freshness problem. Check `cycle`:
- `buses_stale > 0` (how widespread).
- `duplicate_feed = true` — the worker ingested the same feed again, so there's
  genuinely no new data (upstream feed stalled, not a TransitJazz bug).
Then `snap` with `is_stale = true` to see which vehicles, and `lerp` with a small
`pos_delta_km` over a large `time_delta_sec` to spot vehicles that should have moved.

### "Updates are slow / laggy"
Check `cycle`:
- `cycle_execution_seconds` high — slow reconciliation.
- `sidecar_buffer_occupancy` rising and `sidecar_dropped_records > 0` — the logging
  sidecar is shedding load (telemetry loss under pressure; a sign of hot-path
  pressure, though it doesn't itself slow the map).

### "We're losing telemetry / data looks incomplete"
Check `cycle`:
- `sidecar_dropped_records > 0` — records dropped under backpressure (DropWrite).
- `sidecar_persist_failures > 0` — uploads to blob failing (credential / managed
  identity / storage problem). This means missing parquet files downstream.

### "A specific bus is behaving weirdly"
Filter by `vehicle_id` across layers:
- `snap`: `vehicle_id = '...'` → outcomes, `snap_distance_km` (large = off-route GPS),
  `is_stale`.
- `lerp`: `vehicle_id = '...'` → `pos_delta_km`, `speed_delta`, `time_delta_sec`
  (teleports, frozen position, impossible speeds).

### "GPS / snapping looks wrong" (buses off their route)
Check `snap`:
- `snap_distance_km > 1.0` (tune threshold) — raw fixes landing far from the route.
- `snap_outcome = 'Stale'` or `'Stationary'` clusters.

## Interpreting results

- **Empty result** usually = "none matched", which is often good news for a
  failure-mode filter (e.g. no `sidecar_persist_failures > 0` rows → no persist
  failures that day). Say so plainly.
- **Truncated/large result** on a busy day → tighten the filter (add a route, vehicle,
  or higher threshold) and re-query rather than guessing at totals.
- Correlate with `cycle_id` (links snap/lerp rows to their cycle) and `vehicle_id`
  (links a bus across snap/lerp) by querying each dataset and reasoning across them.

## Wrap-up

Summarize: symptom → what the telemetry showed → most likely cause → suggested next
check. If the data is inconclusive, say what additional day/vehicle/route would help
and offer to keep digging.
