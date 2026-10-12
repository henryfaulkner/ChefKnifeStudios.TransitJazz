namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

/// <summary>Hand-authored values used as the independent expected side of Stats acceptance checks.</summary>
public static class StatsOverlayAcceptanceFixtures
{
    public static IReadOnlyList<string> Cities { get; } = ["atlanta", "toronto"];
    public static IReadOnlyList<string> Categories { get; } = ["bus", "rail", "unknown"];

    public static IReadOnlyList<FixtureRouteHour> SelectedRouteHours { get; } =
        Enumerable.Range(0, 10).Select(hour => Row("atlanta", "bus-a", "bus", hour, 10m, 1, 12, 12))
            .Concat(Enumerable.Range(0, 6).Select(hour => Row("atlanta", "bus-b", "bus", hour, 10m, 1, 8, 8)))
            .ToArray();

    public static IReadOnlyList<FixtureRouteHour> RatioHours { get; } =
    [
        Row("atlanta", "route-x", "bus", 20, 20m, 4, 120, 60),
        Row("atlanta", "route-x", "bus", 21, 10m, 2, 60, 60)
    ];

    public static IReadOnlyList<FixtureRouteHour> ContextRows { get; } =
    [
        Row("atlanta", "inactive-recorded", "rail", 1, 0m, 0, 0, 12),
        Row("atlanta", "stationary-active", "bus", 2, 0m, 2, 24, 12),
        Row("atlanta", "transfer-route-a", "bus", 3, 3m, 1, 12, 12),
        Row("atlanta", "transfer-route-b", "rail", 3, 2m, 1, 12, 12),
        Row("atlanta", "unknown-category", "unknown", 4, 1m, 1, 12, 12),
        Row("atlanta", "category-changed", "bus", 5, 1m, 1, 12, 12),
        Row("atlanta", "category-changed", "rail", 6, 1m, 1, 12, 12),
        Row("toronto", "streetcar-a", "rail", 1, 7m, 2, 24, 12),
    ];

    public static IReadOnlyList<FixtureCumulativeBucket> GapPrefix { get; } =
    [
        new(0, 100m, 100m, 100m),
        new(1, null, 100m, null),
        new(2, 40m, 140m, 140m)
    ];

    static FixtureRouteHour Row(
        string city,
        string route,
        string category,
        int hour,
        decimal kilometers,
        long routeVehicleHours,
        long activitySamples,
        long sampleDenominator) => new(
            city,
            route,
            category,
            DateTime.SpecifyKind(DateTime.UnixEpoch.AddHours(hour), DateTimeKind.Utc),
            kilometers,
            routeVehicleHours,
            activitySamples,
            sampleDenominator,
            CohortState.Complete);
}

public sealed record FixtureRouteHour(
    string CitySlug,
    string RouteJoinKey,
    string CategoryKey,
    DateTime HourStartUtc,
    decimal Kilometers,
    long RouteVehicleHours,
    long ActivitySamples,
    long SampleDenominator,
    CohortState State);

public sealed record FixtureCumulativeBucket(int Hour, decimal? HourlyKnownKilometers, decimal? CumulativeObservedKilometers, decimal? PlotCumulativeKilometers);

public enum CohortState { Complete, Missing, Conflicted }
