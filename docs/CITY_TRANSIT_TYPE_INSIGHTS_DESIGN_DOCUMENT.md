# City and Transit Type Insights — Design Document

**Status:** Proposed

**Date:** 2026-10-01

**Scope:** Historical, server-observed statistics by city and transit category

## Goal

Answer questions such as:

- How far does an observed vehicle travel during an hour, on average, for each city's bus, rail, streetcar, or other configured category?
- How many vehicles are active on average for each city and category during a chosen hour?
- How many tone opportunities does the worker publish per hour for each city and category, and how does that rate vary by hour of day?

The results must carry their time range, unit, definition, and coverage. A missing observation must never appear as zero. Store UTC; convert to a city's local time zone only when grouping by local hour of day. "Transit type" means the app's configured route category, rather than the raw GTFS `route_type` number.

## Current boundary

`public.city_minute_statistics` has one row per city and minute. It has no category or traveled-distance column. Its `vehicles_processed` and `tones_emitted` values are the latest city-cycle samples in each minute, so summing them does not produce hourly counts. Existing rows cannot be divided into historical bus/rail/streetcar values.

The worker already loads `RouteShapeFeature.Properties.Category` into a route-to-category map and computes each snapped vehicle's distance along a route. `CrossingDetector` identifies checkpoint crossings. The current city-level `TonesEmitted` value is the count of crossing records prepared in a cycle. The client schedules the audible note after receiving those records. The client's `ActiveCountsByCategory` can also be filtered by the viewer's selection, so it is not a source for global historical counts.

Relevant code: `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`, `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Checkpoints/CrossingDetector.cs`, `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Metrics/WorkerMetricsReporter.cs`, and `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/ViewModels/RouteFilterViewModel.cs`.

## Definitions

| Measure | Definition | Unit and interpretation |
| --- | --- | --- |
| Category | The lowercase configured route category at observation time. Keep `unknown` separate; never silently classify an unmatched route as `bus`. | Category names are city-specific and may include more than bus and rail. |
| Active vehicles | Distinct vehicle IDs with a valid position, a successfully joined route, and a successful current city feed/cycle. Include stationary vehicles. Count each vehicle once in its category for that cycle. | Vehicles observed by the worker, not proof of in-service status or a viewer's selected routes. |
| Average active vehicles | Sum of eligible per-cycle active counts divided by the number of eligible cycle samples in the requested period. | Mean observed vehicles per cycle; do not sum the counts. |
| Observed travel distance | Absolute change in snapped along-route meters between two fresh observations of the same vehicle on the same route. Accept forward and reverse movement; include valid zero movement. Exclude first sightings, duplicate/stale feed positions, route changes, invalid geometry, and jumps beyond the existing 2,000 m teleport guard. | Estimated meters along the stored shape, not an odometer or completed-trip distance. This is especially important for interpolated rail positions. |
| Average distance per observed vehicle-hour | Sum of accepted travel distance in a complete hour divided by the number of distinct active vehicles seen in that city/category/hour. An active vehicle with no accepted movement contributes zero distance. | Meters per observed vehicle-hour. Across several hours, sum distance and distinct-vehicle counts from each complete hour; a vehicle present in two hours contributes two vehicle-hours. |
| Average distance per vehicle update | Sum of accepted distance changes divided by the number of eligible same-route vehicle intervals, including zero-change intervals. | Meters per observed vehicle interval. Also expose total observed distance and the interval count. Do not label this a trip average. |
| Tone opportunity cadence | Number of crossing records in city cycles whose batch publish succeeds, grouped by the crossing's route category and UTC hour. | Published crossing opportunities per covered hour. It is not a count of notes heard by browsers. |

An exact mean **distance per completed vehicle trip** needs trip/session boundaries and a trip-level denominator. It is a separate extension. An exact **audible note** cadence needs client playback events with a clearly defined listener population; server crossing records alone cannot establish that measure.

## Capture path

1. In `Worker.ProcessSpatialReconciliationAsync`, resolve each joined vehicle's category with the existing route map. Build per-category counters during the existing vehicle loop. Deduplicate vehicle IDs within the cycle before counting active vehicles. Keep the current city-level metrics and SignalR wire format unchanged.
2. Before replacing prior vehicle state, compare the current snapped route distance with the previous snap index on the same route. Apply the freshness, route-change, and teleport rules above. Add accepted absolute deltas and interval counts to the category accumulator. Track rejected-distance counts for diagnosis.
3. Attribute each crossing record to its resolved route category. After `PublishBatchAsync` returns success, add those records to the category's published-crossing count. A failed or indeterminate publish makes the cadence observation unavailable for that cycle, not zero.
4. Update in-memory per-category minute and hour accumulators during each completed city cycle. Keep an hour-scoped set of active vehicle IDs for the distinct denominator; discard that set after the hour closes. Include configured categories and `unknown` with observed zero vehicles/crossings when the feed is valid. Failed feed or missing route-index cycles carry failure/coverage information; they do not create apparently healthy zeros. No vehicle IDs enter the database or telemetry queue.
5. At each UTC minute boundary, finalize immutable city/category/minute aggregates. At each hour boundary, finalize hourly aggregates including the distinct vehicle count. Send only finalized rows to a bounded asynchronous PostgreSQL writer with bounded retries; the worker does not wait for the database. Persist and reconcile all 60 minute rows before marking their hour row `Complete`. A crash or queue overflow can make the affected minute or hour incomplete, which queries must report as a gap. The current one-replica deployment avoids normal duplicate producers; detect and report conflicting rows during revision overlap.

The first release begins collecting after deployment. Current city-minute rows cannot provide a per-category backfill. A separate backfill is possible only if an older source is found that actually retained category-tagged positions and crossing events; its availability must be demonstrated before promising history.

## Storage

Add `public.city_category_minute_statistics`, keyed by `(city_slug, category, stat_minute_utc)`, and `public.city_category_hour_statistics`, keyed by `(city_slug, category, hour_start_utc)`. The minute table supports partial periods and coverage checks. The hour table holds the exact distinct-vehicle denominator, which cannot be reconstructed by summing minute-level distinct counts. Leave `city_minute_statistics` and its source contract intact. Persist only aggregate values, never vehicle, route, trip, or listener identifiers.

| Column group | Proposed columns | Purpose |
| --- | --- | --- |
| Identity | `city_slug`, `category`, `stat_minute_utc`, `definition_version`, `category_map_version` | Stable grain, metric semantics, and the route-category mapping in force. |
| Coverage | `observed_cycle_count`, `valid_active_sample_count`, `valid_publish_cycle_count`, `failed_cycle_count`, `first_cycle_utc`, `last_cycle_utc`, `max_observation_gap_seconds`, `collection_status` | Separate observed zero from gaps and incomplete minutes. |
| Activity | `active_vehicle_count_sum` | Numerator for average active vehicles. |
| Movement | `distance_meters_sum`, `distance_interval_count`, `distance_rejected_count` | Total accepted movement and the denominator for its average. Use a nonnegative fixed-precision numeric for meters. |
| Soundscape | `crossings_published_count` | Additive count of successfully published crossing opportunities. |

The hour table stores the same additive numerators and sample counts, plus `distinct_active_vehicle_count`, `covered_minutes`, and `collection_status`. It is finalized from the same accumulator; for a complete hour, its additive measures must equal the sum of its 60 minute rows. A partial hour may retain observed values for diagnosis but cannot be reported as a definitive average.

Use `Complete`, `Partial`, and `NoData` with explicit rules: `Complete` requires valid feed/route processing and a known publish outcome for all observed cycles, at least one valid sample, and no observation gap across the minute boundary beyond a documented healthy-cadence limit calibrated from worker timings. `Partial` has useful values but a failed/unknown cycle or excessive gap; `NoData` has no valid sample. Missing whole minutes are found by comparing keys against a generated minute series. A minute with no vehicles and a valid feed is `Complete` with zero activity. An hour is `Complete` only when all 60 minute rows are complete, its in-memory distinct set remained intact, and its category mapping did not change during the hour.

Write finalized rows idempotently: on key conflict, accept an identical row, flag a different row for reconciliation, and do not silently add its counters again. Record safe failure and coverage summaries without raw feed payloads or IDs. Keep category labels bounded to the configured route catalog plus `unknown`; recalculate the existing metrics-series budget if category-tagged Grafana diagnostics are added.

At the current seven-city scale, four categories per city would create at most 40,320 minute rows and 672 hour rows per day. Retain hourly aggregates for long-term insight queries. A proposed 90-day minute retention must delete a minute only after its hour row has been reconciled; confirm the actual category count and database growth before setting that policy.

## Query contract

For an hourly city/category result, use the `Complete` hour row, which is backed by 60 `Complete` minute rows. Return `covered_minutes` alongside every measure. Across multiple full hours, weight averages by their underlying denominators, not by the average of displayed hourly averages.

```sql
SELECT city_slug,
       category,
       hour_start_utc,
       covered_minutes,
       distance_meters_sum /
           nullif(distinct_active_vehicle_count, 0) AS avg_meters_per_observed_vehicle_hour,
       distance_meters_sum /
           nullif(distance_interval_count, 0) AS avg_meters_per_vehicle_update,
       active_vehicle_count_sum::numeric /
           nullif(valid_active_sample_count, 0) AS avg_active_vehicles,
       crossings_published_count AS published_crossings_per_hour
FROM public.city_category_hour_statistics
WHERE hour_start_utc >= :from_utc
  AND hour_start_utc < :to_utc
  AND collection_status = 'Complete'
  AND covered_minutes = 60
ORDER BY hour_start_utc, city_slug, category;
```

For a typical-hour-of-day view, map each city's complete UTC-hour row to its configured IANA time zone and group by local hour. Keep local date and UTC offset when showing individual hours so daylight-saving transitions do not merge two different hours. Define the average tone cadence across days as total published crossings divided by the number of complete observed hours in that category/hour-of-day bucket.

## Validation and rollout

1. Unit-test category resolution, duplicate vehicle IDs, stationary samples, reverse movement, route transfer, feed repeats, first sighting, teleport rejection, and failed publish. Verify that per-category crossing counts reconcile to the successful city batch count.
2. Test minute bucketing at UTC hour boundaries, distinct vehicle counts across cycles and category changes, zero versus missing, restart/gap detection, hourly-to-minute reconciliation, idempotent database retries, and conflicting revision writes. Use a real PostgreSQL integration test for both composite keys and upsert behavior.
3. Deploy the schema first, then enable capture for one city while comparing the sum of category active counts and generated crossings with the corresponding city-cycle diagnostics. Explain any difference in eligibility rules. Watch worker-cycle duration, queue depth, dropped summaries, database write latency, and complete-minute coverage.
4. Expand to all cities after the one-city comparison passes. Publish queries or an API only after the coverage rules are proven. Label charts with the metric definitions above and the first collectible date; do not imply older city-only records contain category history.

## Explicit limits

- The worker's published crossings measure possible notes, not actual browser playback. A later client telemetry design may measure notes started per listener session with consent, aggregation, and duplicate suppression.
- The primary distance average is per observed vehicle-hour; the per-update average is a diagnostic. Per-trip distance requires a separate trip/session lifecycle and cannot be derived from these aggregates.
- The asynchronous writer protects the worker cadence but cannot recover a minute or hour lost before durable storage. Coverage and gap reporting are part of the result, and stronger durability would require a durable queue or per-cycle transactional record.
