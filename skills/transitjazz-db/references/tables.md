# Database tables and meaning

This guide reflects the checked-in model and producers reviewed on 2026-10-04. It documents what rows mean, not the selected database's current row counts or rollout status. Check live schema/history before using a table. Resolve paths from the repository root.

## Source map

Use these directory aliases for the file references below:

- **Data**: `src/Server/ChefKnifeStudios.TransitJazz.Server.Data`
- **API**: `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI`
- **Worker**: `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker`
- **Contracts**: `specs/056-city-transit-type-insights/contracts`

`Data/AppDbContext.cs` exposes exactly three entity sets. `Data/Configurations/*.cs`, the two creation migrations, and `Data/Migrations/AppDbContextModelSnapshot.cs` define the physical model. Stores and Contracts address these tables in `public`.

| Table | One row represents | Primary key | Appropriate use |
| --- | --- | --- | --- |
| `public.city_minute_statistics` | One city's dashboard observation for a closed UTC minute | `city_slug, stat_minute_utc` | Historical worker health, input status, latest processing samples, rates, latency, and cache sizes |
| `public.city_category_minute_statistics` | One city/category's accumulated cycle observations in a UTC minute | `city_slug, category, stat_minute_utc` | Category capture coverage, activity/movement/publication diagnostics, and hourly backing evidence |
| `public.city_category_hour_statistics` | One city/category's finalized UTC hour, with exact hour-scoped distinct activity population | `city_slug, category, hour_start_utc` | Verified hourly movement, average activity, published cadence, weighted period and local-hour reports |
| `public."__EFMigrationsHistory"` | An applied EF migration | `"MigrationId"` in the standard EF history table | Establish which checked-in migrations were applied; introspect actual columns/location first |

The three statistics entities do not inherit `BaseEntity`. There are no `id`, `is_deleted`, `created_on_utc`, or `modified_on_utc` columns in their mappings. The creation migrations declare composite primary keys and no foreign keys or additional indexes. Definition versions are metadata, not part of those primary keys.

There are no route, vehicle, trip, listener, or user entity tables in this context. `API/Repositories/InMemoryKeyValueRepository.cs` and `API/GtfsStatic/GtfsStaticLoader.cs` supply the runtime GTFS catalog. A deployed database may contain unrelated or legacy tables; inspect them rather than assigning TransitJazz meaning without a producer/mapping.

All time windows use UTC, start inclusive and end exclusive. `timestamptz` is PostgreSQL `timestamp with time zone`; normalize display to UTC. Configured city slugs are `atlanta`, `washington-dc`, `boston`, `new-york-city`, `toronto`, `philadelphia`, and `denver` in the committed server settings. Re-read `API/appsettings.json` and the selected runtime configuration when resolving current membership.

## city_minute_statistics

**Sources**: `Data/Models/CityMinuteStatistic.cs`, `Data/Configurations/CityMinuteStatisticConfiguration.cs`, `Data/Migrations/20260920201730_CreateCityMinuteStatistics.cs`, `Data/Statistics/CityMinuteStatisticsStore.cs`, and `API/Statistics/{WorkerDashboardStatisticsCatalog,GrafanaPrometheusStatisticsSource,HistoricalStatisticsCollector,HistoricalStatisticsOptions}.cs`. The frozen source contract is `specs/055-database/contracts/worker-dashboard-statistics-v1.md`.

**Producer**: `HistoricalStatisticsCollector` queries the Grafana/Prometheus source once per minute, with configurable ingestion grace and overlap. Defaults are two minutes of grace and five minutes of overlap. Source gauges use `last_over_time(...[1m])`; cycle counters use `rate(...[1m])`; latency uses histogram quantile 0.95 over one-minute bucket rates. The source evaluates a represented minute at its close and maps that result back to the minute start. It is not a raw event log or a sum of every cycle in that minute.

**Writes**: a new city/minute creates a row; compatible retries leave confirmed values unchanged and can fill missing source fields. Incompatible values or versions mark `Discrepant` without overwriting those source values. Historical backfill queries this metrics source when separately configured; it does not generate category history. `HistoricalStatistics.Enabled=false` and `DryRun=true` in committed defaults do not prove current deployment settings.

The first four columns below are required. All 23 source fields are nullable. Decimal fields are `numeric(20,6)`, counts/timestamps are `bigint`, and flags are `boolean`.

| Column | Type / nullable | Usage and contents |
| --- | --- | --- |
| `city_slug` | `varchar(64)`, required | Canonical city label; first primary-key component |
| `stat_minute_utc` | `timestamptz`, required | Represented closed minute start; second primary-key component |
| `source_definition_version` | `varchar(64)`, required | Current code constant `worker-dashboard-statistics-v1` |
| `collection_status` | `varchar(16)`, required | Stored text: `Complete`, `Partial`, `NoData`, or `Discrepant` |
| `last_cycled_unix_seconds` | `bigint`, nullable | Latest completed city cycle, seconds since Unix epoch; historical age is minute close minus this timestamp |
| `last_worked_unix_seconds` | `bigint`, nullable | Latest cycle doing useful vehicle/tone work, Unix seconds; calculate historical age at minute close |
| `cycle_rate_per_second` | `numeric(20,6)`, nullable | Estimated completed-cycle rate over the source's one-minute window, cycles/s |
| `cycle_error_rate_per_second` | `numeric(20,6)`, nullable | Estimated cycle-error rate, errors/s; source fetch failure has its own flag |
| `cycle_duration_p95_seconds` | `numeric(20,6)`, nullable | Estimated 95th percentile city-cycle duration, seconds; requires source histogram evidence |
| `healthy` | `boolean`, nullable | Source health ratio accepted only when exactly 0 or 1 |
| `input_fetch_ok` | `boolean`, nullable | Latest source fetch-success ratio, 0/1 converted to boolean |
| `input_records_valid` | `bigint`, nullable | Latest valid input record count; source sample rather than minute total |
| `has_input_records` | `boolean`, nullable | Latest input-nonempty ratio |
| `input_lag_seconds` | `numeric(20,6)`, nullable | Latest measured source lag, seconds; a zero with `input_timestamp_known=false` means unknown freshness |
| `input_timestamp_known` | `boolean`, nullable | Whether input freshness has a known timestamp |
| `input_source_failures` | `bigint`, nullable | Latest count of source failures reported by the city fetch result |
| `vehicles_processed` | `bigint`, nullable | Latest processed vehicle sample; summing minute rows does not establish processed vehicles over the period |
| `tones_emitted` | `bigint`, nullable | Latest worker tone-emission sample; neither a minute total nor evidence of browser playback |
| `batch_wire_bytes` | `bigint`, nullable | Latest serialized batch size in bytes; source uses zero for no batch |
| `crossings_suppressed_first_seen` | `bigint`, nullable | Latest count of first-observation crossing suppressions |
| `crossings_suppressed_delta_leq_zero` | `bigint`, nullable | Latest count of suppressions for nonpositive movement delta |
| `crossings_suppressed_teleport` | `bigint`, nullable | Latest count of implausible-jump suppressions |
| `crossings_suppressed_transfer` | `bigint`, nullable | Latest count of route-transfer suppressions |
| `vehicle_state_cache` | `bigint`, nullable | Latest vehicle state cache size, entries |
| `crossing_baseline_cache` | `bigint`, nullable | Latest crossing baseline cache size, entries |
| `route_index` | `bigint`, nullable | Latest route index size, entries |
| `route_trigger_point_cache` | `bigint`, nullable | Latest trigger-point cache size, entries |

`Complete` means all 23 fields are present with acceptable source evidence. `Partial` contains some values or source quality warnings. `NoData` contains no source values. `Discrepant` preserves conflicting/version-mismatched evidence. NoData's nullable fields stay null; a missing city/minute key is a separate missing observation. Database creation for this table does not add the C# validation rules as SQL CHECK constraints.

For an exact metric name, expression, or unit, use `WorkerDashboardStatisticsCatalog.Fields`; do not reconstruct names from instrument names or apply a new rate to an already sampled gauge. Do not average minute p95 values and label the result a period p95.

## Category capture shared meaning

**Sources**: `Worker/Worker.cs`, `Worker/Statistics/{CityCategoryStatisticsCapture,CategoryStatisticsAccumulator,FinalizedCategoryStatisticsBatch,CityCategoryInsightsOptions}.cs`, `API/Statistics/{CategoryStatisticsWriter,CategoryStatisticsCaptureServiceCollectionExtensions}.cs`, `Data/Statistics/CityCategoryStatisticsStore.cs`, and `Contracts/observed-category-statistics-v1.md`.

The worker records eligible activity once per vehicle/city/cycle. The first eligible joined, positioned entity in feed order chooses its representative category and movement observation; duplicate activity entities do not inflate that cycle's distinct count. Actual published crossing records are counted independently. Vehicle identities are retained transiently for deduplication and hourly population sets and are not persisted in these tables.

Category labels come from the city's route category map. `API/GtfsStatic/GtfsStaticLoader.cs` classifies configured GTFS route types; without a city mapping the fallback is `rail` for types 0/1/2 and `bus` otherwise. `Worker.ResolveCategory` falls back to `unknown` when the route category lookup misses. Capture trims/lowercases labels and maps blank labels to `unknown`. Discover retained labels in the database; do not invent a fixed bus/rail-only enumeration.

Movement is the absolute change in snapped cumulative along-route meters for increasing per-vehicle source timestamps on unchanged route/geometry. Stationary zero-meter intervals are accepted. First sightings, missing/repeated/older timestamps, route transfers, invalid/changed geometry, and jumps above 2,000 meters are rejected. Accepted meters are rounded to six decimals. The completing city cycle owns a cross-boundary interval once. Rejection details are counted together in `distance_rejected_count`; per-reason history cannot be recovered from that column.

Publication counts are credited only after `PublishBatchAsync` returns true, and represent all actual crossing records in the successful city batch. A healthy cycle with no publication needed can provide a known zero. Publication failure can leave eligible activity diagnostics while cadence is unavailable. Empty healthy feeds with a ready route index can supply valid zeros for configured categories. Unknown activates only on actual eligible evidence within an hour; preceding unknown minutes are not fabricated and incomplete activation windows are Partial.

Capture is independent of `HistoricalStatistics`. `CityCategoryInsights.Enabled=false`, with no enabled cities, in committed defaults. Category aggregates are future capture only, with no automatic retention deletion or category backfill in v1. Restart, timing gaps, failed processing, route-catalog changes, queue loss, and incomplete lifecycle windows can leave Partial, NoData, or absent rows. Row timestamps represent observation windows, not database insertion time.

### Shared category columns

Both category tables have these columns plus their respective window key. All aggregate counters are required nonnegative values. Decimal meters/gaps use `numeric(20,6)`.

| Column | Type / nullable | Usage and contents |
| --- | --- | --- |
| `city_slug` | `varchar(64)`, required | Lowercase canonical city; primary-key component |
| `category` | `varchar(64)`, required | Normalized route category label, including retained historical labels and eligible `unknown`; primary-key component |
| `definition_version` | `varchar(64)`, required | Current code constant `observed-city-category-statistics-v1`; incompatible versions must be reported separately |
| `collection_status` | `varchar(16)`, required | Immutable capture status stored as `Complete`, `Partial`, or `NoData` |
| `has_conflict` | `boolean`, required, default false | Monotone quarantine flag set when the same identity receives a different frozen payload |
| `healthy_cadence_limit_seconds` | `integer`, required | Coverage gap policy saved with the row, 1–60 seconds at the SQL layer; enabled capture validates it above the cycle interval (default cycle 10s, gap 30s) |
| `observed_cycle_count` | `bigint`, required | Represented completed category/city-cycle observations |
| `valid_active_sample_count` | `bigint`, required | Eligible activity samples; the activity mean's denominator |
| `valid_publish_cycle_count` | `bigint`, required | Cycles with known valid publication/no-publication outcomes; distinguishes observed zero from unavailable cadence |
| `failed_cycle_count` | `bigint`, required | Failed/invalid completed cycle observations tracked by the accumulator |
| `first_cycle_utc` | `timestamptz`, nullable | First observed completion inside the window; null when no cycles observed |
| `last_cycle_utc` | `timestamptz`, nullable | Last observed completion inside the window; same availability rule as first cycle |
| `max_observation_gap_seconds` | `numeric(20,6)`, nullable | Largest recorded coverage gap, including boundary timing evidence; null means missing gap evidence |
| `active_vehicle_count_sum` | `bigint`, required | Sum of eligible per-cycle distinct vehicle counts; repeatedly observed vehicles contribute once in each eligible cycle |
| `distance_meters_sum` | `numeric(20,6)`, required | Accepted absolute along-route meters, including zero intervals |
| `distance_interval_count` | `bigint`, required | Accepted fresh movement intervals; diagnostic movement denominator |
| `distance_rejected_count` | `bigint`, required | Rejected movement observations, including baseline establishment and freshness/geometry/transfer/jump exclusions |
| `crossings_published_count` | `bigint`, required | Crossing records from successfully published batches; published tone opportunities |

SQL checks enforce aligned UTC keys, supported statuses, nonnegative counters/gaps, sample counts no greater than observed cycles, and status-specific requirements. A Complete row requires observed cycles, activity/publication samples for every observed cycle, no failed cycles, and known healthy timing. NoData has no eligible measures, but may retain observation/failure/rejection diagnostics. Zero-filled required columns in a NoData row do not establish observed zero activity or cadence.

### city_category_minute_statistics

**Model/mapping**: `Data/Models/CityCategoryMinuteStatistic.cs` and `Data/Configurations/CityCategoryMinuteStatisticConfiguration.cs`.

| Additional column | Type / nullable | Usage |
| --- | --- | --- |
| `stat_minute_utc` | `timestamptz`, required | Exact UTC minute start; third primary-key component |

Contains additive diagnostics for one minute, not the distinct population across an hour. Partial rows can supply clearly labeled eligible minute diagnostics. NoData and conflicts cannot supply definitive measures. The request's subminute boundaries expose the full aggregate only as context, with actual window bounds; never prorate a minute.

Use `Contracts/minute-coverage.sql` to generate the expected time grid and distinguish missing identities, stored status, conflicts, unsupported/mismatched definitions, and partial boundaries.

### city_category_hour_statistics

**Model/mapping**: `Data/Models/CityCategoryHourStatistic.cs` and `Data/Configurations/CityCategoryHourStatisticConfiguration.cs`. Both category tables are created by `Data/Migrations/20261001204629_CreateCityCategoryStatistics.cs`.

| Additional column | Type / nullable | Usage |
| --- | --- | --- |
| `hour_start_utc` | `timestamptz`, required | Exact UTC hour start; third primary-key component |
| `distinct_active_vehicle_count` | `bigint`, nullable | Exact union of eligible active identities for this city/category/hour; primary movement denominator. Null when population integrity is unavailable. Sum across complete hours gives vehicle-hours, not unique vehicles over the whole period. |
| `covered_minutes` | `integer`, required | Capture's Complete-minute count, 0–60. Recompute durable compatible coverage when reporting; this stored count alone does not verify the hour. |

Category store retries compare frozen payloads. Identical retries preserve them. Different payloads set `has_conflict=true` without replacing counters; minute conflicts also quarantine an existing parent hour. Capture status can still read `Complete` after quarantine. Partial/NoData payloads are diagnostic and are not later upgraded by filling fields as in the dashboard table.

A Complete hourly candidate is stored only when its 60 durable Complete/nonconflicting minutes agree on definition, cadence policy, and every additive field. The store can omit an otherwise Complete candidate when that backing evidence is unavailable. Subsequent queries must recheck backing rows because later conflicts can invalidate previously stored evidence.

Authoritative-hour requirements:

1. Whole hour is contained in the requested UTC range.
2. Hour status is Complete, no conflict, 60 covered minutes, and nonnull exact distinct population.
3. Exactly 60 minute identities in that hour, all Complete/nonconflicting with the same definition and cadence limit.
4. Minute sums equal the hour's observed-cycle, valid-activity, valid-publication, failed-cycle, active-count, distance-meter, accepted-interval, rejected-observation, and published-crossing totals.

The checked-in SQL implements this selection in one statement. Combined coverage/measure reports use one read-only Repeatable Read transaction.

| Reported metric | Numerator / denominator | Unit and interpretation |
| --- | --- | --- |
| Primary movement | `distance_meters_sum / distinct_active_vehicle_count` for a verified hour | Meters per observed vehicle-hour; along-route observation, not completed-trip distance |
| Diagnostic movement | `distance_meters_sum / distance_interval_count` | Meters per accepted vehicle update; accepted stationary intervals remain in the denominator |
| Average activity | `active_vehicle_count_sum / valid_active_sample_count` | Average observed vehicles per eligible cycle; does not establish in-service status |
| Hourly cadence | `crossings_published_count / complete_hour_count` | Published opportunities per complete observed hour; count is one for an individual verified hour |

Use numeric division and `NULLIF(denominator, 0)`. Period reports sum the relevant numerators and denominators within definition/policy groups. Local-hour cadence divides the bucket's summed crossings by contributing complete UTC hours, preserving both UTC instances of a repeated local hour.

`first_collectible_utc` in the recipes is the earliest retained minute key for the city/category across definitions, even outside the requested range. Label it beginning of retained category capture. It is not proof of uninterrupted capture or recovered history.
