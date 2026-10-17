# Reported Vehicles rollout record

This is a release runbook, not rollout approval. No migration, capture enablement, read enablement, deployment, or count-only backfill has been performed for this feature.

## Current checked-in posture

- `StatsOverlay.ReportedVehiclesEnabled` is `false`.
- `ReportedVehicleHistory.Mode` is `Disabled` and `SelectedCities` is empty.
- The source catalog and finite capture limits in `src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json` are proposals for future validation. They do not certify agency identity semantics, feed cadence, or production owner topology.
- The migration `20261015114433_CreateCityReportedVehicleHours` is checked in but has not been applied to a database in this implementation run.
- Existing route/category history cannot be used to backfill exact reported-vehicle memberships.

## Release gates still open

1. For every enabled endpoint, have the transit-data owner verify a stable identity field, whether it is persistent across feed snapshots, and the correct identity namespace. The current definition counts descriptor IDs and MARTA train IDs; fallback rules must remain explicitly reviewed. Record evidence per source in `contracts/reported-vehicle-capture-v1.md`.
2. Measure sequential poll timing and outages for each source. Validate configured cadence, maximum gaps, and predecessor/successor boundary rules against those real timings.
3. Assign and verify exactly one persistence owner per city. WebAPI and standalone Worker capture cannot both persist the same city.
4. Apply the migration only to the selected environment after backup/change review. Confirm schema and indexes there; checked-in migration files do not prove schema state.
5. Run the disposable PostgreSQL store/query/migration suite, source-cadence review, memory/queue-bound checks, matched-load benchmarks, browser/accessibility review, and five-visitor comprehension check. See `validation.md`, `quickstart.md`, and the feature's performance/browser evidence files.
6. Audit retained cohorts for membership parity, conflicts, supported definition/source/cadence fingerprints, and truthful Complete/Partial/NoData coverage before enabling reads.

## Staged operational procedure

1. Verify source contracts and the one-owner-per-city assignment. Save the verified source/cadence policy fingerprints with the change record.
2. Apply the schema migration to the selected environment using the approved deployment path. Confirm both reported-vehicle tables and the migration history row.
3. Keep `StatsOverlay.ReportedVehiclesEnabled=false`. Set capture to `DryRun` for selected test cities first; verify bounded diagnostics, source counts, extraction limits, and that map/audio work continues through queue or storage failures.
4. After dry-run and measured limits pass, select a single capture owner and explicitly selected cities with `ReportedVehicleHistory.Mode=Persistence`. Keep the read flag false while complete hours accumulate.
5. Inspect coverage and conflict rates using aggregate/header-only checks. Do not query or export raw identity values. Confirm retention and source-window coverage, then review all release evidence and authorize read enablement as a separate operation.
6. Enable `StatsOverlay.ReportedVehiclesEnabled=true` only after those checks. Confirm the existing distance output and reported-vehicle coverage/count states independently.

## Rollback

Disable reported-vehicle reads first by setting `StatsOverlay.ReportedVehiclesEnabled=false`; the Stats endpoint then uses its relation-free disabled query branch and returns unavailable reported summaries. Next disable capture with `ReportedVehicleHistory.Mode=Disabled`. Keep already retained cohorts for investigation/retention cleanup unless the operator separately authorizes a data-retention action. Do not drop the schema as part of routine rollback.
