# Tasks: Worker Observability

**Input**: Design documents from `/specs/053-worker-observability/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Required by FR-022 and the specification's three-layer test strategy. Write contract/lifecycle tests before the implementation tasks they validate.

**Organization**: Tasks are grouped by user story. User Story 1 delivers the first independently testable operational increment; later stories consume its emitted metric contract without changing its behavior.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Different files with no incomplete-task dependency.
- **[US#]**: User story traceability label.
- Every task names an exact repository path.

## Phase 1: Setup and Governance

**Purpose**: Record the selected co-hosted topology and clear mandatory production gates before any Cloud enablement.

- [X] T001 Verify that the separately approved Constitution IV, co-hosted-topology, and worker-artifact amendments plus external-telemetry egress approval exist, then record approval evidence in `docs/observability/worker-governance.md` before enabling production metrics; do not modify `.specify/memory/constitution.md` in this task.
- [X] T002 [P] Create the monitoring asset directories and source placeholders in `observability/grafana/dashboards/`, `observability/grafana/alerts/`, `observability/grafana/provisioning/`, `observability/grafana/terraform/`, and `observability/prometheus/`.
- [X] T003 [P] Add explicit `OpenTelemetry.Extensions.Hosting` and `OpenTelemetry.Exporter.OpenTelemetryProtocol` production package references to `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.csproj` and `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/ChefKnifeStudios.TransitJazz.Server.WebAPI.csproj`.
- [X] T004 [P] Add `Microsoft.Extensions.Diagnostics.Testing` and development-only `OpenTelemetry.Exporter.Prometheus.HttpListener` references to `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.csproj` and `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/ChefKnifeStudios.TransitJazz.Server.WebAPI.csproj`.
- [X] T005 [P] Add safe default `Worker` and `Metrics` configuration sections to `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json` and `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.Development.json`.

---

## Phase 2: Foundational Contracts and Instrumentation

**Purpose**: Build the city-result, configuration, reporter, and host wiring foundation required by every story.

**⚠️ CRITICAL**: Complete this phase before closing any user story.

- [X] T006 Create `CityFetchResult`, `CityFetchOutcome`, `CityCycleMetrics`, `WorkerCycleMetrics`, `MetricsOptions`, and `WorkerOptions` in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Metrics/` according to `data-model.md`.
- [X] T007 Define `IWorkerMetricsReporter` and `NullWorkerMetricsReporter` in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Metrics/IWorkerMetricsReporter.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Metrics/NullWorkerMetricsReporter.cs`.
- [X] T008 Add focused model/validation tests for the new fetch outcomes and options in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/CityFetchResultTests.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/MetricsOptionsTests.cs`.
- [ ] T009 Change the fetch contract from `FeedMessage` to `CityFetchResult` in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Cities/ITransitCity.cs` and update its test fakes in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/CityLoopTests.cs`.
- [ ] T010 [P] Adapt GTFS-RT source success, empty response, source timestamp, and partial/failure handling in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Cities/GtfsRtCity.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/CityFetchResultTests.cs`.
- [ ] T011 [P] Adapt MARTA rail/bus result aggregation to `CityFetchResult` in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Cities/MartaCity.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/RailRealtime/RailRealtimeAdapter.cs`.
- [ ] T012 [P] Adapt New York multi-feed aggregation to `CityFetchResult` in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Cities/NymtaCity.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/NymtaCityFaultIsolationTests.cs`.
- [ ] T013 Implement the sealed, `IMeterFactory`-backed reporter and create all worker/city instruments once in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Metrics/WorkerMetricsReporter.cs`.
- [ ] T014 Bind and validate metrics options, register the null/real reporter, configure metrics-only OTLP export, and add stable service/environment identity plus a unique resource-only `service.instance.id` in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs`.
- [ ] T015 Add production startup validation that rejects local Prometheus listener configuration and malformed Cloud endpoint settings in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Metrics/MetricsOptions.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/MetricsOptionsTests.cs`.

**Checkpoint**: The host has a disabled-by-default, testable reporter contract; all city fetchers distinguish valid empty input from failed input.

---

## Phase 3: User Story 1 — Establish Worker Health (Priority: P1) 🎯 MVP

**Goal**: An operator can distinguish stopped, idle, input-starved, failed, and working conditions for the worker and for an individual configured city.

**Independent Test**: Drive successful, idle, input-starved, failed, and stopped cycle paths with one city degraded and another healthy; assert heartbeats, work timestamps, error counters, and city identity without reading logs.

### Tests for User Story 1

- [ ] T016 [P] [US1] Write reporter contract tests for worker and city heartbeat, idle work timestamp, city initialization, and explicit unknown resets in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/WorkerMetricsReporterTests.cs`.
- [ ] T017 [P] [US1] Write worker lifecycle tests for successful, idle, city-failed, and cancellation paths in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/WorkerMetricsLifecycleTests.cs`.

### Implementation for User Story 1

- [ ] T018 [US1] Inject `IWorkerMetricsReporter` and `WorkerOptions` into the worker constructor while preserving compatibility for existing tests in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`.
- [ ] T019 [US1] Extract a single testable full-cycle execution seam and replace the hard-coded timer with `WorkerOptions.CycleIntervalSeconds` in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`.
- [ ] T020 [US1] Build and report `CityCycleMetrics` exactly once per configured city outside the `EmitsTelemetry` gate in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`.
- [ ] T021 [US1] Report city non-cancellation errors in the city catch path and worker completion in an outer finally in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`.
- [ ] T022 [US1] Initialize the complete `ITransitCity.Name` set at host startup and verify metrics disabled uses `NullWorkerMetricsReporter` in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/WorkerMetricsLifecycleTests.cs`.
- [ ] T023 [US1] Force-flush pending metrics during orderly host shutdown without changing existing sidecar shutdown behavior in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Metrics/WorkerMetricsReporter.cs`.

**Checkpoint**: Worker and city liveness are independently observable; a failed city does not suppress healthy-city reporting.

---

## Phase 4: User Story 2 — Diagnose Operational Behavior (Priority: P2)

**Goal**: Operators can inspect per-city input, output, reconciliation quality, duration, and city-attributable resource state, then use structured logs only for entity detail.

**Independent Test**: Feed slow, poor-quality, input-degraded, and resource-pressured summaries for one city while another remains healthy; verify metrics and panels identify the affected city.

### Tests for User Story 2

- [ ] T024 [P] [US2] Add metric contract tests for city input, output, cache, duration, batch-wire, reconciliation-outcome, and suppression gauges in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/WorkerMetricsReporterTests.cs`.
- [ ] T025 [P] [US2] Add tests that preserve actual reconciliation outcomes and suppression values in `CityTickResult` summaries, without introducing a synthetic decision score, in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/WorkerMetricsLifecycleTests.cs`.

### Implementation for User Story 2

- [ ] T026 [US2] Carry real reconciliation outcomes needed for quality diagnostics from `ProcessSpatialReconciliationAsync` into `CityTickResult` in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`.
- [ ] T027 [US2] Emit the complete worker and city metric surface from `contracts/metrics-contract.md`, keeping heap, working set, and sidecar state worker-wide in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Metrics/WorkerMetricsReporter.cs`.
- [ ] T028 [US2] Create the Health, Work, Input, and Resources dashboard panels with city comparison/filtering, derived worker/city composite-health expressions, explicit configured/unconfigured quality-threshold behavior, and required descriptions in `observability/grafana/dashboards/transitjazz-worker-overview.json`.
- [ ] T029 [US2] Add dashboard-to-emitted-metric binding assertions, composite-health state assertions, configured/unconfigured quality-threshold checks, and prohibited-label checks in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/MonitoringAssetBindingTests.cs`.

**Checkpoint**: Every emitted metric has a documented panel, and per-city degradation is visible without an entity-level metric label.

---

## Phase 5: User Story 3 — Receive Actionable Alerts (Priority: P3)

**Goal**: On-call receives verified worker and city-specific alerts, including the case where one city disappears while the worker and other cities continue.

**Independent Test**: Evaluate each alert with fixture metrics for a stopped worker, one missing city, all missing cities, errors, slow cycle, input stopped, and high known input lag.

### Tests for User Story 3

- [ ] T030 [P] [US3] Add alert-contract tests for referenced metric names, distinct `WorkerStalled` versus `WorkerGone` semantics, exact liveness/input/error/slow/lag thresholds, `NoData = Alerting`, city labels, and explicit presence logic in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/MonitoringAssetBindingTests.cs`.
- [ ] T031 [P] [US3] Add metric fixtures for stopped-worker, one-city-missing, all-worker-missing, known-high-lag, and unaffected-city cases in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/MonitoringAlertFixtureTests.cs`.

### Implementation for User Story 3

- [ ] T032 [US3] Define `WorkerStalled` as a heartbeat older than three configured intervals and `WorkerGone` as an absent full-cycle counter/series with `NoData = Alerting`, then define `CityMissing`, city input/error/slow/lag rules, exact thresholds, severities, and city summaries in `observability/grafana/alerts/transitjazz-worker-alerts.json`.
- [ ] T033 [US3] Implement Grafana Cloud dashboard, rule group, contact point, and notification-policy resources from the canonical assets in `observability/grafana/terraform/`.
- [ ] T034 [US3] Add local Grafana alert-rule provisioning only, consuming the canonical alert source after T032, in `observability/grafana/provisioning/`.
- [ ] T035 [US3] Document deliberate alert firing, notification delivery, and city-missing verification in `docs/observability/worker-alert-runbook.md`.

**Checkpoint**: One degraded city produces one named city alert; a totally stopped worker produces critical global no-data alerts.

---

## Phase 6: User Story 4 — Operate Observability Safely (Priority: P4)

**Goal**: Developers can run local monitoring without Cloud telemetry, while production keeps secrets, labels, and network exposure safe.

**Independent Test**: Scrape local metrics, inspect labels, validate no production listener configuration, and review Bicep/secret configuration without exposing credentials.

### Tests for User Story 4

- [ ] T036 [P] [US4] Add local Prometheus scrape tests that lock translated names, `transit_city` values, units, resource-only `service.instance.id`, and prohibited-label absence in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/PrometheusScrapeContractTests.cs`.
- [ ] T037 [P] [US4] Add deployment-configuration tests for disabled-by-default metrics and production local-listener rejection in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/MetricsOptionsTests.cs`.

### Implementation for User Story 4

- [ ] T038 [US4] Add development-only Prometheus listener registration behind `Metrics:LocalPrometheusEnabled` in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs`.
- [ ] T039 [US4] Create local Prometheus scrape configuration and Grafana dashboard provisioning only, after the canonical dashboard and alert assets exist, in `observability/prometheus/prometheus.yml` and `observability/grafana/provisioning/`; do not duplicate T034 alert provisioning.
- [ ] T040 [US4] Add Prometheus and Grafana services with local-only environment defaults in `docker-compose.observability.yml`.
- [ ] T041 [US4] Add Key Vault and ACA secret-reference support for publisher/provisioning credentials in `bicep/main.bicep`, `bicep/modules/containerApp.bicep`, and `bicep/modules/keyVault.bicep`.
- [ ] T042 [US4] Wire production `Metrics__*` secret references without adding ingress, port mappings, or `/metrics` probes in `bicep/main.bicep` and `bicep/main.prod.bicepparam`.
- [ ] T043 [US4] Update the FR-024 documentation matrix across `docs/observability/worker-governance.md` and `specs/053-worker-observability/quickstart.md`, covering credential rotation, 14-day retention, external-egress approval, dashboard identity, co-hosted-ingress exception, local isolation, approved contact points, and collector-adoption criteria.

**Checkpoint**: Local dashboards use local data only; production credentials are secret-backed and production metrics add no inbound surface.

---

## Phase 7: Polish and Cross-Cutting Validation

**Purpose**: Prove the full monitoring contract, series budget, documentation matrix,
acceptance evidence, operational readiness, and developer workflow.

- [ ] T044 [P] Calculate the exact series count from instrument, label combination, histogram bucket, configured-city, and maximum-replica counts; record the selected service quota, tenfold-headroom calculation, usage-panel queries, and pass/fail release gates in `docs/observability/worker-series-budget.md`.
- [ ] T045 [P] Add distinct `WorkerStalled`/`WorkerGone` behavior, active-series, samples-per-second, token-rotation, orderly-shutdown, and deliberate-notification verification steps to `docs/observability/worker-alert-runbook.md`.
- [ ] T046 Run the complete worker test suite and resolve instrumentation regressions in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/`.
- [ ] T047 Run the local workflow and validate every command and safety check in `specs/053-worker-observability/quickstart.md`.
- [ ] T048 Perform the production-readiness drill for all required alert conditions and document the FR-024 documentation matrix plus SC-001/SC-002/SC-007 verification evidence in `docs/observability/worker-release-checklist.md`, proving 5/5 state-identification cases, three consecutive stopped-worker rollout tests within three intervals, 6/6 designated notification routes at stated severity, passing series-budget gates, and composite-health cases.

---

## Dependencies and Execution Order

### Phase Dependencies

- Phase 1 establishes governance, assets, packages, and safe configuration.
- Phase 2 depends on Phase 1 and blocks all completed user stories.
- US1 depends on Phase 2 and is the MVP.
- US2 and US3 require US1's emitted lifecycle contract; their test authoring can begin after Phase 2.
- US4 can develop its local assets after Phase 2, but completes after the reporter and alert assets exist.
- Phase 7 depends on all selected stories.

### User Story Completion Order

```text
Setup → Foundational → US1 (MVP)
                       ├── US2 (diagnostic panels)
                       ├── US3 (alerting)
                       └── US4 (safe local/production operation)
US2 + US3 + US4 → Polish and release readiness
```

### Parallel Opportunities

- T002–T005 can proceed in parallel after T001's governance record is created.
- T010–T012 can proceed in parallel once T009 changes the fetch interface.
- T016 and T017, T024 and T025, T030 and T031, and T036 and T037 are parallel test tasks.
- T028 and T032 can proceed in parallel once their dependent metric contract is stable; T034 depends on T032, and T039 depends on the canonical dashboard and alert assets from T028/T032.
- T041 and T044 can proceed in parallel with local asset work after their respective prerequisites.

## Parallel Example: User Story 1

```text
Task: T016 Reporter contract tests in src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/WorkerMetricsReporterTests.cs
Task: T017 Worker lifecycle tests in src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/WorkerMetricsLifecycleTests.cs
```

## Implementation Strategy

### MVP First

1. Complete Phases 1 and 2.
2. Complete T016–T023 for US1.
3. Prove worker and per-city health under success, idle, failed-city, and stopped-worker scenarios.
4. Keep Cloud export disabled until the required governance gates are complete.

### Incremental Delivery

1. US1 makes liveness and city health observable.
2. US2 adds diagnostic depth and city comparison.
3. US3 turns conditions into verified notifications.
4. US4 adds local workflow and deployment safety.
5. Phase 7 proves capacity and operational readiness.

## Notes

- No task authorizes a git commit.
- `EmitsTelemetry` remains a legacy Parquet sidecar gate and must never gate operational metrics.
- The user-selected option C preserves current API ingress; metrics may not add a public endpoint.
