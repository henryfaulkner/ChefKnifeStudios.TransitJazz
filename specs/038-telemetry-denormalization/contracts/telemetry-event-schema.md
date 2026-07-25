# Contract: `TelemetryEvent` — C# POCO ⇄ parquet column schema

The single serialized record. `ParquetSerializer.SerializeAsync(rows, stream)` reflects this POCO into the parquet schema; `[ParquetColumn(Name = "…")]` pins each snake_case wire-contract column name so the C# property name and the parquet column name cannot drift (FR-006, FR-024). This column set is the durable contract the Go allow-list (contracts/query-validator.md) and the mj-data-explorer reference docs mirror.

## Shape (illustrative — final syntax confirmed against Parquet.Net 5.x in implementation)

```csharp
using Parquet.Serialization.Attributes; // ParquetColumn

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;

/// <summary>
/// One denormalized telemetry row. event_type discriminates PerCityCycle vs FullCycle.
/// Nullable properties are populated only for the event type(s) that define them; they
/// serialize as null on the other type's rows.
/// </summary>
public sealed record TelemetryEvent : IEventArgs
{
    // ── Common (every row) ────────────────────────────────────────────────
    [ParquetColumn(Name = "event_type")]      public required string EventType { get; init; }        // "PerCityCycle" | "FullCycle"
    [ParquetColumn(Name = "event_id")]        public required string EventId { get; init; }          // Guid "N"
    [ParquetColumn(Name = "observation_utc")] public required DateTime ObservationUtc { get; init; }

    // ── PerCityCycle-only (null on FullCycle) ─────────────────────────────
    [ParquetColumn(Name = "city_name")]              public string? CityName { get; init; }
    [ParquetColumn(Name = "feed_freshness_seconds")] public double? FeedFreshnessSeconds { get; init; }

    // ── FullCycle-only (null on PerCityCycle) ─────────────────────────────
    [ParquetColumn(Name = "cities_processed_count")] public int? CitiesProcessedCount { get; init; }
    [ParquetColumn(Name = "cities_processed_csv")]   public string? CitiesProcessedCsv { get; init; }

    // ── Shared (both types; scope differs) ────────────────────────────────
    [ParquetColumn(Name = "time_taken_seconds")]              public double? TimeTakenSeconds { get; init; }
    [ParquetColumn(Name = "health_ok")]                       public bool?   HealthOk { get; init; }
    [ParquetColumn(Name = "tones_emitted")]                   public int?    TonesEmitted { get; init; }        // summed on FullCycle
    [ParquetColumn(Name = "vehicles_processed")]              public int?    VehiclesProcessed { get; init; }   // summed on FullCycle
    [ParquetColumn(Name = "gc_heap_bytes")]                   public long?   GcHeapBytes { get; init; }         // reused per tick
    [ParquetColumn(Name = "process_working_set_bytes")]       public long?   ProcessWorkingSetBytes { get; init; } // reused per tick
    [ParquetColumn(Name = "vehicle_state_cache_size")]        public int?    VehicleStateCacheSize { get; init; }        // summed on FullCycle
    [ParquetColumn(Name = "crossing_baseline_cache_size")]    public int?    CrossingBaselineCacheSize { get; init; }    // summed on FullCycle
    [ParquetColumn(Name = "route_index_size")]                public int?    RouteIndexSize { get; init; }               // summed on FullCycle
    [ParquetColumn(Name = "route_trigger_point_cache_size")]  public int?    RouteTriggerPointCacheSize { get; init; }   // summed on FullCycle
}
```

## Column ⇄ property ⇄ kind table (authoritative)

| parquet column (Name) | CLR property | CLR type | Go kind | Notes |
|---|---|---|---|---|
| `event_type` | `EventType` | `string` | string | discriminator |
| `event_id` | `EventId` | `string` | string | per-row identity |
| `observation_utc` | `ObservationUtc` | `DateTime` | timestamp | UTC |
| `city_name` | `CityName` | `string?` | string | PerCityCycle only |
| `feed_freshness_seconds` | `FeedFreshnessSeconds` | `double?` | numeric | PerCityCycle only |
| `cities_processed_count` | `CitiesProcessedCount` | `int?` | numeric | FullCycle only |
| `cities_processed_csv` | `CitiesProcessedCsv` | `string?` | string | FullCycle only |
| `time_taken_seconds` | `TimeTakenSeconds` | `double?` | numeric | shared |
| `health_ok` | `HealthOk` | `bool?` | bool | shared |
| `tones_emitted` | `TonesEmitted` | `int?` | numeric | shared; summed on FullCycle |
| `vehicles_processed` | `VehiclesProcessed` | `int?` | numeric | shared; summed on FullCycle |
| `gc_heap_bytes` | `GcHeapBytes` | `long?` | numeric | shared; reused per tick |
| `process_working_set_bytes` | `ProcessWorkingSetBytes` | `long?` | numeric | shared; reused per tick |
| `vehicle_state_cache_size` | `VehicleStateCacheSize` | `int?` | numeric | shared; summed on FullCycle |
| `crossing_baseline_cache_size` | `CrossingBaselineCacheSize` | `int?` | numeric | shared; summed on FullCycle |
| `route_index_size` | `RouteIndexSize` | `int?` | numeric | shared; summed on FullCycle |
| `route_trigger_point_cache_size` | `RouteTriggerPointCacheSize` | `int?` | numeric | shared; summed on FullCycle |

## Serialization contract

- **Write**: `await ParquetSerializer.SerializeAsync(rows, memoryStream)` where `rows` is `IReadOnlyCollection<TelemetryEvent>`. Snappy compression (match today's `CompressionMethod.Snappy`) if exposed via serializer options; otherwise default is acceptable (verify in impl).
- **Column names**: exactly the 17 `Name=` values above — no PascalCase leakage.
- **Nulls**: a property left unset for its non-applicable event type serializes as parquet null and reads back as an empty cell (FR-004).
- **Order**: not contractually fixed (the Go allow-list is name-keyed, not position-keyed), but the schema test asserts the full set is present.

## Schema test contract (`TelemetryEventSchemaTests.cs`)

Given a `List<TelemetryEvent>` containing one PerCityCycle row (city-only fields set, full-cycle fields null) and one FullCycle row (full-cycle fields set, city-only fields null), after serialize→deserialize:
1. The reflected schema contains **all 17 columns** with the names above.
2. Each column's type matches the CLR type (nullable where specified).
3. Values round-trip, and the non-applicable columns are **null** on each row (city_name/feed_freshness null on FullCycle; cities_processed_* null on PerCityCycle).
