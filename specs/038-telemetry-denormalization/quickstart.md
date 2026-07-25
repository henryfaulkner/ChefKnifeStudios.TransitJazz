# Quickstart: Telemetry Denormalization

Build/test/run verification for the single-table telemetry redesign. All commands from repo root on Windows PowerShell.

## Prerequisites

- .NET 10 SDK, Go toolchain (for the MCP validator tests).
- For the manual end-to-end: the worker configured against MARTA (the only city with `EmitsTelemetry: true`) and a blob target (`Logging:Telemetry:BlobServiceUri` + managed identity, or a dev `ConnectionString` via env var `Logging__Telemetry__ConnectionString`).

## 1. C# unit tests (schema + pipeline)

```powershell
dotnet test src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests.csproj
```

Expect:
- **`TelemetryEventSchemaTests`** (new) green: reflected schema has all 17 columns with correct types; a PerCityCycle row and a FullCycle row round-trip, with the non-applicable columns null on each (`city_name`/`feed_freshness_seconds` null on FullCycle; `cities_processed_*` null on PerCityCycle).
- **`ChannelLoadSheddingTests`** / **`FailureIsolationTests`** green against the single-buffer service (constructing `TelemetryEvent` now).
- **`PartitionPathTests`** green: path is `dt=…/part-*.parquet` with no `snap|lerp|cycle` segment.
- The old `SnapParquetSchemaTests` / `LerpParquetSchemaTests` / `CycleParquetSchemaTests` are **gone** (deleted).

## 2. Go validator tests

```powershell
Push-Location tools/telemetry-mcp; go test ./internal/validate/...; Pop-Location
```

Expect the accept/reject vectors in contracts/query-validator.md to pass: `telemetry` is the only valid dataset; `event_type = 'PerCityCycle'` accepted; retired columns (`snap_distance_km`, `pos_delta_km`, `last_update_cache_size`) rejected as unknown; `snap`/`lerp`/`cycle` rejected as datasets.

## 3. Build the whole worker

```powershell
dotnet build src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.csproj
```

Expect no references to the deleted `SnapEventArgs`/`LerpEventArgs`/`CycleEventArgs`/`LogEventArgs`/`TelemetryColumns` anywhere (compile fails loudly if `Worker.cs` still posts the old types).

## 4. Manual end-to-end (the real proof)

Run the worker locally (via the Aspire AppHost or the worker project directly) against MARTA for at least one flush interval (default 5 min, or lower `Logging:Telemetry:FlushIntervalSeconds` for the test).

Confirm:
1. Exactly **one** `telemetry/dt=…/part-*.parquet` file appears per flush interval (not three files under `snap/`/`lerp/`/`cycle/`).
2. Force each health path and confirm a PerCityCycle row appears every tick regardless:
   - normal run → `health_ok=true`, `vehicles_processed`/`tones_emitted` populated;
   - empty feed → `health_ok=true`, `vehicles_processed=0`;
   - throw in processing → `health_ok=false`, memory/cache columns present, processing fields 0/null;
   - route index not ready (start before index builds) → `health_ok=false`.
3. Exactly **one** FullCycle row per tick, with `cities_processed_count`/`cities_processed_csv` set and `tones_emitted`/`vehicles_processed`/cache sizes = the sum across that tick's cities; the two memory columns identical to the PerCityCycle rows that tick.

## 5. Query round-trip via the MCP bridge

```
mcp__telemetry-query-bridge__query_telemetry
  dataset = "telemetry"
  filter  = "event_type = 'PerCityCycle'"
```
then
```
  dataset = "telemetry"
  filter  = "event_type = 'FullCycle'"
```

Confirm:
- Both shapes return rows for today's partition.
- On PerCityCycle rows, `cities_processed_count`/`cities_processed_csv` are empty (null); on FullCycle rows, `city_name`/`feed_freshness_seconds` are empty (null) — FR-004 round-trips.
- A retired-column filter (e.g. `snap_distance_km > 0`) is rejected as unknown.

## 6. Docs sanity

- `.claude/skills/mj-data-explorer/references/telemetry-schema.md` describes one `telemetry` table + the `event_type` discriminator (no three-dataset tables).
- `.claude/skills/mj-data-explorer/references/telemetry-query-guide.md` shows `dataset = "telemetry"`, updated accept/reject examples, and the `event_type` filtering pattern; `last verified` dates bumped.

## Done when

All of §1-§2 green, §3 builds clean, §4-§5 show both event shapes in one `telemetry/` partition with correct nulls/sums, and §6 docs match the new schema.
