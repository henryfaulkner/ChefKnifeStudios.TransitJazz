using System;

namespace ChefKnifeStudios.TransitJazz.Shared.TelemetryData;

/// <summary>
/// One denormalized telemetry row, mirrored from the TransitDataWorker's
/// TelemetryEvent parquet record (event_type discriminates PerCityCycle vs FullCycle).
/// </summary>
public sealed record TelemetryEventDto
{
    public string EventType { get; init; } = "";
    public string EventId { get; init; } = "";
    public DateTime ObservationUtc { get; init; }

    public string? CityName { get; init; }
    public double? FeedFreshnessSeconds { get; init; }

    public int? CitiesProcessedCount { get; init; }
    public string? CitiesProcessedCsv { get; init; }

    public double? TimeTakenSeconds { get; init; }
    public bool? HealthOk { get; init; }
    public int? TonesEmitted { get; init; }
    public int? VehiclesProcessed { get; init; }
    public long? GcHeapBytes { get; init; }
    public long? ProcessWorkingSetBytes { get; init; }
    public int? VehicleStateCacheSize { get; init; }
    public int? CrossingBaselineCacheSize { get; init; }
    public int? RouteIndexSize { get; init; }
    public int? RouteTriggerPointCacheSize { get; init; }
}
