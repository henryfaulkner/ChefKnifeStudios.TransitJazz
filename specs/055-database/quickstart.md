# Quickstart: Historical Transit Statistics

## Purpose

This runbook creates the single city-minute schema, proves the read-only metrics source, and enables recurring collection. Initial database backfill has been removed; collection reads only the newest safe closed minute and the configured retry overlap.

## Prerequisites

- The current metrics source is live and the actual Grafana retention window has been confirmed.
- A dedicated Grafana Cloud access policy restricted to `metrics:read` exists. Do not use the OTLP publisher or provisioning credential.
- The server runtime has a Key Vault-backed TransitJazz database connection string and the Grafana reader settings. Secrets must not appear in settings files, terminal history, reports, or logs.
- The configured canonical cities match the dashboard's `transit_city` labels.
- The feature's `worker-dashboard-statistics-v1` source contract has been reviewed against the committed dashboard.

## Build and verify the schema

1. Build the solution and run the existing test suites, including the new statistics model, source-client, and collector tests.
2. Create and inspect the EF migration for `city_minute_statistics`. It must contain only schema creation and the composite `(city_slug, stat_minute_utc)` primary key.
3. Build the existing EF migration bundle and inspect its generated migration script. Do not place a Grafana query, token, or data backfill inside the migration.
4. After CI builds and pushes the image, apply the migration manually from a machine whose IP PostgreSQL allows. Check out the same commit SHA shown in the waiting Server CI/CD run. From the repository root, with `ConnectionStrings__TransitJazzDB` set to the intended database through your local secret manager, run:

   ```powershell
   git rev-parse HEAD
   dotnet tool restore
   dotnet ef database update --project src/Server/ChefKnifeStudios.TransitJazz.Server.Data --startup-project src/Server/ChefKnifeStudios.TransitJazz.Server.Data --configuration Release
   dotnet ef migrations list --project src/Server/ChefKnifeStudios.TransitJazz.Server.Data --startup-project src/Server/ChefKnifeStudios.TransitJazz.Server.Data --configuration Release
   ```

   Confirm the SHA matches the waiting run, the database update succeeds, and `20260920201730_CreateCityMinuteStatistics` is applied to the intended database. The connection string must require TLS; keep its credentials out of command arguments and shell history. Do not approve deployment if any check fails.
5. In the waiting GitHub Actions run, approve the `server-dev` deployment. This releases the server image only after manual migration verification. Each later server deployment needs the same review, even if no new migration is pending.

## Configure collection safely

Start with statistics collection disabled by default. Supply the reader endpoint and credential through deployment secrets, then configure:

- source contract version: `worker-dashboard-statistics-v1`;
- one-minute collection interval;
- two-minute ingestion grace;
- five-minute idempotent retry overlap;
- dry-run enabled for the first execution.

The server GitHub Actions deployment updates the Container App image only. It does not add or change the collector's environment variables or Key Vault references. Deploy the reviewed Bicep configuration separately: set `transitJazzDbSecretUri`, `grafanaMetricsReaderEndpoint`, and `grafanaMetricsReaderSecretUri`; set `enableHistoricalStatistics=true` with `historicalStatisticsDryRun=true` for the first run. Remove any existing `HistoricalStatistics__InitialBackfill`, `HistoricalStatistics__BackfillStartUtc`, and `HistoricalStatistics__BackfillEndUtc` environment variables. The schema migration alone does not start collection, and dry-run collection does not write rows.

At startup, the server logs whether collection, the database binding, source endpoint, and reader credential are configured. An enabled collector logs one safe outcome summary per run, including dry-run mode, row counts, warning count, and fixed failure codes. The collector does not log secret values, endpoint URLs, raw responses, or database errors.

The deployment must not change worker metric instruments, labels, exporter cadence, dashboard JSON, alerts, or production metrics ingress.

## Dry-run recurring collection

1. Run the collector in dry-run mode.
2. Confirm each run makes a read-only source query at a 60-second step for the newest safe closed minute and the prior five minutes.
3. Review the safe report: effective source contract version, actual returned interval, expected city labels, complete/partial/no-data coverage, query warnings, and created/unchanged/discrepant counts.
4. Select three closed minutes for every source field and city where available. Compare each database-ready value with the exact frozen expression. Check a normal, zero/empty, and edge minute.
5. If any source field, city label, source range, or credential scope is wrong, leave writes disabled, correct the issue, and repeat the dry run. Do not fill gaps with another source.

## Enable writes and verify collection

1. Set `historicalStatisticsDryRun=false` after the dry run passes reconciliation.
2. Confirm one row exists for every configured city/minute in the returned coverage, including `Partial` or `NoData` rows where the source is absent.
3. Verify subsequent runs reread overlapping minutes without creating duplicate composite keys; compatible rows are unchanged and conflicting confirmed values are reported without replacement.
4. Query one city's bounded day and verify that grouping and aggregation need only a city filter and UTC range. Do not sum sampled gauge fields such as tones or vehicles and call them exact totals.
5. Preserve the approved safe report with the release evidence. It is operational evidence, not a database table.

## Monitor recurring collection

Leave recurring collection enabled. Each run collects the newest safe closed minute and rereads the prior five minutes. Investigate repeated `Partial`, `NoData`, or `Discrepant` status through the metrics source and structured server logs; do not alter metrics or fabricate database values.

## Release-gate commands and evidence

Use operator-provided secret values only through the deployment secret store. The following commands are examples; do not put credentials, endpoint query strings, or raw responses in shell history or evidence:

```bash
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.csproj --filter Statistics
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.csproj --filter WorkerDashboardStatisticsContractTests
az deployment sub what-if --location eastus2 --template-file bicep/main.bicep --parameters @bicep/main.prod.bicepparam
```

Record only the safe result of each gate: authenticated read access and actual retention, expected city labels, one-minute query coverage, warnings, row outcome counts, representative exact/tolerant comparisons, and idempotent rerun counts. Keep `HistoricalStatistics__Enabled=false` and `HistoricalStatistics__DryRun=true` until the 15-minute preflight and bounded dry run are reviewed. A warning, missing field/city, retention gap, source-version mismatch, or reconciliation discrepancy fails closed and leaves writes disabled.

After approval, preserve the safe dry-run report, enable recurring writes, and verify zero duplicate composite keys plus unchanged/filled/discrepant counts across overlapping runs. Record one fresh city-minute row and a city/day query that does not sum sampled gauges as totals.
