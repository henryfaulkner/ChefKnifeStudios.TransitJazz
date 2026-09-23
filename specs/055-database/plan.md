# Implementation Plan: Historical Transit Statistics

**Branch**: `055-database` | **Date**: 2026-09-20 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/055-database/spec.md`

## Summary

Add one deliberately wide PostgreSQL table that stores a dashboard-parity observation for each configured city and closed UTC minute. The data project owns the entity, EF mapping, migration, and conflict-safe store; the co-hosted Web API owns a read-only Grafana/Prometheus query client and a configuration-gated collector. The collector first backfills only source history still available, then rereads a small closed-minute overlap every minute for ongoing history. It never changes or recreates worker metric semantics, and the schema migration never calls Grafana.

The historical source contract is fixed as `worker-dashboard-statistics-v1`: it contains the 23 city-scoped measures already rendered by the committed worker dashboard. It uses literal one-minute PromQL windows, nullable source values, and safe structured reports. Worker-wide measures are deliberately excluded from v1 so every stored row remains meaningful for a city-by-city aggregation.

## Technical Context

**Language/Version**: C# / .NET 10.0
**Primary Dependencies**: EF Core 10, Npgsql EF provider, existing ASP.NET Core hosted-service and `HttpClient` facilities, existing OpenTelemetry/Grafana Cloud metrics source; no new third-party runtime package
**Storage**: Newly provisioned TransitJazz PostgreSQL database, one `city_minute_statistics` table keyed by `(city_slug, stat_minute_utc)`
**Testing**: xUnit in the existing WebAPI and worker test projects; EF model metadata and migration-script validation; fake HTTP source-client tests; PostgreSQL integration test for key/upsert semantics; source-contract/dashboard binding tests; controlled live preflight and reconciliation evidence
**Target Platform**: .NET 10 Linux process co-hosted in an Azure Container App; existing Linux EF migration bundle in CI
**Project Type**: Server data model plus internal hosted collector; no public endpoint or client UI in v1
**Performance Goals**: A normal collection run completes without changing the ten-second worker cadence; city/day aggregation uses the primary city/time key; the initial seven-city, 14-day upper bound is about 141,120 rows
**Constraints**: One simple denormalized table; one city per UTC minute; no normalized source/import/coverage tables; metrics remain Grafana-authoritative; query the source only with a dedicated `metrics:read` credential; do not log secrets, URLs, raw response bodies, entity IDs, or payloads
**Scale/Scope**: Seven configured cities; approximately ten-second source export cadence; fixed one-minute historical observations; 23 city dashboard fields; source retention is expected to be 14 days but must be proven before the backfill

## Constitution Check

### Initial Gate — Pass with release prerequisites

| Constitution area | Result | Plan response |
|---|---|---|
| I. Decoupled cloud architecture | Pass | The collector runs inside the existing WebAPI/worker Container App and introduces no public endpoint, ingress, or separately deployed unit. |
| II. No frontend secrets | Pass | Database and Grafana reader credentials stay in Key Vault-backed server configuration; no client project changes. |
| III. Two-pass real-time processing | Pass | No worker pass, GTFS entity, route mapping, or SignalR payload changes. The collector reads the independent metrics source after export. |
| IV. OpenTelemetry observability | Pass | Existing metrics, labels, exporter, dashboard, and alerts remain authoritative and unchanged. The collector emits only safe operational summaries. |
| V. GitHub Actions CI/CD | Pass | The existing Data migration bundle remains schema-only and continues before deployment. Runtime secret delivery and validation are explicit deployment tasks. |
| VI. GTFS ID mapping | Pass | The model stores only canonical city slugs; it never stores route, trip, vehicle, or feed identifiers. |
| VII–XIII. Map, music, interaction, presentation | Not applicable | No frontend, mapping, audio, or user-interaction surface is added. |
| Governance | Conditional release gate | A dedicated Grafana `metrics:read` credential, actual retention/query proof, bounded backfill dry-run, source reconciliation, runtime database secret, and safe report must pass before enabling writes. |

**Initial decision**: Planning and local schema/client work may proceed. Production historical writes are blocked until every governance prerequisite is evidenced.

### Post-design Gate — Pass with the same release prerequisites

The design preserves the one-table city-first decision, does not manufacture missing source history, and has no client secret or new public network surface. Direct range queries are necessary to make the ongoing records and time-sensitive backfill use identical dashboard semantics. The fixed contract, read-only credential, bounded chunks, nullable values, and conflict-safe persistence bound that integration.

## Project Structure

### Documentation (this feature)

```text
specs/055-database/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── worker-dashboard-statistics-v1.md
└── tasks.md                         # generated later by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── ChefKnifeStudios.TransitJazz.sln
└── Server/
    ├── ChefKnifeStudios.TransitJazz.Server.Data/
    │   ├── AppDbContext.cs                       # DbSet and mapping assembly discovery
    │   ├── AppDbContextFactory.cs                # TransitJazzDB design-time configuration
    │   ├── ServiceCollectionExtensions.cs        # DbContext/factory registration
    │   ├── Models/CityMinuteStatistic.cs         # only persistent statistics entity
    │   ├── Configurations/CityMinuteStatisticConfiguration.cs
    │   ├── Statistics/CityMinuteStatisticsStore.cs # conflict-safe batch persistence
    │   └── Migrations/                           # schema-only EF migration
    ├── ChefKnifeStudios.TransitJazz.Server.WebAPI/
    │   ├── Program.cs                            # Data, HttpClient, options, hosted collector DI
    │   ├── appsettings.json                      # disabled-by-default non-secret settings
    │   └── Statistics/
    │       ├── HistoricalStatisticsOptions.cs
    │       ├── WorkerDashboardStatisticsCatalog.cs
    │       ├── GrafanaPrometheusStatisticsSource.cs
    │       ├── HistoricalStatisticsCollector.cs
    │       └── StatisticsCollectionReport.cs
    ├── ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/
    │   └── Statistics/                           # model, catalogue, source, store, collector tests
    └── ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/
        └── Metrics/                              # dashboard/metric contract regression test

bicep/
├── main.bicep                                    # runtime database and metrics-reader parameters
├── main.json                                     # regenerated from Bicep; never hand-edited
└── modules/containerApp.bicep                    # Key Vault-backed secret/env wiring

.github/workflows/server.yml                      # existing migration bundle and server deployment flow
```

**Structure Decision**: `Server.Data` owns the one-table persistence boundary and EF migration. `Server.WebAPI` is the actual deployed metrics host, so it owns the read-only source query client and recurring hosted collector. No new public API, UI, worker instrument, or standalone service is needed. Existing test projects gain the feature tests rather than adding a test-project topology.

## Design Details

### Source contract and one-minute semantics

1. The committed `transitjazz-worker-overview` dashboard is the v1 catalogue authority. Its literal mapping is documented in [worker-dashboard-statistics-v1.md](contracts/worker-dashboard-statistics-v1.md).
2. The range-query client uses the Grafana Cloud Metrics Prometheus API, UTC, a 60-second step, and exact source expressions. It replaces `$__rate_interval` with a contract-owned `[1m]` trailing window for `rate` and histogram p95 expressions.
3. Gauges use `last_over_time(...[1m])`, avoiding an implicit multi-minute lookback from masquerading as a fresh value. Raw city heartbeat timestamps are stored; cycle/work ages are derived from the row minute when needed.
4. A row is a dashboard-parity minute sample. `tones_emitted`, `vehicles_processed`, suppressions, and cache sizes are not summed to claim exact daily totals unless a later contract explicitly introduces a total expression.
5. All direct source values are nullable. A true zero remains zero; missing data produces nullable fields and `Partial`/`NoData` collection status. The documented input-lag/known-timestamp pair retains the distinction between unknown and fresh.

### Data migration and collection strategy

1. Generate an EF migration containing only `city_minute_statistics` and its composite primary key. Do not seed statistics or reference Grafana from an EF migration.
2. Add a purpose-built store rather than routing time-series writes through the generic repository. It batches rows by city/range, inserts new rows with PostgreSQL conflict protection, compares existing compatible rows as unchanged, fills compatible incomplete rows, and records but never silently overwrites a mismatch.
3. Add disabled-by-default collector options: source endpoint, reader authorization, source contract version, `Enabled`, `DryRun`, `InitialBackfill`, `IngestionGraceMinutes = 2`, and `OverlapMinutes = 5`. Validate HTTPS, positive bounded intervals, and the fixed one-minute source granularity at startup without revealing configuration values.
4. When `InitialBackfill` is explicitly enabled, compute the actual returned historical range and query it in sequential six-hour UTC chunks. Build complete minute grids for the closed range and the configured city set, overlay source matrices, and persist one row per city/minute with source status.
5. After the backfill passes, recurring collection handles the newest safe closed minute and rereads the five prior minutes. There is no checkpoint table; it derives resumability from the composite key and reviews the recent overlap.
6. A dry run performs the same range queries and validation, emits the safe report, and writes no rows. An applied rerun must have zero duplicate keys and report unchanged/filled/discrepant outcomes.

### Security and deployment configuration

- Provision a separate Grafana Cloud access policy limited to `metrics:read`. The existing OTLP publisher and provisioning tokens remain separate and must not be reused.
- Add Key Vault-backed server references for the TransitJazz database connection and Grafana reader authorization; expose only configuration names to the WebAPI. Keep the reader endpoint as an HTTPS configuration value and never log it.
- Add the Data project reference and `RegisterDataServices` call to the actual WebAPI host. Standardize the design-time factory and runtime registration on `ConnectionStrings:TransitJazzDB`.
- Preserve the existing EF migration bundle in CI. The migration job creates schema before server deploy. Deploy with collection disabled until the live source preflight succeeds, then perform the dry run, applied backfill, reconciliation, and recurring enablement as explicit release gates.

### Test and acceptance strategy

- EF metadata tests assert the sole entity, composite key, no `BaseEntity` audit columns, explicit SQL types, nullable source fields, and no unplanned indexes/tables.
- Store tests cover new, unchanged, partial-to-complete, no-data, duplicate, and conflicting row behavior with PostgreSQL's actual unique-key semantics.
- Source-client tests use fixed range responses to cover literal query construction, minute alignment, all seven labels, raw heartbeat timestamps, 0/1 conversion, null/no-data, warnings, malformed data, counter resets, missing histogram buckets, and secret-free errors.
- Catalogue tests parse the committed dashboard and assert every persisted source metric, label, and field is represented exactly once. Existing worker metric tests verify the source metrics and labels remain unchanged.
- Collector tests cover disabled behavior, dry run, six-hour chunk boundaries, two-minute grace, five-minute overlap, all-city grids, idempotent rerun, cancellation, and summary classification.
- Before write enablement, run live 15-minute preflight and select three deterministic closed minutes per source field/city where available (normal, zero/empty, edge). Re-run the frozen source expression and compare database values: exact for counts/booleans/timestamps and documented tolerance for rate/p95 floats.

## Implementation Sequence

1. **Freeze the contract and release gates.** Confirm the 23 retained city fields against the committed dashboard, write the contract version, and document source meanings that must not be summed. Obtain the separate read-only access policy and verify actual source retention, query limits, city labels, and one-replica assumption without printing credentials.
2. **Implement the single table.** Add `CityMinuteStatistic` without `BaseEntity`, the composite EF configuration, `DbSet`, mapping tests, a schema-only migration, and a corrected `TransitJazzDB` design-time factory. Build the existing EF migration bundle and inspect its script to prove it contains no data query or seed.
3. **Create persistence semantics.** Implement the focused city-minute store using the composite key and bounded batches. Support exact duplicate classification, partial/no-data improvement, safe discrepancy handling, and a report object; do not create an import, source-definition, checkpoint, or coverage table.
4. **Implement the read-only source client.** Add validated options, a typed `HttpClient`, typed Prometheus range-response parsing, and the fixed catalogue. Preserve matrix timestamps, reject unexpected labels/cardinality, translate source warnings/missing buckets/no-data into collection statuses, and ensure every failure is secret-free.
5. **Wire the host safely.** Reference/register the Data project in `Program.cs`, configure the collector as disabled by default, and add a single hosted collection path that cannot affect worker polling, metric emission, labels, dashboard queries, or alerts. Add model/store/client/collector and dashboard-binding tests in the existing test projects.
6. **Provision runtime configuration.** Add Bicep parameters, Key Vault references, server environment bindings, and documented default settings for the database and Grafana reader. Regenerate `main.json`; build, validate, and what-if Bicep. Keep CI's existing migration-bundle ordering and update only needed pipeline validation.
7. **Dry-run the backfill.** Apply the schema migration, turn on collector dry-run with a bounded range, and capture the safe coverage/reconciliation report. Fail closed for absent reader access, expired history, warning/error response, unexpected city, missing source field, or a mismatch.
8. **Backfill and prove idempotency.** Enable writes only after dry-run approval. Backfill six-hour chunks over actual available history; rerun the identical range; prove zero duplicate city/minute keys, unchanged rows for the rerun, and no silent conflict overwrite. Preserve safe report evidence externally.
9. **Enable recurring collection.** Disable one-time backfill mode, enable the two-minute grace/five-minute overlap collector, observe normal worker cadence and dashboard behavior, and verify a fresh city-minute row and city-by-day aggregation. Defer user-facing story or insight UI work to a later feature.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Wide 23-measure row | The user selected a single, city-first, non-normalized table. | Multiple entity/source/import tables would make the routine city/time aggregation and operational backfill unnecessarily complex. |
| Grafana range-query client | It is the only source that can backfill available dashboard history and make recurring values semantics-identical to that source. | Writing from the worker cannot reproduce backfilled rate/p95 dashboard values and creates a second metric-semantics implementation. |
| Short recurring requery overlap | It handles late source ingestion and restart recovery without a checkpoint table. | A separate progress/coverage model conflicts with the one-table constraint; a single no-retry minute risks permanent gaps. |
