using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;

namespace ChefKnifeStudios.TransitJazz.Shared.Stats;

/// <summary>Aggregate-only reported identity count and its independent feed coverage.</summary>
public sealed record StatsReportedVehicleSummary
{
    public const string CurrentDefinitionVersion = "reported-vehicles-v1";
    public string Window { get; init; } = "today";
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public long? Count { get; init; }
    public string State { get; init; } = "unavailable";
    public string PopulationScope { get; init; } = "cityFeeds";
    public string DefinitionVersion { get; init; } = CurrentDefinitionVersion;
    public int ExpectedCityHours { get; init; }
    public int ContributingCityHours { get; init; }
    public int FullyCoveredCityHours { get; init; }
    public int ExpectedCityCount { get; init; }
    public int ContributingCityCount { get; init; }
    public IReadOnlyList<StatsReportedVehicleCitySummary> Cities { get; init; } = [];
    public IReadOnlyList<string> Limitations { get; init; } = [];

    public static StatsReportedVehicleSummary Unavailable(string window, bool pending = false) => new()
    {
        Window = window,
        Count = null,
        State = pending ? "pending" : "unavailable",
    };
}

public sealed record StatsReportedVehicleCitySummary
{
    public required string CitySlug { get; init; }
    public required string WindowReference { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public long? Count { get; init; }
    public required string State { get; init; }
    public int ExpectedHours { get; init; }
    public int ContributingHours { get; init; }
    public int FullyCoveredHours { get; init; }
    public int BoundaryExcludedHours { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public DateTime? FirstContributingUtc { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public DateTime? LastContributingUtc { get; init; }
    public IReadOnlyList<StatsReportedVehicleExclusion> Exclusions { get; init; } = [];
    public long UnidentifiableRecordCount { get; init; }
    public long FallbackRecordCount { get; init; }
    public IReadOnlyList<string> Limitations { get; init; } = [];
}

public sealed record StatsReportedVehicleExclusion(string Code, int Hours);
