using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;

namespace ChefKnifeStudios.TransitJazz.Shared.Stats;

public static class StatsOverlayVocabulary
{
    public const string ContractVersion = "stats-overlay-v1";
    public const string MetricDefinitionVersion = "observed-city-route-hour-statistics-v1";

    public static readonly string[] CoverageStates = ["complete", "partial", "unavailable", "pending"];
    public static readonly string[] WindowReferences = ["today", "last24CompletedHours"];
    public static readonly string[] Reasons =
    [
        "missing_cohort", "partial_cohort", "no_data", "conflict", "unsupported_definition",
        "unsupported_cadence", "inconsistent_cohort", "unknown_route", "route_not_retained",
        "history_disabled", "not_retained", "boundary_excluded", "pending", "future",
        "zero_denominator", "no_active_vehicles"
    ];
    public static readonly string[] Warnings =
    ["partial_coverage", "city_days_differ", "catalog_changed", "boundary_excluded", "historical_membership_unknown", "different_route_periods"];
}

public sealed record StatsOverlayResponse
{
    int? refreshIntervalMinutes;
    DateTime? refreshWindowStartUtc;
    DateTime? nextRefreshAtUtc;

    public string ContractVersion { get; init; } = StatsOverlayVocabulary.ContractVersion;
    public string MetricDefinitionVersion { get; init; } = StatsOverlayVocabulary.MetricDefinitionVersion;
    public DateTime GeneratedAtUtc { get; init; }
    public DateTime CutoffUtc { get; init; }
    [JsonIgnore] public bool RefreshIntervalMinutesPresent { get; private init; }
    [JsonIgnore] public bool RefreshWindowStartUtcPresent { get; private init; }
    [JsonIgnore] public bool NextRefreshAtUtcPresent { get; private init; }
    public int? RefreshIntervalMinutes
    {
        get => refreshIntervalMinutes;
        init
        {
            refreshIntervalMinutes = value;
            RefreshIntervalMinutesPresent = true;
        }
    }
    public DateTime? RefreshWindowStartUtc
    {
        get => refreshWindowStartUtc;
        init
        {
            refreshWindowStartUtc = value;
            RefreshWindowStartUtcPresent = true;
        }
    }
    public DateTime? NextRefreshAtUtc
    {
        get => nextRefreshAtUtc;
        init
        {
            nextRefreshAtUtc = value;
            NextRefreshAtUtcPresent = true;
        }
    }
    public required StatsSelection Selection { get; init; }
    public required IReadOnlyList<StatsCityWindow> Cities { get; init; }
    public required IReadOnlyList<StatsRouteOption> RouteOptions { get; init; }
    public required StatsDistanceSummary TodayDistance { get; init; }
    public required StatsDistanceSummary Last24Distance { get; init; }
    public StatsReportedVehicleSummary TodayReportedVehicles { get; init; } = StatsReportedVehicleSummary.Unavailable("today");
    public StatsReportedVehicleSummary Last24ReportedVehicles { get; init; } = StatsReportedVehicleSummary.Unavailable("last24");
    public required IReadOnlyList<StatsCategoryShare> CategoryShares { get; init; }
    public required IReadOnlyList<StatsBubbleStatistic> Bubbles { get; init; }
    public required StatsTimeline Timeline { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public StatsPreviousDaySnapshot? PreviousDay { get; init; }
}

/// <summary>City-local calendar day shown while today has no eligible completed hours.</summary>
public sealed record StatsPreviousDaySnapshot
{
    public required StatsCityWindow City { get; init; }
    public required StatsDistanceSummary Distance { get; init; }
    public required StatsReportedVehicleSummary ReportedVehicles { get; init; }
    public required IReadOnlyList<StatsCategoryShare> CategoryShares { get; init; }
    public required IReadOnlyList<StatsBubbleStatistic> Bubbles { get; init; }
    public required StatsTimeline Timeline { get; init; }
}

public sealed record StatsSelection(
    string Scope,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? CitySlug,
    IReadOnlyList<string> RouteJoinKeys);

public sealed record StatsCityWindow
{
    public required string CitySlug { get; init; }
    public required string DisplayNameKey { get; init; }
    public required string TimeZone { get; init; }
    public required DateOnly LocalDate { get; init; }
    public DateTime LocalDayStartUtc { get; init; }
    public DateTime LocalDayEndUtc { get; init; }
    public DateTime RequestedTodayStartUtc { get; init; }
    public DateTime RequestedTodayEndUtc { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public DateTime? EligibleTodayStartUtc { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public DateTime? EligibleTodayEndUtc { get; init; }
    public DateTime Last24StartUtc { get; init; }
    public DateTime Last24EndUtc { get; init; }
    public required StatsCoverage TodayCoverage { get; init; }
    public required StatsCoverage Last24Coverage { get; init; }
}

public sealed record StatsCoverage
{
    public required string State { get; init; }
    public int ExpectedClosedHours { get; init; }
    public int ContributingHours { get; init; }
    public int FullyCoveredHours { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public long? ExpectedRouteHours { get; init; }
    public long AvailableRouteHours { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public IReadOnlyList<StatsSelectedRouteCoverage>? SelectedRouteCoverage { get; init; }
    public required IReadOnlyList<StatsExclusion> Exclusions { get; init; }
    public int BoundaryExcludedHours { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public DateTime? FirstContributingUtc { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public DateTime? LastContributingUtc { get; init; }
    public DateTime PendingThroughUtc { get; init; }
    public bool HistoricalMembershipUnknown { get; init; }
}

public sealed record StatsSelectedRouteCoverage(string CitySlug, string RouteJoinKey, int ExpectedHours, int AvailableHours, IReadOnlyList<string> Reasons);
public sealed record StatsExclusion(string Reason, int Hours);

public sealed record StatsDistanceSummary
{
    public required string Window { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public decimal? Kilometers { get; init; }
    public required StatsCoverage Coverage { get; init; }
    public int ContributingCityHours { get; init; }
    public int ExpectedCityHours { get; init; }
    public int ContributingCityCount { get; init; }
    public int ExpectedCityCount { get; init; }
}

public sealed record StatsCategoryShare(
    string CategoryKey,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] decimal? Kilometers,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] decimal? Percent,
    string CoverageReference);

public sealed record StatsRouteOption
{
    public required string CitySlug { get; init; }
    public required string RouteJoinKey { get; init; }
    public required string Label { get; init; }
    public required string CategoryKey { get; init; }
    public DateTime MetadataTimestampUtc { get; init; }
    public bool CategoryChanged { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? AvailabilityReason { get; init; }
}

public sealed record StatsBubbleStatistic
{
    public required string EntityKey { get; init; }
    public required string EntityKind { get; init; }
    public required string CitySlug { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? RouteJoinKey { get; init; }
    public required string Label { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? CategoryKey { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public decimal? DistanceKilometers { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public long? RouteVehicleHours { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public long? ActiveClockHours { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public long? ActivitySampleNumerator { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public long? SampleDenominator { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public decimal? X { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public decimal? Y { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public decimal? MeanActiveVehicles { get; init; }
    public required StatsCoverage Coverage { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? OmissionReason { get; init; }
}

public sealed record StatsTimeline
{
    public required string SeriesKind { get; init; }
    public bool Stacked { get; init; }
    public decimal ElapsedHourExtent { get; init; }
    public required IReadOnlyList<StatsTimelineSeries> Series { get; init; }
}

public sealed record StatsTimelineSeries
{
    public required string Key { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? CategoryKey { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? CitySlug { get; init; }
    public required string WindowReference { get; init; }
    public required IReadOnlyList<StatsTimelineBucket> Buckets { get; init; }
}

public sealed record StatsTimelineBucket
{
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public required DateOnly LocalDate { get; init; }
    public required string LocalEndLabel { get; init; }
    public required string UtcOffset { get; init; }
    public decimal ElapsedHours { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public decimal? HourlyKnownKilometers { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public decimal? CumulativeObservedKilometers { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public decimal? PlotCumulativeKilometers { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public int? AvailableSelectedRoutes { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public int? ExpectedSelectedRoutes { get; init; }
    public required string CoverageState { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? Reason { get; init; }
}
