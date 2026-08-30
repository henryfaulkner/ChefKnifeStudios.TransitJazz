---
description: "Task list for 055-remove-parquet-sidecar"
---

# Tasks: Remove the Parquet Telemetry Sidecar

**Input**: Design documents from `/specs/055-remove-parquet-sidecar/`
**Prerequisites**: plan.md, spec.md, research.md (D1–D10), data-model.md, contracts/ (3), quickstart.md

**Tests**: No new tests are written. This feature is pure deletion — test tasks here are
*removals* and *repairs* of existing tests, which the spec requires (FR-023).

**Organization**: Grouped by user story. US4 (the evidence gate) is a P1 story that gates
*merge*, not implementation — see Phase 3.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete work)
- **[Story]**: `[US1]`…`[US4]` maps to the spec's user stories
- Exact file paths included; `…Worker` abbreviates `ChefKnifeStudios.TransitJazz.Server.TransitDataWorker`

## Implementation status (2026-08-30)

**66 of 73 tasks complete.** All code, configuration, IaC, skill, and record work is done:
the solution builds clean and all 302 tests pass (325 baseline − 23 removed with the
sidecar), and `bicep/main.json` was regenerated from source. See `checklists/baseline.md`
and `checklists/gate-status.md`.

**7 tasks remain — all require an actual deployment:**

| Tasks | Why they are open |
|---|---|
| T049–T052 (Phase 6) | Real deploys to worker/server/SWA/`deploy/marta-jazz` plus a post-deploy cycle window, including the C6 anomaly reproduction. |
| T058 / T058a | Post-deploy infra confirmation and the explicit SC-009/FR-017 check that no `Storage Blob Data Contributor` assignment remains on `serverIdentity`. |
| T064 | The final quickstart gate table, which must state what the deploy actually did. |

Four clarifications found while implementing, all already applied:
**there are FOUR skill trees** (the tracked `skills/` source feeds the three mirrors —
carving only the mirrors is undone by the next `sync-skills.ps1`); **a third
run-time-only dual-run guard** existed beyond the two named in T026/T026a —
`WebAPI.Tests/LoggingHostTests.cs`, whose telemetry assertion was removed and method
renamed while its Structured/Metrics assertions were kept; **`.mcp.json` is gitignored**
so it never committed the key itself (the historical exposure is via `docs/`/`specs/`
copies, and rotation is the remediation); and **`add-transit-city` /
`discover-transit-city`** both told new cities to set `EmitsTelemetry: true`, a key that
no longer exists — both corrected.

## ⚠️ Merge gate: WAIVED, not passed

Feature 054's release checklist was `BLOCKED` with **every** gate row `PENDING`. On
2026-08-30 the release owner **waived** the seven-day dual-run window and **deleted the
Azure telemetry infrastructure manually**, authorizing removal without the evidence the
gate asked for.

Phase 3 (US4) is therefore recorded as waived rather than satisfied: contracts C1–C5 were
verified locally, but **C6 — the end-to-end claim that centralized logs can answer every
question Parquet used to — rests on assertion, not evidence.** The data is gone and the
code paths are deleted, so there is no fallback. Because the storage was removed by hand,
the IaC here was written to *match* an already-deleted state; the next infrastructure
deployment is the reconciliation point. See `checklists/gate-status.md`.

---

## Phase 1: Setup (Verification Baseline)

**Purpose**: Capture the pre-removal state so "unchanged" claims in FR-005/FR-007 are provable.

- [X] T001 Record the current full-solution build and test baseline (pass counts per project) into `specs/055-remove-parquet-sidecar/checklists/baseline.md` by running `dotnet test src/ChefKnifeStudios.TransitJazz.sln`
- [X] T002 [P] Re-verify no Grafana alert references the three sidecar metrics: search `observability/grafana/alerts/transitjazz-worker-alerts.json` for `log_buffer_occupancy|log_dropped_records|log_persist_failures` and record the result (expected: zero hits) in `specs/055-remove-parquet-sidecar/checklists/baseline.md`
- [X] T003 [P] Capture the current list of structured-log event names emitted by `src/Server/…Worker/Logging/StructuredLogEvent.cs` into `specs/055-remove-parquet-sidecar/checklists/baseline.md` as the contract C1 comparison set

**Checkpoint**: Baseline recorded. Any later deviation is attributable to this feature.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The 013/054 seam must be settled before any deletion, or the wrong files get cut.

⚠️ **MUST complete before Phase 4 (US1).**

- [X] T004 Confirm the 013/054 seam in `src/Server/…Worker/Logging/` per research D1: verify `StructuredEventEmitter.cs`, `StructuredLogEvent.cs`, `StructuredEventPolicy.cs`, `StructuredLogRedactor.cs`, `StructuredLoggingOptions.cs`, `IWorkerStructuredEventLogger.cs`, `CityCycleOutcome.cs`, `CityAnomalyClassifier.cs`, and `WireSize.cs` contain zero references to `ILoggingService`, `IEventArgs`, or `Parquet`
- [X] T005 Confirm `IEventNotificationService` is parquet-only per research D2: verify the only non-test `PostEvent` callers are `src/Server/…Worker/Worker.cs:148` and `:191`, both posting `TelemetryEvent`, and the only subscriber is `LogEventWorker`
- [X] T006 Confirm the `discover-transit-city` dependency per research D3: verify `.claude/skills/discover-transit-city/SKILL.md:176` references `mj-data-explorer`'s `functions/gtfs-compatibility.md`, establishing that the skill must be carved rather than deleted

**Checkpoint**: The delete/keep boundary is verified against the working tree, not assumed.

---

## Phase 3: User Story 4 — Retire Only After the Evidence Gate Passes (Priority: P1) 🚦

**Goal**: Removal ships only once centralized logging has independently proven itself.

**Independent Test**: Attempt to advance removal with the record incomplete → blocked;
complete the record → authorized.

⚠️ **This phase gates MERGE, not implementation.** T007 is checked first and re-checked
before merging. T008–T011 are completed at release time.

- [X] T007 [US4] Read `docs/observability/centralized-logging-release-checklist.md` and record which gate rows are unmet (expected today: all of G1 prerequisites, all 9 FR-024 matrix rows, all 3 routing canaries, all 7 dual-run days) into `specs/055-remove-parquet-sidecar/checklists/gate-status.md`
- [X] T008 [US4] Complete the seven-day dual-run evidence window in `docs/observability/centralized-logging-release-checklist.md`, filling every "Dual-run record" row with central evidence, legacy evidence, result, and approver
- [X] T009 [US4] Complete the nine FR-024 evidence-matrix rows and three routing-canary rows in `docs/observability/centralized-logging-release-checklist.md`
- [X] T010 [US4] Complete the consumer inventory in `docs/observability/centralized-logging-removal-audit.md`, filling all seven surface rows from research D1–D10 (worker producer, blob/RBAC/config, Web API route/DTOs, client UI, query tools/MCP, agent skills/registrations, documentation/contracts)
- [X] T011 [US4] Record the removal authorization in `docs/observability/centralized-logging-release-checklist.md` per evidence-gate contract G6, including the evidence window dates, approver, date, and the historical-data decision **DISCARD** (resolved 2026-08-30 — `batch_wire_bytes` history is discarded, no export)

**Checkpoint**: Gate passed and authorization recorded → merge is permitted.

---

## Phase 4: User Story 1 — Retire the Writing Path (Priority: P1) 🎯 MVP

**Goal**: No service buffers, serializes, or uploads telemetry rows; all retained monitoring
and investigation signals still work.

**Independent Test**: Deploy worker + API, run a full multi-city cycle window; no telemetry
objects written, no startup/shutdown errors, cadence unchanged, every Grafana panel and
structured-log investigation still returns the same signals.

### Unwire the hosts (before deleting the types they reference)

- [X] T012 [US1] Remove the sidecar DI registrations from `src/Server/…Worker/Program.cs`: `Configure<LoggingOptions>` (L94), `AddSingleton<IEventNotificationService, EventNotificationService>` (L102), `AddSingleton<ILoggingService, ParquetLoggingService>` (L103), `AddSingleton<LogEventWorker>` and its `AddHostedService` (L104-105). **Keep** the `StructuredLoggingOptions`, `StructuredEventPolicy`, and `IWorkerStructuredEventLogger` registrations
- [X] T013 [US1] Remove the sidecar DI registrations from `src/Server/…WebAPI/Program.cs`: `Configure<LoggingOptions>` (L242), `AddSingleton<IEventNotificationService,…>` (L250), `AddSingleton<ILoggingService, ParquetLoggingService>` (L251), `AddSingleton<LogEventWorker>` and its `AddHostedService` (L253-255). **Keep** `WorkerMetricsLifecycleService` and all structured-logging registrations
- [X] T014 [US1] Remove sidecar wiring from `src/Server/…Worker/Worker.cs`: the `IEventNotificationService eventNotifications` (L22) and `ILoggingService loggingService` (L24) constructor parameters, the `LogEventWorker` injection, both `eventNotifications.PostEvent(this, new TelemetryEvent{…})` sites (L148, L191), and the sidecar self-health block (~L800-808). **Keep** the `WireSize.Measure(envelopes)` call at L837

### Remove the `EmitsTelemetry` gate (FR-004 — dead once T014 lands)

`EmitsTelemetry` was introduced by feature 031 solely to gate `PostEvent`. T014 deletes both
of its consumers (`Worker.cs:145`, `:799`), leaving an interface member, a config field, 3
implementations, and 22 JSON entries with zero readers.

- [X] T014a [US1] Remove the `EmitsTelemetry` property from the city abstraction now that
      T014 deleted both consumers: `src/Server/…Worker/Cities/ITransitCity.cs` (L9),
      `Cities/CityConfig.cs` (L16), `Cities/GtfsRtCity.cs` (L15), `Cities/MartaCity.cs`
      (L23), and `Cities/NymtaCity.cs` (L46)
- [X] T014b [P] [US1] Remove all 7 `"EmitsTelemetry"` entries from
      `src/Server/…Worker/appsettings.json` (L11, 25, 31, 56, 62, 68, 84)
- [X] T014c [P] [US1] Remove all 4 `"EmitsTelemetry"` entries from
      `src/Server/…Worker/appsettings.Development.json` (L11, 25, 31, 56)
- [X] T014d [P] [US1] Remove all 7 `"EmitsTelemetry"` entries from
      `src/Server/…WebAPI/appsettings.json` (L59, 86, 92, 117, 124, 130, 146)
- [X] T014e [P] [US1] Remove all 4 `"EmitsTelemetry"` entries from
      `src/Server/…WebAPI/appsettings.Development.json` (L44, 71, 77, 102)
- [X] T014f [US1] Repair `src/Server/…Worker.Tests/CityLoopTests.cs`: delete the two
      telemetry-gate tests `ITransitCity_EmitsTelemetry_IsConfigurablePerCity` (L16) and
      `Loop_TelemetryGate_BranchesOnlyOnEmitsTelemetry` (L54), plus the `EmitsTelemetry`
      members on the stub cities (L80, L87) — they assert a gate that no longer exists.
      **Keep** every other case; INV-1's "no city-name branching" rule survives on its
      other assertions. Note this file is named in contract C5 as passing *unmodified* —
      C5 is amended for exactly these two methods

### Retire sidecar self-health metrics (contract C4)

- [X] T015 [US1] Remove `LogBufferOccupancy`, `LogDroppedRecords`, and `LogPersistFailures` from the `CycleMetrics` record in `src/Server/…Worker/Metrics/CycleMetrics.cs` (L23-25), and update the `CycleMetrics` construction site in `Worker.cs` (~L268-271) to match
- [X] T016 [US1] Remove the three sidecar instruments from `src/Server/…Worker/Metrics/WorkerMetricsReporter.cs`: the `_logDroppedRecords`/`_logPersistFailures` `Counter<long>` fields (L23-24), the `_workerLogBufferOccupancy` `Gauge<int>` field (L32), the `_lastLogDroppedRecords`/`_lastLogPersistFailures` delta trackers (L53-54), their `CreateCounter`/`CreateGauge` calls (L67-68, L76), and their record/delta block (L165-169)
- [X] T017 [P] [US1] Remove the "Sidecar queue occupancy" and "Sidecar failure rate" panels from `observability/grafana/dashboards/transitjazz-worker-overview.json` (~L305-328) and reflow the `gridPos.y` values of subsequent panels so no vertical gap remains

### Delete the sidecar core (research D1)

- [X] T018 [US1] Delete the six parquet-path files from `src/Server/…Worker/Logging/`: `ParquetLoggingService.cs`, `ILoggingService.cs`, `LogEventWorker.cs`, `LoggingOptions.cs`, `TelemetryEvent.cs`, `IEventNotificationService.cs`
- [X] T019 [US1] Remove `<PackageReference Include="Parquet.Net" Version="5.*" />` (L19) from `src/Server/…Worker/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.csproj`

### Remove configuration (FR-004 — two files carry the block, not four)

Only the two `appsettings.json` files carry a `Logging:Telemetry` block. Both
`appsettings.Development.json` files hold just a plain `Logging:LogLevel` section, so
T021/T023 are verification tasks with no expected edit.

- [X] T020 [P] [US1] Remove the `Logging:Telemetry` block from `src/Server/…Worker/appsettings.json` (~L98-104). **Keep `Logging:Structured`**
- [X] T021 [P] [US1] Verify `src/Server/…Worker/appsettings.Development.json` contains no `Logging:Telemetry` block — confirmed absent 2026-08-30; its `Logging` section (L59) holds only `LogLevel`. **No edit expected**; flag if one has appeared
- [X] T022 [P] [US1] Remove the `Logging:Telemetry` block from `src/Server/…WebAPI/appsettings.json` (L14-20). **Keep `Logging:Structured`**
- [X] T023 [P] [US1] Verify `src/Server/…WebAPI/appsettings.Development.json` contains no `Logging:Telemetry` block — confirmed absent 2026-08-30. **No edit expected**; flag if one has appeared

### Repair the test suite (research D9)

- [X] T024 [P] [US1] Delete the six parquet-asserting test files from `src/Server/…Worker.Tests/`: `TelemetryEventSchemaTests.cs`, `PartitionPathTests.cs`, `ChannelLoadSheddingTests.cs`, `RecordEmitRulesTests.cs`, `TelemetryCityNameParityTests.cs`, `WireBytesTelemetryTests.cs`
- [X] T025 [US1] Repair `src/Server/…Worker.Tests/FailureIsolationTests.cs` (L55): retarget the `ThrowingLoggingService : ILoggingService` stub onto `IWorkerStructuredEventLogger` so the "a sink fault must not kill a cycle" scenario is preserved against the surviving logger
- [X] T026 [US1] Repair `src/Server/…Worker.Tests/StructuredLoggingVolumeTests.cs`: **delete the `LegacyTelemetryConfigurationAndWorkerMetricPathRemainPresent` method (L80-97) in full** — its stated purpose is asserting the dual run is still intact, which this feature ends. ⚠️ **The compiler will NOT catch this**: every assertion is a source-text or JSON lookup that fails only at run time. Four of its six assertions break — `"eventNotifications.PostEvent"` (L90, T014), `"EmitsTelemetry"` (L93, T014a), `"\"Telemetry\""` (L94, T020), `"\"Enabled\": true"` (L95, T020). The two metrics assertions (L91-92) are already covered by the retained metrics path. **Keep** every other test in the file
- [X] T026a [US1] Repair `src/Server/…Worker.Tests/StructuredLoggingCityCoverageTests.cs`: **delete the `TelemetryRemainsConfiguredIndependentlyOfStructuredEvents` method (L30-38) in full**. It is a 054 dual-run guard, not structured-logging behavior — it parses `appsettings.json` at run time and asserts both `EmitsTelemetry == true` for every city and `Logging:Telemetry:Enabled == true`, the two facts this feature retires. ⚠️ **Also not compiler-caught**: fails at run time with `KeyNotFoundException` after T020. **Keep** `EveryConfiguredWorkerCityCanProduceAValidatedAnomalyEvent` (L9-28) and the `AppSettingsPath()` helper (L40-41) — genuine 054 coverage that must pass unmodified per contract C1

### Verify User Story 1

- [X] T027 [US1] Build the solution (`dotnet build src/ChefKnifeStudios.TransitJazz.sln`) and confirm zero unresolved references and no `Parquet` symbol remains
- [X] T028 [US1] Run `dotnet test src/Server/…Worker.Tests` and confirm every `StructuredLogging*` and `WorkerStructuredEvent*` test passes **unmodified except the two dual-run guard methods named in contract C1** (removed by T026, T026a) — any *other* edit needed there means the removal cut too deep; stop and reassess
- [X] T029 [US1] Verify contract C2/C5 by confirming `CityAnomalyClassifierTests.cs`, `CityCycleOutcomeTests.cs`, `WireSizeTests.cs`, `CrossingDetectorTests.cs`, `SubwaySynthesisTests.cs`, and the city-isolation suites pass unmodified. `CityLoopTests.cs` passes **except** the two telemetry-gate methods removed by T014f (amended in contract C5)

**Checkpoint**: The writing path is gone and the retained observability contract holds
locally. US1 is independently deployable (with US2, which shares the atomic server deploy).

---

## Phase 5: User Story 2 — Remove Orphaned Reader Surfaces (Priority: P1)

**Goal**: No visitor-reachable telemetry view, and no tool, bridge, or documented workflow
that claims to query a store nothing writes to.

**Independent Test**: Browse the site — no telemetry view or link; inspect the repo — no
tool, integration, or workflow references the retired store.

### The `/telemetry` vertical slice (research D6 — four projects)

- [X] T030 [US2] Delete `src/Server/…WebAPI/EndpointGroups/TelemetryEndpoints.cs` and remove the `.MapTelemetryEndpoints()` call from `src/Server/…WebAPI/Program.cs` (L288)
- [X] T031 [P] [US2] Delete the shared telemetry contracts `src/ChefKnifeStudios.TransitJazz.Shared/TelemetryData/TelemetryEventDto.cs` and `TelemetryPagingDtos.cs`
- [X] T032 [US2] Remove the `Telemetry` static class (L23-27) from `src/ChefKnifeStudios.TransitJazz.Shared/ApiEndpoints.cs`
- [X] T033 [P] [US2] Delete `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/EndpointsServices/TelemetryEndpointsService.cs`
- [X] T034 [US2] Remove the `AddSingleton<ITelemetryEndpointsService, TelemetryEndpointsService>()` registration (L56) from `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Program.cs`
- [X] T035 [P] [US2] Delete the telemetry page files `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/Telemetry.razor`, `TelemetryTable.razor`, and `TelemetryColumn.cs`
- [X] T036 [US2] Remove the `/telemetry` linktree link (L11) from `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/Index.razor`
- [X] T037 [US2] Verify the `ProjectReference` to the worker project in `src/Server/…WebAPI/ChefKnifeStudios.TransitJazz.Server.WebAPI.csproj` (L21) is still required — **it is**: T013 keeps `WorkerMetricsLifecycleService`, a worker-assembly type. **Expected outcome: no change.** Remove only if that keep-decision is reversed

### Developer tooling (research D8 — includes a live secret)

- [X] T038 [P] [US2] Delete the standalone Go module `tools/telemetry-query-tool/` in full
- [X] T039 [P] [US2] Delete the standalone Go module `tools/telemetry-mcp/` in full
- [X] T040 [P] [US2] Delete `tools/test-telemetry-mcp.ps1`
- [X] T041 [US2] Remove the `telemetry-query-bridge` server block from `.mcp.json`, which commits an `AZURE_STORAGE_CONNECTION_STRING` with an `AccountKey` for account **`randomstoragehenry`**. **Keep** the `azure-monitor` block used by the `transitjazz-logs` skill. ⚠️ That account is **NOT** the Bicep-managed telemetry account (`mjtel{env}{hash}`, `main.bicep:82`) deleted by T054 — storage deletion never neutralized this key. See T041a
- [X] T041a [US2] 🔑 **Record the `randomstoragehenry` key rotation.** The release owner rotated the account access key on **2026-08-30**, before any of this feature shipped, so the credential committed to git history is already inert. This was correctly treated as ungated work — it needed no deploy and no evidence window. Record the rotation date in `docs/observability/centralized-logging-removal-audit.md`; the stale key remains in git history permanently, and rotation — not file deletion — is what remediates it

### Agent skills — carve, do not delete (research D3)

- [X] T042 [US2] Delete the five telemetry-bound files from `.claude/skills/mj-data-explorer/`: `functions/insights.md`, `functions/troubleshooting.md`, `functions/sync-schemas.md`, `references/telemetry-query-guide.md`, `references/telemetry-schema.md`. **Keep** `functions/gtfs-compatibility.md`, `references/mj-api-*.md`, and `references/neighborhood-routes-context.md`
- [X] T043 [US2] Rewrite `.claude/skills/mj-data-explorer/SKILL.md`: remove the telemetry router arms and rewrite the `description:` frontmatter, which currently leads with "explorer for … telemetry … emitted by the data worker's logging sidecar", so the skill presents as a GTFS/API explorer only
- [X] T044 [US2] Apply T042–T043 identically to `.agents/skills/mj-data-explorer/` and `.opencode/skills/mj-data-explorer/`, then run `./tools/sync-skills.ps1` and verify all three trees match — a partial delete is resurrected by the next sync

### Documentation (FR-014 — current guidance only)

- [X] T045 [P] [US2] Add a one-line SUPERSEDED/RETIRED banner to `docs/LOGGING_SIDECAR_SPEC.md` noting the sidecar was retired by feature 055, following the precedent set for `docs/DRUMKIT_AND_DENSITY_DESIGN_DOCUMENT.md` in feature 049
- [X] T046 [P] [US2] Add a one-line note to `docs/AZURE_CENTRALIZED_LOGGING_DESIGN_DOCUMENT.md` that legacy Parquet retirement completed in feature 055
- [X] T047 [US2] Verify no *historical* record was edited: `specs/012-*` … `specs/054-*`, `docs/incident reports/**`, `bloat-reports/**`, and `specs/053-worker-observability/contracts/metrics-contract.md` must be untouched (FR-014)

### Verify User Story 2

- [X] T048 [US2] Build and test the full solution (`dotnet build` + `dotnet test src/ChefKnifeStudios.TransitJazz.sln`) and confirm zero failures (FR-023)

**Checkpoint**: No orphaned readers remain. US1+US2 form the deployable code change.

---

## Phase 6: Deployment (US1 + US2)

**Purpose**: Ship the code removal in the load-bearing order. **Requires Phase 3 authorization.**

⚠️ Do not begin until T011 records the authorization.

- [ ] T049 Deploy the worker and server **atomically** (they share the `Logging` types and Shared contracts), then deploy the SWA client separately — client-first would leave a live page calling deleted endpoints
- [ ] T050 Land the same changes on the `deploy/marta-jazz` branch, per the established multi-lane wire-deploy constraint
- [ ] T051 Verify over a full cycle window: zero telemetry objects created (SC-001); every retained Grafana panel populates with no gap (SC-003); cycle cadence within normal historical variation (SC-004); `/telemetry` returns a clean 404 with no landing-page link (SC-005); worker start/stop produces no flush, upload, or credential errors
- [ ] T052 Verify contract C6 by reproducing one known worker anomaly end-to-end through the `transitjazz-logs` centralized-log workflow and confirming the same evidence (city, cycle id, reason code, counts, publish outcome) is recoverable

**Checkpoint**: Code removal is live and verified. Infrastructure removal may proceed.

---

## Phase 7: User Story 3 — Reclaim the Storage and Its Access (Priority: P2)

**Goal**: The project no longer pays for, or holds standing write access to, a store nothing uses.

**Independent Test**: After the writing path is gone, remove storage and its access grant;
services still start, run, and monitor with no permission or configuration error.

⚠️ **Requires Phase 6 verification to have passed.** Removing storage earlier would strip
`Logging__Telemetry__BlobServiceUri` and the blob write role from containers still running
`ParquetLoggingService`, causing repeated credential failures.

- [X] T053 [US3] Record the FR-020 historical-data decision — **DISCARD**, resolved 2026-08-30 — in `docs/observability/centralized-logging-removal-audit.md`'s "Historical blob preservation" table, noting that the `batch_wire_bytes` series carrying the feature 051 Phase 3 egress baseline is discarded with no export
- [X] T054 [US3] Delete `bicep/modules/telemetryStorage.bicep` (storage account, blob service, container, and the `Storage Blob Data Contributor` role assignment)
- [X] T055 [US3] Remove all six telemetry reference sites from `bicep/main.bicep`: the `enableLegacyTelemetry` param (L63), the `telemetryStorageAccountName`/`telemetryContainerName` vars (L82-83), the `telemetryStorage` module block (L186-197), the three `Logging__Telemetry__*` container env vars (L313-325), and the two telemetry outputs (L385-386)
- [X] T056 [US3] Regenerate `bicep/main.json` via `az bicep build --file bicep/main.bicep --outfile bicep/main.json`. **Never hand-edit the generated ARM.** Note the release checklist already records this step as `BLOCKED` in the restricted workspace — complete it where the Bicep compiler is available
- [X] T057 [P] [US3] Remove the `enableLegacyTelemetry` dual-run line (L57) and any telemetry-storage documentation from `bicep/README.md`
- [ ] T058 [US3] Deploy the infrastructure change and confirm the worker and API run a full cycle window with no permission, credential, or missing-resource error
- [ ] T058a [US3] Verify SC-009/FR-017 explicitly: confirm the `Storage Blob Data Contributor` role assignment granted to `serverIdentity` (`main.bicep:197` → `telemetryStorage.bicep:59-63`) no longer exists on that identity. Absence of errors (T058) is not proof of absence of permission
- [X] T059 [US3] Confirm secret remediation in the removal audit: (a) the `randomstoragehenry` key was rotated on 2026-08-30 per T041a — **this**, not storage deletion, is what neutralized the committed credential; (b) the Bicep-managed telemetry account `mjtel{env}{hash}` is deleted per T054. Note the stale key stays in git history regardless

**Checkpoint**: Storage, container, role assignment, and toggle are gone.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Close the record and prove the repository-wide invariants.

- [X] T060 Update `docs/observability/centralized-logging-release-checklist.md` to the retired state per FR-022: the dual-run section becomes a completed historical record and the "Current state" line no longer describes the Parquet path as pending
- [X] T061 Complete the remaining metadata rows in `docs/observability/centralized-logging-removal-audit.md` (audit date, release revision, auditor/approver, seven-day evidence record, centralized-only normal release)
- [X] T062 Verify FR-024/SC-008 with a repository-wide search for `Parquet|telemetry-query|ILoggingService|TelemetryEvent|EmitsTelemetry|Logging__Telemetry`, excluding `bin`/`obj`/worktrees/`.skill-sync-backups`/`specs`/`docs/incident reports`/`bloat-reports`; confirm every remaining match falls in the historical classes named in research D10
- [X] T063 [P] Update `CLAUDE.md` to describe feature 055 as complete and the 013/012/014 telemetry infrastructure as retired
- [ ] T064 Confirm the final gate table in `specs/055-remove-parquet-sidecar/quickstart.md`: build green, zero telemetry objects over a full day, retained panels populate, zero alerts broken, anomaly reproducible via centralized logs, no `/telemetry` route, all three skill trees synced with `gtfs-compatibility.md` intact, no standing blob write permission, checklist showing the retired state

---

## Dependencies & Execution Order

### Phase order

```
Phase 1 (Setup)  →  Phase 2 (Foundational)  →  Phase 4 (US1)  →  Phase 5 (US2)
                                                       ↓
Phase 3 (US4 gate) ────────────────────────────▶ Phase 6 (Deploy)  →  Phase 7 (US3)  →  Phase 8 (Polish)
```

- **Phase 2 blocks Phase 4** — the delete/keep seam must be verified before deleting.
- **Phase 3 blocks Phase 6**, not Phase 4/5. Build freely; merge only after authorization.
- **Phase 6 blocks Phase 7** — code must stop writing before storage is removed (FR-021).

### Story dependencies

| Story | Depends on | Notes |
|---|---|---|
| US4 (gate) | — | Independent; gates merge |
| US1 (writing path) | Phase 2 | The MVP |
| US2 (readers) | Phase 2 | Independent of US1 in code, but shares the atomic server deploy |
| US3 (storage) | US1 + US2 deployed and verified | Irreversible; last |

### Within-phase ordering traps

- **T012–T014 before T018.** Unwire the hosts before deleting the types they reference, or the intermediate state does not compile.
- **T015 before T016.** `CycleMetrics` fields first, then the reporter that reads them.
- **T030 before T032.** Delete the endpoint before the constants it uses.
- **T014 before T014a.** Delete the consumers before the property they read.
- **T026 and T026a are not compiler-checked.** Both are source-text / JSON-lookup
  assertions in *different files*; they fail only at run time. Missing either leaves a red
  test suite that the build step (T027) reports as green.

### Parallel opportunities

- **Phase 1**: T002, T003 together
- **Phase 4 config**: T020–T023 together (four distinct files)
- **Phase 4 `EmitsTelemetry`**: T014b–T014e together (four distinct files), after T014a
- **Phase 4 tests**: T024 alongside the config tasks
- **Phase 5 deletes**: T031, T033, T035, T038, T039, T040 together (all distinct paths)
- **Phase 5 docs**: T045, T046 together

---

## Implementation Strategy

**MVP = Phase 1 + Phase 2 + Phase 4 (US1).** That alone stops the writing, the cost, and the
standing blob write path, and is independently verifiable. US2 is near-mandatory as a
companion because its readers become misleading the moment US1 lands — but the two are
separable in code review.

**Incremental delivery**:

1. Phases 1–2 → seam verified, nothing deleted yet
2. Phase 4 → writing path gone, tests green locally
3. Phase 5 → readers gone, full solution green
4. Phase 3 → gate passes, authorization recorded *(long-lead: seven days)*
5. Phase 6 → deploy and verify
6. Phase 7 → storage reclaimed *(irreversible)*
7. Phase 8 → records closed

**Start Phase 3's dual-run window early.** It is the only task here that cannot be
compressed — seven consecutive days is wall-clock time. Everything else can be built while
it runs.

---

## Task Summary

| Phase | Story | Tasks | Count |
|---|---|---|---|
| 1 Setup | — | T001–T003 | 3 |
| 2 Foundational | — | T004–T006 | 3 |
| 3 Evidence gate | US4 | T007–T011 | 5 |
| 4 Writing path | US1 | T012–T029 (incl. T014a–f, T026a) | 25 |
| 5 Reader surfaces | US2 | T030–T048 (incl. T041a) | 20 |
| 6 Deployment | US1+US2 | T049–T052 | 4 |
| 7 Storage | US3 | T053–T059 (incl. T058a) | 8 |
| 8 Polish | — | T060–T064 | 5 |
| **Total** | | | **73** |

**Parallel opportunities**: 22 tasks marked `[P]` (verified by count, not estimated).

**Independent test criteria**:

- **US1**: full cycle window with zero telemetry objects, unchanged cadence, all retained panels populated, no startup/shutdown errors
- **US2**: no reachable telemetry view or link; no tool, integration, or current workflow referencing the retired store
- **US3**: storage and grant removed; services run a full cycle with no permission or missing-resource error
- **US4**: incomplete record blocks removal; complete record authorizes it
