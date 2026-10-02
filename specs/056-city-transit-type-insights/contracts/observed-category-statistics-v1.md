# Observed City Category Statistics v1

**Definition version**: `observed-city-category-statistics-v1`  
**Consumers**: Operators and analysts through documented read-only queries  
**Producer**: Existing city-cycle processing  
**Public network interface**: None added in this release

## Capture and sink contract

The worker counts eligible activity once per vehicle/city/cycle across all categories, using the first eligible joined entity in feed order for its representative category and movement observation. This selection does not alter live entity processing or the count of actual published crossing records. Movement requires increasing per-vehicle source timestamps on the same route/geometry generation. It commits one aggregate observation at city-cycle completion. All timestamps are UTC. Distance completing across a boundary belongs once to that completing cycle.

The synchronous sink operation is `bool TryEnqueue(FinalizedCategoryStatisticsBatch batch)`. A batch contains finalized aggregate rows for one city and optional closed-hour rows. It contains no vehicle, route, trip, or listener identity. True means accepted into bounded process memory; false means lost admission. This contract does not acknowledge persistence.

Cycle processing never awaits database/channel capacity. The writer reports `Inserted`, `Unchanged`, `Conflict`, `BackingMinutesUnavailable`, and `Dropped` with bounded aggregate counts and reason codes. Conflict quarantine never replaces counters. No existing live message, metric, or publisher method changes.

## Query inputs and result envelope

Inputs are canonical city, requested category or category set, supported definition version, `from_utc < to_utc`, and the city's configured IANA zone. All ranges are start-inclusive/end-exclusive. Named-zone validation occurs before grouping. Categories may include retained historical labels even if absent from the current catalog.

Every result carries:

- Requested UTC range and actual contributing complete UTC hours/ranges.
- City, category, definition version, metric definition, unit, numerator, denominator, and nullable value.
- Requested minutes/hours, durable complete minutes, contributing complete hours, excluded windows with reasons, conflicts, and minute diagnostics when requested.
- `first_collectible_utc`, or unavailable when no category observations exist.
- Individual hour UTC identity, local date/hour, and UTC offset; partial coverage is explicitly labeled.

Generate a requested grid to identify missing periods. `unknown` is included only for an hour with actual eligible unknown observations. Earlier unknown minutes or cycles are never synthesized: an activation minute with omitted earlier cycles is Partial, even within minute zero, and the hour is incomplete. Complete unknown hours require every cycle and boundary to be represented. Do not interpret unobserved unknown buckets as complete zeros.

Averages use underlying summed denominators. A zero denominator produces null, never zero. Incompatible versions or cadence policies cannot silently pool into one complete result.

Changed measure eligibility, denominators, or category meanings require a new definition version; ordinary membership changes under stable category meanings do not. Preserve old rows and separate versions in query results. Never silently reuse the v1 definition for incompatible semantics.

| Measure | Numerator | Denominator | Unit |
| --- | --- | --- | --- |
| Primary movement | accepted meters | sum of complete-hour distinct active counts | meters per observed vehicle-hour |
| Diagnostic movement | accepted meters | accepted movement intervals | meters per vehicle update |
| Activity | sum of eligible cycle distinct counts | valid active samples | average observed vehicles per cycle |
| Hourly cadence | published crossing records | one complete UTC hour | published opportunities per covered hour |
| Typical local-hour cadence | published crossing records across bucket | count of complete UTC hours in bucket | opportunities per complete observed hour |

## Authoritative complete-hour selection

The following parameterized query illustrates the durable verification required for hourly measures. Parameters are bound values, never interpolated SQL. Application/report metadata supplies definitions and coverage; this result set alone is not the complete response envelope.

```sql
WITH minute_evidence AS (
    SELECT city_slug, category, definition_version,
           date_trunc('hour', stat_minute_utc, 'UTC') AS hour_start_utc,
           count(*) AS minute_count,
           bool_and(collection_status = 'Complete' AND NOT has_conflict) AS all_complete,
           min(healthy_cadence_limit_seconds) AS min_limit,
           max(healthy_cadence_limit_seconds) AS max_limit,
           sum(observed_cycle_count) AS observed_cycle_count,
           sum(valid_active_sample_count) AS valid_active_sample_count,
           sum(valid_publish_cycle_count) AS valid_publish_cycle_count,
           sum(failed_cycle_count) AS failed_cycle_count,
           sum(active_vehicle_count_sum) AS active_vehicle_count_sum,
           sum(distance_meters_sum) AS distance_meters_sum,
           sum(distance_interval_count) AS distance_interval_count,
           sum(distance_rejected_count) AS distance_rejected_count,
           sum(crossings_published_count) AS crossings_published_count
    FROM public.city_category_minute_statistics
    WHERE city_slug = :city_slug
      AND category = :category
      AND definition_version = :definition_version
      AND stat_minute_utc >= :from_utc
      AND stat_minute_utc < :to_utc
    GROUP BY city_slug, category, definition_version,
             date_trunc('hour', stat_minute_utc, 'UTC')
), verified_hours AS (
    SELECT h.*
    FROM public.city_category_hour_statistics h
    JOIN minute_evidence m USING (city_slug, category, definition_version, hour_start_utc)
    WHERE h.city_slug = :city_slug AND h.category = :category
      AND h.definition_version = :definition_version
      AND h.hour_start_utc >= :from_utc
      AND h.hour_start_utc + interval '1 hour' <= :to_utc
      AND h.collection_status = 'Complete' AND NOT h.has_conflict
      AND h.covered_minutes = 60 AND h.distinct_active_vehicle_count IS NOT NULL
      AND m.minute_count = 60 AND m.all_complete
      AND m.min_limit = h.healthy_cadence_limit_seconds
      AND m.max_limit = h.healthy_cadence_limit_seconds
      AND m.observed_cycle_count = h.observed_cycle_count
      AND m.valid_active_sample_count = h.valid_active_sample_count
      AND m.valid_publish_cycle_count = h.valid_publish_cycle_count
      AND m.failed_cycle_count = h.failed_cycle_count
      AND m.active_vehicle_count_sum = h.active_vehicle_count_sum
      AND m.distance_meters_sum = h.distance_meters_sum
      AND m.distance_interval_count = h.distance_interval_count
      AND m.distance_rejected_count = h.distance_rejected_count
      AND m.crossings_published_count = h.crossings_published_count
)
SELECT city_slug, category, definition_version, healthy_cadence_limit_seconds,
       hour_start_utc, covered_minutes,
       distance_meters_sum,
       distinct_active_vehicle_count,
       distance_interval_count, distance_rejected_count,
       valid_active_sample_count, valid_publish_cycle_count,
       distance_meters_sum / nullif(distinct_active_vehicle_count, 0)
           AS avg_meters_per_observed_vehicle_hour,
       distance_meters_sum / nullif(distance_interval_count, 0)
           AS avg_meters_per_vehicle_update,
       active_vehicle_count_sum::numeric / nullif(valid_active_sample_count, 0)
           AS avg_active_vehicles,
       crossings_published_count AS published_crossings_per_hour
FROM verified_hours
ORDER BY hour_start_utc;
```

Primary keys and alignment checks guarantee exactly one minute identity per category/window. Version grouping deliberately prevents mismatched minute versions from satisfying the 60-minute requirement. A query executes hour selection and minute verification in one database snapshot, so late minute quarantine cannot be overlooked.

For period summaries, replace the final selection with sums over `verified_hours`, grouped by city, category, definition version, and healthy-cadence limit; do not average the displayed hourly averages. Return separate groups when a range spans coverage policies. Preserve no-contributing-hours as unavailable with zero contributing hour count.

## Missing minutes and partial periods

Generate exact UTC minute starts intersecting the requested range and left-join category minute rows. A missing key has `Missing` response coverage, not a synthesized persistent `NoData` row. A stored conflicted row has effective incomplete coverage and `has_conflict=true`; retain its original capture status only as diagnostic metadata.

If a requested boundary cuts through a stored minute, return that whole-minute aggregate only as boundary context with its actual bounds. Do not prorate it, count it as fully covered requested time, or claim exact statistics for unobserved subminute slices. Full contained minutes can supply explicitly labeled minute diagnostics. Hourly measures use only full contained verified UTC hours.

For missing/unverifiable hours, explain whether the cause is no category history, partial capture, missing durable minutes, missing intact distinct population, version/policy mismatch, additive mismatch, or conflict. Response `covered_minutes` is counted from durable complete nonconflicting minute evidence; a partial stored hour's capture count is diagnostic only.

## Local time and daylight-saving contract

Convert each verified UTC hour through `AT TIME ZONE :time_zone_id`. Preserve the UTC hour key, derive local date/hour, and calculate offset by comparing named-zone local time with UTC local representation. Never identify an individual hour solely by local hour number.

Typical-hour results group by city/category/version/healthy-cadence limit and local hour, then divide summed published crossings by the number of verified UTC hour rows. The repeated local hour contributes twice when both underlying UTC hours are complete; a skipped local hour contributes no row. Publish contributing days/hours and excluded coverage alongside cadence.

## Acceptance examples and failures

- Complete hour: 1,200 meters, three distinct vehicles, two intervals -> 400 meters/vehicle-hour and 600 meters/update.
- Two hours: 1,200/3 and 1,800/2 -> 3,000/5 = 600 meters/vehicle-hour.
- Activity cycles: 2, 0, 4 -> 6/3 = two observed vehicles per cycle.
- Typical hour: 50 and 70 crossings in two complete observations -> 120/2 = 60 opportunities/hour.
- Complete zero-vehicle hour -> zero activity and known crossing cadence, null distance-per-vehicle-hour and null update mean if no intervals.
- Missing one of 60 durable minute keys -> no definitive hourly row.
- Late conflict on a minute -> parent hour excluded even if its capture status remains Complete.
- NoData/failed publish -> unavailable cadence, not zero.
- Changing synthetic position with repeated source timestamp -> no accepted distance interval.

## Executable analyst recipes

Bind every parameter through the PostgreSQL client. Each recipe addresses one city/category pair; a category-set request runs the recipes for each requested retained label. Validate an ordered UTC range and use the city's configured IANA zone. These files define read-only queries and do not add an endpoint or authorize publication before the pilot gate.

| File | Result and denominator |
| --- | --- |
| [hourly-insights.sql](hourly-insights.sql) | Individual verified UTC hours; exact whole-hour distinct vehicle count and accepted interval count remain separate |
| [activity-insights.sql](activity-insights.sql) | Period activity: `active_vehicle_count_sum / valid_active_sample_count` |
| [cadence-insights.sql](cadence-insights.sql) | Period published opportunities: `crossings_published_count / complete_hour_count` |
| [period-insights.sql](period-insights.sql) | All period measures with their summed numerators and distinct denominators |
| [local-hour-cadence.sql](local-hour-cadence.sql) | Individual local-hour observations with bucket totals, complete UTC-hour count, and contributing-day count |
| [minute-coverage.sql](minute-coverage.sql) | Requested minute/hour grids, boundary context, actual stored status, missing/conflicting evidence, and exclusion reasons |

Common bound parameters are `city_slug`, `category`, `definition_version`, `from_utc`, and `to_utc`. The local-hour recipe additionally binds `time_zone_id`. The hourly recipe selects an explicit supported definition. The other recipes accept a typed SQL null for `definition_version` to inspect all retained definitions, while returning separate version/policy groups. A consumer must have a definition and unit mapping for each supported group; an unknown definition stays an explicitly unsupported result and is never presented with v1's labels.

The activity, cadence, and period recipes return at least one unavailable group for an ordered range with no contributing hours. Its `complete_hour_count` is zero, contributing bounds are null, and means are null. Zero-valued sum fields describe an empty contributing set; they do not claim observed zeros. When no observation determines a policy, `healthy_cadence_limit_seconds` is null. A requested category without retained observations returns null `first_collectible_utc`, including an explicitly requested, never-observed `unknown` category. No persistent unknown rows or prior zeros are created.

`first_collectible_utc` is the earliest retained category-minute key, across definitions and outside the requested range if appropriate. Label it **beginning of retained category capture**. It is neither a recovered historical start nor a guarantee that all subsequent minutes are available. A later retention decision may move that date forward.

### Reading the coverage result

The coverage recipe returns one `row_kind = minute` row for each UTC minute intersecting the request and one `row_kind = hour` row for each intersecting UTC hour. The full source bounds are `window_start_utc` and `window_end_utc`; `requested_segment_from_utc` and `requested_segment_to_utc` describe only their intersection with the request. Rows with `is_partial_boundary = true` carry whole-window context. Their observations are not prorated and cannot be labeled exact statistics for the selected subminute slice.

`collection_status` is immutable capture evidence; `has_conflict` is monotone authority metadata. A stored Complete row can have a conflict flag and be unavailable. A missing identity has null stored status and `is_missing = true`, preserving the distinction from a stored NoData row. `is_definitive_complete_hour` is the authoritative flag for hourly measures; require it before presenting vehicle-hour means or hourly cadence.

Hour `durable_complete_minute_count` counts stored Complete, nonconflicting minutes in the full UTC hour. `compatible_complete_minute_count` additionally requires the stored hour's definition and cadence policy. Both can be diagnostic for an excluded hour, and neither alone establishes authority. An absent hour cannot establish a compatible-hour policy, so its compatible count is zero even when minute observations survive. Minute rows have null hourly coverage counts. A report's `covered_minutes` for a defined hour uses compatible durable evidence, while requested fully covered minutes exclude boundary context and incompatible/conflicted observations.

Minute eligible numerators and means may survive Partial coverage as clearly labeled diagnostics. NoData, conflict, or unsupported requested-definition evidence masks measures. Observed/sample/failure/rejection counts and raw distinct-population diagnostics remain visible when a row exists; they cannot make an unavailable measure definitive. A valid publication sample with zero crossings is a known zero; no valid publication sample yields an unavailable crossing value. Hour measures are masked unless the entire requested UTC hour is verified and contained.

| Exclusion reason | Interpretation |
| --- | --- |
| `Missing` | No retained identity, including pre-collection or unknown-before-activation periods |
| `NoData` | Stored failure/rejection diagnostics without eligible activity, movement, or publication samples |
| `Partial` | Useful capture with incomplete processing, timing, lifecycle, or loss evidence |
| `Conflict` | The row or a backing minute is quarantined |
| `DefinitionMismatch` | Requested and stored definitions differ, or backing minute versions disagree |
| `CadenceMismatch` | Backing minutes do not use the hour's stored coverage policy |
| `BackingMinutesUnavailable` | Fewer than 60 durable minute identities survive |
| `IncompleteBackingMinutes` | All identities exist but at least one is not Complete and nonconflicting |
| `DistinctPopulationUnavailable` | The exact whole-hour population is unavailable |
| `AdditiveMismatch` | Full backing evidence does not reconcile with the captured hour |
| `PartialBoundary` | The request contains only part of this otherwise covered window |

### Assembling a report

Read selected measure recipes and `minute-coverage.sql` on the same connection in a read-only Repeatable Read transaction. This keeps their combined metadata in one snapshot when a late conflict appears. Report generation must preserve requested bounds, actual contributing bounds, definition and policy groups, all relevant denominators, and excluded windows/reasons. The complete-hour count applies to definitive means; fully covered minute counts apply to the requested coverage grid. Never multiply a sampled rate to invent missing hours.

Use these v1 descriptions with each measure:

- **Observed along-route distance per vehicle-hour**: accepted absolute movement divided by exact distinct eligible vehicles summed over complete UTC hours; unit meters per observed vehicle-hour. This is an along-route estimate, not completed-trip distance.
- **Observed distance per accepted update**: accepted absolute movement divided by accepted fresh intervals, including stationary intervals; unit meters per vehicle update. Rejected and repeated-source observations remain outside the denominator.
- **Average observed activity**: distinct joined positioned vehicles per eligible city/category cycle, weighted by actual sample count; unit observed vehicles. This does not confirm in-service status.
- **Published tone opportunities**: crossing records in successfully published city batches; unit published opportunities per complete observed hour. Browser selections, mutes, playback, and notes actually heard do not enter capture.

The local-hour recipe preserves `hour_start_utc`, local date/hour, and `utc_offset_seconds` for each individual observation. Its bucket total and mean are repeated on each contributing row for inspection; deduplicate by city/category/version/policy/local hour when displaying a bucket summary. Both UTC instances of a repeated autumn hour contribute. The spring skipped hour has no invented observation. Join excluded coverage from the shared snapshot rather than treating the absence of a local-hour row as zero.
