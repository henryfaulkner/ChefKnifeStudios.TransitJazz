# Quickstart: Hourly Route History

**Status**: Route-hour capture, schema, writer modes and SQL recipes are implemented on the user's current branch. Worker and WebAPI test suites pass; 8 route-hour PostgreSQL checks are gated on an explicitly owned disposable database. The idempotent SQL script and local migration bundle are generated, while the Docker migration-image build, load, 24-hour coverage and analyst checks remain pending.

## Implementation reference

Use [tasks.md](tasks.md), [plan.md](plan.md), [data model](data-model.md), and [contract](contracts/observed-route-hour-statistics-v1.md). Stay on the user's current branch, `056-city-transit-type-insights`; `.specify/feature.json` pins `specs/057-hourly-route-history`. Do not switch branches. Commit and push only when explicitly requested.

Use existing .NET 10 projects and packages. Add the fourth statistics entity, not a new service or Data project. Keep all enabled/dry-run/city controls in the existing HistoricalStatistics section. Capture should be disabled in committed defaults, with dry run true.

## Verify focused behavior

From the repository root, run the focused and regression checks:

```powershell
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.csproj
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.csproj
dotnet build src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/ChefKnifeStudios.TransitJazz.Server.WebAPI.csproj
```

The [plan's validation strategy](plan.md#validation-strategy) specifies eligibility, clock, failure-isolation, writer and host checks. New deterministic tests cover those worker and host behaviors. PostgreSQL tests remain skipped until the explicit disposable connection is configured.

Provision an explicitly owned disposable PostgreSQL database for route tests. The planned independent `ROUTE_HOUR_HISTORY_DISPOSABLE_CONNECTION` environment variable must point only to that database; fixture validation requires its name to contain `disposable`. Inject its connection securely without printing it. New fixtures must never call `EnsureDeleted` against a user database or reuse the legacy city-store destructive fixture. Without the disposable environment, disclose PostgreSQL checks as skipped, not passed. Copy [route-hour-insights.sql](contracts/route-hour-insights.sql) into the WebAPI test output and exercise its prepared parameters.

Required database checks include late-command whole-envelope rollback, concurrent identical/differing cohorts/runs/versions, post-lock fresh lookup, lock/attempt budgets, acknowledgement loss, sticky quarantine/no fragment merging, numeric/UTC constraints, missing/partial/conflicted/boundary/weighted/historical-route and DST queries.

## Create and inspect schema artifacts

Use the existing Data design-time factory and EF 10.0.4 tool. Provide `ConnectionStrings__TransitJazzDB` through the existing secure environment binding; design-time commands require configuration but must not print credentials. Use design-time or owned disposable values for generating artifacts. Check the existing tool manifest before installing anything; use the repository's existing tool setup.

```powershell
dotnet tool restore
dotnet ef migrations add CreateCityRouteHourStatistics --project src/Server/ChefKnifeStudios.TransitJazz.Server.Data --startup-project src/Server/ChefKnifeStudios.TransitJazz.Server.Data
New-Item -ItemType Directory -Path artifacts -Force | Out-Null
dotnet ef migrations script --idempotent --project src/Server/ChefKnifeStudios.TransitJazz.Server.Data --startup-project src/Server/ChefKnifeStudios.TransitJazz.Server.Data --output artifacts/route-hour-statistics-migrations.sql
docker build --file src/Server/ChefKnifeStudios.TransitJazz.Server.Data/Dockerfile --tag transitjazz-data-migrations:local .
```

The schema-only migration is `20261010151240_CreateCityRouteHourStatistics`. Review its generated migration/snapshot and bundle: exactly the new table, key/index and checks, without data reads/backfill or changes to prior statistics tables. The source design and Data Dockerfile use the existing Linux migration bundle. The idempotent SQL script and local bundle were generated; Docker could not build the Data image because the Docker Desktop Linux engine was unavailable. These artifacts do not apply a migration to production.

## Select operator modes

| Existing controls | Required route behavior |
| --- | --- |
| Enabled=false, any DryRun | No route capture, sweep or writer work |
| Enabled=true, DryRun=true | Capture, validate and safely summarize; zero route writes |
| Enabled=true, DryRun=false | Retain finalized immutable city/hour cohorts atomically |
| Explicit Cities | Same resolved selected scope as city-minute history |
| Empty Cities | Existing fallback to configured cities |
| Any CityCategoryInsights.Enabled value | No effect on route selection/mode |

Preserve enabled-history host validation: database configuration and existing source credentials are still required by the city collector even in dry run. Do not expose independent route switches/exclusions. Limits in `HistoricalStatistics:RouteHours` use the [plan defaults](plan.md#initial-resource-settings); the host validates selected-city cadence and IANA zones, and capture bounds catalog keys, route counts and vehicle populations. The standalone Worker supports explicit dry-run capture and rejects persistent route-hour mode without a real sink. Reuse `enableHistoricalStatistics` and `historicalStatisticsDryRun` deployment parameters. No Bicep tunable wiring was needed.

## Read and interpret history

Execute the six-parameter [SQL recipe](contracts/route-hour-insights.sql) using the existing internal database query process: canonical city, explicit ordinal route-key array or NULL/all retained routes, inclusive/exclusive UTC bounds, supported definition, configured IANA zone. Validate inputs and request size. It is one read-only statement and returns request context, city coverage, hourly diagnostics/definitive measures, route coverage, weighted periods and typical local hours in the same snapshot.

Use [contract reference examples](contracts/observed-route-hour-statistics-v1.md#reference-acceptance-examples) to check weighted averages, inactive zeros, missing history, partial boundaries and DST. A retained Complete hour supplies definitive metrics only if fully contained and definition-compatible; Partial/NoData/Conflict are diagnostic, Missing is never zero. No contributing hours gives null means. All-route selection cannot invent routes in an entirely missing hour; inspect the returned city grid and select explicit historical keys when needed.

Typical local-hour cadence uses complete UTC-hour count; repeated local hours can contribute twice. Movement is accepted along-route observation distance, not completed trips. Population sums count route-vehicle-hours, not period/city unique vehicles. Publications count opportunities, not listener-audible notes. Earliest retained history does not assert continuity.

## Schema-first operator handoff and acceptance

Use the existing schema/deployment runbook in [bicep/README.md](../../bicep/README.md) and [database quickstart](../055-database/quickstart.md). Apply/verify the reviewed migration artifact through that operator process before deploying updated capture code. This implementation run does not apply a migration, deploy, or enable collection. Keep committed/deployment defaults disabled and dry run true. Existing selected city scope controls rollout; no new one-city pilot gate is imposed.

Record implementation/operator evidence in `specs/057-hourly-route-history/validation.md` (created during implementation): exact schema/code artifact, focused/regression/database results and skips; timestamp/cadence/catalog preflight; atomic/retry/failure evidence; resource/cardinality/storage growth; matched-load p95 <=5% increase; at least 99% Complete eligible route-hours over 24 healthy hours after startup fragments; zero normal admission/write loss and unexpected conflicts. Expired observed windows must finalize within 15 seconds after deadlines. Ask five analysts to interpret definitions and record at least four correct responses. These outcomes remain pending until measured.

History starts with enabled nondry collection. Do not backfill route detail from city/category aggregates or silently recover/merge crash fragments. Retain without automatic deletion in v1; choose a future horizon from measured growth and preserve whole city/hour cohorts under the same write protocol.
