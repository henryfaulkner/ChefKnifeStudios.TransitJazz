# Quickstart: Historical Transit Statistics

## Purpose

This runbook creates the single city-minute schema, proves the read-only metrics source, backfills only still-queryable dashboard history, and then enables recurring collection. It never reconstructs missing history from logs, legacy telemetry files, or transit feeds.

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
4. Run the repository's migration job against the intended database. Confirm only the one new table is created.

## Configure collection safely

Start with statistics collection disabled by default. Supply the reader endpoint and credential through deployment secrets, then configure:

- source contract version: `worker-dashboard-statistics-v1`;
- one-minute collection interval;
- two-minute ingestion grace;
- five-minute idempotent retry overlap;
- initial backfill window: the actual available source history, never more than the verified retention window;
- dry-run enabled for the first execution.

The deployment must not change worker metric instruments, labels, exporter cadence, dashboard JSON, alerts, or production metrics ingress.

## Dry-run the historical backfill

1. Run the collector in dry-run mode.
2. Confirm it makes only read-only source queries in bounded six-hour UTC chunks at a 60-second step.
3. Review the safe report: effective source contract version, actual returned interval, expected city labels, complete/partial/no-data coverage, query warnings, and created/unchanged/discrepant counts.
4. Select three closed minutes for every source field and city where available. Compare each database-ready value with the exact frozen expression. Check a normal, zero/empty, and edge minute.
5. If any source field, city label, source range, or credential scope is wrong, leave writes disabled, correct the issue, and repeat the dry run. Do not fill gaps with another source.

## Apply and verify the backfill

1. Enable writes and rerun the same bounded range.
2. Confirm one row exists for every configured city/minute in the returned coverage, including `Partial` or `NoData` rows where the source is absent.
3. Rerun the identical range. It must create no duplicate composite keys; compatible rows are unchanged and conflicting confirmed values are reported without replacement.
4. Query one city's bounded day and verify that grouping and aggregation need only a city filter and UTC range. Do not sum sampled gauge fields such as tones or vehicles and call them exact totals.
5. Preserve the approved safe report with the release evidence. It is operational evidence, not a database table.

## Enable recurring collection

After the initial backfill passes reconciliation, disable the one-time backfill mode and leave recurring collection enabled. Each run collects the newest safe closed minute and rereads the prior five minutes. Investigate repeated `Partial`, `NoData`, or `Discrepant` status through the metrics source and structured server logs; do not alter metrics or fabricate database values.

## Release-gate commands and evidence

Use operator-provided secret values only through the deployment secret store. The following commands are examples; do not put credentials, endpoint query strings, or raw responses in shell history or evidence:

```bash
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.csproj --filter Statistics
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.csproj --filter WorkerDashboardStatisticsContractTests
az deployment sub what-if --location eastus2 --template-file bicep/main.bicep --parameters @bicep/main.prod.bicepparam
```

Record only the safe result of each gate: authenticated read access and actual retention, expected city labels, one-minute query coverage, warnings, row outcome counts, representative exact/tolerant comparisons, and idempotent rerun counts. Keep `HistoricalStatistics__Enabled=false` and `HistoricalStatistics__DryRun=true` until the 15-minute preflight and bounded dry run are reviewed. A warning, missing field/city, retention gap, source-version mismatch, or reconciliation discrepancy fails closed and leaves writes disabled.

After approval, preserve the same bounded range and safe dry-run report, enable writes for the six-hour-chunk backfill, rerun the identical range, and verify zero duplicate composite keys plus unchanged/filled/discrepant counts. Only then set `InitialBackfill=false`, enable recurring collection, and record one fresh city-minute row and a city/day query that does not sum sampled gauges as totals.
