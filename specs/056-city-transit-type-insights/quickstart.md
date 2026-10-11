# Quickstart: City and Transit Type Insights

This is the implementation and release runbook for [plan.md](plan.md). Local implementation is present. Capture remains disabled by default; this runbook does not authorize a production migration, deployment, or capture enablement.

## Current implementation status

Local code now includes the two aggregate tables/store and schema migration, Worker capture and movement/activity/publication hooks, a bounded WebAPI writer, automatic memory-only window sweeping and partial shutdown flush, disabled-default configuration, Bicep/workflow checks, and the five analyst query recipes plus the hourly recipe. The WebAPI host registers the writer before the capture sweeper and Worker; shutdown therefore stops Worker capture first, flushes pending fragments, then drains the writer.

Validation evidence recorded so far:

- The complete WebAPI test suite passed **227/227 with zero skipped**. This includes **52 real category PostgreSQL cases** (store, migration, durability, writer, metric queries, and bound `EXPLAIN` plans for all six recipes) plus the legacy city-only store regression on its own isolated database. Each category integration case used a uniquely named disposable database.
- The complete Worker suite, including hosted-sweeper, shutdown-flush, generation/watermark, unknown-boundary regressions, and six live reconciliation integration cases, passed **140/140 with zero skips**.
- The writer tests exercised a full queue while its storage dependency was blocked and confirmed admission returned `false` immediately. A controlled noncooperative dependency also confirmed shutdown returns at its configured deadline; logs retain committed insert/unchanged/conflict counts across backing-minute retries.
- Bicep compiled and `bicep/main.json` was regenerated. The default configuration and deployment parameter keep capture globally disabled and the city-exclusion set empty.
- PostgreSQL integration tests create and remove only uniquely named databases in the explicitly provisioned disposable loopback cluster. No operator database was used.
- The native Windows EF migration bundle built successfully and applied both migrations to a separately owned disposable database. Reapplying it reported no migrations pending. Schema verification found exactly `city_minute_statistics`, the two new category tables, and EF history, with two migration records. Bundle execution required the existing `ConnectionStrings__TransitJazzDB` environment binding even when `--connection` was also supplied.
- The Docker engine was unavailable, so the Linux migration-image build from the Data Dockerfile is unverified. No migration was applied outside disposable test databases.
- The default full-solution build is blocked by `NETSDK1147`: the installed .NET 10 SDK does not have the `wasm-tools` workload required by the existing Client.WebApp. Secondary solution builds with local command-line WASM/AOT overrides also failed with `NETSDK1147`. The server source projects compiled in the affected test/build checks.
- Matched-load p95 measurements, a 24-hour pilot, analyst interpretation review, schema-first operator deployment, and city expansion have not been performed.

## Before enabling capture

1. Review the current [plan](plan.md), [tasks](tasks.md), disabled defaults, and remaining release gates.
2. Keep `CityCategoryInsights.Enabled=false` in committed defaults. Leave the existing `HistoricalStatistics` settings under their independent contract.
3. Supply the existing `ConnectionStrings__TransitJazzDB` using the established server secret configuration. Category capture requires it when enabled; it requires no Grafana reader credential.
4. Confirm `TimeZoneId` for every included city. The global flag enables all configured cities except explicit exclusions. For a one-city pilot, exclude the other configured cities and check the pilot city's source timestamps and normal timing.
5. Record the healthy-gap limit, starting from 30 seconds for the ten-second cycle, and confirm the measured healthy city-cycle spacing fits it.

## Local implementation checks

From the repository root, after implementation:

```powershell
dotnet build src/ChefKnifeStudios.TransitJazz.sln
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.csproj
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.csproj
```

The category PostgreSQL checks require an explicitly provisioned disposable test database through `CITY_CATEGORY_INSIGHTS_DISPOSABLE_CONNECTION`. The legacy city-only store integration check uses its separate `TRANSITJAZZ_TEST_DB` setting and calls `EnsureDeleted`; always give it a separate uniquely named disposable database. Never pass an operator/user database value to either setting. Do not silently treat skipped integration cases as release proof. New feature fixtures create and drop only their uniquely named test databases.

The idempotent EF migration script was generated and inspected. Its feature section creates only `city_category_hour_statistics` and `city_category_minute_statistics`, then records EF migration history; it contains no changes to `city_minute_statistics`. The native Windows bundle was built and exercised against a disposable database. It does not replace the required migration-image validation.

Preserve the existing worker dashboard/source-contract checks. Confirm the expanded EF model contains the original city-minute entity and exactly the two new category entities, without new BaseEntity audit columns.

### Temporary validation environment cleanup

On 2026-10-02, session permissions denied both `pg_ctl stop` and `Stop-Process` for the owned disposable PostgreSQL process (PID 41184). Automatic command review also rejected recursive removal of its temporary directory. The local test cluster and files therefore remain at `C:\Users\hfaul\AppData\Local\Temp\transitjazz-056-72d00dcca63f408a851ff9bdd0c93c7a`. Stop that exact cluster from a normal terminal before removing its directory and `transitjazz-056-cluster-metadata.json` from the same Temp folder:

```powershell
& 'C:\Program Files\PostgreSQL\18\bin\pg_ctl.exe' -D 'C:\Users\hfaul\AppData\Local\Temp\transitjazz-056-72d00dcca63f408a851ff9bdd0c93c7a' stop -m fast -w
```

## Create and inspect the schema-only migration

The schema-only migration `20261001204629_CreateCityCategoryStatistics` has been generated and exercised by disposable PostgreSQL migration checks. Its native Windows EF bundle was built, applied to a disposable database, and rerun with no pending changes. Use the existing design-time Data factory and secret-based connection configuration if regenerating the script or bundle. The bundle command requires `ConnectionStrings__TransitJazzDB` in its environment even when a connection override is provided; no credential is placed in command arguments.

```powershell
dotnet tool restore
dotnet ef migrations script --idempotent --project src/Server/ChefKnifeStudios.TransitJazz.Server.Data --startup-project src/Server/ChefKnifeStudios.TransitJazz.Server.Data --output artifacts/city-category-statistics-migrations.sql
dotnet ef migrations bundle --project src/Server/ChefKnifeStudios.TransitJazz.Server.Data --startup-project src/Server/ChefKnifeStudios.TransitJazz.Server.Data --output artifacts/transitjazz-migrations.exe
docker build --file src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Dockerfile --tag transitjazz-data-migrations:local .
```

Create the local `artifacts` output directory before exporting the script. Set the existing `ConnectionStrings__TransitJazzDB` secret in the process environment before bundle generation. Inspect the new migration section: only the two category tables, their composite keys/checks, and EF migration metadata are added. The existing table/source fields remain intact. Neither the migration nor bundle executes feed/Grafana queries or seeds historical statistics. The native bundle was validated here; Docker image validation remains outstanding.

Apply the reviewed migration through the current operator process from a database-allowed machine, verify the intended database and migration version, then use the existing `server-dev` deployment gate. This runbook does not authorize production deployment.

## Configure bounded capture

Initial nonsecret configuration:

```json
{
  "CityCategoryInsights": {
    "Enabled": false,
    "DisabledCities": [],
    "MaxObservationGapSeconds": 30,
    "QueueCapacity": 256,
    "MaxBatchRows": 128,
    "CommandTimeoutSeconds": 5,
    "MaxWriteAttempts": 3,
    "ShutdownDrainSeconds": 15
  }
}
```

`CityCategoryInsights__Enabled=true` enables capture for every configured city. Optional exclusions use a city name as each indexed setting's value:

```text
CityCategoryInsights__Enabled=true
CityCategoryInsights__Disabled_0=toronto
CityCategoryInsights__Disabled_1=denver
```

This captures the other five configured cities. Exclusions are case-insensitive. With the global flag false, no city captures insights. JSON configuration can use `DisabledCities`, and Bicep uses `enableCityCategoryInsights` plus the optional `cityCategoryInsightsDisabledCities` array. To capture all cities, set the global flag true and leave exclusions empty. The old `EnabledCities` setting has been removed; remove legacy enabled-city environment entries when updating deployment configuration.

For a one-city pilot, exclude every other configured city. Existing Eastern US cities use `America/New_York`; Toronto uses `America/Toronto`; Denver uses `America/Denver`. Validate the included cities' zones and any city-specific cadence override before enabling. The existing database migration and server database binding remain prerequisites.

The global enablement/exclusion configuration was updated on 2026-10-04. The server build and Bicep compilation passed for this update. The earlier test results above predate this configuration change; tests have not been rerun for this update.

Confirm `Channel` admission is synchronous `TryWrite`, with observable false on a full queue. A slow/unavailable database must not make a live transit cycle await storage. Reports expose aggregate insertion, unchanged retry, conflict, omission, and loss counts without raw transit IDs or credentials.

## One-city comparison and pilot

1. Check valid joined activity counts, accounting for duplicate IDs and activity-before-snap eligibility differences from existing city metrics.
2. Compare category crossing counts against actual successfully published city batches. Prepared `TonesEmitted` and failed publication are not successful cadence.
3. Exercise stationary, reverse, repeated/out-of-order/missing timestamps, transfers, and teleport guard examples. Changing interpolated positions with repeated upstream timestamps remain ineligible movement.
4. Observe boundary closure and startup fragments. A complete hour must have 60 durable complete/nonconflicting minutes and the intact exact hour population.
5. Observe 24 healthy hours after the initial partial hour. Require at least 99% complete minutes and explain every missing/incomplete period.
6. Compare matched-load capture disabled/enabled p95 processing duration; require no more than 5% increase and verify storage failure does not interrupt live cycles.
7. Exercise identical retry and conflicting delivery using disposable/local fixtures. Identical counts remain unchanged; conflict quarantine persists and excludes definitive hourly results.

## Query verification

Use [hourly-insights.sql](contracts/hourly-insights.sql) with bound city/category/version/UTC-range values. It deliberately requires durable backing-minute reconciliation; a stored Complete label alone is insufficient.

Use [the result contract](contracts/observed-category-statistics-v1.md) for the accompanying requested-window coverage grid, weighted period totals, local-hour grouping, and definitions.

Reference values:

| Observation | Expected result |
| --- | --- |
| Complete hour: 1,200 m, 3 vehicles, 2 intervals | 400 m/vehicle-hour; 600 m/update |
| Two hours: 1,200/3 and 1,800/2 | 600 m/vehicle-hour |
| Eligible activity cycles 2, 0, 4 | 2 mean vehicles/cycle |
| Local-hour crossing observations 50 and 70 | 60 opportunities/complete hour |
| No active vehicles in a complete hour | Activity zero; vehicle-hour mean unavailable |
| Missing or conflicted backing minute | Definitive hourly measures unavailable |

Verify repeated local hours retain separate UTC identities/date/offset and contribute separately to typical-hour cadence. A skipped local hour has no invented observation. Partial request boundaries show the actual retained minute bounds and are never prorated.

At query handoff, have five representative analysts interpret the returned definitions. At least four must distinguish vehicle-hour from trip distance and published opportunities from audible notes.

Record the first retained category observation. Queries before it report unavailable history; city-only history supplies no category backfill.

## Expand or disable

Remove city exclusions to expand capture after the pilot passes source eligibility, durable reconciliation, coverage, and overhead checks. Keep dynamic categories and per-city zones validated.

Disable capture if the pilot fails, preserving historical rows and their coverage/conflict evidence. Investigate bounded reason summaries and the missing-window queries, then re-enable only after the failing evidence is resolved. V1 has no automatic conflict overwrite, retention deletion, durable replay, listener telemetry, or backfill.
