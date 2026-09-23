---

description: "Implementation tasks for Historical Transit Statistics"
---

# Tasks: Historical Transit Statistics

**Input**: Design documents from `/specs/055-database/`
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `quickstart.md`, and `contracts/worker-dashboard-statistics-v1.md`

**Tests**: Required. The specification calls for xUnit model, store, source-client, collector, dashboard-binding, worker-metric regression, migration, and controlled live-validation coverage. Write the test tasks in each story before its implementation tasks and demonstrate their failure before implementing the corresponding behavior.

**Organization**: Tasks are grouped by user story so the persistent model, historical ingestion, and provenance guarantees can be built and verified as separate increments. This feature deliberately adds no public endpoint or client UI.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes a different file and has no unfinished-task dependency.
- **[Story]**: The user story served by the task (`US1`, `US2`, or `US3`).
- Every task names its target file path.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish the existing-project references and test access needed by the data model and the co-hosted Web API; do not create a new service or test-project topology.

- [X] T001 Add the `ChefKnifeStudios.TransitJazz.Server.Data` project reference to `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/ChefKnifeStudios.TransitJazz.Server.WebAPI.csproj` without adding a new runtime package.
- [X] T002 [P] Add a direct `ChefKnifeStudios.TransitJazz.Server.Data` project reference for database metadata and PostgreSQL store tests in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.csproj`.
- [X] T003 [P] Compare all 23 city-scoped mappings and the seven `transit_city` labels in `specs/055-database/contracts/worker-dashboard-statistics-v1.md` against `observability/grafana/dashboards/transitjazz-worker-overview.json`, recording only contract-safe corrections in the contract file and never changing the dashboard.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Make the existing data project consistently address the `TransitJazzDB` connection and support scoped/worker database access before statistics work starts.

**⚠️ CRITICAL**: Complete this phase before beginning user-story tasks.

- [X] T004 Update `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/AppDbContextFactory.cs` to resolve `ConnectionStrings:TransitJazzDB` from design-time configuration and fail safely when it is absent, replacing the stale `PokerAttackDB` lookup.
- [X] T005 Update `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/ServiceCollectionExtensions.cs` to register both the scoped `AppDbContext` and an `IDbContextFactory<AppDbContext>` against `TransitJazzDB` for the statistics store and hosted collector.
- [X] T006 Add `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/DataRegistrationTests.cs` to prove the data registration resolves the intended context/factory configuration without logging a connection string.

**Checkpoint**: The data project has one consistent connection-string name and can be consumed by the Web API and its tests.

---

## Phase 3: User Story 1 - Preserve Historical City Patterns (Priority: P1) 🎯 MVP

**Goal**: Persist one UTC-minute dashboard-parity observation per canonical city in the sole wide table, retaining every approved nullable value and allowing a direct city/time-range query.

**Independent Test**: Write a fixed dashboard-parity interval for one city through the statistics store, read it back by city and UTC range, and verify all 23 values, source version, status, minute alignment, zero/null distinction, and primary-key behavior.

### Tests for User Story 1

- [X] T007 [P] [US1] Add EF metadata and validation tests for the sole entity, composite key, explicit PostgreSQL types, minute alignment, nullable source values, absent `BaseEntity` columns, and no extra indexes/tables in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/CityMinuteStatisticModelTests.cs`.
- [X] T008 [P] [US1] Add opt-in PostgreSQL integration tests for created, unchanged, partial-to-complete, no-data, duplicate, and incompatible city-minute writes in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/CityMinuteStatisticsStoreTests.cs`.
- [X] T009 [P] [US1] Add a direct city/day and bounded UTC-range retrieval test that demonstrates sampled gauges are not treated as totals in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/CityMinuteStatisticsQueryTests.cs`.
- [X] T010 [US1] Create the non-`BaseEntity` 23-field `CityMinuteStatistic` entity, its collection-status enum, and its invariant validation in `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Models/CityMinuteStatistic.cs`.
- [X] T011 [US1] Configure `city_minute_statistics` with the `(city_slug, stat_minute_utc)` primary key, `timestamptz` minute column, bounded required metadata, explicit scalar column types, and no unplanned indexes in `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Configurations/CityMinuteStatisticConfiguration.cs`.
- [X] T012 [US1] Add the `CityMinuteStatistic` `DbSet` and preserve assembly-discovered mappings without applying audit fields to statistics rows in `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/AppDbContext.cs`.
- [X] T013 [US1] Implement bounded, conflict-safe batch persistence and city/range reads that classify created, unchanged, filled, and discrepant rows without overwriting confirmed values in `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Statistics/CityMinuteStatisticsStore.cs`.
- [X] T014 [US1] Generate the schema-only EF migration and model snapshot for the single table in `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Migrations/`, with no seed data, metrics client, network request, import table, or coverage table.
- [X] T015 [US1] Add migration-script assertions that permit only `city_minute_statistics` schema creation and its composite primary key in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/CityMinuteStatisticsMigrationTests.cs`.

**Checkpoint**: A known minute interval can be stored once, queried by city and UTC range, rerun safely, and distinguished from missing or conflicting source values without any public endpoint.

---

## Phase 4: User Story 2 - Backdate Available Dashboard History Safely (Priority: P1)

**Goal**: Query the approved read-only metrics source, dry-run or write bounded historical ranges in six-hour chunks, and continually reread recent closed minutes without disrupting the worker.

**Independent Test**: Use fixed Prometheus range responses to dry-run and apply a bounded interval twice; verify all configured city/minute rows, literal one-minute expressions, six-hour chunk boundaries, two-minute grace, five-minute overlap, safe reports, and zero duplicate keys.

### Tests for User Story 2

- [X] T016 [P] [US2] Add fixed-HTTP-response tests for literal PromQL construction, UTC minute alignment, seven-label validation, 0/1 conversion, null/no-data, warnings, malformed results, counter resets, incomplete histogram buckets, and secret-free failures in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/GrafanaPrometheusStatisticsSourceTests.cs`.
- [X] T017 [P] [US2] Add collector tests for disabled operation, dry run, initial six-hour chunks, actual returned coverage, all-city minute grids, two-minute grace, five-minute overlap, cancellation, idempotent reruns, and outcome summaries in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/HistoricalStatisticsCollectorTests.cs`.
- [X] T018 [P] [US2] Add options and host-registration tests that reject non-HTTPS endpoints, unsafe intervals, wrong source version, invalid city configuration, and configurations that expose source authorization in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/HistoricalStatisticsOptionsTests.cs`.
- [X] T019 [US2] Implement the fixed `worker-dashboard-statistics-v1` catalogue of all 23 city-scoped fields, literal expressions, units, missing-value rules, and configured-city label rules in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Statistics/WorkerDashboardStatisticsCatalog.cs`.
- [X] T020 [US2] Implement typed Prometheus range-query requests and response parsing that preserve nullable matrices, validate label/cardinality/buckets, classify warnings and no-data, and redact endpoint, authorization, entity IDs, and response bodies in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Statistics/GrafanaPrometheusStatisticsSource.cs`.
- [X] T021 [US2] Add disabled-by-default collector options and startup validation for the fixed contract, HTTPS reader endpoint, reader authorization, one-minute granularity, initial backfill, dry run, two-minute grace, and five-minute overlap in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Statistics/HistoricalStatisticsOptions.cs`.
- [X] T022 [US2] Add safe requested/returned coverage, city scope, row-outcome, gap, warning, failure, and discrepancy summary types with no raw source payload or configuration values in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Statistics/StatisticsCollectionReport.cs`.
- [X] T023 [US2] Register `TransitJazzDB`, the typed reader `HttpClient`, validated statistics options, the source client, store, and disabled-by-default hosted collector in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs` without adding an endpoint or modifying worker metric/export registration.
- [X] T024 [US2] Add non-secret disabled defaults for the statistics collector in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json`.
- [X] T025 [US2] Implement the hosted collection path: source preflight, sequential six-hour initial backfill, closed-minute city grids, dry-run suppression of writes, conflict-safe store calls, and recurring grace/overlap queries in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Statistics/HistoricalStatisticsCollector.cs`.

**Checkpoint**: A deployment with collection disabled performs no source or database history writes; an approved dry-run and applied bounded rerun produce safe coverage/outcome reports and no duplicate city-minute keys.

---

## Phase 5: User Story 3 - Trust the Origin of Every Statistic (Priority: P2)

**Goal**: Make every persisted field demonstrably traceable to exactly one approved worker-dashboard expression and refuse incompatible source semantics while retaining confirmed history.

**Independent Test**: Select each stored field, derive its catalogue entry and source-contract definition, compare it with the committed dashboard and worker metric labels, then show that a changed/unknown definition is reported as incomplete or discrepant rather than overwriting a confirmed value.

### Tests for User Story 3

- [X] T026 [P] [US3] Add a dashboard/catalogue binding test that parses all 23 approved field definitions, expressions, units, one-minute rules, and availability semantics exactly once from `specs/055-database/contracts/worker-dashboard-statistics-v1.md` and `observability/grafana/dashboards/transitjazz-worker-overview.json` in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/WorkerDashboardStatisticsCatalogTests.cs`.
- [X] T027 [P] [US3] Add a regression test proving the source metric names and `transit_city` label semantics remain unchanged in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/Metrics/WorkerDashboardStatisticsContractTests.cs`.
- [X] T028 [P] [US3] Add reconciliation tests for exact integers/booleans/timestamps, documented floating-point tolerances, null preservation, incompatible source versions, and non-overwriting discrepancy reports in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/HistoricalStatisticsReconciliationTests.cs`.
- [X] T029 [US3] Extend catalogue validation to reject non-v1 fields, duplicate definitions, dashboard/contract divergence, and unknown city labels while exposing field provenance needed by collection reports in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Statistics/WorkerDashboardStatisticsCatalog.cs`.
- [X] T030 [US3] Implement representative-minute reconciliation and fail-closed `Partial`, `NoData`, and `Discrepant` handling without replacing original confirmed values in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Statistics/HistoricalStatisticsCollector.cs`.
- [X] T031 [US3] Finalize the reviewed versioned catalogue, source calculation precision, and provenance/reconciliation instructions in `specs/055-database/contracts/worker-dashboard-statistics-v1.md` and `specs/055-database/quickstart.md` without adding a database provenance table.

**Checkpoint**: The only accepted source contract is `worker-dashboard-statistics-v1`; every stored field has one tested dashboard definition, and incompatible collection evidence is safe, visible, and non-destructive.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Provision secret-safe runtime configuration, validate deployment ordering, and execute the gated schema/backfill/recurring-release evidence without altering the worker dashboard or public API surface.

- [X] T032 Add non-secret TransitJazz database and dedicated Grafana `metrics:read` configuration parameters/Key Vault inputs to `bicep/main.bicep`, then regenerate (never hand-edit) `bicep/main.json`.
- [X] T033 Add Key Vault-backed server secret and environment-variable wiring for `ConnectionStrings__TransitJazzDB` and the Grafana reader settings in `bicep/modules/containerApp.bicep` without exposing values in parameters, logs, or outputs.
- [X] T034 Update the migration-bundle and server-deploy validation/order to apply the schema before server deployment while keeping collector writes disabled by default in `.github/workflows/server.yml`.
- [X] T035 [P] Add release-gate commands and evidence expectations for retention/access preflight, bounded dry run, reconciliation, idempotent applied backfill, and recurring enablement in `specs/055-database/quickstart.md`.
- [ ] T036 Build the solution, run the statistics and worker-dashboard regression suites, build the Linux EF migration bundle, and inspect its schema-only script using `src/ChefKnifeStudios.TransitJazz.sln` and `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Migrations/`.
- [X] T037 Validate and what-if the secret-safe infrastructure changes from `bicep/main.bicep`, `bicep/main.dev.bicepparam`, and `bicep/main.prod.bicepparam`, confirming no source endpoint, reader credential, or connection string enters deployment output.
- [ ] T038 With an operator-provisioned `metrics:read` credential, run the 15-minute source preflight and bounded dry run described in `specs/055-database/quickstart.md`; retain only the approved safe report and leave writes disabled on any warning, retention, label, field, or reconciliation failure.
- [ ] T039 After dry-run approval, apply the bounded six-hour-chunk backfill, rerun the identical range, and preserve evidence of one key per city/minute, unchanged/filled/discrepant counts, and representative field comparisons as required by `specs/055-database/quickstart.md`.
- [ ] T040 Disable one-time backfill, enable recurring collection, and verify a fresh city-minute row, a city/day aggregation, normal worker cadence, and unchanged dashboard behavior using `specs/055-database/quickstart.md` and `observability/grafana/dashboards/transitjazz-worker-overview.json`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Starts immediately. T001 and T002 can proceed together; T003 is a contract-review gate that does not alter the dashboard.
- **Foundational (Phase 2)**: Starts after T001/T002 and blocks all story implementation; it standardizes the design-time and runtime database composition root.
- **US1 (Phase 3)**: Starts after Phase 2. It establishes the table and conflict-safe store used by collection.
- **US2 (Phase 4)**: Starts after US1 because it writes via the store. T016-T018 can run in parallel; T019-T022 precede host wiring in T023, and T025 follows T019-T024.
- **US3 (Phase 5)**: Starts after the US2 catalogue and collector exist. T026-T028 can run in parallel; T029 precedes T030; T031 follows the validated catalogue and reconciliation behavior.
- **Polish (Phase 6)**: Begins once source and persistence work are complete. T032-T034 precede deployment validation. T038-T040 are sequential release gates and require external operator credentials and approval.

### User Story Dependencies

- **US1 (P1)**: Depends only on foundational database registration; it is the MVP persistence increment.
- **US2 (P1)**: Depends on US1's entity, migration, and conflict-safe store; it adds import and ongoing source-aligned collection.
- **US3 (P2)**: Depends on US2's catalogue/collector integration; it locks provenance and makes mismatch handling independently testable.

### Parallel Opportunities

- T001/T002/T003 target separate project/document files.
- US1 test tasks T007-T009 can be authored in parallel before the model/store implementation.
- US2 test tasks T016-T018 can be authored in parallel before client/collector implementation.
- US3 test tasks T026-T028 can be authored in parallel before provenance hardening.
- The Bicep/runtime documentation tasks T032-T035 can be divided by file once the interface configuration names are agreed; release gates T038-T040 remain sequential.

## Parallel Example: User Story 1

```text
Task: "Add metadata and validation tests in src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/CityMinuteStatisticModelTests.cs"
Task: "Add PostgreSQL store tests in src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/CityMinuteStatisticsStoreTests.cs"
Task: "Add city/range query tests in src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/CityMinuteStatisticsQueryTests.cs"
```

## Parallel Example: User Story 2

```text
Task: "Add source-client tests in src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/GrafanaPrometheusStatisticsSourceTests.cs"
Task: "Add collector tests in src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/HistoricalStatisticsCollectorTests.cs"
Task: "Add options tests in src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/HistoricalStatisticsOptionsTests.cs"
```

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Setup and Foundational configuration.
2. Complete the US1 test-first model, mapping, store, migration, and script-validation tasks.
3. Validate one fixed city/minute interval through the store and direct city/time query.
4. Stop before enabling any source query or runtime collection; this increment has no new public endpoint or worker-metric change.

### Incremental Delivery

1. Add US1 to provide a single durable, queryable city-minute table.
2. Add US2 to dry-run, backfill, and collect source-aligned minutes without a worker change.
3. Add US3 to prove every value's source definition and fail closed on incompatibility.
4. Execute Phase 6's provisioned release gates: schema, preflight, dry run, applied rerun, then recurring collection.

### Release Safety

- Never put Grafana access, a data import, or source network calls in the EF migration.
- Never use the OTLP publisher or provisioning token for historical reads; use a dedicated `metrics:read` credential supplied only through Key Vault-backed server configuration.
- Never fabricate missing history or silently overwrite a confirmed row. Keep writes disabled until dry-run and reconciliation evidence are approved.
- Do not create a public endpoint, UI, checkpoint/import/coverage table, or any change to worker metrics, dashboard queries, labels, exporter cadence, or alerts.
