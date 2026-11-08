# Quickstart: Implementation and verification handoff

## Scope and prerequisite checks

Work from repository root with .NET 10 SDK. `.specify/feature.json` selects `specs/063-atlanta-rail-statistics`; branch is `064-atlanta-rail-statistics`. Read spec, plan, data model and contracts before implementation. Preserve four modified Worker/WebAPI settings files and untracked source design/investigation/toast documents. Never run git commit or commit-producing hooks. Implementation and release evidence live in [validation-evidence.md](validation-evidence.md); this runbook does not imply unrun database, live-serving or browser gates have passed.

No live key is required for deterministic capture/query/UI work. Use injected HTTP handlers/source doubles/publisher doubles and controllable TimeProvider. PostgreSQL integration uses only an explicitly provisioned disposable database, via `ROUTE_HOUR_HISTORY_DISPOSABLE_CONNECTION`; the existing fixture rejects names without 'disposable' and creates an isolated child database. Provide its secret connection through approved local environment tooling without printing it. A skipped database test is not a pass. Do not run migrations against serving databases in this workflow.

## Implement the first observable regression

1. Add the behavior regression before changing gates in existing Worker/WebAPI tests. Drive one fixed UTC hour with healthy bus positions that produce known nonzero accepted movement and failed rail polls; include predecessor/successor boundary polls and close the hour through injected time, without sleeping.
2. Assert finalized bus valid activity count/numerator/denominator/population and distance survive; rail has no valid zero. Persist the actual envelope using the real disposable store/writer path, query opted-in v2, then assert Partial city coverage, non-null bus distance and null rail measures. Project the returned snapshot into visible selection. Repeat the symmetric outage.
3. Complete-zero must use successful empty observations and proven continuity; both-failed must be null. Add mid-hour outage/recovery/disable cases, independent source boundaries and baseline reseed so no outage movement is accepted. Publication-only failure retains movement/activity.
4. Add database/contract checks for every new evidence field, canonical JSON retry equality, mismatched retry quarantine, atomic insertion, legacy nulls, v1 Complete/Partial admission and unsupported/conflicting/mixed-run/loss exclusions.
5. Verify selection/arithmetic, observed ratios, aligned versus unaligned samples, historical membership, available-distance shares, omitted bubbles, gaps, local-day/DST/grace and cache/refresh/fallback behavior. Preserve healthy other-city and reported-vehicle regressions.

Detailed scenario assertions, exact proposed test paths, lowest reliable layers, RED→GREEN→REFACTOR ordering and FR/SC traceability are supplied by [testing-plan.md](testing-plan.md) and the testing section of [plan.md](plan.md). Extend existing suites; the narrow journey fixture composes the real existing seams rather than introducing a second framework. Its WebAPI test project may add a test-only Client.Shared reference to project the actual persisted/query result; pure projection does not replace the controlled browser gate.

## Validation commands (to run during implementation)

Run from repository root. Commands are instructions, **not results from this planning phase**.

```powershell
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.csproj -c Release
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.csproj -c Release
dotnet test src/ChefKnifeStudios.TransitJazz.Shared.Tests/ChefKnifeStudios.TransitJazz.Shared.Tests.csproj -c Release
dotnet test src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ChefKnifeStudios.TransitJazz.Client.Shared.Tests.csproj -c Release
dotnet build src/ChefKnifeStudios.TransitJazz.sln -c Release
git diff --check
```

Inspect test output for skipped RouteHourPostgresFact cases; rerun meaningful database cases with the guarded disposable connection before claiming persistence completion. Existing suites include RouteHourWorkerIntegrationTests, RouteHourCoverage/Activity/Movement/FailureIsolationTests, RouteHourStatisticsStore/Migration/WriterTests, StatsOverlayQuery/Endpoint/Cache/Projection/WindowTests, Shared StatsOverlayContractTests and client transport/snapshot/projection/chart/accessibility tests. The proposed AtlantaAvailableStatisticsJourneyTests must exercise actual fetch-result eligibility through real persistence/query and client projection; current direct-reconciliation fixtures alone bypass the combined-fetch gate. Focused commands and zero-skip database acceptance are in [testing-plan.md](testing-plan.md#execution-and-evidence-gates). Current CI build/deployment workflows do not run these tests automatically.

After canonical source skill update during implementation:

```powershell
./tools/sync-skills.ps1 -Mode Sync
./tools/sync-skills.ps1 -Mode Check
```

Review generated mj-gtfs copies for all three catalog targets and avoid unrelated skill drift. Planning does not edit/synchronize skill content.

## HTTP acceptance matrix

Use identical POST body/selection across absent, explicit v1 and v2 headers, interleaved and concurrent. Verify contract-specific response/semantics, Vary, no-store, cache identity/scope slots/in-flight isolation and unchanged size/concurrency bounds. Unsupported/empty/multiple header values return 400 `unsupported_stats_contract` without SQL/cache fill. v1 remains conservative; no legacy Partial or opted-in Partial measurements leak into old responses.

In v2: city-wide healthy bus/unavailable rail returns known bus distance with Partial; bus-only historical selection can be Complete; rail-only is unavailable. Shares show bus=100% of available distance and rail=null. Both-failed=null, proven-zero=0 with undefined percentages null. Category/card/timeline distance reconciles. Unaligned aggregate activity is null with reason while valid route bubbles/distance remain. New Partial replaces old Complete/previous-day correctly; cancelled/out-of-order selection results never replace current data.

## Controlled browser gate (later, unrun)

Start local/test WebAPI and client using existing development orchestration and test-only deterministic source stub/fixture. Do not disable production rail. Capture a visible view with nonzero bus statistics while rail is unavailable, then a subsequent view after rail recovery. Verify persistent Partial labels beside cards/headings, available-distance denominator, rail legend/table entry, omitted-bubble reasons and source/hour details. Exercise route changes, current Partial versus fallback, keyboard/table access, mobile, both themes and both unit systems. Check timeline gaps are unconnected. Use actual API results and visible numbers together.

## Authenticated/serving-environment gate (later, unrun)

An operator must provide authorized access to an existing MARTA managed secret reference. Bind the rail endpoint/key from `TransitSources:atlanta:RailRealtime`, with environment override `TransitSources__atlanta__RailRealtime__ApiKey`; this does not depend on Atlanta's position in `Cities[]`. Do not print key/request URI or change secrets/deployments under this workflow. Validate redacted authenticated array fields/types, realtime/position filtering, train deduplication, line alignment and event-time timezone using application HTTP/DTO behavior. Verify the actual serving revision ingests trains once while buses continue and warnings stop. Recovery hour remains Partial; wait for the next fully observed closed UTC hour, ingestion grace and scheduled refresh before expecting Complete. Older outage hours can keep last24 Partial.

## Migration identity, rollout and rollback instructions (documentation only)

The additive EF migration is `20261107184134_AddRouteHourSourceEvidence` in `src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Migrations/`. It adds nine nullable JSONB/scalar/reason columns and version-aware constraints; it performs no data update or history backfill. Existing v1 rows retain null v2 evidence, the v1 Complete constraint branch is preserved, and unknown definitions remain excluded by older readers. The migration's real PostgreSQL upgrade/round-trip/constraint checks are **blocked** until the guarded disposable connection is available; scaffolding/model assertions are not proof of database execution.

Required order is: (1) apply and verify the additive schema, (2) deploy the v2 writer/capture path, (3) deploy the dual-contract server with isolated v1/v2 cache entries, and (4) opt the client into v2. Keep the client on v1 until all earlier stages and their compatibility checks have passed. On rollback, stop v2 writes before reverting to any reader that cannot tolerate the new definition. Retain the additive columns and immutable history; do not run the destructive `Down` migration. Roll the client back through v1 when needed, keep old readers' unknown-version exclusion, and never restore the retired URL or backfill/relabel history to manufacture Complete.

## Evidence ledger required at implementation handoff

Record exact command, result/pass/fail/skip counts, environment, source revision (including uncommitted state), UTC observation/query windows, serving revision where applicable, fixture identity, and browser artifact paths/visible assertions. Record live schema/event-time findings without raw sensitive payload. Explicitly list every unrun gate. Local URL parity, passing fixtures or an old production log do not prove authenticated serving recovery.

Planning status: application/test implementation, Release builds, real database execution, authenticated integration, serving-revision checks, deployment and controlled browser views are **not run**. Planning completion does not declare feature release completion.
