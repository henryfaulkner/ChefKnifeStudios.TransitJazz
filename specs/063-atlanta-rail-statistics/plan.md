# Implementation Plan: Atlanta Rail Recovery and Available Statistics

**Branch**: `064-atlanta-rail-statistics` | **Date**: 2026-11-07 | **Spec**: [spec.md](spec.md)  
**Feature directory**: `specs/063-atlanta-rail-statistics`, selected by `.specify/feature.json`; directory and branch numbers are intentionally independent.  
**Input**: [Primary design](../../docs/ATLANTA_RAIL_RECOVERY_DESIGN_DOCUMENT.md), [investigation](../../docs/ATLANTA_DATA_SOURCE_INVESTIGATION.md). This phase produces documentation and Spec Kit metadata only.

## Summary

Restore authenticated MARTA rail using the replacement endpoint and an existing managed-secret-backed key. Independently retain valid statistics from available sources when a sibling source fails. Extend the existing route-hour envelope/table with bounded versioned source evidence and independent distance/activity/publication validity. Preserve healthy legacy Complete cohorts, exclude unsafe legacy Partial cohorts, and admit trustworthy v2 Partial measures without weakening cohort integrity. Add explicit `stats-overlay-v2` HTTP negotiation, isolated caches and Stats-local presentation of observations, unavailable sources and gaps.

The first increment is a deterministic bus-success/rail-failure closed-hour journey through capture, real persistence, v2 query and visible selection. It preserves bus distance/activity while rail stays null. Live rail recovery is a separate release gate and does not block this increment.

## Technical Context

**Language/Version**: Existing C#/.NET 10 (`net10.0`), Razor/Blazor WebAssembly, JavaScript ES modules and CSS.  
**Primary Dependencies**: Existing ASP.NET Core Minimal API, HttpClient/System.Text.Json, protobuf-net, EF Core 10.0.4/Npgsql provider 10.0.3, MatBlazor, chart interop, TimeProvider and immutable collections. No new service, project, runtime package or test framework.  
**Storage**: Existing PostgreSQL `public.city_route_hour_statistics`, EF model/configuration/migrations, atomic immutable city/hour envelopes and sticky conflict quarantine. Add nullable v2 JSONB evidence and scalar measure-validity/reason/support columns; legacy rows remain unchanged. Category and reported-vehicle stores keep separate definitions.  
**Testing**: Existing xUnit 2 projects: Worker, WebAPI/Data, Shared and Client.Shared; ASP.NET Core TestHost for HTTP. Hand-written HTTP/source/publisher doubles, controllable TimeProvider and real disposable PostgreSQL via `RouteHourStatisticsDatabaseFixture`/`ROUTE_HOUR_HISTORY_DISPOSABLE_CONNECTION`. Explicit TDD: record intended-behavior RED before changing each gate, implement minimal GREEN, then REFACTOR under the focused suite. [Testing plan](testing-plan.md) defines all FR/SC and story-scenario coverage, exact test files, fixture isolation and acceptance gates. No new framework/runtime package.  
**Target Platform**: Existing Azure Container Apps Worker/WebAPI and Static Web App WASM; desktop/mobile browsers. Controlled browser checks and later authenticated serving-revision recovery proof.  
**Project Type**: Cross-layer repair within existing Worker, Data, WebAPI, Shared and Client projects.  
**Performance Goals**: Preserve configured 10-second Worker cycles, 60-minute Stats refresh and default 2-minute ingestion grace. Keep bounded aggregate SQL within existing 5-second read deadline, four concurrent reads, 8 MiB response and 64 MiB cache limits. No extra rail poll or per-poll persistence.  
**Constraints**: Preserve route bounds (default 4,096; hard 16,384), tracked vehicles (25,000 default), route/hour memberships (50,000 default), queue capacity (16 default), 128 rows/command and three write attempts. Source inventory hard bound 32/city, keys at most 64 UTF-8 bytes, route-relevant summaries only; checked counter overflow invalidates evidence rather than truncating. Keep immutable UTC identities, ordinal keys, local-day/DST rules, retention, quarantine, cancellation and movement checks.  
**Scale/Scope**: Atlanta source contract `marta-bus`/`marta-rail`, static bus routes and BLUE/GREEN/RED/GOLD rail routes, city/all-cities and historical route selections. Other adapters retain the conservative legacy path until they supply explicit evidence. No live-music/SignalR payload changes, global warnings, backfill, deployment, secret changes or commits.

## Constitution Check

Constitution **v3.3.2** checked before research and after design. Feature gates pass; no amendment or exception is needed.

| Principles / rule | Initial / post-design | Design response |
| --- | --- | --- |
| I architecture | Pass / Pass | Extend existing units, endpoint and store; no new service. |
| II secrets | Pass / Pass | MARTA key server-only; later existing managed-secret reference; sanitize URI/exception/HttpClient diagnostics. |
| III pipeline, VI identity | Pass / Pass | Both realtime passes and canonical `RouteShapeProperties.JoinKey` remain; no event payload/route-normalization changes. |
| IV observability | Pass / Pass | Bounded source/reason structured fields and writer/query outcomes, without keys, URLs or per-vehicle volume. |
| V delivery | Pass / Pass | Existing GitHub Actions and Release artifacts; additive rollout documented only. |
| VII-X maps/music/selection | Pass / Pass | MapLibre layers, music and persistent selection remain; historical Stats membership stays separate from current live catalogs. |
| XI overlays | Pass / Pass | Current Stats motion/dismissal; no toast/global warning surface. |
| XII localization | Pass / Pass | New labels/ARIA/reasons use existing RouteFilterResources base resource and language variants; add affected Spanish keys in that family. Existing application language-control gaps are outside this feature and are not claimed repaired. |
| XIII theme parity | Pass / Pass | Existing theme/palette mechanism with light/dark, metric/imperial and mobile evidence. |
| AGENTS Git rule | Pass / Pass | Skip optional before/after-plan commit hooks; preserve NEVER COMMIT and prior changes. |

## Project Structure

### Documentation

```text
specs/063-atlanta-rail-statistics/
  spec.md
  checklists/requirements.md
  plan.md
  research.md
  data-model.md
  quickstart.md
  contracts/source-evidence.md
  contracts/stats-overlay-api.md
  contracts/overlay-presentation.md
  testing-plan.md                 # traceability, fixtures, TDD and gates
  tasks.md                        # subsequent speckit-tasks
```

### Source Code

All paths are existing seams except explicitly proposed new value objects/helper/variant.

```text
src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/
  Cities/{MartaCity,CityConfig}.cs
  Metrics/CityFetchResult.cs
  RailRealtime/{RailRealtimeAdapter,RailArrivalDto,RailRealtimeOptions}.cs
  RailRealtime/RailArrivalMapper.cs                     # proposed pure normalization
  Statistics/{RouteHourCatalog,RouteHourHistoryOptions}.cs
  Statistics/{RouteHourStatisticsCapture,RouteHourStatisticsAccumulator}.cs
  Statistics/FinalizedRouteHourStatisticsBatch.cs
  Statistics/{CityCategoryStatisticsCapture,CategoryStatisticsAccumulator}.cs
  Statistics/SourceObservationEvidence.cs               # proposed bounded values
  Worker.cs
  Program.cs
  appsettings{,.Development}.json
src/Server/ChefKnifeStudios.TransitJazz.Server.Data/
  Models/CityRouteHourStatistic.cs
  Configurations/CityRouteHourStatisticConfiguration.cs
  Statistics/{CityRouteHourStatisticsStore,StatsOverlayQuery}.cs
  Migrations/                                          # additive migration + snapshot
src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/
  EndpointGroups/StatsEndpoints.cs
  Statistics/{RouteHourStatisticsWriter,StatsOverlayService,StatsOverlayPolicy}.cs
  Statistics/{StatsOverlayOptions,StatsOverlayTelemetry}.cs
  Program.cs
  appsettings{,.Development}.json
src/ChefKnifeStudios.TransitJazz.Shared/Stats/StatsOverlayResponse.cs
src/Client/ChefKnifeStudios.TransitJazz.Client.Core/
  Services/HttpService.cs
  Services/EndpointsServices/StatsEndpointsService.cs
src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/
  Services/{StatsSnapshotPolicy,StatsSnapshotStore,StatsChartText}.cs
  Components/Stats/{StatsOverlay.razor,StatsOverlay.razor.css,StatsOverlayProjection.cs}
  Components/Stats/StatsOverlayRequestPolicy.cs
  Components/Charts/                                  # models, ChartFrame, chart components
  Resources/RouteFilterResources{,.es}.resx             # .es variant proposed if absent
src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/
src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/
src/ChefKnifeStudios.TransitJazz.Shared.Tests/
src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/
skills/mj-gtfs/SKILL.md
tools/sync-skills.ps1
```

**Structure Decision**: Reuse current owners; new source values and pure normalization sit beside current callers. No parallel ingestion/statistics service. The canonical skill is `skills/mj-gtfs/SKILL.md`; implementation updates it and synchronizes generated copies through the repository workflow.

## Phase 0: Research resolution

[Research](research.md) resolves technical unknowns from code. Both hosts poll through Worker → ITransitCity → MartaCity and register the named RailRealtimeApi client. RailRealtimeAdapter exists separately but has no current production registration/call; keep it consistent without another poll. Resolve expected source inventory independently of reported-history capture because its runtime catalog can be empty when disabled. Separate source coverage/provenance from measure loss and publication failure.

## Phase 1: Design and contracts

1. **Evidence/storage foundation (US1/US4)**: Extend CityFetchResult with bounded source outcomes and explicit dependencies; preserve combined monitoring outcome. Add route/category validity decisions. Add v2 JSONB/scalars and version-aware validation to finalized rows, Data models, writer mapping, store copy/equality, EF constraints/migration. Keep v1 Complete predicates intact and legacy fields null; no history update.
2. **Closed-hour capture (US1)**: Replace combined activity gating with per-route/source decisions after usable index and processing checks. Empty success adds one zero sample; failure adds none. Publication-only failure preserves movement/activity. Track continuity/boundaries per source; reset/reseed movement baselines on outage/disabling/excessive gaps. Use consistent category decisions without changing reconciliation. Localize corruption only when proven; otherwise exclude cohort.
3. **Rail integration (US2, independently deliverable)**: Retain six prepared URL edits in four settings files; verify active index-zero binding. Prompt missing-key failure, encoded request construction, bounded deadline, cancellation and sanitized diagnostics. Share pure normalization across implementations, with finite/ranged positions, route alignment, realtime filtering, deduplication and explicit event-time handling. Live schema/timezone verification remains a release gate; uncertain times cannot create movement.
4. **Query/dual contract (US1/US3/US4)**: Separate SQL integrity from per-measure admission; retain bounded aggregation/numeric safety and historical selection. v1 keeps conservative all-Complete behavior; v2 accepts safe Partial measures. Negotiate before cache access and include contract in full key, retained scope slot, in-flight key, Vary and client identity. Coverage, ratios/shares/timeline use coherent admitted sets.
5. **Overlay (US1/US3)**: Update shared vocabulary/fixtures, per-request transport opt-in, strict validation/deep freeze, projections, fallback, chart models/tables and safe problem-code mappings. Current usable Partial supersedes older Complete/previous-day values; stale/cancelled selections stay rejected. Show per-figure coverage, source periods, unavailable categories and omitted-bubble reasons; preserve gaps. Resource keys precede UI wiring; theme/accessibility changes ship together. Reported vehicles stay city-wide and independent.
6. **Operational handoff (US2/US4)**: Additive migration before new writes; dual server before v2 client. Document existing secret reference, serving-revision verification, authenticated recovery and controlled browser views. Rollback stops v2 writes before incompatible readers, retains additive storage/history and can restore v1 negotiation. Never restore retired URL or disable production rail for screenshots.

[Data model](data-model.md), [source contract](contracts/source-evidence.md), [HTTP/arithmetic contract](contracts/stats-overlay-api.md), [presentation contract](contracts/overlay-presentation.md), [testing plan](testing-plan.md) and [quickstart](quickstart.md) are planning outputs. Testing enrichment supplies traceability and executable test ordering before tasks; this phase adds no application/test code.

## Phase 2: Implementation handoff and gates

Use story-oriented tasks with shared storage/contract safety first, US1 deterministic outage increment, US2 rail work, US3 selections/arithmetic and US4 compatibility/rollback hardening. US4 safety is foundational, never deferred polish. Write behavior tests before changing gates. The core completed-hour fixture must cross real persistence/query rather than seed only final DTOs.

Required groups: symmetric outage; both-failed versus proven zero; intermittent recovery/disabling/config changes; independent boundaries; empty polls; publication-only failure; overlaps/provenance; capture/queue loss, overflow, catalogs, processing; retry/conflict/mixed-run; additive migration/v1/v2; header/cache isolation; coherent formulas/alignment; shares/timeline reconciliation; numeric bounds; historical route selection; all-cities; DST/local midnight/24 completed hours/grace; stale refresh/fallback; healthy-city regressions. Existing test layers and disposable database guards apply. A skipped database test is not passing evidence.

Release requires authenticated ingestion on the serving revision and two controlled browser views: nonzero bus statistics without rail, then both sources after recovery. These gates are **not run during planning** and do not block documentation/testing enrichment/tasks.

## Test-first implementation design

Use `util-testing`'s lowest meaningful layer: unit/component for combinatorial validity, arithmetic and selection policy; real PostgreSQL for JSONB/constraints/SQL/locking; TestHost for negotiation; desktop client tests for pure presentation. Existing Client.Shared tests do not render Razor or run JavaScript. They cannot certify persistent visible labels, gap drawing, keyboard access or responsive/theme layout; those remain explicit controlled-browser gates. Native projects are .NET 10/xUnit 2 with hand-written doubles, not bUnit/Playwright/Moq. The local SDK inspected for planning is `10.0.401`; no application tests/builds were run for this documentation change.

The following groups are implementation acceptance units. `W`, `A`, `S`, `C` expand to exact existing test directories in [testing-plan.md](testing-plan.md#test-projects-and-paths); its detailed matrix gives precise paths and scenario assertions. Existing suites are extended unless marked new.

| Group / story and requirement coverage | Lowest meaningful proof and concrete files | Happy, failure and boundary acceptance |
| --- | --- | --- |
| T01 source/rail: US2.1-4; FR-001-005, FR-027 | HTTP component `W/Cities/MartaCityTests.cs` (new), unit `W/RailRealtime/RailArrivalMapperTests.cs` (new), `A/CityConfigParityTests.cs` | Once-only poll and encoded sentinel key; missing key makes no rail request, all HTTP/parse failures preserve bus; cancellation propagates, coordinate/time/dedup limits, empty versus rejected positions; evidence persists with reported capture off. |
| T02 capture: US1.1-6, US2.5, US4.5; FR-004-010, FR-012 | Component `W/Statistics/RouteHourWorkerIntegrationTests.cs`, `RouteHourActivityTests.cs`, `RouteHourMovementTests.cs`, `RouteHourCoverageTests.cs`, `CategoryWorkerIntegrationTests.cs` | Symmetric outage, both failed versus proven zero, independent continuity, recovery reseed, disabled/configured sources, overlaps, unknown provenance, publication-only loss; startup/shutdown and first full healthy hour. |
| T03 evidence/integrity/storage: US4.1-3/5; FR-010-014 | Unit `W/Statistics/RouteHourFoundationTests.cs`, `RouteHourCatalogAndLimitTests.cs`; PostgreSQL `A/Statistics/RouteHourStatisticsStoreTests.cs`, `RouteHourStatisticsMigrationTests.cs`; writer component `RouteHourStatisticsWriterTests.cs` | All fields round-trip, legacy nulls untouched, typed canonical JSON equality, changed-field quarantine, atomic retry/concurrency; malformed/oversized/overflow evidence and localized/unlocalized loss stay conservative. |
| T04 first observable increment: US1.1-4; FR-015/024-026/029; SC-001/002 | Narrow system `A/Statistics/AtlantaAvailableStatisticsJourneyTests.cs` (new) plus controlled browser | Real MartaCity fixture response → Worker processing/capture → closed envelope → real writer/store/PostgreSQL → v2 service → real client projection. Assert captured nonzero bus distance/activity survives, city Partial, rail null, selectable bus and source explanation; reverse sources and zero/no-observation controls. |
| T05 selection/arithmetic/windows: US3.1-6; FR-015-023 | PostgreSQL `A/Statistics/StatsOverlayQueryTests.cs`; pure `StatsOverlayProjectionTests.cs`, `StatsOverlayWindowTests.cs`, `RouteHourStatisticsLocalTimeTests.cs`; client `C/StatsRouteSelectionTests.cs`, `StatsAccessibleEvidenceTests.cs` | Same admitted row sets reconcile distance/category/timeline; coherent population and support, available-distance shares, null ratios/bubble omissions, historical membership unknown, missing city; UTC/local midnight, DST and cutoff/grace boundaries. |
| T06 dual contract/cache: US4.1-4; FR-013/024 | Contract `S/StatsOverlayContractTests.cs`; TestHost `A/Statistics/StatsOverlayEndpointTests.cs`; component `StatsOverlayCacheTests.cs`; `C/StatsTransportTests.cs` | Preserve frozen v1 semantics and new v2 fields/nulls; absent/explicit v1/v2 interleaving and concurrent isolation across full keys/retained slots/in-flight; invalid header rejected before query; request-local header, disposal/cancellation and global limits. |
| T07 acceptance/presentation: US3.7, US1.1-6; FR-020-028 | Pure `C/StatsSnapshotAcceptanceTests.cs`, `StatsSnapshotStoreTests.cs`, `StatsOverlayProjectionTests.cs`, `StatsChartModelTests.cs`, `StatsResourcesTests.cs`; controlled browser | Current Partial/zero replaces Complete/yesterday; wrong contract, mutated nested evidence, cancelled/obsolete responses rejected; source/category/omission text and gaps persist in models; actual Razor/JS, English/Spanish, themes/units/mobile/accessibility verified in browser. |
| T08 compatibility/release: US2.5, US4.1-5; FR-028-031; SC-005-008 | Focused existing healthy-city/category/reported regressions; disposable upgrade test; documentation and later authenticated/browser gates | Legacy Complete eligibility and Partial exclusion, mixed-version distance versus incompatible activity; rollout/rollback sequence reviewed, unchanged payloads/settings bounds/skill sync; serving-revision recovery cannot be inferred from deterministic tests. |

### Deterministic fixtures and isolation

- Extend `RouteHourCaptureFixture` by composition with explicit Atlanta bus and four rail route dependencies, fixed run IDs, fresh sink/publisher per test, fixed UTC receipt/event times and ordered source outcomes. Existing `RouteHourWorkerIntegrationTests` directly invokes reconciliation and bypasses fetch/combined gating; T04 must drive the actual fetch-result-to-capture orchestration seam so the original regression cannot pass unnoticed. Introduce only a small internal cycle seam if required, without changing live scheduling semantics.
- T04 lives in the existing WebAPI test project, which already reaches Worker; add a **test-only** Client.Shared project reference for `StatsOverlayProjection.Build` and scope desktop browser-platform analyzer suppression as the client tests already do. It invokes pure projection only, with resource-backed localization. No production project dependency/new test project is introduced. Keep extensive projection permutations in Client.Shared tests.
- Run a full fixed hour at configured cadence with predecessor/successor source support, complete through capture sweep/clock advancement, and await writer completion via signals/StopAsync. Use capture-produced envelope, never final DTO-only seeding for T04. Compare accepted movement to independently calculated snapped-route distance within storage precision; keep illustrative exact `120 km` in arithmetic fixtures, not an implausible single movement jump.
- Reuse existing `RouteHourTestClock`/`StatsOverlayTestTimeProvider` patterns, fixed UTC/hour identities, fresh SUT/DI host and isolated child DB per test. Advance UTC and monotonic clocks explicitly; synchronize concurrency with `TaskCompletionSource(RunContinuationsAsynchronously)` and cancellation, not sleeps/retries. Test timeouts guard hangs, not throughput assertions.
- Source/HTTP/publisher faults are local stub responses, typed exceptions and spies; do not call MARTA/network, mutate process environment or use real credentials in low-layer tests. Use an obvious synthetic sentinel containing reserved query characters to assert encoding/redaction without printing request URIs. PostgreSQL fixture alone reads the pre-provisioned disposable environment connection and cleans its own child DB.

### RED → GREEN → REFACTOR dependencies

1. **Foundation / US4 safety**: RED new bounded evidence validation, nullable legacy model/serialization and additive-upgrade tests (intended assertion, not compile/setup error). GREEN add value objects/scalars/mapping/version branches and additive migration only. REFACTOR canonical validation/equality helpers while rerunning Worker foundation and real migration/store cases. Storage compatibility blocks v2 writes.
2. **US1 walking slice**: Add T04 regression alongside focused Worker outage tests before removing combined gating. Record RED that bus samples are lost/cohort excluded under rail failure. GREEN pass independent source validity through the actual Worker seam, capture, writer and per-measure v2 query/projection with the minimum support needed. REFACTOR shared route/category decisions without changing category reconciliation. The real DB journey and projected selection must pass before calling US1 delivered; no live key dependency.
3. **US1 safety variants**: RED zero-versus-failure, recovery bridge, independent boundary, publication-only and loss/overflow cases. GREEN implement source continuity/reseed and independent validity. REFACTOR bounded summary/support accumulation; rerun T02/T03/T04 plus healthy-city/category/reported tests.
4. **US2 independent branch**: RED missing-key/HTTP/error/time/coordinate/dedup contract; GREEN replacement request handling and shared pure mapper in both implementations with once-only production registration; REFACTOR request/redaction duplication. Deterministic success does not close authenticated release gate.
5. **US3 arithmetic and time**: RED each selected admitted-set/formula/gap/window assertion before changing that rule. GREEN SQL eligibility/aggregation and projection using explicit measure sets. REFACTOR shared compatibility/fallback rules only after weighted/alignment/version cases pass; rerun database query and pure projection/window suites.
6. **US4 HTTP and client completion**: RED v1/v2 header/cache cross-contamination, invalid nested evidence, stale completion and current Partial fallback cases. GREEN dual negotiation/isolated cache, strict deep freeze and request-local client opt-in. REFACTOR repeated fixture setup after endpoint/cache/transport/store tests pass; resource keys precede Razor wiring. Complete controlled browser and authenticated gates separately.

### Commands, task generation and completion gates

[Testing plan](testing-plan.md#execution-and-evidence-gates) supplies copyable focused PowerShell commands and broader gates; [quickstart](quickstart.md) retains the four Release test commands and solution build. Do not run deployment workflows as tests: current server/client GitHub workflows build/publish/deploy but do not supply these xUnit/database/browser gates automatically. Record implementation validation explicitly; CI extension is not assumed complete.

Task generation must create a RED task **before** each changed behavior, dependent minimal implementation then focused GREEN/refactor verification; do not hide tests in a final cleanup phase. Trace T01-T08 and FR/SC/story acceptance IDs in task text. New fixture/header/contract work and additive persistence checks are shared prerequisites; deterministic US1 must remain independently demonstrable from US2 live access. A test that already passes is regression evidence, not a RED; select the next missing behavior. Compilation, unavailable DB and missing fixture files are setup failures to fix before recording intended-behavior RED.

Implementation acceptance: all affected fast tests pass, real disposable upgrade/store/query/T04 cases execute with zero skips, unchanged healthy-city/reported/category suites pass, four Release projects and Release solution build succeed, skill Sync/Check passes after its authorized implementation edit, and all required browser assertions are recorded. Full feature release additionally requires authenticated serving-revision recovery, redacted schema/time findings and both browser views. Any missing database/live/browser environment is an explicit unrun gate, never a passing substitute. This enrichment creates no production/test code and runs no deployment, secret, history or commit operation.

## Complexity Tracking

No constitution exception/new infrastructure. Bounded JSONB represents source dependencies without per-poll history or a table per source; SQL scalars keep admission explicit. A small compatibility branch is necessary for old clients and unsafe old Partial history.

