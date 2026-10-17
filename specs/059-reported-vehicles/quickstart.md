# Quickstart and Validation: Reported Vehicles

This is an implementation/acceptance procedure, not a claim that tables or capture are live. Do not use production credentials or apply a production migration during this workflow. Follow repository AGENTS rules: no commits or auto-commit hooks.

## 1. Build and ordinary fixtures

Use existing .NET 10 solution/projects. After implementing the task list, run targeted existing projects (repo root):

```powershell
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.csproj
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.csproj
dotnet test src/ChefKnifeStudios.TransitJazz.Shared.Tests/ChefKnifeStudios.TransitJazz.Shared.Tests.csproj
dotnet test src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/ChefKnifeStudios.TransitJazz.Client.Shared.Tests.csproj
```

Use existing xUnit conventions, injected TimeProvider and deterministic fixtures; no wall-clock sleeps. Inspect current PostgreSQL fixture opt-in configuration in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/Statistics/RouteHourStatisticsDatabaseFixture.cs`, then use equivalent disposable-database setup for new store/query/migration fixtures. Record skipped integration tests explicitly; do not report SQL validation from mocked tests.

## 2. Independent arithmetic oracle

Create manual evidence not derived from the implementation accumulator:

| City/hour | Intact successful identities | Coverage |
| --- | --- | --- |
| A/H1 | bus/descriptor/x, bus/descriptor/y; x also in overlapping second feed | Complete |
| A/H2 | bus/descriptor/x, rail/agency-train-id/x | Partial (one expected source failed) |
| A/H3 | Empty successful response | Partial (startup boundary absent) |
| A/H4 | bus/descriptor/z | Conflict; excluded |
| B/H1 | bus/descriptor/x | Complete |

For A window H1-H4, count=3, expected=4, contributing=3, fullyCovered=1, Partial. Hourly counts sum to four, intentionally wrong. H3 alone returns 0/Partial, not Unavailable. H4 alone returns null/Unavailable. All over those windows sums A's three plus B's one =4; no cross-city deduplication. Ordinal X vs x contributes separately. Use actual window grid to construct day/24-hour cases; remaining absent hours count in denominator and never become zeros. Route selection cannot change these counts.

## 3. Source-before-filtering fixtures

- GTFS descriptor-ID record missing coordinates/route remains counted. Trip-update/alert/deleted record and whitespace ID never establishes vehicle. FeedEntity ID without certified rule is excluded with missing-ID disclosure. Repeated stale records count once.
- MARTA original TRAIN_ID survives invalid Line/coordinates and realtime=false; missing ID is diagnostic. Valid empty list succeeds; null/malformed root fails. Generic empty protobuf feed succeeds. Raw decoder failure never produces healthy zero.
- NYC missing/unavailable stop-offset table still performs raw fetch and reports identities while synthesis returns no positions. Capture exact independent response receipt times across hour boundary while whole-city synthesis/fetch completion is later.
- NYC overlapping subway endpoints share domain; bus/rail text collisions remain separate. Policy config reaches both host registrations. Disabled/empty sources are not expected.

## 4. Coverage and integrity fixtures

Use controlled source timelines with explicit predecessor/first/last/successor receipts. Verify source-local gap, start/end witness, failed attempt, missing source, successful-empty source, startup/restart/shutdown and maximum gap deadline. Healthy source cannot hide another outage. Successor witnesses do not assign identities backward. Missing IDs qualify intact Partial; oversize/unrepresentable IDs, integer overflow, byte/item/source/header limits, clock regression and lost extraction exclude entire cohort.

Exercise finite byte caps as well as count caps; prove reservations released after transfer/write/drop and all clone/buffer lifetimes accounted. Queue saturation never blocks poll; DB timeout/retry/drain leaves missing coverage. Source-set/policy mutations get explicit exclusion. Never silently combine runs or concurrent producers into Complete.

## 5. Real PostgreSQL fixtures

Apply generated migration only to disposable test DB, verify constraints/ordinal behavior/FK cascade/byte lengths/UTC alignment and new empty header support. Test atomic failure midway through memberships (no half cohort), exact retries, changed header and changed membership sets, changed run, concurrent writers, sticky quarantine after retry, and bounded cleanup beyond supported windows. Empty Partial cohort yields observed zero; unsupported fingerprint/definition/cadence has distinct exclusion.

Query fixtures must independently seed headers/memberships and compare COUNT DISTINCT population with hand-built oracle. Ensure no route-hour rows still permits complete feed counts and complete route data with absent reported rows gives Unavailable. Enabled reads use one statement snapshot during concurrent atomic write. Disabled SQL on a DB without new tables still serves distance and unavailable reported summaries; old response payload lacking summaries renders Unavailable. No raw identities leave Data query or public JSON. Cache key switches on read/definition/source/policy changes.

## 6. Window and presentation acceptance

Reuse existing Stats window fixtures: local midnight before/after ingestion grace, DST repeated/skipped hours, fractional UTC offset and current-hour exclusion. Today date is determined before grace, and both counts use identical adjacent distance boundaries. Early today with no eligible hours gives Pending. All uses each city's local day, preserves per-city dates/zones, sums populations and discloses mixed coverage/pending cities.

Browser check real Stats dialog at 320px and larger, 200% zoom, both themes, long headings and large counts. Verify distance remains visually primary, integer/null/zero formatting, separate coverage, persistent route-scope label, accessible count heading association and keyboard reading order. Confirm audio/map/navigation remain unchanged. At least four of five representative visitors must explain periods, route independence, observed identity vs physical fleet and partial/unavailable meaning; record results, not an inferred pass.

## 7. Performance and rollout evidence

Benchmark matched existing history/Stats load with and without reported capture/read against original recorded baseline. Record worker p95 cycle overhead, extraction/accumulator/queue allocations, max reserved/live memory, query EXPLAIN ANALYZE, cold/prepared Stats p95 and whole five-second timeout, response/cache bytes and seven-day projected membership storage. Combined overhead <=5%; no second allowance. Bound-sized cases must fail explicitly without starving polling; validate actual source cadence especially sequential NYC feeds. Proposed finite defaults are subject to these measurements, never certified by writing this document.

Release order requires a separately authorized operational task: verify actual identity contracts and single owner per city; deploy schema; DryRun; enable selected-city persistence; audit retained source coverage/integrity; pass performance/browser/visitor evidence; then enable reported reads. Default settings remain disabled in source control. Disable reads then collection for rollback. No backfill from existing hourly counts is possible.

During implementation record actual target, schema version, UTC coverage range, source contract references, limits, ownership, query plans, fixtures, browser/visitor and budget evidence in `specs/059-reported-vehicles/validation.md`. Update canonical `skills/transitjazz-db/references/{tables,queries,results}.md` to distinguish exact membership union from sampled/hourly counts, then run existing skill synchronization procedure. Planning does not query live DB or enable collection.
