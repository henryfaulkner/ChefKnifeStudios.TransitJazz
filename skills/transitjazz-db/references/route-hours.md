# Hourly route history (feature 057)

Read this for `public.city_route_hour_statistics` queries. Resolve source paths from the repository root. Reviewed against the checked-in implementation on 2026-10-10; this does not establish deployment, retained rows, or production acceptance.

## Sources and capture

- Intent: `specs/057-hourly-route-history/{spec,plan,data-model}.md` and `contracts/observed-route-hour-statistics-v1.md`.
- SQL: `specs/057-hourly-route-history/contracts/route-hour-insights.sql`.
- Data: `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/{AppDbContext.cs,Models/CityRouteHourStatistic.cs,Configurations/CityRouteHourStatisticConfiguration.cs,Statistics/CityRouteHourStatisticsStore.cs,Migrations/20261010151240_CreateCityRouteHourStatistics.cs}`.
- Worker: `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Statistics/{RouteHourCatalog,RouteHourStatisticsCapture,RouteHourStatisticsAccumulator,FinalizedRouteHourStatisticsBatch,RouteHourHistoryOptions,RouteHourCaptureLifecycleService}.cs` and `Worker.cs`.
- Host: `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Statistics/{HistoricalStatisticsOptions,RouteHourStatisticsWriter,RouteHourCaptureServiceCollectionExtensions}.cs` and `appsettings.json`.

Existing `HistoricalStatistics.Enabled`, `DryRun`, and host-resolved `Cities` govern city history and route capture together. Empty city selection falls back to configured cities. `HistoricalStatistics:RouteHours` contains limits/cadence only; there is no independent route enablement or city selector. `CityCategoryInsights` remains independent. Committed history defaults are disabled and dry-run true. Dry run captures/validates/reports without route writes. Enabled history retains the host's existing database/source configuration requirements, including dry run.

The worker observes its existing pass and accumulates UTC hours in bounded memory. Completion time owns the cycle's observations; a boundary successor proves continuity but contributes its counts to the new hour. An asynchronous writer retains finalized cohorts. Enqueue success proves memory admission only. Restart, startup/shutdown fragments, gaps, catalog changes, capture/resource loss, and retention loss can leave incomplete or missing history. There is no route-minute/per-cycle ledger, route backfill from category/dashboard statistics, or automatic deletion job. Apply schema before capture through the existing operator process; query requests do not authorize rollout.

## Identity and columns

One row is one city/canonical route/UTC hour. Primary key: `(city_slug, route_join_key, hour_start_utc)`; secondary index: `(city_slug, hour_start_utc, route_join_key)`. Route keys are the live catalog's resolved `RouteShapeProperties.JoinKey`, preserving ordinal spelling/case (`COLLATE "C"`), nonblank and at most 512 UTF-8 bytes. Aliases, collapsed directions/branches, and contributing static IDs do not add historical routes. Keys do not guarantee permanent service identity. There is no foreign key to today's catalog.

All fields are required except those explicitly marked nullable. Counts are nonnegative `bigint`; meters/gaps use `numeric(20,6)`; timestamps use `timestamptz`.

| Columns | Type / meaning |
| --- | --- |
| `city_slug`, `route_join_key`, `hour_start_utc` | `varchar(64)`, case-sensitive `text`, aligned UTC hour start; half-open hour window |
| `route_short_name`, `static_route_id` | Nullable `text`; first metadata retained in the hour. Static ID is populated only when unique; neither field is a key. Entity validation caps each at 512 characters. |
| `category` | Normalized `varchar(64)` label; discover retained values rather than assuming bus/rail only |
| `route_catalog_fingerprint` | `varchar(64)` lower-case SHA-256 hex of versioned canonical key/category, sorted contributing static IDs, and actual ordered route geometry; not a catalog refresh number |
| `catalog_changed` | Boolean; cohort catalog change preserves old/new route union and first metadata, marks the whole hour incomplete |
| `definition_version` | `varchar(64)`; current `observed-city-route-hour-statistics-v1` |
| `capture_run_id`, `persisted_at_utc` | Process-run UUID and database-generated `CURRENT_TIMESTAMP` of the retention transaction; persistence time is not observation time or precise commit time |
| `collection_status`, `has_conflict`, `incomplete_reasons` | `varchar(16)` Complete/Partial/NoData, monotone boolean quarantine, and `text[]` sorted unique reason codes |
| `healthy_cadence_limit_seconds` | Integer applied gap policy; default 30s for a 10s cycle, runtime must exceed cycle interval and be at most 60s |
| `observed_cycle_count`, `valid_active_sample_count`, `valid_publish_cycle_count`, `failed_cycle_count` | Cycle observations, eligible active samples, known publication/no-publication samples, and failed/invalid cycles |
| `first_cycle_utc`, `last_cycle_utc` | Nullable first/last completion within the represented hour; absent when no cycles observed |
| `max_observation_gap_seconds`, `start_boundary_ok`, `end_boundary_ok` | Nullable maximum gap including boundary evidence, and boolean continuity proofs |
| `active_vehicle_count_sum`, `peak_active_vehicle_count` | Sum and maximum of eligible per-cycle distinct active counts; repeated vehicles contribute once per eligible sample |
| `distinct_active_vehicle_count` | Nullable exact hourly route population; null when population integrity is lost, zero for an intact inactive route |
| `vehicle_observations_processed_count`, `stale_observations_count` | Processing updates including duplicates, and stale subset; not unique vehicles |
| `distance_meters_sum`, `distance_interval_count`, `distance_rejected_count` | Accepted absolute along-route meters, accepted fresh intervals including stationary zeros, and rejected representative observations |
| `crossings_detected_count`, `crossings_published_count` | Actual prepared crossing records and records credited only after publisher success |
| `crossings_suppressed_first_seen`, `crossings_suppressed_delta_leq_zero`, `crossings_suppressed_teleport`, `crossings_suppressed_transfer` | Counts of observations receiving each live suppression reason; not estimates of notes lost |

Activity uses the first eligible joined, positioned vehicle representative in feed order, once per city/cycle; processing and crossing counts remain independent. Movement needs increasing source timestamps and unchanged resolved route/geometry. Missing/repeated/older timestamps, first sightings, transfers, invalid/changed geometry, and jumps above 2,000m reject movement. Accepted deltas round once to six decimals, away from zero. Rejection alone does not invalidate activity coverage. Published opportunities do not establish notes heard by a listener. Vehicle/trip/listener identities and raw feeds are not persisted.

## Coverage and cohort retention

The store atomically retains the entire immutable city/hour route cohort. Exact retries are Unchanged. Any different cohort or payload (including run ID, definition, policy, metadata, or counters) quarantines every original city/hour row without merging, replacing counters, or inserting extra routes. Equality excludes generated persistence time and the monotone conflict flag. Query cohort conflict across **all retained routes**, before filtering selected keys; any row's conflict excludes every retained route in that city/hour, even if an individual row still says Complete.

Complete requires positive observed cycles, eligible activity and known publication for every cycle, no failed cycles, both proven boundaries, known maximum gap within policy, intact distinct population, stable catalog, and no incomplete reason. Continuity, not a nominal 360 samples, establishes coverage. There is no `covered_minutes` column or durable-minute reconciliation as in category hours.

Partial retains useful observations without complete evidence. NoData has no eligible activity/publication samples or accepted measures, but can retain processing/detection/rejection/suppression diagnostics. Missing is an absent route/hour key or whole lost cohort; it is never an observed zero. Healthy empty cycles supply eligible zeros for all canonical routes. A Complete inactive route has positive samples and zero activity/cadence, while zero-denominator movement means stay null.

Reason vocabulary: `startup_fragment`, `shutdown_fragment`, `source_failure`, `route_index_unavailable`, `processing_failure`, `publication_unavailable`, `boundary_unproven`, `gap_exceeded`, `catalog_changed`, `clock_regression`, `capture_failure`, `identity_limit_exceeded`. Lost population sets the distinct denominator null; an oversized unrepresentable cohort is dropped whole.

## Definitive measures and reporting

Only fully contained Complete, cohort-nonconflicting hours with a supported definition contribute. Keep definition/cadence partitions separate. Partial boundary hours supply whole-hour context without proration. Keep diagnostics for Partial/NoData separate from definitive measures.

| Measure over contributing hours | Calculation | Unit |
| --- | --- | --- |
| Mean active vehicles | Sum active count / sum eligible active samples | Observed vehicles/sample |
| Peak active vehicles | Maximum hourly peak | Observed vehicles/sample |
| Processed observations | Sum processed count | Updates, not unique vehicles |
| Stale fraction | Sum stale / sum processed | Ratio; multiply by 100 only when labeled percent |
| Accepted movement | Sum distance meters | Observed along-route meters, not trip distance |
| Movement per route-vehicle-hour | Sum meters / sum intact hourly route populations | Meters/observed route-vehicle-hour |
| Movement per accepted update | Sum meters / sum accepted intervals | Meters/update, including accepted stationary intervals |
| Published cadence | Sum published crossings / contributing Complete UTC hours | Published opportunities/hour |

Use numeric division and `NULLIF(denominator, 0)`; never average displayed means. Summed hourly populations are route-vehicle-hours, not unique vehicles in the period/city; transfers can contribute to multiple routes. Do not allocate city feed latency, wire bytes, memory, or processing duration to routes.

Report explicit/discovered keys, exact UTC bounds, requested/fully-contained/contributing hour counts, Missing/Partial/NoData/Conflict and unsupported-definition exclusions, version/policy, fingerprints/catalog changes, and actual numerators/denominators. Earliest retained route hour is not evidence of continuous capture. All-route discovery uses retained history; today's catalog cannot establish missing historical membership. Keep a city-hour grid even when no routes are discovered. Missing pairs do not prove a route existed or service was absent.

Local-hour analysis uses the city's configured IANA zone. Preserve UTC key, local date/hour, and offset. Repeated DST hours contribute separately; skipped hours are not synthesized. Typical cadence divides summed published records by contributing Complete UTC hours, not calendar days or distinct local labels. Read [queries.md](queries.md) for recipe parameters and execution caveats, and [results.md](results.md) for display/export conventions.
