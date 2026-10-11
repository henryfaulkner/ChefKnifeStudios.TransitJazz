# Hourly Route History Validation

## Setup baseline — 2026-10-10

- Branch: `056-city-transit-type-insights`; no branch switch or commit.
- Worker baseline: `dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.csproj` — 140 passed, 0 skipped.
- WebAPI baseline: `dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.csproj` — 172 passed, 52 skipped. The skipped cases were existing database-backed checks.
- EF tools: local `dotnet-ef` 10.0.4 is installed from `.config/dotnet-tools.json`.
- `ROUTE_HOUR_HISTORY_DISPOSABLE_CONNECTION` is not configured. No database was created or modified.
- Existing package warning: NU1903 reports the known Microsoft.OpenApi 2.0.0 advisory.

## Implementation and deterministic verification — 2026-10-10

- Worker suite: 170 passed, 0 skipped.
- WebAPI suite: 187 passed, 60 skipped. The 60 include the 52 pre-existing database checks and 8 route-history PostgreSQL checks; route-history tests compile but did not execute without the explicit disposable connection.
- WebAPI build succeeded. Data Release build succeeded with 0 warnings and 0 errors.
- Added schema-only migration `20261010151240_CreateCityRouteHourStatistics` and updated the EF model snapshot. The idempotent migration SQL script and local migration bundle were generated for review; no migration was applied.
- `docker build` could not build the Data migration image because the Docker Desktop Linux engine was unavailable. The image build remains pending.
- Deterministic coverage includes canonical route identity/fingerprints, movement and activity accounting, cycle/boundary coverage, caps/catalog changes, late completion after sweep, isolated source/publication/sink failures, mode and host registration, bounded writer admission, and lost-acknowledgement retry.
- The route-history SQL and prepared PostgreSQL fixtures cover missing/historical route keys and spring/fall DST behavior, but those database assertions remain unexecuted.
- No production database, deployment, or live collection was touched. Defaults remain disabled with dry run enabled.

## Outstanding acceptance

- Execute the 8 existing route-history PostgreSQL tests against an explicitly owned disposable database. Expand the query fixtures for weighted measures, partial-boundary rows, definition/cadence partitions and an entirely missing all-route city window before closing T029/T032-T035; then verify both DST transitions and local-zone handling.
- Finish the remaining planned host-scope/category, writer timeout/classification/shutdown, instrumentation fault-injection, and stop-order tests (T022, T023, T036-T038 in `tasks.md`).
- Complete selected-city catalog/key/population/timestamp/cadence preflight, matched-load overhead and resource/cardinality/storage measurements, and 24-hour production observation through the existing operator process.
- Record at least four correct interpretations from five representative analysts. No analyst contact was made.
- No deployment or schema application is authorized or performed by this implementation run. Keep pending operational evidence distinct from local build/test results.
