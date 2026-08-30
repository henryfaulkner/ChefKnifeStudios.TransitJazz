# Tasks: Centralized Structured Logging

**Input**: Design documents from `/specs/054-centralized-logging/`  
**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [quickstart.md](quickstart.md), and [contracts/](contracts/)

**Tests**: Required. The specification mandates JSON-shape, event-contract, redaction, rate-limit, routing, table-plan, skill, `doctor`, Grafana-correlation, and removal-guard evidence. Use the actual `TransitDataWorker.Tests` project; do not add feature tests to the orphan test directory.

**Organization**: P1 stories are ordered by dependency: safe event capture (US2) enables anomaly investigation (US1). P1 cutover (US4) is last because it consumes the completed capture/investigation/diagnostic evidence. This does not lower its release priority.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Different files and no dependency on another incomplete task.
- **[Story]**: User story label. Setup, foundational, and polish tasks intentionally have no story label.

## Phase 1: Setup and Release-Evidence Scaffolding

**Purpose**: Create the evidence locations and test support used throughout the feature without changing production routing or retiring legacy telemetry.

- [X] T001 [P] Create the baseline/cost/Basic-query-proof template in `docs/observability/centralized-logging-baseline.md`.
- [X] T002 [P] Create the FR-024 and success-criteria release-evidence matrix in `docs/observability/centralized-logging-release-checklist.md`.
- [X] T003 [P] Create the no-other-consumer and historical-blob-preservation audit template in `docs/observability/centralized-logging-removal-audit.md`.
- [X] T004 [P] Add an in-memory `ILogger` JSON-capture and fake-time helper to `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/StructuredLoggingTestHelpers.cs`.

---

## Phase 2: Foundational Contracts and Infrastructure

**Purpose**: Establish the typed event primitives, safe host registration, and declarative Azure routing surface that block all user stories.

**⚠️ CRITICAL**: Do not begin user-story work until this phase is complete. These tasks create no production cutover and must preserve the Parquet sidecar.

- [X] T005 Create the v1 event envelope, event-name/outcome/reason enums, bounded diagnostic context, and `CycleId` value rules in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Logging/StructuredLogEvent.cs`.
- [X] T006 Create configurable structured-event policy options and an injected-clock coalescing/recovery state machine in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Logging/StructuredEventPolicy.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Logging/StructuredLoggingOptions.cs`.
- [X] T007 Create source-side allowed-property validation and safe endpoint-identity formatting in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Logging/StructuredLogRedactor.cs`.
- [X] T008 Create the worker event-emitter abstraction, null implementation, and `ILogger` implementation in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Logging/IWorkerStructuredEventLogger.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Logging/StructuredEventEmitter.cs`.
- [X] T009 [P] Add schema/taxonomy/required-field tests for `StructuredLogEvent` in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/StructuredLogEventTests.cs`.
- [X] T010 [P] Add fake-clock initial/transition/reminder/recovery tests for the policy in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/StructuredEventPolicyTests.cs`.
- [X] T011 [P] Add allow-list, secret-bearing URL/header/connection-string, and exception-text rejection tests in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/StructuredLoggingRedactionTests.cs`.
- [X] T012 Add UTC one-line JSON console registration, production category filters, emitter DI, and dual-run sidecar preservation to `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Program.cs`.
- [X] T013 Add host-level JSON formatter, disabled-noise, and preserved-metrics tests in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/LoggingHostTests.cs`.
- [X] T014 Replace the legacy destination/shared-key interface with the Azure Monitor destination in `bicep/modules/containerAppsEnvironment.bicep`.
- [X] T015 Create managed-environment diagnostic-category, workspace-table-policy, and reader-role modules in `bicep/modules/logAnalyticsDiagnosticSettings.bicep`, `bicep/modules/logAnalyticsTablePolicies.bicep`, and `bicep/modules/workspaceRoleAssignment.bicep`.
- [X] T016 Wire the new diagnostic settings, table plans, 30-day retention, reader principal parameter, and dual-run-only legacy storage settings through `bicep/main.bicep`, `bicep/main.dev.bicepparam`, and `bicep/main.prod.bicepparam`.
- [ ] T017 Regenerate the compiled ARM output from `bicep/main.bicep` into `bicep/main.json` and document the new validation/parameter prerequisites in `bicep/README.md`.
- [ ] T018 Record successful `az deployment sub validate` and `what-if` output, including no ingress/secret/client-SDK expansion, in `docs/observability/centralized-logging-release-checklist.md`.

**Checkpoint**: Event primitives, host setup, test harness, and reviewable Azure routing artifacts exist; the legacy Parquet producer and readers are still intact.

---

## Phase 3: User Story 2 — Safely Capture Meaningful Events (Priority: P1)

**Goal**: Emit sparse, bounded, redacted structured worker evidence while preserving Grafana metrics and the legacy Parquet path during dual run.

**Independent Test**: Run normal, anomalous, repeated-failure, recovery, and secret-bearing input cases through the worker. Captured JSON has no normal-cycle duplicate, retains initial/recovery evidence, contains no secrets, and leaves metrics/Parquet behavior intact.

### Tests for User Story 2

- [X] T019 [P] [US2] Add fetch/route-index/publish outcome-seam and missing-tone-reason tests in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/CityCycleOutcomeTests.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/CityAnomalyClassifierTests.cs`.
- [X] T020 [P] [US2] Add startup/shutdown, input, route-index, anomaly, publish, and worker-cycle emitter tests in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/WorkerStructuredEventTests.cs`.
- [X] T021 [P] [US2] Add normal-cycle-zero-information-row, ten-identical-failures, periodic-reminder, recovery, and metric-continuity tests in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/StructuredLoggingVolumeTests.cs`.

### Implementation for User Story 2

- [X] T022 [US2] Add explicit route-index state, publish attempted/succeeded state, safe exception classification, and cycle correlation fields to `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Logging/CityCycleOutcome.cs`.
- [X] T023 [US2] Implement the deterministic single-reason missing-tone classifier in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Logging/CityAnomalyClassifier.cs` using `CityFetchResult`, source freshness, duplicate state, reconciliation counts, and publish outcome.
- [X] T024 [US2] Wire one `CycleId` per worker tick and emit the eleven v1 lifecycle/input/anomaly/publish/worker events through `StructuredEventEmitter` in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`.
- [X] T025 [P] [US2] Replace raw feed URL, request URI, and exception-object logging with safe event fields in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Cities/GtfsRtCity.cs`, `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Cities/MartaCity.cs`, and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Cities/NymtaCity.cs`.
- [X] T026 [P] [US2] Replace request-URI/error logging and demote routine success messages in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/RailRealtime/RailRealtimeAdapter.cs`, `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/SignalRHubPublisher.cs`, and `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/GtfsStatic/GtfsStaticLoader.cs`.
- [X] T027 [US2] Set production information/debug category filters that suppress per-tick noise without hiding structured events in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json` and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/appsettings.json`.
- [X] T028 [US2] Keep `EmitsTelemetry` limited to legacy Parquet behavior and verify new structured events cover every configured city in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/StructuredLoggingCityCoverageTests.cs`.
- [X] T029 [US2] Run the worker and Web API test projects from `specs/054-centralized-logging/quickstart.md` and record US2 results in `docs/observability/centralized-logging-release-checklist.md`.

**Checkpoint**: Worker events are safe and independently testable locally; no existing Grafana signal or Parquet dual-run evidence has been removed.

---

## Phase 4: User Story 1 — Investigate a Worker Anomaly (Priority: P1)

**Goal**: Let an investigator use city/time or existing Grafana panel context to find bounded centralized-log evidence and reproduce the query.

**Independent Test**: In a controlled routed environment, trigger a safe zero-tone anomaly, retrieve the one event by city/time and `CycleId`, show its reason/counts/publish state plus effective KQL, then repeat from Grafana context.

### Tests and Acceptance Assets for User Story 1

- [X] T030 [P] [US1] Create the scripted anomaly/city-time/Grafana-context acceptance fixture in `docs/observability/transitjazz-logs-us1-acceptance.md`.
- [X] T031 [P] [US1] Create the v1 event-name/reason/field reference from the captured contract in `skills/transitjazz-logs/references/event-contract.md`.
- [X] T032 [P] [US1] Create Basic-compatible, table/range/project/limit-bounded draft recipes for event ID, `CycleId`, city/time, zero tones, input, route index, publish, recovery, revision, and freshness in `skills/transitjazz-logs/references/kql-recipes.md`.

### Implementation for User Story 1

- [X] T033 [US1] Create the read-only investigation workflow, Grafana composition, context precedence, table-first presentation, JSON-on-request behavior, and query-display requirement in `skills/transitjazz-logs/SKILL.md`.
- [X] T034 [US1] Register the source skill for Codex, Claude, and OpenCode in `skills/_skill-sync/catalog.json` and add `skills/transitjazz-logs/.skill-sync/codex.json` only if an actual Codex UI asset is supplied.
- [ ] T035 [US1] Run `tools/sync-skills.ps1` and verify generated skill copies and catalog metadata from `skills/transitjazz-logs/` without editing generated copies directly.
- [ ] T036 [US1] Apply the reviewed routing only in a controlled environment, emit a safe anomaly/canary, capture the actual `ContainerAppConsoleLogs.Log` JSON shape, and attach the row/query evidence to `docs/observability/transitjazz-logs-us1-acceptance.md`.
- [ ] T037 [US1] Replace recipe placeholders only after T036 with the observed JSON parser/projections in `skills/transitjazz-logs/references/kql-recipes.md`.
- [ ] T038 [US1] Execute the city/time and Grafana-panel acceptance flow in under five minutes, record the effective context/KQL/table result, and document empty-result explanations in `docs/observability/transitjazz-logs-us1-acceptance.md`.

**Checkpoint**: A safe anomaly is explainable through read-only, reproducible Log Analytics evidence while Grafana remains the numeric symptom source.

---

## Phase 5: User Story 3 — Diagnose Access and Ingestion Problems (Priority: P2)

**Goal**: Supply a bounded read-only query path and `doctor` result that identifies the first unavailable layer without accepting credentials or altering Azure.

**Independent Test**: Exercise interface absence, identity/authentication, workspace, RBAC, connectivity, table/plan, ingestion-delay, Basic compatibility, minimal-query, and empty-result cases; every case returns one first failure and a secret-free next action.

### Tests and Acceptance Assets for User Story 3

- [X] T039 [P] [US3] Create the full first-failure `doctor` matrix and read-only mutation-rejection cases in `docs/observability/transitjazz-logs-doctor-matrix.md`.
- [X] T040 [P] [US3] Create safe table-default, JSON-output, direct-KQL-preservation, context-precedence, and result-limit acceptance cases in `docs/observability/transitjazz-logs-us3-acceptance.md`.

### Implementation for User Story 3

- [X] T041 [US3] Add the ordered interface/identity/workspace/RBAC/table-plan/freshness/minimal-query/empty-result diagnostic workflow to `skills/transitjazz-logs/references/doctor.md`.
- [X] T042 [US3] Enforce allowed tables, finite UTC ranges, Basic KQL exclusions, 1-100 limits, explicit-query preservation, defensive output redaction, and all mutation refusals in `skills/transitjazz-logs/SKILL.md`.
- [X] T043 [US3] If the preferred Azure Monitor interface fails the recorded Basic-query proof, implement the constrained caller-identity-only Search API fallback and its allow-list in `tools/transitjazz-logs-query/transitjazz-logs-query.ps1`; otherwise record the preferred-interface proof in `docs/observability/centralized-logging-baseline.md` and do not create a helper.
- [X] T044 [US3] If T043 creates the fallback, add rejection tests for arbitrary workspace/table/URL/header/method, unbounded range, unsupported KQL, and result limits in `tools/transitjazz-logs-query/tests/QueryGuard.Tests.ps1`.
- [X] T045 [US3] Add the selected preferred-interface or constrained-helper invocation and `BasicQueryUnsupported` Analytics-fallback decision path to `skills/transitjazz-logs/SKILL.md`.
- [ ] T046 [US3] Execute every `doctor` matrix and mutation-rejection case, with no credential output and no retry of persistent failures, in `docs/observability/transitjazz-logs-doctor-matrix.md`.
- [ ] T047 [US3] Execute bounded query, default table, JSON, copied Azure-link, explicit-overrides-context, and empty-result scenarios in `docs/observability/transitjazz-logs-us3-acceptance.md`.

**Checkpoint**: Investigation is query-only and diagnosable; it cannot become an Azure administration or credential-handling path.

---

## Phase 6: User Story 4 — Cut Over Without Losing Evidence (Priority: P1, Release-Dependent)

**Goal**: Prove the Azure route, retention, cost, queryability, and dual-run parity before disabling new Parquet writes; preserve historical evidence and defer destructive removal.

**Independent Test**: Complete the baseline-to-seven-day release matrix, including pre/post canaries, a zero-tone anomaly, representative failure parity, and day-one retention. Verify that an incomplete gate blocks sidecar disablement and leaves blobs unchanged.

### Implementation and Release Evidence for User Story 4

- [ ] T048 [P] [US4] Measure legacy console ingestion, identify noisy categories/messages, verify workspace/retention, estimate sparse-event ingestion/query scans, and record the result in `docs/observability/centralized-logging-baseline.md`.
- [ ] T049 [P] [US4] Record the separately approved feature-053 topology/artifact prerequisites and intended workspace-scoped reader principal in `docs/observability/centralized-logging-release-checklist.md`.
- [ ] T050 [US4] Supply the approved reader principal ID through `bicep/main.dev.bicepparam` and `bicep/main.prod.bicepparam`; verify deployment grants only workspace-scoped `Log Analytics Reader`.
- [ ] T051 [US4] Run Bicep build, subscription validation, and what-if for the selected environment using `bicep/main.bicep`, then append the immutable command/result evidence to `docs/observability/centralized-logging-release-checklist.md`.
- [ ] T052 [US4] Emit and record a timestamped safe pre-change console canary in `docs/observability/centralized-logging-release-checklist.md` before enabling the Azure Monitor diagnostic route.
- [ ] T053 [US4] Deploy the reviewed managed-environment destination, exact diagnostics, Basic/Analytics table policies, 30-day retention, and reader role from `bicep/main.bicep` while retaining `Logging__Telemetry__*` environment settings and Blob access.
- [ ] T054 [US4] Verify standard-table routing, plans, retention, and reader scope after deployment and record the evidence in `docs/observability/centralized-logging-release-checklist.md`.
- [ ] T055 [US4] Emit two fresh post-change markers, wait up to 90 minutes for diagnostic activation plus normal ingestion delay, and record standard-table KQL/streaming/Grafana evidence in `docs/observability/centralized-logging-release-checklist.md`.
- [ ] T056 [US4] Start the consecutive seven-day dual-run clock and record daily legacy/central status in `docs/observability/centralized-logging-release-checklist.md`.
- [ ] T057 [US4] Capture representative input-failure, publish-failure, zero-tone, normal-success, repeated-failure/recovery, and redaction evidence from both paths in `docs/observability/centralized-logging-release-checklist.md`.
- [ ] T058 [US4] Verify the day-one safe event remains queryable after day seven and record both table plans, retention, ingestion, scan volume, and query-latency review in `docs/observability/centralized-logging-release-checklist.md`.
- [ ] T059 [US4] Enforce every migration-gate state transition and document any block without disabling the sidecar in `docs/observability/centralized-logging-release-checklist.md` and `specs/054-centralized-logging/contracts/migration-gates.md`.
- [ ] T060 [US4] Only after T056-T059 pass, disable new Parquet writes through the dedicated dual-run setting in `bicep/main.bicep` and retain all existing Blob data/resources.
- [ ] T061 [US4] Observe and record one normal centralized-logs-only release, including unaffected Grafana metric/dashboard/alert behavior, in `docs/observability/centralized-logging-release-checklist.md`.
- [ ] T062 [US4] Audit every remaining telemetry storage/API/UI/tool/skill/configuration consumer before removal in `docs/observability/centralized-logging-removal-audit.md`.
- [ ] T063 [US4] After T062 and separate archival/deletion approval, remove only worker-side Parquet producer code, Blob/Parquet package references, and sidecar-specific metrics/tests from `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Logging/`, `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`, `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Metrics/`, and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/`.
- [ ] T064 [US4] After T062 and separate approval, remove the Parquet read API and DTOs from `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/EndpointGroups/TelemetryEndpoints.cs`, `src/ChefKnifeStudios.TransitJazz.Shared/TelemetryData/`, and `src/ChefKnifeStudios.TransitJazz.Shared/ApiEndpoints.cs`.
- [ ] T065 [US4] After T062 and separate approval, remove the retired telemetry client/UI and query tools from `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/EndpointsServices/TelemetryEndpointsService.cs`, `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/Telemetry.razor`, `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TelemetryTable.razor`, `tools/telemetry-query-tool/`, and `tools/telemetry-mcp/`.
- [ ] T066 [US4] After T062 and separate approval, retire the legacy Parquet-only `mj-data-explorer` source/generated skill and legacy bridge registration in `skills/mj-data-explorer/`, `skills/_skill-sync/catalog.json`, `.mcp.json`, and `.codex/config.toml` without reading or exposing credential values.
- [ ] T067 [US4] After T062 and separate archival/deletion approval, remove only confirmed Blob telemetry resources/role assignments/configuration from `bicep/main.bicep`, `bicep/modules/telemetryStorage.bicep`, `bicep/main.json`, and the Web API/worker `appsettings*.json` files while leaving historical blobs intact.
- [ ] T068 [US4] Record this design as superseding the unchanged-Parquet statements and sidecar metrics contract in `specs/053-worker-observability/plan.md`, `specs/053-worker-observability/data-model.md`, and `specs/053-worker-observability/contracts/metrics-contract.md`.

**Checkpoint**: Centralized logs are proven before any retirement. Legacy writes are disabled only on passing evidence; deletion remains a separately authorized decision.

---

## Phase 7: Polish and Cross-Cutting Verification

**Purpose**: Ensure the completed release remains documented, secure, reproducible, and ready for review without creating a commit.

- [ ] T069 [P] Reconcile the final event, routing, skill, and removal evidence with `docs/AZURE_CENTRALIZED_LOGGING_DESIGN_DOCUMENT.md` and `specs/054-centralized-logging/contracts/`.
- [X] T070 [P] Run all worker/Web API tests named in `specs/054-centralized-logging/quickstart.md` and attach results to `docs/observability/centralized-logging-release-checklist.md`.
- [ ] T071 [P] Regenerate/synchronize Bicep and skills using `bicep/main.bicep` and `tools/sync-skills.ps1`, then review generated outputs for no credentials or manual edits.
- [ ] T072 Review the complete release evidence, removal guard, source redaction, and constitutional prerequisites in `docs/observability/centralized-logging-release-checklist.md` before requesting human approval for any production transition.

---

## Dependencies and Execution Order

### Phase Dependencies

- **Phase 1** has no dependencies.
- **Phase 2** depends on Phase 1 and blocks every user story.
- **US2 (Phase 3)** depends on Phase 2; it supplies the observable v1 data required by the remaining stories.
- **US1 (Phase 4)** depends on US2 and the reviewed routing artifacts from Phase 2.
- **US3 (Phase 5)** depends on the US1 skill source/recipes and the chosen read-only query interface.
- **US4 (Phase 6)** depends on US1 and US3 acceptance as well as US2 capture; its seven-day release gate intentionally executes last.
- **Polish (Phase 7)** depends on all desired implementation/release tasks. It does not authorize cutover, cleanup, or a commit.

### User Story Dependencies

```text
Foundation
    └── US2: safe, sparse event capture
          └── US1: anomaly investigation
                └── US3: read-only doctor and access diagnosis
                      └── US4: routing proof, dual run, controlled cutover, later retirement
```

### Parallel Opportunities

- **Setup**: T001-T004 can proceed in parallel.
- **Foundation**: T009-T011 can run concurrently after T005-T008; T014 and T015 can proceed in parallel once their shared Bicep inputs are agreed.
- **US2**: T019-T021 are parallel tests; T025 and T026 are independent source-file safety audits after T024's emitter contract is stable.
- **US1**: T030-T032 are independent acceptance/reference documents; run them in parallel before T033.
- **US3**: T039 and T040 are independent acceptance matrices.
- **US4**: T048 and T049 can proceed in parallel; later release actions are intentionally serialized by evidence gates.
- **Polish**: T069-T071 can proceed in parallel.

## Parallel Examples

### User Story 2

```text
Task: "Add outcome/classifier tests in src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/CityCycleOutcomeTests.cs and CityAnomalyClassifierTests.cs"
Task: "Add worker event tests in src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/WorkerStructuredEventTests.cs"
Task: "Add logging-volume tests in src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/StructuredLoggingVolumeTests.cs"
```

### User Story 1

```text
Task: "Create the scripted acceptance fixture in docs/observability/transitjazz-logs-us1-acceptance.md"
Task: "Create skills/transitjazz-logs/references/event-contract.md"
Task: "Create skills/transitjazz-logs/references/kql-recipes.md"
```

### User Story 3

```text
Task: "Create docs/observability/transitjazz-logs-doctor-matrix.md"
Task: "Create docs/observability/transitjazz-logs-us3-acceptance.md"
```

## Implementation Strategy

### MVP: Safe Capture Plus Anomaly Investigation

1. Complete Setup and Foundation.
2. Complete US2, retaining the Parquet sidecar and Grafana metrics.
3. Complete US1 in a controlled routed environment and validate a safe anomaly with reproducible KQL.
4. Stop for review: this delivers meaningful, safe diagnostic evidence without authorizing a production cutover.

### Incremental Delivery

1. Setup + Foundation → typed contracts, test harness, and deployable-but-unapplied routing artifacts.
2. US2 → sparse, redacted v1 worker events and existing metrics/Parquet coexistence.
3. US1 → read-only anomaly evidence linked from Grafana context.
4. US3 → bounded query/doctor/refusal behavior and proven Basic fallback decision.
5. US4 → measured production routing and seven-day evidence before any sidecar disablement or later cleanup.

## Task Validation

- All 72 tasks use `- [ ]`, sequential task IDs, exact paths, and `[US#]` only in user-story phases.
- Tests are included because the specification explicitly requires automated and documented acceptance evidence.
- Tasks T060-T068 are release-gated and must not run merely because earlier code tasks completed.
- No task authorizes `git commit`; leave changes for human review and commit.
