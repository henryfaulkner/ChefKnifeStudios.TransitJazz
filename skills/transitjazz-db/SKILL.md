---
name: transitjazz-db
description: Connect to the TransitJazz PostgreSQL database, run queries, explain table contents from the codebase, and report statistics with their units and coverage. Use when the user asks to query TransitJazz data, inspect database tables or schema, investigate historical worker statistics, or analyze city, transit category, and route-hour activity, movement, or published tone opportunities.
---

# TransitJazz database

Use the repository's PostgreSQL statistics model to answer database questions. Resolve all source paths below from the repository root. The schema guide describes the checked-in implementation; inspect the selected database before claiming that migrations or collection are active.

## Connect and choose the query

1. Read [connection.md](references/connection.md) when connecting or diagnosing access. Respect an explicit environment. Otherwise use an already configured `TransitJazzDB` connection; if none exists and available targets are ambiguous, ask which target to use. Keep credentials in memory or the caller's existing credential mechanism.
2. Read the relevant table section in [tables.md](references/tables.md) before interpreting fields. Recheck its listed source files when the schema, producer, or definition version differs. The ground truth is `AppDbContext`, EF mappings/migrations, producer code, stores, and SQL contracts.
3. Read [queries.md](references/queries.md) for schema discovery, dashboard snapshots, and category or route-hour report recipes. For route history, also read [route-hours.md](references/route-hours.md). Choose a finite UTC range, canonical city, relevant categories or ordinal route keys, explicit columns, deterministic ordering, and a reasonable displayed row limit. If omitted, start with the latest closed UTC hour and 100 displayed rows; state those defaults. Keep coverage counts over the entire requested range.
4. Use `scripts/query.ps1` with reviewed read-only SQL. It accepts the application's Npgsql connection format or existing libpq settings, runs all supplied queries in one read-only Repeatable Read transaction, and restores temporary connection environment settings. Use a role with SELECT privileges when available. Database modification, migrations, backfills, and capture enablement require a task that explicitly requests those actions.
5. Format the answer using [results.md](references/results.md). Include the actual target, UTC range, definitions, denominators, contributing coverage, and exclusions relevant to the question. Return a concise Markdown table by default; use the user's requested export format when supplied.

## Quick start

From the repository root, with `ConnectionStrings__TransitJazzDB` already configured:

```powershell
& ./skills/transitjazz-db/scripts/query.ps1 -Sql @'
SELECT current_database() AS database_name,
       current_schema() AS schema_name,
       current_setting('transaction_read_only') AS read_only,
       current_setting('transaction_isolation') AS isolation,
       current_setting('TimeZone') AS time_zone;
'@
```

For category measures, execute the existing measure recipe and `minute-coverage.sql` together. See the runnable example in [queries.md](references/queries.md); bare `:name` placeholders in those files need the helper's quoting or real client parameter binding.

## Interpretation rules

- `city_minute_statistics` contains 23 nullable dashboard samples per city/minute. Counts such as `vehicles_processed` and `tones_emitted` are the latest sampled values, not minute totals.
- `city_category_minute_statistics` and `city_category_hour_statistics` contain aggregates from worker cycles. Category history cannot be recovered from the city dashboard table.
- Definitive category hours require an intact distinct population and reconciliation against 60 complete, nonconflicting backing minutes with the same definition and cadence policy. Use the checked-in verified-hour SQL rather than filtering the hour table on `Complete` alone.
- `city_route_hour_statistics` retains canonical resolved routes in immutable city/hour cohorts. Route keys preserve case and are not raw feed/static IDs. Definitive route measures require fully contained Complete hours, supported definitions, and no conflict anywhere in the city/hour cohort. Route hours have no backing route minutes or `covered_minutes` reconciliation.
- Missing keys, stored `NoData`, `Partial`, and conflicts represent different evidence. Preserve nulls and genuine zeros; never fill gaps with zeros. A zero denominator gives a null mean.
- Sum compatible numerators and denominators for period means. Hourly distinct vehicles cannot be reconstructed from minute counts. Separate definition versions and cadence policies.
- Movement is observed along-route distance. Activity is observed joined, positioned vehicles. Crossings are published tone opportunities; they do not measure notes heard by listeners.
- Preserve UTC identities, named local zones, dates, and offsets when grouping by local hour. Historical capture may be absent: city/route history uses `HistoricalStatistics`, category capture uses `CityCategoryInsights`, and both are disabled in committed defaults. Live settings can differ.

When updating this skill, edit `skills/transitjazz-db/`, update its source references with the code, and run `tools/sync-skills.ps1 -Mode Sync`. Generated agent copies are managed by the repository's skill catalog.
