# Quickstart: Logging Sidecar Service

How to build, run, and verify the sidecar end-to-end. Assumes the .NET 10 SDK and an Azure Storage account reachable via your developer identity.

## 1. Dependencies

Add to `ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.csproj`:

```xml
<PackageReference Include="Parquet.Net" Version="5.*" />
<PackageReference Include="Azure.Storage.Blobs" Version="12.*" />
<!-- Azure.Identity is already referenced -->
```

## 2. Configuration (no secrets in source)

Local dev — `appsettings.Development.json` (gitignored) or user-secrets / env:

```jsonc
{
  "Logging": {
    "Telemetry": {
      "BlobServiceUri": "https://<youraccount>.blob.core.windows.net",
      "Container": "telemetry",
      "FlushIntervalSeconds": 300,
      "ChannelCapacity": 10000,
      "Enabled": true
    }
  }
}
```

Authenticate locally with `az login` (resolved by `DefaultAzureCredential`). In Azure, the worker's managed identity needs **Storage Blob Data Contributor** on the account/container. **Never commit an account key or connection string** (security gate, feature 012 FR-020).

> Tip: to drive the loop quickly during manual verification, temporarily set `FlushIntervalSeconds` low (e.g. `15`).

## 3. Build & run

```powershell
dotnet build src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker
# Run via Aspire AppHost (recommended — wires the API the worker depends on):
dotnet run --project src/ChefKnifeStudios.MartaJazz.AppHost
```

The worker polls the live GTFS-RT feed every 10s and, with the sidecar enabled, posts Snap/Lerp/Cycle events that flush to blob every interval.

## 4. Verify (maps to spec acceptance / success criteria)

1. **Cycle telemetry lands (US1 / SC-002)** — after one flush interval, confirm a part-file exists under `telemetry/cycle/dt=<today UTC>/` and contains one row per completed cycle with counts matching the worker's `Spatial reconciliation:` log line.
2. **Snap telemetry (US2)** — `telemetry/snap/dt=<today>/` part-file has one row per snapped vehicle; `snap_outcome` is a readable name (`Moved`, `Stale`, …).
3. **Lerp telemetry (US3)** — `telemetry/lerp/dt=<today>/` rows carry prior state + deltas for vehicles seen on consecutive cycles.
4. **Queryable via the tool (SC-005)** — point the `telemetry-query-tool` (or feature-012 MCP bridge) at a day's partition:
   ```sql
   SELECT cycle_id, buses_stale, buses_skipped_unknown_route
   FROM read_parquet('azure://telemetry/cycle/dt=2026-06-04/*.parquet');
   ```
5. **Hot-path isolation (SC-001/SC-003)** — disable the destination (wrong container or revoke access) and confirm: worker keeps completing cycles, `Spatial reconciliation` logs continue, no exception escapes into the processing loop; on the next Cycle row `sidecar_persist_failures` is incremented.
6. **Load-shedding (SC-004)** — set `ChannelCapacity` very low (e.g. `5`) and confirm `sidecar_dropped_records` climbs while memory stays bounded and cycles are unaffected.
7. **Shutdown (SC-006)** — Ctrl-C / SIGTERM the worker; confirm it exits promptly whether or not blob is reachable (best-effort final flush, no hang).

## 5. Automated tests

```powershell
dotnet test src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests
```

Covers: parquet schema round-trip (write→read columns match the contract), partition-path derivation (UTC date + unique part name), channel `DropWrite` load-shedding + drop counting, and persistence-failure isolation (failure swallowed, counter incremented, no throw).

## 6. Notes / deferred

- **Retention** is out of scope for the worker: configure an **Azure Storage lifecycle-management policy** (blob TTL on the `telemetry` container) in infra (Bicep) to age out old `dt=` partitions. Tracked as deferred (research R7).
- **Feature-012 alignment**: once datasets exist, update feature 012's allow-list columns and `TELEMETRY_DATASET_URI` to the Snap/Lerp/Cycle schemas in `contracts/parquet-schemas.md`. That change ships with feature 012, not this branch — call it out in the PR.
