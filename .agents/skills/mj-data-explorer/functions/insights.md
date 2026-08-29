<!-- last verified: 2026-06-07 -->

# Function: Insights

Surface interesting patterns and trends across the telemetry. You were routed here
because the user is curious / exploring rather than chasing a specific bug. Be a
guide: suggest a worthwhile angle, query for it, narrate what you find, and offer the
next thread to pull.

Use [../references/telemetry-query-guide.md](../references/telemetry-query-guide.md)
for tool mechanics and
[../references/telemetry-schema.md](../references/telemetry-schema.md) for columns.

## Working within a filter-only tool

The tool filters rows of one day's partition — no GROUP BY, no SELECT, no ORDER BY.
So "insights" come from **comparative filtering and reasoning**, not server-side
aggregation:

- Approximate counts by filtering to a condition and counting returned rows (watch for
  output truncation on busy days — narrow if you suspect it).
- Compare buckets by running the same threshold at different cutoffs (e.g.
  `snap_distance_km > 0.5` vs `> 2.0`) and comparing how many rows come back.
- Compare across days by querying each day separately (one `date` per call) and
  reasoning over the deltas.
- Always read the rows and translate to a human-meaningful observation.

## Starter angles (offer one, don't list all to the user)

### Operational health over the day (`cycle`)
- `cycle_execution_seconds > 5` — how often cycles ran slow.
- `duplicate_feed = true` — how much of the day was redundant feed ingestion.
- `sidecar_dropped_records > 0` — was there telemetry loss / load pressure.
- `buses_skipped_unknown_route > 0` — recurring GTFS mapping gaps.

### Movement & behavior (`lerp`)
- `pos_delta_km > 2.0` — big movers between cycles.
- `time_delta_sec > 120` — vehicles with sparse updates.
- `speed_delta > 5` — sharp speed changes (acceleration/teleport candidates).

### Snapping quality (`snap`)
- `snap_distance_km > 1.0` — how much GPS lands off-route.
- `snap_outcome = 'Stale'` vs `= 'Stationary'` vs `= 'Moved'` — the mix of outcomes
  (run each as a separate filter and compare row counts).
- `is_stale = true` — staleness prevalence.

### Route / vehicle ID drift (`cycle`)
Each cycle row carries `active_route_ids` and `active_vehicle_ids` — comma-separated
CSV strings of the distinct IDs seen in that cycle (only those that were actually
processed, not skipped). Use these to answer:

- **Which routes ran today?** Query a cycle row (e.g. `cycle_execution_seconds > 0`),
  read `active_route_ids`, and split the CSV. Compare against the API's known routes
  (`GET /gtfs/routes/shapes`) to find any unexpectedly absent or present routes.
- **Did route IDs change over time?** Query the same filter across two dates and
  compare the CSV contents. A route appearing or disappearing signals a GTFS feed
  update or a real service change.
- **How many buses were on a route?** Because `active_vehicle_ids` is per-cycle,
  count how many vehicle IDs appear in a representative cycle for the peak hour you
  care about. Comparing that across days reveals fleet drift.
- **Did a vehicle jump routes?** Cross-reference a specific vehicle in `active_vehicle_ids`
  on one day (filter a cycle with `active_vehicle_ids = '<vehicle_id>'` won't work —
  the column is CSV, not atomic) with snap rows (`vehicle_id = 'v001'`) to see which
  routes it appeared on.

> **Note on filtering**: `active_route_ids` and `active_vehicle_ids` are CSV strings,
> so equality filters like `active_route_ids = '110'` only match a cycle where that
> ID is the *only* active route. For multi-value membership you need to pull cycle rows
> and inspect the CSVs in conversation. A useful approach: query
> `buses_processed > 0` to get cycles with activity, then read and parse the CSV from
> the results.

### Per-route / per-vehicle focus
- Pick a `route_id` (snap) or `vehicle_id` (snap/lerp) the user cares about and
  characterize its day: distances, outcomes, movement.
- If the user doesn't know which route IDs exist, call `GET /gtfs/routes/shapes` via
  the API to enumerate them — see
  [../references/mj-api-query-guide.md](../references/mj-api-query-guide.md).

## How to present an insight

1. Lead with the finding in plain language ("About a third of cycles today ingested a
   duplicate feed — the upstream source stalled for stretches.").
2. Back it with the concrete numbers you observed (rows matched, thresholds used,
   the day queried).
3. Note the caveat if the result might be truncated or the day is partial.
4. Offer the next thread: a deeper cut, a different day to compare, or a related
   dataset. Keep it one suggestion, conversational.

## Comparing days (common request)

To answer "is today worse than yesterday?": run the same filter with two `date`
values, count rows from each, and report the direction and rough magnitude of the
change — being explicit that these are row-count comparisons, not exact aggregates.
