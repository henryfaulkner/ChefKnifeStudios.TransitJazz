# Tasks: Client Experience Observability

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [capture/intake contract](contracts/client-experience-v1.md), [operator contract](contracts/operator-metrics-v1.md), and [quickstart.md](quickstart.md).  
**Branch**: `065-client-experience-observability`  
**Tests**: Required by the specification's independent tests and the plan's browser, database, cohort, and alert acceptance gates. For each test-first task, make the expected behavior fail before implementation; a missing-API compile error alone is not the behavioral red step.  
**Git rule**: Leave all changes uncommitted. Never run a Spec Kit auto-commit hook.

## Phase 1: Setup

**Purpose**: Fix the catalog and deployment defaults before changing capture behavior.

- [X] T001 Inventory every accepted owner outcome, journey/action code, and all 26 `NotificationIssueCode` members against `specs/063-client-experience-observability/contracts/client-experience-v1.md`; record any evidence gaps and keep unsupported freshness/audibility claims disabled.
- [X] T002 Set disabled-by-default client-experience intake and capture configuration, version 1 measurement defaults, and enabled-without-`TransitJazzDB` validation in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json`.
- [X] T003 [P] Add a disposable-PostgreSQL client-experience test fixture following the existing safety guard in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ClientExperience/ClientExperienceDatabaseFixture.cs`.

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: Supply one safe event format, browser delivery path, and durable intake for every story. Complete this phase before a story phase.

- [X] T004 Define fixed string codes and explicit mappings for all version 1 event kinds, journeys, actions, capabilities, outcomes, reasons, and notification issues in `src/ChefKnifeStudios.TransitJazz.Shared/ClientExperience/ClientExperienceCatalog.cs`; disallow arbitrary property bags and raw visitor values.
- [X] T005 Define strict versioned event/batch/acknowledgement DTOs, bounded fields, and per-kind payloads in `src/ChefKnifeStudios.TransitJazz.Shared/ClientExperience/ClientExperienceContracts.cs`; add `/api/client-experience/batch` to `src/ChefKnifeStudios.TransitJazz.Shared/ApiEndpoints.cs`.
- [X] T006 Add a no-throw passive capture interface and null implementation usable by Client.Core in `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/ClientExperience/IClientExperienceCapture.cs`.
- [X] T007 [P] Add append-only raw event model/configuration with server-assigned receipt/environment, event UUID key, and bounded query indexes in `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Models/ClientExperienceEvent.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Configurations/ClientExperienceEventConfiguration.cs`.
- [X] T008 Register the raw entities in `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/AppDbContext.cs` and scaffold/review the initial client-experience migration under `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Migrations/` without applying it to a shared database.
- [X] T009 Write failing TestHost cases for anonymous intake, `no-store`, valid/invalid member responses, unknown-length body cap, 400/413/429/503, and no payload echo in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ClientExperience/ClientExperienceEndpointTests.cs`.
- [X] T010 Implement closed-catalog and per-kind validation, UUID/array/string/duration bounds, safe member rejection, and skew marking in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/ClientExperience/ClientExperienceValidator.cs`.
- [X] T011 Write failing disposable-PostgreSQL tests for concurrent same-UUID inserts, immutable first receipt, partial-member acceptance (paired with the endpoint-level test in `ClientExperienceEndpointTests.cs`), transaction rollback, and lost-ack replay in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ClientExperience/ClientExperienceStoreTests.cs`.
- [X] T012 Implement one-transaction accepted-member insert with `ON CONFLICT (event_id) DO NOTHING` and durable/duplicate acknowledgement in `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/ClientExperience/ClientExperienceEventStore.cs`; preserve the first receipt and acknowledge nothing on transaction failure.
- [X] T013 [P] Make the existing rate-rejection callback endpoint-aware and add the dedicated client-experience source/global limit with bounded concurrency and `Retry-After` in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/EndpointGroups/StatsRateLimitingExtensions.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/EndpointGroups/ClientExperienceRateLimitingExtensions.cs`.
- [X] T014 Implement the anonymous bounded endpoint, 10-second request timeout, safe response codes, and disabled-intake retriable response in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/EndpointGroups/ClientExperienceEndpoints.cs`; map it in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs` and make T009 pass.
- [X] T015 Register fixed-label intake outcome, duration, accepted/duplicate/rejected count, and bounded loss metrics in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/ClientExperience/ClientExperienceTelemetry.cs` and the WebAPI meter configuration in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs`.
- [X] T016 Build one native IndexedDB outbox with atomic per-event writes/deletes, 1,000-event/4-MiB/seven-day bounds, 20-event/64-KiB batches, one sender per page, memory fallback, acknowledgement-only removal, retry/backoff/`Retry-After`, 413 split, and bounded kind-level loss reports in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/client-experience.js`.
- [X] T017 Start one page UUID and buffered `session_started` before Blazor, using the same outbox module and configured non-secret API base URL, in `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/wwwroot/index.html`.
- [X] T018 Implement application-lifetime typed reporter, bootstrap handoff, safe non-recursive failure containment, and explicit disposal in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/ClientExperience/ClientExperienceReporter.cs`.
- [X] T019 Register the reporter before owner initialization without resolving scoped presentation services from root DI in `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Program.cs` and `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/App.razor`.

**Checkpoint**: A disabled-by-default, migration-backed intake and bounded passive browser reporter are available. No health or toast inference is enabled yet.

## Phase 3: User Story 1 - See whether core journeys succeed (Priority: P1) — MVP

**Goal**: Operators see eligible bootstrap, city/map, live, audio, and explicit-action starts, confirmed outcomes, unresolved attempts, and readiness time even when no toast appears.

**Independent Test**: Seed successful, failed, inline-failed, timed-out, cancelled, and result-before-start journeys; verify one current-scope attempt/result and accurate start-cohort counts without relying on toast capture.

### Tests for User Story 1

- [X] T020 [P] [US1] Write failing controlled-time tests for one accepted attempt, one terminal result, user retry versus internal retry, 24-hour expiry, and foreground deadlines in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ClientExperience/ClientExperienceAttemptTests.cs`.
- [ ] T021 [P] [US1] Write failing owner-integration tests for usable city geometry/map, connected-but-not-joined live delivery, accepted audio unlock versus stale completion, and successful zero-toast sessions in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ClientExperience/ClientExperienceJourneyTests.cs`.
- [ ] T022 [P] [US1] Write failing database fixtures for result-before-start, unmatched results, duplicate/conflicting terminal results, `S/F/D/U/X` denominators, `N/A`, and provisional correction in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ClientExperience/ClientExperienceAttemptViewTests.cs`.

### Implementation for User Story 1

- [X] T023 [US1] Implement the scope-aware attempt coordinator, one-result acceptance, foreground/wall elapsed clocks, and deadline expiry in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/ClientExperience/ClientExperienceAttemptTracker.cs`; make T020 behavior pass.
- [X] T024 [US1] Capture one bootstrap ready/fatal result from the application's actual initialization outcome in `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/App.razor` and `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/ClientExperience/ClientExperienceReporter.cs`, preserving the pre-Blazor page ID.
- [X] T025 [US1] Capture accepted route readiness and current-city usable map startup only after required route geometry/layers are confirmed in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/ViewModels/ApplicationViewModel.cs` and `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Map.razor.cs`.
- [X] T026 [US1] Capture live start/resume attempts and results from desired delivery plus confirmed current-city join, excluding transport-only success and stale generations, in `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/SignalRNotificationService.cs`.
- [X] T027 [US1] Capture accepted native audio unlock/resume gesture attempts, running/suspended/failed results, inline failures, continue-muted cancellation, and stale `CompleteAttempt` rejection in `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs` and `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Services/Audio/AudioUnlockAttemptState.cs`.
- [X] T028 [US1] Capture fixed-code settings effect and separate requested persistence attempts under one action ID, never serializing `ConfirmedValue`, in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Services/SettingsChangeController.cs`.
- [X] T029 [US1] Capture confirmed audio-stop/mute outcome, including urgent still-playing failure, and source-only city-navigation outcome without claiming destination readiness in `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs` and `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Services/CityNavigationController.cs`.
- [X] T030 [US1] Create reviewed attempt-correlation and quality views with start-receipt cohorts, `S/F/D/U/X`, unmatched/conflicting diagnostics, version isolation, and safe readiness latency in `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/ClientExperience/Sql/client-experience-attempt-views.sql`; include them in the migration path and make T022 pass.
- [X] T031 [US1] Add the journey/startup/action/readiness panels with sample size, unresolved share, provisional status, affected sessions, and zero-denominator `N/A` in `observability/grafana/dashboards/transitjazz-client-experience.json`.
- [ ] T032 [US1] Prove a healthy zero-toast session, inline audio failure, connected-but-not-joined live attempt, and stale city result end to end in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ClientExperience/ClientExperienceJourneyTests.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ClientExperience/ClientExperienceAttemptViewTests.cs`.
- [X] T033 [US1] Create the initial acceptance-evidence section for bootstrap handoff, map/live/audio success and failure, and the P1 independent test in `docs/observability/client-experience-runbook.md`.

**Checkpoint**: P1 is a useful standalone journey-health view with successful-session denominators; it does not depend on toast or duration presentation.

## Phase 4: User Story 2 - Understand degraded time and recovery (Priority: P2)

**Goal**: Operators see only observed eligible capability time and verified recovery, with deliberate exclusions and suspension gaps explicit.

**Independent Test**: Drive one capability through requested, degraded, recovered, hidden, muted, suspended, and disposed states; verify no fabricated time or recovery and no overlapping accepted windows.

### Tests for User Story 2

- [ ] T034 [P] [US2] Write failing controlled-time tests for requested-state eligibility, stop/unlock exceptions, 60-second and partial windows, 15-second suspension gaps, verified recovery, and incomplete episodes in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ClientExperience/ClientExperienceHealthTests.cs`.
- [ ] T035 [P] [US2] Write failing database tests for balanced/non-overlapping windows, replay and overlap quarantine, duration denominators, and recovery evidence in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ClientExperience/ClientExperienceWindowViewTests.cs`.

### Implementation for User Story 2

- [X] T036 [US2] Implement one foreground scheduler and non-overlapping capability observation windows with state partitions, excluded/gap time, partial scope flush, and late-callback suspension handling in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/ClientExperience/ClientExperienceObservationTracker.cs`; make T034 pass.
- [X] T037 [US2] Feed foreground/background and requested delivery/audio changes from a single disposed browser listener and reassess current owner state on return in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/client-experience.js` and `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/ClientExperience/ClientExperienceReporter.cs`.
- [X] T038 [US2] Capture accepted route/map/live capability state and reason transitions before notification gating, including `not_requested`, unknown and current-generation rejection, in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/ViewModels/ApplicationViewModel.cs`, `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Map.razor.cs`, and `src/Client/ChefKnifeStudios.TransitJazz.Client.Core/Services/SignalRNotificationService.cs`.
- [X] T039 [US2] Capture requested audio context/required-voice state independently of conditional toast flags, with no claim of physical audibility, in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Services/Audio/AudioCapabilityHealthTracker.cs` and `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs`.
- [X] T040 [US2] Validate window bounds, partition sums, nonnegative coarse counters, and allowed capability transitions at intake in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/ClientExperience/ClientExperienceValidator.cs`.
- [X] T041 [US2] Create reviewed validated-window, episode/recovery, and quality views that quarantine overlaps/conflicts and keep unknown/gap/excluded time distinct in `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/ClientExperience/Sql/client-experience-health-views.sql`; include them in a migration and make T035 pass.
- [X] T042 [US2] Add capability health, gap/exclusion, recovery and incomplete-episode panels without summing overlapping capabilities in `observability/grafana/dashboards/transitjazz-client-experience.json`.
- [ ] T043 [US2] Keep live freshness diagnostic/unknown without a source cadence contract and show bounded accepted/failed batch ages only when owner evidence exists in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/ClientExperience/ClientExperienceObservationTracker.cs` and `observability/grafana/dashboards/transitjazz-client-experience.json`.
- [ ] T044 [US2] Record a real-browser visibility/suspension/recovery acceptance run, including abrupt termination coverage limits, in `docs/observability/client-experience-runbook.md`.

**Checkpoint**: P2 reports observed health and verified recovery even for capability episodes with no notification.

## Phase 5: User Story 3 - Relate feedback to actual health (Priority: P3)

**Goal**: Operators can distinguish accepted warning/error presentations from owner outcomes and inspect combined/escalated issue classifications.

**Independent Test**: Exercise an accepted combined toast, suppressed/stale/failed toasts, inline failure, and warning-to-error escalation; compare durable presentations with independently eligible owner events.

### Tests for User Story 3

- [ ] T045 [P] [US3] Write failing coordinator and durable-view tests for immutable city/time/array snapshot, accepted-only presentation, combined issue codes, escalation, host remount, and failed presenter isolation in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ClientExperience/ClientExperiencePresentationTests.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ClientExperience/ClientExperiencePresentationViewTests.cs`.

### Implementation for User Story 3

- [X] T046 [US3] Snapshot city, accepted UTC time, local presentation ID, displayed severity, and copied summary arrays under the coordinator state lock; report outside the lock in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Services/Notifications/NotificationCoordinator.cs` and `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Services/Notifications/NotificationIssue.cs`.
- [X] T047 [US3] Attach one application-lifetime committed-presentation subscriber, independent of remountable `NotificationHost`, with known episode links only and passive failure handling in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/ClientExperience/ClientExperienceReporter.cs`.
- [X] T048 [US3] Add one-row-per-accepted-presentation and quality views preserving displayed severity and represented/explained issue arrays in `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/ClientExperience/Sql/client-experience-presentation-views.sql`; include them in a migration.
- [X] T049 [US3] Add feedback and known-link panels that count each combined toast once and never infer health from absence of toasts in `observability/grafana/dashboards/transitjazz-client-experience.json`.
- [X] T050 [US3] Remove unused direct warning/error toast entry points after proving all production warnings/errors enter the typed coordinator in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Services/ToastService.cs` and `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ToastServiceTests.cs`.
- [ ] T051 [US3] Verify one accepted toast gives one durable presentation, zero suppressed/stale/failed/inline-only presentations, and separate escalation/re-arm events in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ClientExperience/ClientExperiencePresentationTests.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ClientExperience/ClientExperiencePresentationViewTests.cs`.

**Checkpoint**: P3 makes feedback interpretable without treating toast count as a health denominator.

## Phase 6: User Story 4 - Detect incidents and collection failures (Priority: P4)

**Goal**: Operators get guarded health and collection alerts plus independent startup coverage, with correction-aware long-term trends and a usable runbook.

**Independent Test**: Seed fresh failing, delayed, low-traffic, no-data, and healthy-recovery cases; verify health and collection rules, synthetic separation, alert routing, and daily correction after replay.

### Tests for User Story 4

- [X] T052 [P] [US4] Write disposable-PostgreSQL tests for late result correction, affected-bucket replacement, mergeable latency buckets, watermark catch-up, repeated-job idempotence, and failed-aggregate rollback preserving raw rows in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ClientExperience/ClientExperienceDailyTests.cs`. Execution remains skipped unless `CLIENT_EXPERIENCE_DISPOSABLE_CONNECTION` is configured.
- [X] T053 [P] [US4] Add fixture-driven SQL alert assertions for traffic/freshness/unresolved/gap guards, fresh stop failure, backlog, valid zero, no data, and missing city series in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ClientExperience/ClientExperienceAlertQueryTests.cs`. All six tests pass against disposable PostgreSQL 17.

### Implementation for User Story 4

- [X] T054 [US4] Add daily aggregate and maintenance-watermark models/configurations, register them in `AppDbContext`, and scaffold/review the migration under `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Migrations/`.
- [X] T055 [US4] Implement hourly advisory-locked correction-aware aggregation, ten-day plus watermark catch-up, atomic affected-bucket replacement, fixed latency buckets and session-day labels in `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/ClientExperience/ClientExperienceDailyAggregator.cs`.
- [X] T056 [US4] Implement bounded raw/13-month daily cleanup after aggregate rebuild, with lag/failure recording, in `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/ClientExperience/ClientExperienceDailyAggregator.cs`.
- [X] T057 [US4] Register one cancellable aggregate/retention worker and disabled-by-default configuration in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/ClientExperience/ClientExperienceMaintenanceService.cs`.
- [X] T058 [US4] Add weighted long-term, coverage, skew, delay, loss, unmatched/conflict, action reliability, and collection-health panels with correct time-basis/version/sample labels in `observability/grafana/dashboards/transitjazz-client-experience.json`. Datasource UID, OTel metric-name mapping, and panel queries still require staging verification.
- [X] T059 [US4] Add bounded database/ingest readiness probe without fake visitor events and dedicated fixed-label persistence/maintenance metrics in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/ClientExperience/ClientExperienceHealthProbe.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/ClientExperience/ClientExperienceTelemetry.cs`.
- [X] T060 [US4] Implement SQL time-series health rules with fixed configured city series, provisional 10%/traffic thresholds, two-minute freshness, 10% coverage guards, and immediate fresh `still_playing` stop alert in `observability/grafana/alerts/transitjazz-client-experience-alerts.json`; make T053 pass. Queries are sourced from `observability/grafana/alerts/sql/` and regenerated by `generate-client-experience-alerts.ps1`.
- [X] T061 [US4] Add collection/maintenance rules and explicit alerting states for no-data/query errors, six-evaluation missing-series retention, and diagnostic-only toast-volume advisories in `observability/grafana/alerts/transitjazz-client-experience-alerts.json`. Metric datasource mapping and Grafana firing/routing remain staging gates.
- [X] T062 [US4] Create the five-minute Grafana Cloud Synthetic Monitoring k6 browser-check artifact for shell, usable current-city map, and confirmed live join, with separate app/runner failure metrics, in `observability/synthetics/client-experience/check.js`; record required schedule, probe, and secret configuration in its README. Provisioning and staging execution remain open.
- [X] T063 [US4] Redirect check telemetry to a dedicated server-controlled synthetic intake with a runner-held credential, reject browser-supplied environment flags, and exclude synthetic rows from real-session dashboard cohorts in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/EndpointGroups/ClientExperienceEndpoints.cs`, `observability/synthetics/client-experience/check.js`, and the dashboard. Staging isolation proof remains open.
- [X] T064 [US4] Expand the operator runbook with measurement definitions, low traffic, backlog, gaps, failed collection, version changes, dashboard interpretation, alert routing/recovery, and coverage limits in `docs/observability/client-experience-runbook.md`.
- [ ] T065 [US4] Apply the reviewed migration to an isolated staging database, seed controlled observations, and demonstrate Grafana rule firing, contact-point delivery, no-data handling, synthetic failure, and adequately observed recovery; record evidence in `docs/observability/client-experience-runbook.md`.
- [ ] T066 [US4] Measure representative successful-session volume, 30-day raw/13-month aggregate query plans, insert latency, and provisional threshold suitability before operational routing; record results in `docs/observability/client-experience-runbook.md`.

**Checkpoint**: P4 alerts on supported evidence, surfaces collection failure, and has independent pre-bootstrap detection; long-term trends are correction-aware.

## Phase 7: Polish and cross-cutting verification

**Purpose**: Prove end-to-end safety, operational fit, and release order across all four stories.

- [X] T067 [P] Test representative payloads/logs for absence of setting values, coordinates, route/vehicle IDs, URLs, tokens, raw exceptions, arbitrary labels, and rendered copy in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ClientExperience/ClientExperiencePrivacyTests.cs`.
- [ ] T068 [P] Verify production `/api/client-experience/batch` proxy route, allowed origin, early-bootstrap URL, rate-limit source identity, and no frontend secret; record the configuration proof in `docs/observability/client-experience-runbook.md`.
- [ ] T069 Test offline → enqueue → reload/city change → reconnect → replay, multi-tab writes, unavailable storage, queue expiry/capacity, lost acknowledgement, and 413 split in real browsers; record exact IDs/counts and coverage limits in `docs/observability/client-experience-runbook.md`.
- [ ] T070 Verify trusted mobile/desktop audio gesture, map readiness, toast reading time, settings/stop actions, and no changed visitor behavior under reporter/storage failure in `docs/observability/client-experience-runbook.md`.
- [X] T071 Run affected client/server suites and Release builds from `specs/063-client-experience-observability/quickstart.md`; record pass/fail and PostgreSQL/browser evidence in `docs/observability/client-experience-runbook.md`.
- [ ] T072 Review migration SQL and read-only Grafana grants, apply migration before compatible server release, then enable client emission gradually; document actual deployment/rollback order in `docs/observability/client-experience-runbook.md`.
- [ ] T073 Measure browser CPU/memory/queue bytes, request/event rate, map/audio responsiveness, and subscription count under normal and flapping/offline load; record actual overhead and configured bounds in `docs/observability/client-experience-runbook.md`.
- [X] T074 Recheck Constitution v3.3.2, `specs/063-client-experience-observability/spec.md` FR/SC coverage, and `specs/063-client-experience-observability/quickstart.md` release gates; document remaining coverage limits and leave all changes uncommitted.

## Dependencies and execution order

- **Setup (T001-T003)** has no prior feature dependency. T003 can run while T001-T002 proceed.
- **Foundation (T004-T019)** blocks all story phases. T004-T005 define the wire format; T007-T008 precede durable-store work; T009/T011 are behavioral tests before T010/T012/T014; T016-T019 establish browser delivery before owner capture. T007 and T013 can be worked on in separate files; T015 can proceed separately from browser work.
- **US1 (T020-T033)** starts after foundation and is the suggested MVP. T020-T022 are independent test tracks; T023-T029 feed the reviewed attempt view T030, then journey dashboard T031 and integrated proof T032-T033.
- **US2 (T034-T044)** starts after foundation. It can use seeded capability events without US1; runtime integration shares owner files, so coordinate edits with US1. T034/T035 can proceed together; T036-T040 feed views T041 and panels T042-T043.
- **US3 (T045-T051)** starts after foundation. It can use seeded owner outcomes without US1/US2; its coordinator/reporter edits must be coordinated with those stories. T045 precedes T046-T047, then view/panel/integration tasks.
- **US4 (T052-T066)** collection/probe/synthetic work can start after foundation. Health-alert integration depends on the US1 attempt view and US2 health view; presentation panels benefit from US3. T052/T053 are separate test tracks. Aggregate, alert, and staging gates follow the views they consume.
- **Polish (T067-T074)** follows all desired story phases. Release order is migration → compatible server → controlled client emission → baseline → operational routing; never treat a dashboard import as alert-delivery proof.

## Parallel examples

- **US1**: Run T020 attempt-state tests, T021 owner-outcome tests, and T022 database cohort fixtures in separate test files before T023-T030 implementation.
- **US2**: Run T034 controlled-time health tests and T035 database window fixtures independently before tracker and view changes.
- **US3**: T045 presentation tests can proceed alongside completed US1/US2 work; T048 view work can proceed after its event shape is fixed while T046-T047 wire the client.
- **US4**: Run T052 correction/retention fixtures and T053 alert-query fixtures independently; T059 collection probe can be developed separately from T054-T058 aggregation and dashboard work.

## Implementation strategy

1. Complete setup and foundation with meaningful red/green tests for intake and durable replay.
2. Deliver **US1** as the MVP: owner-confirmed journey rates and readiness time, including successful zero-toast sessions and inline audio failures. Validate its independent test before extending scope.
3. Add **US2** observed duration/recovery and **US3** committed presentations as separate slices; each can be demonstrated with its own fixtures and browser acceptance.
4. Add **US4** guarded alerts, daily history, collection checks, and controlled synthetic coverage. Verify no-data, missing-series, backlog, and low-traffic cases before operational routing.
5. Complete cross-cutting privacy, browser, database, performance, and release checks. Retain explicit coverage limits and do not create a Git commit.
