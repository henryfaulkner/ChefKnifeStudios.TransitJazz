# Reported Vehicles implementation evidence

This file records implementation evidence separately from release certification. A blank or not-run item is not a pass.

## Targets and coverage

| Field | Actual value |
| --- | --- |
| Target environment | Local source tree; no deployment target selected |
| Database/schema | Disposable local PostgreSQL 16 container on loopback; per-test databases created and dropped by the opt-in fixture. The feature migration was applied in those databases. A pre-feature schema ending at `20261010151240_CreateCityRouteHourStatistics` also served the disabled-query compatibility test. No production database was used. |
| Synthetic UTC fixture range | 2026-08-20 08:00Z through 2026-08-21 14:00Z for window, coverage, empty, excluded, and All-scope SQL cases; worker timeline tests also cover 2026-10-01 11:59:30Z through 2026-10-01 14:00:00Z. No live vehicle history was captured. |
| Source contracts | Pending agency/operator verification; candidate policies are in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json`. See [capture contract](contracts/reported-vehicle-capture-v1.md). |
| Capture owner topology | Pending operator verification; exactly one persistence owner per city remains a release gate. |
| Original Stats baseline | [Feature 058 validation](../058-stats-overlay/validation.md) and its matched-load protocol. No new matched run was performed. |

## Requirement evidence matrix

| Requirement | Evidence | State |
| --- | --- | --- |
| FR-001 | `StatsOverlay.razor` places separate count/coverage sections under each distance; distance remains the primary card metric. Browser layout review is pending. | Implemented; browser not run |
| FR-002 | `ReportedVehicleSourceExtractionTests` verifies raw GTFS/MARTA/NYC observations survive route, coordinate, realtime, and stop-offset failures. | Automated tests pass |
| FR-003 | PostgreSQL `ReportedVehicleQueryTests` seeds overlapping identities in two hours and verifies the whole-window result is 3 while hourly counts sum to 4. | Disposable PostgreSQL test passes |
| FR-004 | Worker identity tests cover ordinal equality and namespaces/kinds; store/query tests cover case-sensitive and bus/rail tuple separation and per-city separation. | Automated and PostgreSQL tests pass |
| FR-005 | Raw source tests cover deleted/non-vehicle and missing identity records; projection exposes missing/fallback record counts and allowlisted limitations. | Automated tests pass; agency fallback certification pending |
| FR-006 | `StatsOverlayWindowTests` and reported-scope parity tests cover local midnight, grace, 23/25-hour DST days, Kathmandu fractional offset, Pending today, and the reused 24-hour grid. | Automated tests pass |
| FR-007 | Worker accumulator test verifies a successor-hour identity is not assigned backward; receipts are recorded at decode boundaries. | Automated tests pass |
| FR-008 | Service and PostgreSQL tests verify route selection does not change reported city-feed counts. | Automated and PostgreSQL tests pass |
| FR-009 | All-scope PostgreSQL oracle verifies city A=3 and city B=1 are summed as 4 without cross-city deduplication; service tests preserve different city zones. | Automated and PostgreSQL tests pass |
| FR-010 | Store tests exercise exact membership persistence, restart-safe immutable cohorts, retries, sticky conflicts, and retention cascade. | Disposable PostgreSQL tests pass; no historical backfill performed |
| FR-011 | Shared DTO and service projection carry nullable counts, independent states/denominators, per-city windows, and bounded limitations. | Shared and WebAPI tests pass |
| FR-012 | Worker boundary/gap tests and PostgreSQL diagnostic tests verify source coverage and identity integrity gates. | Automated and PostgreSQL tests pass; real-source cadence not certified |
| FR-013 | PostgreSQL fixtures cover missing, NoData, conflict, source/cadence mismatch, unsupported definition, identity loss, malformed diagnostics, empty Partial, and contributing Partial cases. Exclusions have one primary reason per hour; limitations do not change denominators. | Disposable PostgreSQL tests pass |
| FR-014 | Client projection tests distinguish null from observed zero and verify Partial coverage copy, Pending/Unavailable copy, and allowlisted coverage explanations. | Reported-vehicle projection tests pass |
| FR-015 | PostgreSQL store tests verify semantic retry, conflicting member/run quarantine, concurrent identical writes, sticky conflict, and atomic rollback. Worker tests verify restart cannot manufacture Complete coverage. | Automated and PostgreSQL tests pass |
| FR-016 | Options and reservation tests enforce finite source, identity, byte, queue, write, and retention bounds; PostgreSQL tests verify identity byte constraints and bounded retention. | Automated and PostgreSQL tests pass; production-size memory/performance measurements pending |
| FR-017 | Extraction tests cover unavailable spatial readiness; queue admission/sink failure tests verify capture errors do not escape polling and reservations are released. | Automated tests pass; browser audio/map regression review pending |
| FR-018 | Shared serialization tests verify public summaries omit identity material; SQL returns aggregate-only windows, and an in-flight transaction test observes pre-commit then post-commit populations. | Automated and PostgreSQL tests pass |
| FR-019 | A real PostgreSQL test applies only pre-reported migrations and runs the disabled SQL branch without the new relations; older JSON payload defaults are tested in Shared. | Automated and PostgreSQL tests pass; cache-key compatibility tests remain open |
| FR-019a | Global mode/city configuration tests, exclusive WebAPI host guard tests, DryRun registration checks, independent read catalog tests and real PostgreSQL missing-policy tests cover the capture revision. | All related cases pass; production defaults remain disabled |
| FR-020 | English count/coverage resources, responsive theme styles, explicit observed-identity definition, and heading associations are implemented. | Code and projection tests pass; browser/accessibility review not run |
| FR-021 | Independent arithmetic, source-before-filtering, restart/conflict, disposable PostgreSQL, and project test evidence is recorded here. | Partially complete; browser, visitor, matched-load, real-source, and release-owner evidence remain open |
| SC-001 | Independent PostgreSQL oracle verifies city count 3 and All total 4 with cross-hour and cross-city distinctions. | Automated fixture passes |
| SC-002 | Unmappable raw feed identities are tested before mapping filters; deleted/non-vehicle records are excluded. | Worker tests pass |
| SC-003 | Complete, Partial, successful-empty zero, missing, NoData, conflict, policy mismatch, identity loss, and Pending scenarios are covered. | Automated and PostgreSQL tests pass |
| SC-004 | Existing Stats policy grid tests cover local midnight, grace, DST folds/gaps, fractional offset, current-hour exclusion, route independence, and All city windows. | Automated tests pass |
| SC-005 | Razor markup and CSS compile; visual widths, 200% zoom, theme contrast, and assistive technology were not exercised. | Not run |
| SC-006 | Five-visitor comprehension session has not been conducted. | Not run |
| SC-007 | No matched-load benchmark or EXPLAIN ANALYZE comparison against feature 058 was run. | Not run |

## Project test results (latest: 2026-10-17)

| Project/check | Result |
| --- | --- |
| TransitDataWorker.Tests | 217 passed, 0 skipped, 0 failed |
| WebAPI.Tests full suite | 311 passed, 0 skipped, 1 failed with all three disposable PostgreSQL fixture variables configured; failure is the existing category migration table-list assertion described below |
| WebAPI.Tests reported-vehicle cases within full run | 21 passed, 0 skipped, 0 failed, including all reported-vehicle PostgreSQL fixtures |
| Shared.Tests | 40 passed, 0 failed |
| Client.Shared.Tests | 127 passed, 0 skipped, 1 failed; the failure is `StatsOverlayProjectionTests.CityProjectionPreservesServerSharesRatiosZeroAndKnownAmountsAcrossPlotGap`, which asserts the city category timeline is stacked. The reported-vehicle projection tests pass (3/3). |
| Data, Worker, WebAPI, Shared and Client.Shared builds through project test runs | Passed; existing analyzer/package warnings remain. |
| Skill references (previous implementation run) | `tools/sync-skills.ps1 -Mode Check` reported generated copies synchronized after `-Mode Sync`; not rerun for this revision because skill references were unchanged. |

The WebAPI failure is `CityCategoryStatisticsMigrationTests.SchemaMigrationAddsTwoCategoryTablesAndPreservesCityOnlyTable` at line 19. Its exact expected table list omits the two already checked-in reported-vehicle tables; the fixture migrates to the latest schema and returns those tables. No schema, migrations or category test files changed in this revision.

The remaining Client.Shared failure is in the existing city distance timeline expectation. The reported-vehicle change does not modify that timeline projection path; the test remains visible rather than being changed as part of this feature.

## Global capture revision evidence (2026-10-17)

Capture configuration has no per-city selector. Disabled yields an empty catalog/runtime; DryRun and Persistence derive all eligible cities from the configured registry. No-policy cities are explicitly unsupported; partial endpoint catalogs (including NYC bus and enabled MARTA rail) and invalid policies fail startup. Configured city and source caps remain enforced. Both checked-in host settings use Mode=Disabled, and the WebAPI read flag remains false.

`ReportedVehicleConfigurationTests` proves every eligible city is observed and finalized in each supported host/mode, Disabled emits no batches, missing/unconfigured cities cannot emit evidence, invalid catalogs fail safely, and capacity limits remain enforced. `ReportedVehicleHostTests` proves identical global Persistence settings are accepted for WebAPI and rejected for standalone Worker before startup, DryRun registers no writer/retention service, Disabled is a no-op, and read policies remain available independently of capture. Operational single-owner topology for multiple WebAPI replicas remains a release prerequisite.

`ReportedVehicleScopeTests` proves unsupported cities stay in the original expected windows while the service sends one bounded query with only supported read policies; unsupported results are unavailable/null with zero contributing/fully covered hours. `ReportedVehicleQueryTests.MissingPoliciesReturnUnavailableEvenWithRetainedMemberships` runs real PostgreSQL with supported, unsupported-with-old-evidence and unsupported-without-evidence cities. The missing-policy state is source_policy_mismatch, never observed zero; the supported count remains intact. An entirely empty policy set also returns null counts. Source identity/cadence policies, schema and route filtering are unchanged.

All 56 test cases whose names contain ReportedVehicle passed: Worker 31, WebAPI 21, Shared 1, Client.Shared 3. The four full project runs above include these cases; the full-suite failures are explicitly retained above.

Commands ran from the repository root (all use --no-restore):

```powershell
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests --no-restore --logger 'trx;LogFileName=capture-global-worker.trx' --results-directory TestResults/059-global-capture
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests --no-restore --logger 'trx;LogFileName=capture-global-webapi.trx' --results-directory TestResults/059-global-capture
dotnet test src/ChefKnifeStudios.TransitJazz.Shared.Tests --no-restore --logger 'trx;LogFileName=capture-global-shared.trx' --results-directory TestResults/059-global-capture
dotnet test src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests --no-restore --logger 'trx;LogFileName=capture-global-client.trx' --results-directory TestResults/059-global-capture
```

WebAPI ran with REPORTED_VEHICLE_HISTORY_DISPOSABLE_CONNECTION, ROUTE_HOUR_HISTORY_DISPOSABLE_CONNECTION and CITY_CATEGORY_INSIGHTS_DISPOSABLE_CONNECTION pointing to the freshly provisioned local PostgreSQL 16 Alpine container transitjazz-059-global-capture (loopback port 55459, disposable admin database). Fixtures created/dropped isolated test databases. The temporary container was removed after validation. TRX files are local ignored artifacts under TestResults/059-global-capture. git diff --check passed. No production database, live feed, deployment, production capture enablement or git commit was involved.

## Explicitly not run

- `ReportedVehicleHistory.Mode` and `StatsOverlay.ReportedVehiclesEnabled` remain disabled in checked-in settings. No application migration, capture, read enablement, deployment, or count-only backfill was performed.
- No live agency feed was polled for cadence measurements and no source identity contract or single-owner assignment was certified.
- Browser, accessibility, audio/map/navigation regression, and local dialog acceptance were not run. See [browser validation](browser-validation.md).
- Matched-load, SQL plan, memory/reservation-lifetime, and storage-growth measurements were not run. See [performance validation](performance-validation.md).
- The requested five-visitor comprehension session was not run.

## Unresolved release certifications

Verify each agency's stable identity field and namespace, fallback rules, sequential cadence and gap limits, and exactly one persistence owner per city. Measure capture allocations/reservations, PostgreSQL index/storage growth, cleanup lag, query plan and latency under supported bounds, whole Stats latency against feature 058, and worker overhead against its matched baseline. Complete browser/accessibility and visitor reviews. Keep source-control capture/read settings disabled until those operational gates and a separate rollout authorization are complete.
