# Research: Logging Sidecar Service

Phase 0 resolves the open technical choices behind the clarified spec. All spec-level `[NEEDS CLARIFICATION]` were already resolved during `/speckit-clarify` (parquet build mechanism, 5-min flush, daily `dt=` partitioning, per-event datasets, self-health on Cycle). The remaining unknowns are implementation choices below.

---

## R1. In-process parquet writer for .NET

**Decision**: Use **Parquet.Net** (`Parquet.Net` NuGet, Aqua/Elastacloud lineage, actively maintained, MIT).

**Rationale**:
- FR-004a requires building parquet in-process with no external process and no DuckDB on the worker host. Parquet.Net is pure managed code, no native dependency — safe in a Linux container image.
- Supports writing to a `Stream`, so we can write to a `MemoryStream` and hand the bytes straight to the Azure Blob SDK without a temp file.
- Column-oriented API (`ParquetWriter` + `DataColumn` over typed arrays) matches our "accumulate rows, flush a batch" model and produces a single self-contained file per flush (FR-004b — no append/mutate).
- DuckDB's parquet reader is fully interoperable with Parquet.Net output (standard parquet); the `telemetry-query-tool` reads it via the Azure extension with no special handling.

**Alternatives considered**:
- *DuckDB `COPY … TO 'azure://…' (FORMAT PARQUET)` from the worker* — rejected at clarification (Q1=A): pulls a native DuckDB dependency + Azure extension onto the production worker host, larger image, and re-introduces the very SQL-execution surface feature 012 is trying to contain.
- *Apache Arrow (`Apache.Arrow`) + parquet* — heavier, more ceremony for simple flat row batches; Parquet.Net is the lighter fit for three flat schemas.
- *Write CSV/JSON then convert later* — rejected at clarification (Q1, option C): adds a second pipeline stage and delays queryability.

**Notes for implementation**:
- One `ParquetWriter` per flush per non-empty dataset → one blob. Use Snappy compression (DuckDB reads it; good size/speed default).
- Keep a stable, explicit `ParquetSchema` per event type defined once in code (see data-model + contracts) so column names/types are a fixed contract for the query tool.

---

## R2. Azure Blob upload + authentication (security gate)

**Decision**: Upload with **`Azure.Storage.Blobs`** using **`DefaultAzureCredential`** (`Azure.Identity`, already referenced). No account key, no connection string in source or committed config.

**Rationale**:
- Constitution + feature 012 FR-020 flagged a **hardcoded live `AccountKey`** in committed Go source as a defect to remediate. This feature MUST NOT repeat that. `DefaultAzureCredential` resolves to **managed identity** in Azure (the worker's Container App identity) and to developer credentials (`az login` / `AZURE_*` env) locally.
- The blob **account/container** is non-secret configuration → bind from `IConfiguration` (`Logging:Telemetry:*`), supplied via Aspire/env in dev and app settings/Key Vault references in prod.
- `BlobContainerClient.GetBlobClient(path).UploadAsync(stream, overwrite:false)` writes one immutable part-file; unique `part-<utcTimestamp>` names mean `overwrite:false` never collides (FR-004c).

**Alternatives considered**:
- *Account key / connection string in config* — rejected: that is the exact anti-pattern 012/FR-020 exists to fix. A key without restriction is a secret (Constitution II reasoning) and must not be committed.
- *SAS token* — viable for the downstream read tool, but for the writer side managed identity is strictly better (no rotation, no expiry handling on the hot host).

**Cross-feature note (out of scope here, surface in tasks/PR)**:
- The `telemetry-query-tool` reads via `AZURE_STORAGE_CONNECTION_STRING` and feature 012's MCP bridge has an allow-list grammar currently hardcoded to the **iris** dataset (`sepal_length`, `species`, …) with a single `TELEMETRY_DATASET_URI`. Once this feature's datasets exist, 012's `allowedColumns`/`DatasetURI` will need to be pointed at the real Snap/Lerp/Cycle schemas. **That update belongs to feature 012, not here** — but the column names this plan freezes (R4) are the contract 012 will consume. Call this out in the PR so the two stay aligned.

---

## R3. Decoupling mechanism: notification service + bounded channel + hosted consumer

**Decision**: Reproduce the source-spec design, grounded in the repo's existing pattern:
- `IEventNotificationService` with `event EventReceivedEventHandler EventReceived` and `void PostEvent(object, IEventArgs)` — **a server-side copy of the existing `Client.Core/Services/EventNotificationService.cs`** (FR-014). Registered singleton.
- Data-processing code calls `PostEvent(this, new SnapEventArgs(...))` etc. The handler does a non-blocking `_channel.Writer.TryWrite(e)` into a **bounded `Channel<IEventArgs>`** (capacity 10,000, `BoundedChannelFullMode.DropWrite`) — FR-003 load-shedding, FR-002 no back-pressure.
- A single `LogEventWorker : IHostedService` owns the channel, runs a background `await foreach` consumer that accumulates rows per dataset, and a 5-minute `PeriodicTimer` flush (FR-004b). On `StopAsync` it completes the writer, drains best-effort, does one final flush, then returns (FR-011, SC-006).

**Rationale**:
- `Channel` `TryWrite` is allocation-light and lock-free-ish; posting from the hot path is O(1) and never awaits (SC-001).
- A bounded channel with `DropWrite` gives exactly the "drop newest, count it, never block producer, never grow unbounded" behavior FR-003/SC-004 require. The drop count is observable (compare attempted vs. accepted) and feeds the Cycle health columns.
- Hosting it as `IHostedService` (vs. the source spec's manual `ConsumeAsync` never being started) ensures the consumer actually runs and that shutdown is wired through the host lifecycle.

**Alternatives considered**:
- *`System.Threading.Tasks.Dataflow` `BufferBlock`* — heavier dependency, same semantics; `Channel` is the modern BCL choice.
- *Direct `ILogger` + OTEL only* — rejected: doesn't produce queryable parquet datasets, which is the entire point (SC-005). OTEL remains for operational telemetry (Constitution IV), not a substitute.
- *Per-event-type channels* — unnecessary; one channel carrying the `LogEventArgs` base, dispatched by type in the consumer, is simpler and keeps a single capacity budget.

**Bug to avoid (carried from source spec snippet)**: the source `Dispose` references a non-existent `_logEventNotificationService` and `ConsumeAsync` is never started/`ex` is undefined. The implementation must wire `EventReceived -= HandleEventReceived` correctly and start the consumer in `StartAsync`.

---

## R4. Canonical column schema (downstream query-tool contract)

**Decision**: Freeze flat, snake_case column names per dataset, defined as constants in `TelemetryColumns.cs` and used to build the `ParquetSchema`. snake_case because the DuckDB query tool and its allow-list grammar (feature 012) treat column identifiers as `[a-z_][a-z0-9_]*`.

**Rationale**:
- DuckDB reads parquet column names verbatim; snake_case avoids quoting in `WHERE` filters and matches 012's `isIdentifierChar` rules (letters/digits/underscore).
- Flat (no nested structs) keeps each dataset a simple uniform table the tool can `read_parquet(...)` and filter directly. The spec's "position delta", "bus delta" etc. are flattened into prefixed columns (e.g. `pos_delta_km`, `speed_delta`).
- A `cycle_id` column on every Snap/Lerp row (FR-009) enables correlation joins to the Cycle dataset.

**Alternatives considered**:
- *Nested parquet (struct columns)* — DuckDB supports it but it complicates the allow-list grammar and operator queries; flatten instead.
- *PascalCase (matching C# records)* — rejected: forces quoted identifiers in DuckDB and conflicts with 012's identifier grammar.

(Exact columns enumerated in `data-model.md` and `contracts/parquet-schemas.md`.)

---

## R5. Partition path & UTC date derivation

**Decision**: Blob path per flush, per non-empty dataset:
`{container}/{dataset}/dt={yyyy-MM-dd}/part-{yyyyMMddTHHmmssfffZ}-{shortGuid}.parquet`
where `dataset ∈ {snap, lerp, cycle}`, the `dt=` date is the **UTC** date at flush time, and the timestamp+short-guid guarantee per-day uniqueness (FR-004c) even across process restarts within the same minute.

**Rationale**:
- `dt=YYYY-MM-DD` Hive-style partitioning is what DuckDB's `read_parquet('azure://…/snap/dt=2026-06-04/*.parquet')` globs cleanly (SC-005); DuckDB can also auto-derive the `dt` column via `hive_partitioning=true`.
- UTC matches every timestamp the worker already produces (`DateTime.UtcNow` throughout `Worker.cs`), avoiding off-by-one shard boundaries (spec assumption "Daily UTC sharding").
- A flush that straddles midnight UTC is bucketed by flush-instant date; rows are not split across two folders within one part-file. Acceptable: at most one 5-min file near midnight is attributed to the later day. (Documented in data-model edge cases.)

**Alternatives considered**:
- *Flat files with date-prefixed names* — rejected at clarification (Q2, option C): folder partitioning is the idiomatic, glob-friendly layout.
- *One file per day* — rejected at clarification (Q2, option B): unbounded in-memory accumulation and a full day at risk on crash, violating SC-007.

---

## R6. Capturing data without changing pass logic

**Decision**: The worker already computes everything needed. Post events at the existing points in `ProcessSpatialReconciliationAsync` and the cycle epilogue:
- **CycleId**: generate one `Guid`/ULID at the top of each reconciliation cycle; thread it into posted Snap/Lerp events and the final Cycle event.
- **Snap**: the per-vehicle branch already builds a `BatchDebugRecord` with raw/snapped lat-lon, snap distance/index, outcome string, speed, bearing → map to `SnapEventArgs`.
- **Lerp**: the prior-state branch already has `prior` (`VehicleState`) and current deltas (`DeltaFromPriorSnapKm`, `SecondsSincePriorObservation`, speed/bearing) → map to `LerpEventArgs`.
- **Cycle**: the existing counters (`movedCount`, `unchangedCount`, `stationaryCount`, `staleCount`, `skippedNoRouteId`, `skippedUnknownRoute`), `feedTs`, `feedIsDuplicate`, and `_lastUpdateCache.Count` / `_vehicleStateCache.Count` → map to `CycleEventArgs`, plus sidecar health (buffer occupancy, dropped count, persist-failure count) read from the `LogEventWorker`.

**Rationale**: Minimizes hot-path edits to a few `PostEvent` calls and a `CycleId` variable — the heavy data is already there. The existing `WriteBatchToDiskAsync(debugBatch)` local-JSON dump can remain (debug aid) or be retired later; out of scope to remove here.

**Alternatives considered**:
- *Compute telemetry in the sidecar from raw feed* — rejected: duplicates the snap/delta math and risks divergence from what was actually published.

---

## R7. Outstanding policy deferred from clarification

**Retention / partition cleanup** (flagged Deferred in `/speckit-clarify`): how long `dt=` partitions live in blob. **Decision for this feature**: out of scope — the sidecar only writes; lifecycle management is an Azure Storage lifecycle-management policy (blob TTL) configured in infra (Bicep), not worker code. Note in quickstart so it isn't forgotten. No code dependency.
