# Contract: Blob layout

Single date-partitioned path, replacing the three per-dataset paths. Immutable part-file convention preserved (FR-007, FR-029).

## Path

```
{container}/telemetry/dt={yyyy-MM-dd}/part-{yyyyMMddTHHmmssfffZ}-{shortGuid}.parquet
```

- `{container}` = `LoggingOptions.Container`, default `"telemetry"` (unchanged, `LoggingOptions.cs:10`).
- `dt=` day partition = UTC date at flush time.
- `part-…` = flush timestamp (`yyyyMMddTHHmmssfffZ`) + 8-char `Guid.NewGuid().ToString("N")[..8]` — one immutable file per flush, uploaded with `overwrite: false`.

**Resolved (was open in this doc; confirmed against prod config)**: the blob
container is **not** guaranteed to be named `telemetry` — prod's
`Logging:Telemetry:Container` is `"parquet"`. So `{dataset}/` does not collapse into
the container name; it must survive as a literal `telemetry/` virtual-directory
prefix inside whichever container is configured. The full storage location is
`{container}/telemetry/dt={date}/*.parquet`, which the query bridge reads for the
single `telemetry` dataset.

## Change vs. the original three-dataset layout (`ParquetLoggingService.BuildBlobPath`)

```csharp
// BEFORE (three datasets)
return $"{dataset}/dt={now:yyyy-MM-dd}/part-{now:yyyyMMddTHHmmssfffZ}-{shortGuid}.parquet";
// AFTER (one dataset; "telemetry/" is now a fixed literal, not a {dataset} variable)
return $"telemetry/dt={now:yyyy-MM-dd}/part-{now:yyyyMMddTHHmmssfffZ}-{shortGuid}.parquet";
```

The Go bridge's source template mirrors this: `{StorageURI}/telemetry/dt={date}/*.parquet`
(`internal/query/runner.go`) — `telemetry/` is a literal prefix, not a `{dataset}`
substitution, so it matches regardless of what the container itself is named.

## Flush behavior (unchanged)

- One `FlushAsync` serializes the single `ConcurrentBag<TelemetryEvent>` drain and uploads one part-file (only if non-empty).
- Periodic every `FlushIntervalSeconds` (default 300) + best-effort on shutdown (`LogEventWorker.StopAsync`).
- Container `CreateIfNotExists` on first use (unchanged, `ParquetLoggingService.cs:252-255`).

## PartitionPathTests contract

Assert the produced path:
1. Contains `dt={yyyy-MM-dd}` for the flush's UTC date.
2. Matches `part-\d{8}T\d{9}Z-[0-9a-f]{8}\.parquet`.
3. Does **not** contain a `snap/`, `lerp/`, or `cycle/` segment.
