# Research: Historical Transit Statistics

**Feature**: [Historical Transit Statistics](spec.md)
**Date**: 2026-09-20

## Decisions

### 1. Use one wide city-minute table with a composite key

**Decision**: Create exactly one persistent table, `city_minute_statistics`, keyed by canonical city slug and UTC minute start. It contains direct scalar columns for the approved city dashboard measures, a source-definition version, and a collection status. It has no surrogate identifier, related definition table, import-run table, coverage-gap table, JSON payload, or partitioning.

**Rationale**: The user explicitly chose a simple, denormalized, city-first model. The composite key makes a city-and-time-range aggregation use the primary index and prevents duplicate minutes without an extra index. At seven cities and 14 days, the initial bounded history is about 141,120 rows, which is modest for this shape.

**Alternatives considered**:

- Separate worker, city, source-definition, import-run, and coverage tables: rejected because it violates the single-table constraint and complicates the common city aggregation path.
- Entity-attribute-value rows: rejected because it multiplies row count, requires pivots for ordinary insight queries, and makes field validation harder.
- A surrogate `Id` plus a unique city/time index: rejected because city and minute are the natural identity and the extra identifier has no user value.
- A partitioned or time-series extension design: rejected as premature for the known seven-city scope.

### 2. Persist dashboard-parity minute observations, not raw worker cycles

**Decision**: The collector reads the same Prometheus-compatible metrics source rendered by the committed Grafana dashboard. A row represents a city's observation for the closed UTC minute, not a sum of every ten-second worker tick. The source contract uses literal one-minute expressions and a 60-second range-query step.

**Rationale**: Historical backfill is only possible from the metrics source, and querying it for both backfill and ongoing collection makes the database semantics match the operational dashboard. Writing `CityCycleMetrics` directly would recreate counter-rate and histogram-quantile logic in a second place and would not prove dashboard parity.

**Alternatives considered**:

- Write data directly from `Worker` or `WorkerMetricsReporter`: rejected because it cannot faithfully reproduce current rate and p95 panels and cannot backdate history.
- Store every ten-second export: rejected because the user chose one-minute city records and daily totals would be easy to overcount.
- Store raw counter and histogram buckets: rejected because the result would no longer be the intentionally simple, insight-oriented table.

### 3. Freeze a versioned source contract from the dashboard

**Decision**: Add one source-controlled `worker-dashboard-statistics-v1` contract that maps every persisted field to a literal PromQL expression, unit, missing-value rule, and source metric. Store only its version string on each row. Do not use Grafana's UI-only `$__rate_interval` macro in the collector.

**Rationale**: Dashboard panels use dynamic range macros. A fixed one-minute trailing window makes collection reproducible across Grafana zoom levels and source changes, while versioning prevents a dashboard edit from silently changing historical meaning.

**Alternatives considered**:

- Copy the dashboard macro verbatim: rejected because it has no fixed meaning outside a Grafana panel context.
- Derive metric names mechanically from C# instruments: rejected because the committed dashboard exposes the authoritative Prometheus-facing metric names and labels.
- Persist a relational source-definition catalogue: rejected by the single-table requirement; source control provides the published contract.

### 4. Preserve city semantics and omit worker-wide context in v1

**Decision**: Persist only the 23 city-scoped values from the current worker dashboard. Store the raw `last_cycled` and `last_worked` timestamps; derive a historical age from the row's minute boundary when an insight needs it. Do not repeat worker-wide gauges or rates on each city row in v1.

**Rationale**: Every persistent row is city-specific. Repeating worker-wide values would make cross-city sums misleading and gives no city-pattern value. The specification permits retaining worker-wide values only when chosen; omitting them is the simpler city-first choice.

**Alternatives considered**:

- Add a worker-only row with an empty city: rejected because every row must be city-scoped.
- Copy worker-wide fields to every city row: rejected for v1 because it creates duplicated values that callers could accidentally sum.
- Store dashboard age fields directly: rejected because source heartbeat timestamps plus the row minute reproduce age without redundant data.

### 5. Use a dedicated read-only metrics credential and an ingestion grace period

**Decision**: Query the Grafana Cloud Metrics Prometheus HTTP API with a dedicated access policy limited to `metrics:read`. The collector waits two closed minutes before querying and rechecks the preceding five closed minutes on every run. It uses only HTTPS endpoint and secret values injected into the server at deployment.

**Rationale**: The existing OTLP publisher credential is for metric export, not read access. The source is exported every ten seconds, so a two-minute grace plus a short idempotent overlap accommodates ingestion delay and restarts without a checkpoint table.

**Alternatives considered**:

- Reuse the publisher or provisioning token: rejected by least privilege.
- Query a public metrics endpoint or enable production `/metrics`: rejected because production metrics are outbound-only and the source is already Grafana Cloud.
- Add a database checkpoint table: rejected by the single-table constraint; the collector can derive gaps from absent city-minute keys and safely revisit an overlap.

### 6. Separate schema migration from historical backfill

**Decision**: The EF migration bundle creates only the table and its primary key. The deployed, configuration-gated collector performs a dry-run preflight, initial bounded backfill, reconciliation, and recurring one-minute collection. It emits safe structured summaries and an operator/CI report; it never makes network calls in an EF migration.

**Rationale**: Schema deployment needs to be deterministic and repeatable. Historical source availability, credentials, and API limits are runtime concerns. The repository already builds and runs an EF migration bundle before the server deployment.

**Alternatives considered**:

- Fetch Grafana data inside `Up()`: rejected because migrations must not depend on a network service or contain credentials.
- Add a second database table for import history: rejected by the clarified table constraint.
- Add a new backfill executable and deployment unit: rejected for v1 because the existing co-hosted server can run the bounded, disabled-by-default collector without another application surface.

## Source and implementation findings

- The dashboard source of truth is `observability/grafana/dashboards/transitjazz-worker-overview.json` (UID `transitjazz-worker-overview`). It is UTC, refreshes every ten seconds, and visualizes the seven canonical `transit_city` labels.
- `WorkerMetricsReporter` reports the city values and exports metrics every ten seconds. The host in `Server.WebAPI/Program.cs` is the deployed metrics host; the standalone worker does not configure the exporter.
- `CityCycleMetrics` is produced once per city loop, but it is not an appropriate persistence seam because it is not source-equivalent for counter-rate and histogram p95 dashboard values.
- `Server.Data` has Npgsql and configuration-by-assembly discovery, but currently contains no entity, `DbSet`, configuration, migration, test project, or runtime host registration. `AppDbContextFactory` uses a stale connection-string name and must be aligned with `TransitJazzDB`.
- The existing database migration bundle already receives `CONNECTIONSTRINGS__TRANSITJAZZDB` in the CI workflow. The server runtime does not yet receive/register that connection string, so the plan adds a Key Vault-backed runtime secret reference.
- The committed observability guidance expects a rolling 14-day metrics history. The actual Grafana plan, query endpoint, labels, query limits, and remaining history must be proven during release preflight; a source gap is recorded, never reconstructed from logs, Parquet, or feed data.

## External references

- [Grafana Cloud Metrics HTTP API querying](https://grafana.com/docs/grafana-cloud/observe-and-act/send-data/metrics/metrics-prometheus/query-http-api/)
- [Grafana Cloud access policy scopes](https://grafana.com/docs/grafana-cloud/platform/security-and-account-management/security-and-access/authentication-and-permissions/)
- [Prometheus HTTP API range queries](https://prometheus.io/docs/prometheus/latest/querying/api/)
- [Prometheus functions: rate, histogram quantile, and over-time functions](https://prometheus.io/docs/prometheus/latest/querying/functions/)
- [Prometheus query basics and staleness](https://prometheus.io/docs/prometheus/latest/querying/basics/)
- [PostgreSQL `INSERT` and conflict handling](https://www.postgresql.org/docs/18/sql-insert.html)
- [EF Core migration bundles](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying?tabs=dotnet-core-cli)
