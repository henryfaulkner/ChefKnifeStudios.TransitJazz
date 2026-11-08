# Atlanta Rail Recovery and Available Statistics — Validation Evidence

## Baseline

- Feature directory: `specs/063-atlanta-rail-statistics` (selected by `.specify/feature.json`).
- Branch: `064-atlanta-rail-statistics`; the branch and feature directory numbers are intentionally independent.
- Baseline revision: `16670b3685885d9c88210cc218dde3844fe39195`.
- Baseline working tree was already dirty. Existing changes preserved: `AGENTS.md`; Worker and WebAPI `appsettings.json` and `appsettings.Development.json`; untracked `docs/ATLANTA_DATA_SOURCE_INVESTIGATION.md`, `docs/ATLANTA_RAIL_RECOVERY_DESIGN_DOCUMENT.md`, `docs/CLIENT_TOAST_OBSERVABILITY_DESIGN_PLAN.md`; and the untracked feature directory containing its specification/planning artifacts.
- Environment: Windows 11 x64, .NET SDK `10.0.401`, MSBuild `18.9.11`; .NET 10 runtimes available. No `global.json` was found.
- Existing setup: root `.gitignore` covers .NET outputs and common local files; root `.dockerignore` covers build outputs, secrets and local dependencies. No ignore-file edits were needed.
- Database: `ROUTE_HOUR_HISTORY_DISPOSABLE_CONNECTION` is absent from process, user and machine environments; the user confirmed it cannot be provisioned in this environment. The fixture correctly guards for a database name containing `disposable`, creates an isolated child database, migrates it, and drops it. No database connection was attempted; PostgreSQL-dependent work remains blocked.
- Baseline test/build status: not run for this implementation.

## Test and fixture conventions

- Fixed fixture hour: `2026-11-07T12:00:00Z`; Worker cadence: 10 seconds.
- Resolve contract fixtures from the repository root or explicitly copy them through the owning test project.
- Use the existing `TimeProvider` test patterns and fixed UTC identities; use task-completion signals for concurrency rather than sleeps.
- Keep database tests behind `RouteHourStatisticsDatabaseFixture` and its disposable database guard.

## T01–T08 implementation test groups

### T01 — Source and rail

Status: **PARTIAL**. Source outcome extraction and the Atlanta inventory mapping have focused tests; HTTP deadline/credential handling, shared mapper and authenticated source verification remain open.

### T02 — Capture and outage isolation

Status: **UNRUN**. Record symmetric outages, supported zero, continuity/recovery, publication-only failure, overlap and loss behavior.

### T03 — Evidence, integrity and storage

Status: **BLOCKED** for PostgreSQL portions by the missing disposable connection. Record unit evidence and any later executed migration/store results here.

### T04 — Completed-hour available-statistics journey

Status: **BLOCKED** pending the guarded disposable PostgreSQL database. Required proof crosses actual fetch/capture, persistence, query and client projection.

### T05 — Selection, arithmetic and windows

Status: **BLOCKED** for database query cases pending the guarded disposable database. Record isolated client/policy evidence separately.

### T06 — Versioned contract, HTTP and cache

Status: **UNRUN**. Record v1/v2 compatibility, request negotiation, cache isolation and request-local transport evidence.

### T07 — Snapshot and presentation

Status: **UNRUN**. Record snapshot acceptance, fallback, projection, resource/model and rendered-browser evidence separately.

### T08 — Compatibility and release readiness

Status: **UNRUN**. Record healthy-city compatibility and operational review; do not infer release readiness from unrun external gates.

## Browser acceptance

### B1 — Outage and available-source view

Status: **UNRUN**. Requires a controlled browser view showing available bus statistics during rail failure.

### B2 — Source recovery view

Status: **UNRUN**. Requires a controlled browser view after both sources recover.

### B3 — Selection, arithmetic and accessibility views

Status: **UNRUN**. Requires actual rendered checks across selection, theme, units, locale, viewport, keyboard and gap behavior.

## Authenticated serving gates

### L1 — Authenticated ingestion on the serving revision

Status: **UNRUN**. Requires authorized serving-environment access and redacted evidence.

### L2 — Live rail recovery

Status: **UNRUN**. Requires authorized live-source/serving evidence; deterministic tests cannot close this gate.

## R1 — Rollout and rollback review

Status: **REVIEW PASS / ROLLOUT UNRUN**. Documentation identifies migration `20261107184134_AddRouteHourSourceEvidence`, the nullable/additive legacy-null guarantee, schema to v2 writer to dual-contract server to client opt-in order, and stopping v2 writes before incompatible reader rollback while retaining columns/history. Real database execution remains blocked by T002; no rollout, secret change, backfill, history edit or deployment was run.

## Execution log

| Task / gate | Result | Evidence |
|---|---|---|
| T026 T04 fixture preparation | PASS / DB-UNRUN | Added the WebAPI-test-only Client.Shared reference, deterministic MARTA HTTP/protobuf/rail fixture, fixed run/hour and signal-wrapped real writer/store. The fixture calls the same `Worker.ExecuteCityCycleAsync` path used by the hosted loop. The fixture compiles; `RouteHourStatisticsWriterTests`: 3 passed, 0 failed, 0 skipped. It was not instantiated because the user confirmed the guarded PostgreSQL connection is unavailable; the actual journey remains T036/DB-gated. |
| T027 T01c intended RED | RED | Worker filter `FullyQualifiedName~ReportedVehicleSourceExtractionTests|FullyQualifiedName~ReportedVehicleConfigurationTests`: 7 failed, 0 passed, 0 skipped. All four fetch-evidence assertions and all three source inventory/mapping assertions reached the expected missing behavior; no compile/setup error was used as RED evidence. |
| T028 T01c focused GREEN | PASS | Same Worker filter: 7 passed, 0 failed, 0 skipped. Source outcomes and injected UTC receipt are independent of reported capture; bus/rail failure and disabled rail remain explicit; raw reported IDs retain their pre-position/realtime filtering policy. Atlanta bus aliases and BLUE/GREEN/RED/GOLD map to explicit dependencies; unknown/ambiguous Atlanta provenance and non-Atlanta category guesses cannot claim known membership. |
| T029 T02a actual-fetch intended RED | PARTIAL / DB-UNRUN | Before the capture change, both actual `MartaCity.FetchVehiclesAsync` → `Worker.ExecuteCityCycleAsync` regressions reached route-hour finalization and failed the intended assertion: a healthy bus/rail sibling had `ValidActiveSampleCount` 0 instead of 1 when the other MARTA feed failed. The fixed full-hour PostgreSQL → service → projection journey, all-failed null, supported-empty Complete0 and snapped-distance assertions could not be run without T002; T029 remains unchecked, and the available RED evidence is limited to the worker capture slice. |
| T030 T02a focused GREEN | PASS / DB-UNRUN | Worker Release project: 251 passed, 0 failed, 0 skipped. Both actual-fetch outage regressions now emit v2 evidence: the supported row has one valid activity sample and its sibling has one failed source poll, `source_failure`, and no valid sample. Added actual-fetch checks distinguish all-source failure (`NoData`, zero valid samples) from a successful empty poll (one valid zero-population sample); the latter is one poll and does not prove full-hour Complete0. WebAPI filters `FullyQualifiedName~RouteHourStatisticsWriterTests|FullyQualifiedName~StatsOverlayProjectionTests`: 14 passed, 0 failed, 0 skipped. No database was opened; persistence and end-to-end projection remain unverified under T013/T029/T036. |
| T013 T03 refactor/verification follow-up | PARTIAL / BLOCKED | Prior full Worker Release project: 261 passed, 0 failed, 0 skipped. WebAPI filter `FullyQualifiedName~RouteHourStatisticsMigrationTests|FullyQualifiedName~RouteHourStatisticsStoreTests|FullyQualifiedName~RouteHourStatisticsWriterTests`: 7 passed, 0 failed, 8 skipped. The model-only additive migration assertions and writer tests ran; every PostgreSQL upgrade/store/concurrency case skipped under the missing T002 disposable-connection guard. Route-hour capture now computes v2 source evidence for the explicit Atlanta inventory, but the actual PostgreSQL persistence path remains unverified; T013 stays open and foundation is not complete. |
| T025 T08b rollout documentation review | PASS | Reviewed and updated `quickstart.md` and `contracts/stats-overlay-api.md` against the generated migration `20261107184134_AddRouteHourSourceEvidence`. Both state nullable legacy evidence, old-reader unknown-version exclusion, schema then writer then dual server then client order, and stop-v2-writes/retain-history rollback rules. The review is documentation-only; migration execution and rollout remain UNRUN under T002. |
| T011 T03b intended RED | RED | Worker filters `FullyQualifiedName~RouteHourFailureIsolationTests|FullyQualifiedName~Failed_source_is_unavailable_not_a_valid_zero_population|FullyQualifiedName~Catalog_refresh_preserves_measures_for_an_unchanged_route_with_known_dependencies|FullyQualifiedName~OptionsRetainTheDocumentedMaximumCatalogAndWriterBounds`: 5 failed, 4 passed, 0 skipped. The new assertions reached finalization and failed because explicit source/measure evidence was still emitted as v1; existing isolation and bounds checks passed. |
| T012 T03b focused GREEN | PASS | Worker filter `FullyQualifiedName~Statistics.RouteHour`: 42 passed, 0 failed, 0 skipped. This covers localized activity overflow, publication-only failure, unlocalizable capture/processing loss, checked-counter overflow without wrap, failed-source no-data, healthy-support retention across a source outage, unchanged-route catalog refresh, queue admission loss propagation, catalog/membership bounds, movement behavior and capture options. WebAPI filter `FullyQualifiedName~RouteHourStatisticsWriterTests`: 3 passed, 0 failed, 0 skipped, including queue-capacity rejection and complete v2 writer mapping. Focused Worker options tests assert the 16,384 route ceiling and queue16/25,000 identities/50,000 memberships/128 rows/three attempts. PostgreSQL migration/store checks remain skipped or blocked and T013 is still open. |
| T020 T07a intended RED | RED | Client snapshot filter `FullyQualifiedName~StatsSnapshotAcceptanceTests|FullyQualifiedName~InvalidRefreshPublishesOneAtomicStateAndPreservesAcceptedEvidence`: 2 failed, 38 passed, 0 skipped. Both intended v2 acceptance/identity assertions failed because the client still only accepts v1; the invalid nested v2 refresh preservation case passed as rejection evidence. |
| T021 T07a focused GREEN | PASS | Client snapshot filter `FullyQualifiedName~StatsSnapshotAcceptanceTests|FullyQualifiedName~InvalidRefreshPublishesOneAtomicStateAndPreservesAcceptedEvidence`: 40 passed, 0 failed, 0 skipped. Exact v1/v2 version pairs, bounded source/reason/period/numeric checks, deep collection freezes, nested-evidence identity and atomic preservation passed. |
| T022 T06a transport intended RED | RED | `FullyQualifiedName~StatsTransportTests`: 3 failed, 11 passed, 0 skipped. New request-local header and Stats endpoint opt-in assertions failed while transport metadata compatibility assertions passed; expected safe mapping for `unsupported_stats_contract` was missing. |
| T023 T06a transport focused GREEN | PASS | `FullyQualifiedName~StatsTransportTests`: 14 passed, 0 failed, 0 skipped. Concurrent v2 and unrelated requests kept headers isolated; endpoint overload sent only its explicit version; cancellation, response disposal, Retry-After and safe problem mapping passed. |
| T024 local contract/cache/client safety checks | PARTIAL | Shared `StatsOverlayContractTests`: 12 passed; WebAPI endpoint/cache filters: 53 passed; client snapshot/transport filters: 54 passed; all with 0 failures and 0 skips. Real PostgreSQL storage/migration checks cannot run because T002's disposable connection gate is absent, so T024 remains open and foundation is not declared complete. |
| T006 T03c migration intended RED | RED / BLOCKED | Added a model/migration assertion for the missing additive migration; focused test failed at the expected missing-migration lookup (1 failed, 0 passed, 0 skipped). Real upgrade/round-trip/constraint cases still require T002's absent disposable PostgreSQL gate and remain UNRUN. |
| T006 PostgreSQL upgrade follow-up | BLOCKED | `RouteHourStatisticsMigrationTests`: 3 passed, 0 failed, 1 skipped; the guarded pre-feature Complete/Partial upgrade, v2 round-trip and atomic constraint case was skipped because `ROUTE_HOUR_HISTORY_DISPOSABLE_CONNECTION` is absent. This is not database validation. |
| T007 additive schema implementation | PARTIAL | EF scaffolded `AddRouteHourSourceEvidence` with nine nullable JSONB/scalar/reason columns and v1-null/v2-shape/reason/Complete constraints; model migration assertion passes (1 passed, 0 failed, 0 skipped). Real PostgreSQL upgrade remains unrun under T002. |
| T008 writer/store persistence mapping | PARTIAL | Writer mapping RED was observed, then `RouteHourStatisticsWriterTests`: 3 passed, 0 failed, 0 skipped after adding camelCase JSON and all scalar/reason mappings. Store now clones and compares every v2 field with property-order canonical JSON; `RouteHourStatisticsStoreTests`: 1 passed, 0 failed, 7 skipped. Real write/read and atomic DB assertions remain blocked. |
| T001 baseline ledger | PASS | Recorded baseline revision, dirty state, selected feature, SDK and initially unrun gates above. |
| T002 environment guard | BLOCKED | SDK 10.0.401 is installed. Disposable connection variable is absent from process, user and machine scopes. Fixture guard inspected; no database operation attempted. |
| T003 fixture conventions | PASS | Existing xUnit projects, fixed-time provider, root-relative fixture approach and signal-based concurrency conventions recorded above. |
| T004 T03a intended RED | RED | `dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.csproj -c Release --filter "FullyQualifiedName~EvidenceRejectsUnknownOutcomeReasonAndNonUtcReceipt"`: 1 failed, 0 passed, 0 skipped. The intended `ArgumentException` was absent for invalid source evidence; the project compiled with only a neutral validation stub. |
| T005 T03a focused GREEN | PASS | Worker filter `FullyQualifiedName~SourceObservationEvidenceTests|FullyQualifiedName~RouteHourFoundationTests`: 18 passed, 0 failed, 0 skipped. WebAPI filter `FullyQualifiedName~CityRouteHourStatisticModelTests`: 3 passed, 0 failed, 0 skipped. Ran on .NET SDK 10.0.401; model tests did not open a database. Restore reported existing `NU1903` for Microsoft.OpenApi 2.0.0. |
| T014 T06a intended RED | RED | `dotnet test src/ChefKnifeStudios.TransitJazz.Shared.Tests/ChefKnifeStudios.TransitJazz.Shared.Tests.csproj -c Release --filter "FullyQualifiedName~V2FixtureRoundTripsBoundedSourceMeasureCategoryAndTimelineEvidence"`: 1 failed, 0 passed, 0 skipped. Deserialization retained the v2 discriminator but the first nested v2 evidence field was dropped by the legacy DTO, as intended. |
| T015 T06a focused GREEN | PASS | Shared filter `FullyQualifiedName~StatsOverlayContractTests`: 12 passed, 0 failed, 0 skipped. v2 source/measure/category/timeline fields round-trip, explicit numeric nulls remain, and v1 serialization omits v2-only fields. |
| T016 T06a HTTP intended RED | RED | Endpoint filters for contract negotiation and invalid headers: 2 failed, 0 passed, 0 skipped. The endpoint omitted `Vary`, kept returning v1 for v2, and accepted unsupported headers. Empty headers cannot be represented by the `HttpClient` test request (they arrive as absent), so an isolated `HeaderDictionary` assertion covers an explicitly empty value. |
| T017 T06b cache intended RED | RED | Cache filters for retained slots and read admission: 2 failed, 0 passed, 0 skipped. v2 returned the v1 discriminator and the in-flight waiter for the v2 variant coalesced with v1 instead of starting its own query. Signal timeout was the test's bounded evidence of the missing independent fill. |
| T018–T019 T06 HTTP/cache GREEN | PASS | Endpoint filters: 3 passed, 0 failed, 0 skipped. Cache variant filters: 2 passed, 0 failed, 0 skipped. Absent/v1 share legacy results; v2 has its own retained slot and in-flight identity; invalid values return 400 before query; all responses vary by the contract header and remain `no-store`; global four-read admission remains shared. |

