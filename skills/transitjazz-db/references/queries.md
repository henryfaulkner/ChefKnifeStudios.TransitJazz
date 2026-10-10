# Running queries

Resolve repository paths from the root. Use `scripts/query.ps1` through its canonical path `./skills/transitjazz-db/scripts/query.ps1`, even when this skill was loaded from a generated agent directory. Connection selection is documented in [connection.md](connection.md).

Accept a nonempty UTC range (`from_utc < to_utc`), a canonical city, and retained category labels or ordinal canonical route keys. Normalize caller local-time requests using the selected city's named zone and retain the resolved UTC bounds. Inspect available labels/definitions when uncertain. If a range is omitted, start with the latest closed UTC hour and state it. Preview at most 100 rows unless the user asks for more; compute aggregate/coverage summaries over all requested evidence.

## Connection, schema, and migration discovery

Before the first report against a target, confirm the connection and schema with narrow read-only introspection:

```powershell
& ./skills/transitjazz-db/scripts/query.ps1 -Sql @'
SELECT current_database() AS database_name, current_schema() AS schema_name,
       current_setting('transaction_read_only') AS read_only,
       current_setting('transaction_isolation') AS isolation,
       current_setting('TimeZone') AS time_zone;

SELECT table_schema, table_name
FROM information_schema.tables
WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
ORDER BY table_name
LIMIT 100;

SELECT table_name, ordinal_position, column_name, data_type, is_nullable,
       character_maximum_length, numeric_precision, numeric_scale
FROM information_schema.columns
WHERE table_schema = 'public'
  AND table_name IN ('city_minute_statistics', 'city_category_minute_statistics',
                     'city_category_hour_statistics', 'city_route_hour_statistics',
                     '__EFMigrationsHistory')
ORDER BY table_name, ordinal_position;
'@
```

Inspect observed migrations only after confirming the table exists:

```sql
SELECT "MigrationId", "ProductVersion"
FROM public."__EFMigrationsHistory"
ORDER BY "MigrationId";
```

Checked-in creation migrations are `20260920201730_CreateCityMinuteStatistics`, `20261001204629_CreateCityCategoryStatistics`, and `20261010151240_CreateCityRouteHourStatistics`. A model file or migration class does not prove it has been applied. If a live table/column differs from [tables.md](tables.md), establish the active migration and producer contract before interpreting it. Do not run `dotnet ef database update` or the migration image for a query request.

## Inspect city dashboard history

Use explicit columns, half-open UTC filters, and ordered limited rows:

```powershell
$dbParameters = @{
    city_slug = 'atlanta'
    from_utc = '2026-10-03T12:00:00Z'
    to_utc = '2026-10-03T13:00:00Z'
    row_limit = 100
}
& ./skills/transitjazz-db/scripts/query.ps1 -Parameters $dbParameters -Sql @'
SELECT city_slug, stat_minute_utc, source_definition_version, collection_status,
       healthy, input_fetch_ok, input_timestamp_known, input_lag_seconds,
       cycle_rate_per_second, cycle_error_rate_per_second,
       cycle_duration_p95_seconds, vehicles_processed, tones_emitted
FROM public.city_minute_statistics
WHERE city_slug = :city_slug
  AND stat_minute_utc >= :from_utc::timestamptz
  AND stat_minute_utc < :to_utc::timestamptz
ORDER BY stat_minute_utc
LIMIT :row_limit;
'@
```

The example's dates are illustrative. Source fields are nullable and counts are latest gauge samples. Group statuses to report source quality; do not compute minute totals from those samples.

To count missing city minutes, generate the grid and left-join rather than counting stored NoData rows alone:

```sql
WITH requested AS (
    SELECT generate_series(
        date_trunc('minute', :from_utc::timestamptz, 'UTC'),
        date_trunc('minute', :to_utc::timestamptz - interval '1 microsecond', 'UTC'),
        interval '1 minute') AS minute_start
), evidence AS (
    SELECT r.minute_start, s.collection_status,
           r.minute_start >= :from_utc::timestamptz
           AND r.minute_start + interval '1 minute' <= :to_utc::timestamptz AS fully_contained
    FROM requested r
    LEFT JOIN public.city_minute_statistics s
      ON s.city_slug = :city_slug AND s.stat_minute_utc = r.minute_start
)
SELECT fully_contained, coalesce(collection_status, 'Missing') AS coverage,
       count(*) AS minute_count
FROM evidence
GROUP BY fully_contained, coalesce(collection_status, 'Missing')
ORDER BY fully_contained DESC, coverage;
```

Partially intersected windows are context; they are counted separately from fully contained requested minutes. Historical cycle/work age uses the represented minute close, not the current wall-clock time:

```sql
extract(epoch FROM (stat_minute_utc + interval '1 minute')) - last_cycled_unix_seconds
```

Preserve null age when the source timestamp is missing. An unexpected negative age is diagnostic, not grounds to silently change the source value.

## Discover category history

Use a finite range for retained category/version/policy discovery:

```sql
SELECT city_slug, category, definition_version, healthy_cadence_limit_seconds,
       collection_status, has_conflict, count(*) AS retained_minutes,
       min(stat_minute_utc) AS first_minute_in_range,
       max(stat_minute_utc) AS last_minute_in_range
FROM public.city_category_minute_statistics
WHERE city_slug = :city_slug
  AND stat_minute_utc >= :from_utc::timestamptz
  AND stat_minute_utc < :to_utc::timestamptz
GROUP BY city_slug, category, definition_version, healthy_cadence_limit_seconds,
         collection_status, has_conflict
ORDER BY category, definition_version, healthy_cadence_limit_seconds, collection_status, has_conflict
LIMIT :row_limit;
```

For a selected city/category's beginning of retained capture, the recipes intentionally use an all-history `min(stat_minute_utc)` constrained to that pair. Do not rename the first key in a requested slice as the beginning of capture.

## Use the existing category report recipes

These SQL files live under `specs/056-city-transit-type-insights/contracts/`. Read and run them from the repository instead of maintaining copies in the skill. Each file verifies durable complete-hour evidence; `minute-coverage.sql` also exposes incomplete and missing windows.

| File | Question it answers |
| --- | --- |
| `hourly-insights.sql` | What are each verified hour's movement, activity, and crossing measures? |
| `activity-insights.sql` | What is the sample-weighted average observed activity over the period? |
| `cadence-insights.sql` | How many published opportunities per complete observed hour? |
| `period-insights.sql` | What are all period measures, with separately summed denominators? |
| `local-hour-cadence.sql` | Which complete observations contribute to each local-hour bucket and its typical cadence? |
| `minute-coverage.sql` | Which requested minutes/hours are missing, incomplete, conflicting, incompatible, or cut by a boundary? |

The hourly file requires an explicit definition. Other files accept SQL null for `definition_version` to inspect all retained versions while returning separate groups. Unknown versions need their own semantics before labeling values; v1 labels cannot be silently reused.

Execute the chosen measure and coverage files in **one helper invocation** to share a Repeatable Read snapshot. Example for the latest closed UTC hour:

```powershell
$dbNowUtc = [datetime]::UtcNow
$dbToUtc = $dbNowUtc.Date.AddHours($dbNowUtc.Hour)
$dbParameters = @{
    city_slug = 'atlanta'
    category = 'bus'
    definition_version = 'observed-city-category-statistics-v1'
    from_utc = $dbToUtc.AddHours(-1).ToString('o')
    to_utc = $dbToUtc.ToString('o')
}
$dbContracts = './specs/056-city-transit-type-insights/contracts'
& ./skills/transitjazz-db/scripts/query.ps1 -Parameters $dbParameters -SqlFile @(
    "$dbContracts/period-insights.sql"
    "$dbContracts/minute-coverage.sql"
)
```

For version discovery set `$dbParameters.definition_version = $null`; the helper writes actual SQL NULL, not the string `'NULL'`. For local-hour cadence additionally set `time_zone_id` to the selected city's configured IANA zone. Server defaults use `America/New_York` for Atlanta, Washington DC, Boston, New York City, and Philadelphia; `America/Toronto` for Toronto; `America/Denver` for Denver. Validate the named zone against current configuration and `pg_timezone_names`.

### Parameter handling

The repository SQL uses client-style bare placeholders such as `:city_slug` and `:from_utc::timestamptz`. Passing those files unchanged to psql with `-v city_slug=atlanta` expands bare SQL text and does not safely quote it. The helper rewrites placeholder tokens to psql's quoted-literal syntax `:'city_slug'`, preserving `::` casts and skipping ordinary SQL strings/comments. It imports nonempty values through temporary environment variables and trusted internal `\getenv` commands, avoiding Windows PowerShell's command-line handling of embedded quotes. Null parameters become SQL NULL and empty strings become `''`. Never pass credentials as SQL parameters.

This adapter is for reviewed SELECT statements and the checked-in recipes. It is not a full PostgreSQL parser or authorization system. For application code use real Npgsql parameter binding; use fixed, reviewed identifiers rather than substituting table/column names. Do not interpolate caller values directly into SQL or execute psql backslash commands from a supplied file.

### Coverage and authority

`minute-coverage.sql` returns `row_kind=minute` and `row_kind=hour`, exact `window_start_utc`/`window_end_utc`, requested intersections, stored statuses, conflict/missing flags, and exclusion reasons. Summarize whole requested evidence; display a limited selection after calculating counts. Use `is_definitive_complete_hour` for hour authority. `covered_minutes` for an hour is compatible durable evidence, not merely the captured hour's stored count.

The reason codes are `Missing`, `NoData`, `Partial`, `Conflict`, `DefinitionMismatch`, `CadenceMismatch`, `BackingMinutesUnavailable`, `IncompleteBackingMinutes`, `DistinctPopulationUnavailable`, `AdditiveMismatch`, and `PartialBoundary`. The contract documents their ordering and meaning. Do not multiply sampled rates to invent missing history.

The period/activity/cadence files produce an unavailable group when no hours contribute: zero `complete_hour_count`, null contributing bounds, and null means. Zero sums describe an empty contributing set, not observed zero service. Coverage stays visible alongside the means.

For local-hour reports keep `hour_start_utc`, local date/hour, and `utc_offset_seconds`. The local-hour SQL repeats bucket totals on each contributing hour row; deduplicate summaries by city/category/version/cadence policy/local hour before presenting or summing bucket totals. Both UTC occurrences of a repeated autumn hour contribute; a skipped spring hour is absent rather than zero.

## Route-hour discovery and report recipe

Read [route-hours.md](route-hours.md) before interpreting route measures. Discover historical keys from hours intersecting the finite request, preserving case:

```sql
SELECT route_join_key, category, definition_version, healthy_cadence_limit_seconds,
       count(*) AS retained_hours_in_range,
       min(hour_start_utc) AS first_hour_in_range,
       max(hour_start_utc) AS last_hour_in_range
FROM public.city_route_hour_statistics
WHERE city_slug = :city_slug
  AND hour_start_utc < :to_utc::timestamptz
  AND hour_start_utc + interval '1 hour' > :from_utc::timestamptz
GROUP BY route_join_key, category, definition_version, healthy_cadence_limit_seconds
ORDER BY route_join_key COLLATE "C", category, definition_version, healthy_cadence_limit_seconds
LIMIT :row_limit;
```

This is a discovery preview, not coverage or proof of completeness. Do not use its display limit to choose an all-route population. Earliest retained route history requires a separate all-history minimum constrained to city/key, not the minimum in this slice.

The maintained `specs/057-hourly-route-history/contracts/route-hour-insights.sql` returns one JSON object with `request`, `city_hour_coverage`, `hourly`, `route_coverage`, `periods`, and `typical_local_hours`, sharing one statement snapshot. It exposes expected hour grids, whole-cohort quarantine, boundary context, weighted denominators, version/policy partitions, and local offsets. It does not need category backing-minute recipes.

| Positional parameter | Type / value |
| --- | --- |
| `$1` | Canonical city, `text` |
| `$2` | Nonempty distinct ordinal route keys, `text[]`, or SQL NULL for all-route discovery; empty array is invalid |
| `$3`, `$4` | Finite inclusive/exclusive UTC bounds, `timestamptz` |
| `$5` | Supported definition, `observed-city-route-hour-statistics-v1` |
| `$6` | Validated configured city IANA zone, `text` |

The helper handles named `:parameters`, not positional `$1` bindings. Do not pass this recipe unchanged to `-SqlFile` or treat a PowerShell array as a safely serialized PostgreSQL array. Use real positional client binding, or adapt only the six fixed placeholder tokens in the reviewed SQL to named placeholders and pass values through `-Parameters`. For a named route-array adapter, pass a JSON-serialized array as `route_keys_json` and use `CASE WHEN :route_keys_json::jsonb IS NULL THEN NULL::text[] ELSE ARRAY(SELECT jsonb_array_elements_text(:route_keys_json::jsonb)) END`; actual SQL NULL means all-route discovery. Validate nonempty/distinct string keys before execution. Never interpolate caller values into the recipe.

**Checked-in recipe caveat (2026-10-10):** the contract specifies all-route discovery from retained keys overlapping the range, but the recipe's `route_keys` CTE currently scans all history for the city. Before using NULL selection for that contract, adapt its discovery branch to select from `retained` (the already range-filtered CTE), or bind an explicit untruncated set of keys discovered in-range in the same report snapshot. If no keys are discovered, still return the city-hour Missing grid; do not substitute an invalid empty explicit array. Do not silently describe the current all-history selection as in-range discovery.

Feature 057's plan records deterministic checks as implemented, while real disposable PostgreSQL, migration-image/load, production observation, and analyst validation remain pending. The contract/SQL still carry planning labels. Inspect the selected schema and exercise the recipe read-only before claiming operational validation.

Display at most the requested preview rows after calculating full coverage and period results. Keep every selected route's missing pairs explicit. A zero-contributor period's zero sums describe an empty contributing set, not observed inactivity. For local-hour results, use `hourly` for individual dates/offsets/UTC keys and `typical_local_hours` for bucket totals; no averaging hourly ratios or counting local labels as elapsed hours.

## JSON and CSV

`-Format csv` emits native psql CSV with a SQL-null marker of `NULL`. Multi-file reports emit separate result sets. See [results.md](results.md) before exporting them.

For lossless JSON use a server-side wrapper around a bounded, selected query:

```sql
WITH selected AS (
    SELECT city_slug,
           to_char(stat_minute_utc AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS stat_minute_utc,
           collection_status, healthy, input_lag_seconds
    FROM public.city_minute_statistics
    WHERE city_slug = :city_slug
      AND stat_minute_utc >= :from_utc::timestamptz
      AND stat_minute_utc < :to_utc::timestamptz
    ORDER BY stat_minute_utc
    LIMIT :row_limit
)
SELECT coalesce(jsonb_agg(to_jsonb(selected) ORDER BY stat_minute_utc), '[]'::jsonb) AS rows
FROM selected;
```

Extract the returned JSON value; do not turn CSV text cells into guessed numeric or boolean JSON types. If an export requests all rows, remove the preview limit and retain the requested time/selector bounds.
