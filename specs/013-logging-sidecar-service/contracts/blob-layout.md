# Contract: Azure Blob Layout & Write Protocol

## Container & path layout

```
{container = "telemetry"}/
├── snap/
│   └── dt=YYYY-MM-DD/
│       └── part-{yyyyMMddTHHmmssfffZ}-{shortguid}.parquet
├── lerp/
│   └── dt=YYYY-MM-DD/
│       └── part-{yyyyMMddTHHmmssfffZ}-{shortguid}.parquet
└── cycle/
    └── dt=YYYY-MM-DD/
        └── part-{yyyyMMddTHHmmssfffZ}-{shortguid}.parquet
```

- `dt=` value is the **UTC** date at flush time (Hive-style partition; DuckDB `hive_partitioning=true` can surface `dt` as a column).
- Part-file name carries a millisecond UTC timestamp + short guid → unique within a day across restarts (FR-004c).
- Files are **immutable**: written once, never appended or overwritten (FR-004b). Upload uses `overwrite: false`.

## Write protocol (per flush, per non-empty dataset)

1. Serialize accumulated rows for the dataset to parquet in a `MemoryStream` (Parquet.Net, Snappy).
2. Compute path from dataset name + current UTC date + timestamped part name.
3. `BlobContainerClient.GetBlobClient(path).UploadAsync(stream, overwrite: false)`.
4. On success: clear that dataset's buffer.
5. On failure: increment `sidecar_persist_failures`, `ILogger.LogError`, **swallow** (FR-010) — do not rethrow, do not block the worker. Buffer disposition on failure: retain for one retry on next flush is **optional**; default is drop-after-failure to keep memory bounded (failures are counted and visible on the Cycle row). [Implementer choice — keep it simple: drop and count.]

## Authentication

- Credential: `DefaultAzureCredential` (managed identity in Azure; `az login`/`AZURE_*` env locally). **No account key or connection string in source or committed config** (security gate; feature 012 FR-020).
- Blob service endpoint + container name come from configuration (`Logging:Telemetry:BlobServiceUri`, `Logging:Telemetry:Container`), injected via Aspire/env (dev) and app settings / Key Vault reference (prod).

## Configuration keys (`LoggingOptions`)

| Key | Default | Meaning |
|---|---|---|
| `Logging:Telemetry:BlobServiceUri` | (required) | e.g. `https://<account>.blob.core.windows.net` |
| `Logging:Telemetry:Container` | `telemetry` | container name |
| `Logging:Telemetry:FlushIntervalSeconds` | `300` | 5-minute flush cadence (FR-004b) |
| `Logging:Telemetry:ChannelCapacity` | `10000` | bounded channel size (FR-003) |
| `Logging:Telemetry:Enabled` | `true` | kill switch; when false, sidecar no-ops |

## Reader contract (downstream)

The `telemetry-query-tool` (feature 012) reads `azure://{container}/{dataset}/dt={date}/*.parquet`. Its `TELEMETRY_DATASET_URI` and allow-list columns must match the dataset names here and the column names in `parquet-schemas.md`. Keeping `dataset` names (`snap`/`lerp`/`cycle`) and column names stable is the cross-feature contract.
