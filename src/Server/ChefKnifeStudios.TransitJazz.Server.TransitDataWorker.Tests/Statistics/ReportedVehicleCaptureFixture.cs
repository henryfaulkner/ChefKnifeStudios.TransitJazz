namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

/// <summary>Hand-authored expected populations; deliberately independent of production code.</summary>
public static class ReportedVehicleCaptureFixture
{
    public const string CityA = "fixture-a";
    public const string CityB = "fixture-b";
    public static readonly DateTime Hour1 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime Hour2 = Hour1.AddHours(1);
    public static readonly DateTime Hour3 = Hour1.AddHours(2);
    public static readonly DateTime Hour4 = Hour1.AddHours(3);

    public static readonly IReadOnlyList<ReportedVehicleFixtureHour> Hours =
    [
        new(CityA, Hour1, "Complete", [Id("bus", "descriptor", "x"), Id("bus", "descriptor", "y")]),
        new(CityA, Hour2, "Partial", [Id("bus", "descriptor", "x"), Id("rail", "agency-train-id", "x")]),
        new(CityA, Hour3, "Partial", []),
        new(CityA, Hour4, "Conflict", [Id("bus", "descriptor", "z")]),
        new(CityB, Hour1, "Complete", [Id("bus", "descriptor", "x")]),
    ];

    // A's H1-H4 population is x, y, and rail/x. H3 is a real empty partial, H4 is excluded.
    public static IReadOnlySet<ReportedVehicleFixtureIdentity> ExpectedCityAWindow =>
        DistinctFor(CityA, Hour1, Hour4);

    public static IReadOnlySet<ReportedVehicleFixtureIdentity> DistinctFor(string city, DateTime start, DateTime end)
    {
        var identities = new HashSet<ReportedVehicleFixtureIdentity>();
        foreach (var hour in Hours)
        {
            if (!string.Equals(hour.City, city, StringComparison.Ordinal)
                || hour.HourStartUtc < start || hour.HourStartUtc > end
                || hour.Status == "Conflict") continue;
            identities.UnionWith(hour.Identities);
        }
        return identities;
    }

    static ReportedVehicleFixtureIdentity Id(string domain, string kind, string value) => new(domain, kind, value);
}

public sealed record ReportedVehicleFixtureHour(
    string City,
    DateTime HourStartUtc,
    string Status,
    IReadOnlyList<ReportedVehicleFixtureIdentity> Identities);

public sealed record ReportedVehicleFixtureIdentity(string Domain, string Kind, string ExactValue);
