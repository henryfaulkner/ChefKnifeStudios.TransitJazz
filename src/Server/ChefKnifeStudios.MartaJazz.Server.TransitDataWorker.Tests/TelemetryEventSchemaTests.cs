using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;
using Parquet.Serialization;
using Xunit;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests;

public class TelemetryEventSchemaTests
{
    static readonly string[] ExpectedColumns =
    [
        "event_type", "event_id", "observation_utc",
        "city_name", "feed_freshness_seconds",
        "cities_processed_count", "cities_processed_csv",
        "time_taken_seconds", "health_ok", "tones_emitted", "vehicles_processed",
        "gc_heap_bytes", "process_working_set_bytes",
        "vehicle_state_cache_size", "crossing_baseline_cache_size",
        "route_index_size", "route_trigger_point_cache_size",
        "crossings_suppressed_first_seen", "crossings_suppressed_delta_leq0",
        "crossings_suppressed_teleport", "crossings_suppressed_transfer"
    ];

    static TelemetryEvent PerCityCycleRow(DateTime now) => new()
    {
        event_type = "PerCityCycle",
        event_id = "event-1",
        observation_utc = now,
        city_name = "MARTA",
        feed_freshness_seconds = 3.5,
        time_taken_seconds = 0.2,
        health_ok = true,
        tones_emitted = 4,
        vehicles_processed = 120,
        gc_heap_bytes = 1_000_000,
        process_working_set_bytes = 2_000_000,
        vehicle_state_cache_size = 120,
        crossing_baseline_cache_size = 120,
        route_index_size = 30,
        route_trigger_point_cache_size = 60,
        crossings_suppressed_first_seen = 2,
        crossings_suppressed_delta_leq0 = 80,
        crossings_suppressed_teleport = 1,
        crossings_suppressed_transfer = 0
    };

    static TelemetryEvent FullCycleRow(DateTime now) => new()
    {
        event_type = "FullCycle",
        event_id = "event-2",
        observation_utc = now,
        cities_processed_count = 1,
        cities_processed_csv = "MARTA",
        time_taken_seconds = 0.3,
        health_ok = true,
        tones_emitted = 4,
        vehicles_processed = 120,
        gc_heap_bytes = 1_000_000,
        process_working_set_bytes = 2_000_000,
        vehicle_state_cache_size = 120,
        crossing_baseline_cache_size = 120,
        route_index_size = 30,
        route_trigger_point_cache_size = 60,
        crossings_suppressed_first_seen = 2,
        crossings_suppressed_delta_leq0 = 80,
        crossings_suppressed_teleport = 1,
        crossings_suppressed_transfer = 0
    };

    [Fact]
    public async Task Schema_ContainsAllExpectedColumns_WithContractNames()
    {
        var now = new DateTime(2026, 6, 4, 12, 0, 0, DateTimeKind.Utc);
        var rows = new List<TelemetryEvent> { PerCityCycleRow(now), FullCycleRow(now) };

        using var ms = new MemoryStream();
        var schema = await ParquetSerializer.SerializeAsync(rows, ms);

        var columnNames = schema.DataFields.Select(f => f.Name).ToArray();
        Assert.Equal(ExpectedColumns.Length, columnNames.Length);
        foreach (var expected in ExpectedColumns)
            Assert.Contains(expected, columnNames);
    }

    [Fact]
    public async Task RoundTrip_PerCityCycleRow_FullCycleOnlyColumnsAreNull()
    {
        var now = new DateTime(2026, 6, 4, 12, 0, 0, DateTimeKind.Utc);
        var rows = new List<TelemetryEvent> { PerCityCycleRow(now) };

        using var ms = new MemoryStream();
        await ParquetSerializer.SerializeAsync(rows, ms);
        ms.Position = 0;

        var read = await ParquetSerializer.DeserializeAsync<TelemetryEvent>(ms);
        var row = Assert.Single(read);

        Assert.Equal("PerCityCycle", row.event_type);
        Assert.Equal("MARTA", row.city_name);
        Assert.Equal(3.5, row.feed_freshness_seconds);
        Assert.Null(row.cities_processed_count);
        Assert.Null(row.cities_processed_csv);
        Assert.Equal(2, row.crossings_suppressed_first_seen);
        Assert.Equal(80, row.crossings_suppressed_delta_leq0);
        Assert.Equal(1, row.crossings_suppressed_teleport);
        Assert.Equal(0, row.crossings_suppressed_transfer);
    }

    [Fact]
    public async Task RoundTrip_FullCycleRow_PerCityOnlyColumnsAreNull()
    {
        var now = new DateTime(2026, 6, 4, 12, 0, 0, DateTimeKind.Utc);
        var rows = new List<TelemetryEvent> { FullCycleRow(now) };

        using var ms = new MemoryStream();
        await ParquetSerializer.SerializeAsync(rows, ms);
        ms.Position = 0;

        var read = await ParquetSerializer.DeserializeAsync<TelemetryEvent>(ms);
        var row = Assert.Single(read);

        Assert.Equal("FullCycle", row.event_type);
        Assert.Equal(1, row.cities_processed_count);
        Assert.Equal("MARTA", row.cities_processed_csv);
        Assert.Null(row.city_name);
        Assert.Null(row.feed_freshness_seconds);
    }
}
