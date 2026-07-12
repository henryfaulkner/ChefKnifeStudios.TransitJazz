using System.Collections.Generic;

namespace ChefKnifeStudios.MartaJazz.Shared.TelemetryData;

/// <summary>Closed set of columns a caller may sort a telemetry table by (server enforces this — no
/// free-text column name reaches the sort). Names match TelemetryEventDto properties.</summary>
public enum TelemetrySortColumn
{
    ObservationUtc,
    EventId,
    CityName,
    FeedFreshnessSeconds,
    CitiesProcessedCount,
    CitiesProcessedCsv,
    TimeTakenSeconds,
    HealthOk,
    TonesEmitted,
    VehiclesProcessed,
    GcHeapBytes,
    ProcessWorkingSetBytes,
    VehicleStateCacheSize,
    CrossingBaselineCacheSize,
    RouteIndexSize,
    RouteTriggerPointCacheSize,
}

/// <summary>One (event_type, city_name) group's row count for today, used to render table shells before any page loads.</summary>
public sealed record TelemetryTableSummaryDto
{
    public string EventType { get; init; } = "";
    public string? CityName { get; init; }
    public int TotalCount { get; init; }
}

public sealed record TelemetryTodaySummaryDto
{
    public IReadOnlyList<TelemetryTableSummaryDto> Tables { get; init; } = [];
}

/// <summary>One page of rows for a single (event_type, city_name) table.</summary>
public sealed record TelemetryPageDto
{
    public IReadOnlyList<TelemetryEventDto> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}
