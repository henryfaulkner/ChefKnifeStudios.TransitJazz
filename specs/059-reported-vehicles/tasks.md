# Tasks: Reported Vehicles in Stats

**Input**: Design artifacts in `specs/059-reported-vehicles/`: spec.md, plan.md, research.md, data-model.md, both contracts, and quickstart.md.
**Branch**: `059-reported-vehicles`
**Tests**: Required by FR-021. Use existing xUnit projects, injected TimeProvider, an independent arithmetic oracle, and opt-in disposable PostgreSQL. Record skips explicitly; mocked SQL is not database validation.
**Organization**: Shared correctness foundations precede three prioritized stories. This file schedules implementation; generation executes no tasks, migration, enablement, deployment, or commits.

## Format and execution rules

Every task has a sequential ID, exact repository-relative paths, and a story label in story phases. `[P]` marks work that can run concurrently with other marked tasks in the same named group after that group's prerequisites. Different phases are sequential unless dependencies explicitly permit otherwise. Proposed new files need not already exist. A migration's generated timestamp is assigned by EF; its fixed migration name is `CreateCityReportedVehicleHours`.

Read the capture/reporting contracts before implementation. Preserve independent feed coverage from the first increment; never label incomplete observations Complete. No git commits or commit hooks at any checkpoint. Keep default capture/read settings disabled and use disposable databases for migrations/tests.

## Phase 1: Setup

**Purpose**: Establish repeatable fixtures and evidence using existing projects; add no runtime project or dependency.

- [X] T001 Create the implementation evidence record in `specs/059-reported-vehicles/validation.md` with requirement/fixture links, original Stats baseline reference, unresolved release certifications, actual UTC coverage/target fields, and explicit not-run/skipped states.
- [X] T002 [P] Create hand-authored hour/source/population fixtures in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/Statistics/ReportedVehicleCaptureFixture.cs` from quickstart section 2; preserve expected counts independently of production deduplication and include ordinal/domain collisions and successful empty observations.
- [X] T003 [P] Add opt-in disposable PostgreSQL setup in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/ReportedVehicleDatabaseFixture.cs` following `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/RouteHourStatisticsDatabaseFixture.cs`; isolate schemas/data, expose cancellation, and report missing database configuration as skipped integration evidence.

**Checkpoint**: T002/T003 may run together after T001. Fixtures do not require production credentials.

## Phase 2: Foundational correctness (blocks all stories)

**Purpose**: Reproducible raw identities, truthful source coverage, bounded delivery, immutable history, and independently nullable summaries. These components serve all stories.

### Contract-first tests

- [X] T004 [P] Add identity/source catalog/options tests in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/Statistics/ReportedVehicleFoundationTests.cs` for exact spelling/case, city/domain/kind boundaries, missing versus oversized/NUL values, source membership/fingerprint stability, and finite count/byte/retention validation.
- [X] T005 [P] Add raw-response adapter tests in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/Statistics/ReportedVehicleSourceExtractionTests.cs` for unmappable GTFS vehicles, deleted/nonvehicle entities, undocumented entity/trip fallback exclusion, stale/differential records, MARTA prefilter TRAIN_ID/realtime=false, successful empty versus malformed/null failure, and NYC raw fetch with unavailable stop offsets; assert individual receipt hours and CityFetchResult.Combine retention.
- [X] T006 [P] Add controlled per-source timelines and loss/lifecycle tests in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/Statistics/ReportedVehicleCoverageTests.cs` covering predecessor/successor boundaries, source-local gaps/failures, missing IDs versus identity loss, no-success NoData, empty Partial, clock/config changes, startup/restart/shutdown, checked-counter overflow, and no successor identity reassignment.
- [X] T007 [P] Add disposable database model/migration/store tests in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/ReportedVehicleStoreTests.cs` for UTC/byte/ordinal/foreign-key constraints, successful-empty headers, atomic failure during chunk insertion, exact retries, differing header/membership/run, concurrent writers, sticky conflict, and cascading bounded cleanup that preserves active windows.
- [X] T008 [P] Extend `src/ChefKnifeStudios.TransitJazz.Shared.Tests/StatsOverlayContractTests.cs` for explicitly serialized null counts, unavailable defaults on older payloads, independent distance/feed summaries, safe aggregate numbers, and absence of raw IDs/URLs/credentials in public JSON.

### Contracts and schema

- [X] T009 Implement exact bounded identity and immutable source observation contracts in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Statistics/ReportedVehicleIdentity.cs`, `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Statistics/ReportedVehicleSourceObservation.cs`, and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Statistics/FinalizedReportedVehicleHourBatch.cs` per capture contract, including bounded diagnostic enums/codes and checked counts; distinguish missing identifiers from integrity loss.
- [X] T010 Implement independent Disabled/DryRun/Persistence configuration, explicit source definitions/catalog, fingerprints, and conservative byte reservation/admission in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Statistics/ReportedVehicleHistoryOptions.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Statistics/ReportedVehicleSourceCatalog.cs`; validate all contract caps, account for simultaneous extracted/active/queued buffers and transfer/disposal lifetimes without unbounded preallocation.
- [X] T011 [P] Add header/membership entities, ordinal mappings, constraints, composite keys, cascade and bounded JSON/arrays in `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Models/CityReportedVehicleHour.cs`, `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Models/CityReportedVehicleHourMembership.cs`, `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Configurations/CityReportedVehicleHourConfiguration.cs`, and `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Configurations/CityReportedVehicleHourMembershipConfiguration.cs`; expose sets in `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/AppDbContext.cs`.
- [X] T012 [P] Add aggregate-only summary/per-city coverage models in `src/ChefKnifeStudios.TransitJazz.Shared/Stats/StatsReportedVehicleSummary.cs` and additive unavailable-default properties in `src/ChefKnifeStudios.TransitJazz.Shared/Stats/StatsOverlayResponse.cs`, preserving explicit nulls, window references, states, independent denominators and bounded allowlisted diagnostics.
- [X] T013 Generate the `CreateCityReportedVehicleHours` migration/Designer under `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Migrations/` and update `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Migrations/AppDbContextModelSnapshot.cs` using existing EF conventions after T011; apply only in T007's disposable fixture and verify index key byte envelope and empty header support.

### Capture and adapter integration

- [X] T014 Carry explicit endpoint source keys, namespaces/rules/cadence and immutable source observations through `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Cities/CityConfig.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Metrics/CityFetchResult.cs`; retain observations in Combine independently of synthesized feed outcome and reject ambiguous endpoint policy mappings.
- [X] T015 [P] Extract nondeleted raw descriptor identities before remapping/position/route checks in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Cities/GtfsRtCity.cs`, recording injected TimeProvider receipt immediately after each decode, per-source failure disposition and bounded admission; preserve existing map results.
- [X] T016 [P] Extract original MARTA TRAIN_ID and bus descriptor IDs before realtime/Line/coordinate filtering in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Cities/MartaCity.cs` and share the small rail extractor with `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/RailRealtime/RailRealtimeAdapter.cs`; distinguish valid empty from malformed/null payloads without changing mapping semantics.
- [X] T017 [P] Move raw subway fetch/decode ahead of stop-offset readiness in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Cities/NymtaCity.cs`; capture each endpoint's receipt/population even when geometry fails, keep synthesis conditional, and preserve shared subway versus separate bus domains with bounded extraction.
- [X] T018 Implement per-city current/prior-hour accumulation and source-local rolling coverage in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Statistics/ReportedVehicleAccumulator.cs`, finalizing Complete/Partial/NoData according to both boundary witnesses/gaps, preserving identity-loss exclusions and stable policies, and never upgrading restart/concurrent partial evidence through union.
- [X] T019 Implement nonblocking Disabled/DryRun/Persistence sinks and capture ownership/reservation transfers in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Statistics/ReportedVehicleSinks.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Statistics/ReportedVehicleCapture.cs`; queue saturation drops entire envelopes with explicit diagnostics and releases memory, never fabricates successful-zero history.
- [X] T020 Add independent <=15-second sweeps and bounded shutdown finalization in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Statistics/ReportedVehicleCaptureLifecycleService.cs`; submit source observations before spatial/map/music guards in `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs`, enforcing single submission and fresh run IDs without storage waits in polling.

### Persistence, hosting and safety

- [X] T021 Implement atomic immutable header/full-population storage and bounded retention deletion in `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Statistics/CityReportedVehicleHoursStore.cs` and register it in `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/ServiceCollectionExtensions.cs`; use reported-specific advisory locks/read-committed reread, <=128-row chunks, exact semantic header/sorted tuple comparison, sticky quarantine and cancellation/deadlines.
- [X] T022 Implement bounded queue/byte admission, <=3 write attempts, command/attempt deadlines and <=15-second drain in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Statistics/ReportedVehicleStatisticsWriter.cs`; distinguish unchanged/quarantined/failed delivery, release reservations on every exit and emit bounded non-identity operational diagnostics.
- [X] T023 Implement hosted <=24-header cleanup batches at configured cadence in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Statistics/ReportedVehicleRetentionService.cs`, validating finite 7-30-day retention against all supported Stats windows/ingestion grace; honor cancellation and report cleanup lag without running cleanup in public requests.
- [X] T024 Register writer before lifecycle/Worker and wire independent options/source policies through `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Statistics/ReportedVehicleCaptureServiceCollectionExtensions.cs`, `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Program.cs`, and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Program.cs`, including separate NYC bus/subway construction and default-disabled settings in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json`; DryRun must not require a database.
- [ ] T025 Add host parity, disabled/dry-run isolation, byte reservation release, saturation/write timeout and shutdown ordering tests in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/ReportedVehicleHostTests.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/Statistics/ReportedVehicleFailureIsolationTests.cs`; demonstrate map/audio/polling continue during storage failure and run T004-T008 tests, recording real PostgreSQL execution/skips in `specs/059-reported-vehicles/validation.md`.

**Dependencies**: T004-T008 may run together after Setup. T009 then T010 establish Worker contracts; T011/T012 may run together after contracts are fixed; T013 requires T011. T014 requires T009/T010; T015-T017 form an adapter parallel group after T014. T018-T020 are sequential after adapter integration. T021 requires T009/T011/T013; T022 follows T019/T021; T023 follows T021; T024 follows T020/T022/T023. T025 closes foundations. New tests should establish failing behavior before their implementation where practical; compile-time additions become runnable once contract shells exist.

**Checkpoint**: Raw populations, coverage, immutable history and nullable DTOs are verified. No optimistic Complete state is deferred to US2.

## Phase 3: User Story 1 — See reported vehicles beside distance (P1, MVP)

**Goal**: Correct independent city counts in both existing cards, using adjacent distance windows and one reporting snapshot.
**Independent Test**: Seed the quickstart oracle directly into disposable PostgreSQL; open a single city's Stats without route selection. Cross-hour/overlapping identities deduplicate, independent domains stay separate, unmappable identities contribute, and both headings/integers appear beneath distance. Excluded/missing evidence remains qualified or unavailable from this first increment.

### Tests

- [X] T026 [P] [US1] Add independently seeded SQL population/snapshot tests in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/ReportedVehicleQueryTests.cs` for whole-window union (never hourly sums), ordinal/domain collisions, successful-empty zero, membership-count mismatch exclusion, independent route/feed eligibility, and single-statement reads during atomic writes.
- [X] T027 [P] [US1] Extend `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/ReportedVehicleScopeTests.cs` for reported window parity at midnight before/after grace, DST folds/gaps, fractional offsets, current-hour exclusion and Pending today, using existing policy grid rather than a separate window calculation.
- [X] T028 [P] [US1] Extend `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/StatsOverlayProjectionTests.cs` for city count headings/N0 formatting, zero/null distinction, omitted-old-server summaries, independent count versus distance states and existing request/refresh/cancellation behavior.

### Implementation

- [X] T029 [US1] Add the default-false reported read flag and trusted per-city definition/source/cadence policy input in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Statistics/StatsOverlayOptions.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Statistics/StatsOverlayService.cs`; reuse existing StatsOverlayPolicy grid/cutoff/date and whole-request budget, with unavailable/pending disabled summaries.
- [X] T030 [US1] Extend `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Statistics/StatsOverlayQuery.cs` with enabled reported-hour/member CTEs and a disabled SQL variant with no new relation references; verify supported fingerprints/definition, intact status/population count, build independent diagnostics, deduplicate domain/kind/value across each city/window and return bounded aggregates only in the existing statement snapshot.
- [X] T031 [US1] Project per-city summaries and update read/definition/sorted-source/cadence cache identity in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Statistics/StatsOverlayService.cs`; enforce checked safe integers, <=8-MiB response and existing timeout/cancellation/cache/admission bounds without adding a request or timeout allowance.
- [X] T032 [US1] Implement independently formatted summary projection and backward-compatible unavailable defaults in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Stats/StatsOverlayProjection.cs`; preserve existing loading/retry/stale request handling and minimum truthful Complete/Partial/Unavailable/Pending text.
- [X] T033 [US1] Add secondary count heading/value/independent coverage groups beneath corresponding distances in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Stats/StatsOverlay.razor`, responsive/theme styles in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Stats/StatsOverlay.razor.css`, and exact English headings/explanation/state copy in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Resources/RouteFilterResources.resx`; associate headings accessibly and keep distance primary.
- [ ] T034 [US1] Run city-count/query/window/projection fixtures and a local city Stats check using `specs/059-reported-vehicles/quickstart.md`, recording the independent count=3 oracle, qualified evidence, unmappable source inclusion and unchanged map/audio/distance behavior in `specs/059-reported-vehicles/validation.md`.

**Checkpoint**: City counts are demoable with real seeded evidence. Default-disabled deployment remains safe; US1 is an internal MVP, not authorization to enable capture or publish incomplete scope behavior.

## Phase 4: User Story 2 — Understand coverage and identity limits (P2)

**Goal**: Explain contributing/expected/fully-covered hours, source/identity limits, and excluded evidence without conflating distance coverage.
**Independent Test**: Use complete, successful-empty Partial, missing, failed-only, conflicted/lost, unsupported-policy and pending fixtures. Assert specified numeric/null states and reason denominators; no absent evidence becomes zero, and missing IDs are record diagnostics rather than fleet estimates.

### Tests

- [X] T035 [P] [US2] Add reporting eligibility/state tests in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/ReportedVehicleCoverageQueryTests.cs` for all exclusion reasons, source boundary/gap/failure evidence, malformed diagnostics, unsupported policies, no-data versus empty Partial, independent feed/distance state and bounded non-additive overlapping reason counts.
- [ ] T036 [P] [US2] Add disabled/pre-migration and cache/endpoint compatibility tests in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/ReportedVehicleCompatibilityTests.cs` proving disabled SQL works without new tables, enabled missing-schema follows existing error policy, cache separates flag/definition/fingerprint changes, and timeout/cancellation preserve existing endpoint behavior.
- [X] T037 [P] [US2] Add presentation detail tests in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/StatsOverlayProjectionTests.cs` for exact partial qualification, contributing/expected city-hours, Pending/Unavailable copy, record-based missing/fallback disclosure, accessible labels and independent distance coverage.

### Implementation

- [X] T038 [US2] Complete validation/projection of bounded per-source and per-city coverage explanations in `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Statistics/StatsOverlayQuery.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Statistics/StatsOverlayService.cs`, distinguishing contributing limitations from excluded-hour reasons and preserving nulls/genuine zeros; never add overlapping reasons into denominators.
- [X] T039 [US2] Add accessible coverage/identity detail rendering in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Stats/StatsOverlay.razor` and `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Stats/StatsOverlayProjection.cs`, with allowlisted English text in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Resources/RouteFilterResources.resx`; show explicit observed-identity rather than verified-fleet meaning, actual windows and contributing bounds.
- [ ] T040 [US2] Run coverage/compatibility fixtures and local failure/empty/pending UI scenarios from `specs/059-reported-vehicles/quickstart.md`, recording independent denominators, exclusions, restart/conflict outcomes and old-server/pre-migration behavior in `specs/059-reported-vehicles/validation.md`.

**Checkpoint**: Every state is independently explainable; disabling reads preserves existing distance even before schema deployment.

## Phase 5: User Story 3 — Understand route and All-city scope (P3)

**Goal**: Keep reported populations city-wide under route selection and sum independently deduplicated city populations in All, retaining local-day windows and mixed coverage.
**Independent Test**: Requery fixed evidence with selected/cleared routes and compare unchanged city counts. All oracle equals A=3 plus B=1=4 even with identical ID text; different city-local days and pending/absent cities preserve denominators and disclosure.

### Tests

- [X] T041 [P] [US3] Add route-independence/All arithmetic tests in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/ReportedVehicleScopeTests.cs` for route changes with unmatched IDs, per-city union then sum, different zones/local dates, pending cities, partial missing cities and 24 expected hours per included last24 city.
- [X] T042 [P] [US3] Add scope presentation tests in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ReportedVehicleScopeProjectionTests.cs` for persistent route-selection label, sum-of-cities disclosure, per-city local windows and summary stability through existing refresh/selection behavior.

### Implementation

- [X] T043 [US3] Finalize city-wide eligibility and All sum/state composition in `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Statistics/StatsOverlayQuery.cs` and `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Statistics/StatsOverlayService.cs`; route keys affect only existing distance, city-specific window references remain intact, zero-hour cities do not invent hours and mixed pending/boundary exclusions yield qualified aggregates.
- [X] T044 [US3] Add persistent "All city feeds; independent of route selection" and All sum/per-city-window disclosure in `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Stats/StatsOverlay.razor` and `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/Stats/StatsOverlayProjection.cs`, backed by `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Resources/RouteFilterResources.resx`; retain existing route distance behavior.
- [ ] T045 [US3] Run route/All fixture and local dialog selection checks from `specs/059-reported-vehicles/quickstart.md` and record oracle All=4, unchanged city counts and per-city window/coverage results in `specs/059-reported-vehicles/validation.md`.

**Checkpoint**: All three stories meet their independent scope/arithmetic/state criteria.

## Phase 6: Polish and release evidence

**Purpose**: Validate shared budgets, source contracts, browser usability and operational safety; these are acceptance gates, not permission to deploy.

- [X] T046 [P] Update canonical schema/query/result documentation in `skills/transitjazz-db/references/tables.md`, `skills/transitjazz-db/references/queries.md`, and `skills/transitjazz-db/references/results.md` with identity-membership union, immutable conflicts, empty/null/state/coverage interpretation, units and count-only-backfill prohibition; run `tools/sync-skills.ps1 -Mode Sync` and review generated copies.
- [ ] T047 [P] Execute browser acceptance from `specs/059-reported-vehicles/quickstart.md` at >=320px, 200% zoom, both themes, long headings and large/null/zero values; record screenshots, keyboard/assistive heading associations, route/All coverage copy and unchanged audio/map/navigation in `specs/059-reported-vehicles/browser-validation.md`.
- [ ] T048 [P] Run matched-load capture/read benchmarks and independent SQL EXPLAIN ANALYZE following `specs/059-reported-vehicles/quickstart.md`, recording original-baseline comparison, combined worker p95 <=5% overhead, prepared/cold read p95 <=250ms/2s, whole Stats <=5s, finite admission/clone/reservation lifetimes, response/cache bytes, seven-day storage and real source cadence in `specs/059-reported-vehicles/performance-validation.md`; adjust limits/query only if measurements justify and rerun affected checks.
- [ ] T049 Conduct the requested five-visitor comprehension check and consolidate actual results in `specs/059-reported-vehicles/validation.md` linked to browser/performance evidence; require >=4/5 understanding periods, route independence, observed identities versus physical fleet and partial/unavailable meaning, leaving unmet/not-run gates explicit.
- [ ] T050 Record verified source identity/domain/fallback contracts and operational owner topology plus schema-first/DryRun/selected-persistence/coverage-audit/read-enable and read-disable-before-capture rollback procedures in `specs/059-reported-vehicles/rollout.md`; preserve default-disabled settings in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json`, require separate operational authorization, and neither execute rollout nor backfill count-only history.
- [ ] T051 Run the four targeted project checks and applicable disposable PostgreSQL fixtures listed in `specs/059-reported-vehicles/quickstart.md`, review public privacy/bounded diagnostics and source-before-filtering regression results, then finish the FR-001–FR-021/SC-001–SC-007 evidence matrix in `specs/059-reported-vehicles/validation.md`; report any skipped DB/browser/visitor/performance/release gate rather than claiming acceptance.

## Dependencies and execution order

```text
Setup T001–T003
  -> Foundations T004–T025
  -> US1 T026–T034
  -> US2 T035–T040
  -> US3 T041–T045
  -> Polish T046–T051
```

Stories are independently testable but deliberately implemented sequentially because they extend the same query/service/UI files. US2 requires US1's integrated summary path; US3 requires US1/US2's arithmetic/coverage path. Correctness-critical coverage, disabled compatibility contracts and safe state defaults are foundational or US1; later stories enrich explanation/scope without allowing earlier incorrect counts. Query implementation waits for schema/DTO/capture contracts; UI waits for summary projection; benchmarks wait for integrated behavior.

Within stories write independent fixtures before behavior changes, then query/service projection, then presentation, then story acceptance. Do not execute tasks that depend on an unfinished contract. Retention/writer are foundational, not optional release polish.

### Parallel opportunities

Only marked tasks in each group share no edited files. Groups run after their prerequisites; unmarked tasks remain sequential. Shared evidence-file writers must not run concurrently.

- Setup: T002 + T003 after T001.
- Foundation tests: T004 + T005 + T006 + T007 + T008 after Setup; new implementation APIs may require initial compile shells before red tests execute.
- Foundation models: T011 + T012 after T009/T010 establish agreed contracts.
- Source adapters: T015 + T016 + T017 after T014 (do not edit shared CityFetchResult or catalog concurrently).
- US1 fixtures: T026 + T027 + T028 after Foundation.
- US2 fixtures: T035 + T036 + T037 after US1.
- US3 fixtures: T041 + T042 after US2.
- Polish: T046 + T047 + T048 after US3; their documentation outputs are separate. T049 consolidates evidence afterwards; T050 then T051 finish release review.

### Parallel example: US1

```text
T026: Seed and test SQL distinct-union arithmetic in ReportedVehicleQueryTests.cs.
T027: Test completed-hour parity in StatsOverlayWindowTests.cs.
T028: Test client formatting/defaults in StatsOverlayProjectionTests.cs.
Then execute T029–T034 sequentially.
```

### Parallel example: US2

```text
T035: Test source/identity eligibility in ReportedVehicleCoverageQueryTests.cs.
T036: Test disabled/pre-migration transport/cache in ReportedVehicleCompatibilityTests.cs.
T037: Test coverage detail copy in ReportedVehicleCoverageProjectionTests.cs.
Then execute T038–T040 sequentially.
```

### Parallel example: US3

```text
T041: Test city-wide and All arithmetic in ReportedVehicleScopeTests.cs.
T042: Test scope/window presentation in ReportedVehicleScopeProjectionTests.cs.
Then execute T043–T045 sequentially.
```

## Implementation strategy

1. Complete Setup and Foundation, verifying source-before-filtering, byte limits, immutable history and independent coverage with real fixtures.
2. Deliver US1 as the internal city-count MVP. Validate complete/qualified/unavailable/pending behavior; keep capture/reads disabled outside authorized test environments.
3. Add US2's detailed explanations and compatibility validation, then US3's route/All scope. Validate each story independently against hand-authored evidence and preserve distance behavior.
4. Complete browser/visitor, PostgreSQL, performance and release-source/ownership evidence. No release is approved merely because code tests pass; unmet gates stay recorded.
5. Leave all changes for user review. Production migration, capture enablement and deployment require a separate operational task; never create commits.

## Implementation status

41 of 51 tasks are complete. The ten remaining tasks require broader host-failure tests, local dialog/browser acceptance, cache/endpoint compatibility scenarios, matched-load and visitor evidence, or source/operator release certifications. Their checkboxes remain open until the evidence exists. See [validation.md](validation.md) for exact test results and [rollout.md](rollout.md) for the current operational gates.
