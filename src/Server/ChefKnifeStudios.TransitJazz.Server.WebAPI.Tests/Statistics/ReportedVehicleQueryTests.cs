using System.Text.Json;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class ReportedVehicleQueryTests
{
    [ReportedVehiclePostgresFact]
    public async Task DisabledQueryRunsAgainstSchemaBeforeReportedVehicleMigration()
    {
        await using var database = await ReportedVehicleDatabaseFixture.CreateAsync(
            targetMigration: "20261010151240_CreateCityRouteHourStatistics");
        var hour = new DateTime(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc);
        var result = await new StatsOverlayQuery(database).ReadAsync(new StatsOverlayQueryInput
        {
            Scope = "city",
            Hours = [Hour("atlanta", hour)],
            ReportedVehiclesEnabled = false,
        }, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Empty(result.ReportedVehicleWindows);
    }

    [ReportedVehiclePostgresFact]
    public async Task MissingPoliciesReturnUnavailableEvenWithRetainedMemberships()
    {
        await using var database = await ReportedVehicleDatabaseFixture.CreateAsync();
        var hour = new DateTime(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc);
        await using (var context = database.CreateDbContext())
        {
            context.CityReportedVehicleHours.AddRange(
                Header("atlanta", hour, [Membership("atlanta", hour, "bus", "descriptor", "known")]),
                Header("unsupported", hour, [Membership("unsupported", hour, "bus", "descriptor", "old-policy")]));
            await context.SaveChangesAsync();
        }
        var query = new StatsOverlayQuery(database);
        var hours = new[] { Hour("atlanta", hour), Hour("unsupported", hour), Hour("absent", hour) };
        var policies = Input("city", [Hour("atlanta", hour)]).ReportedVehiclePolicies;
        var input = Input("all", hours, policies: policies);

        var result = await query.ReadAsync(input, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(6, result.ReportedVehicleWindows.Count);
        foreach (var row in result.ReportedVehicleWindows)
        {
            if (row.CitySlug == "atlanta")
            {
                Assert.Equal(1, row.Count);
                Assert.Equal(1, row.ContributingHours);
                continue;
            }
            Assert.Null(row.Count);
            Assert.Equal(0, row.ContributingHours);
            Assert.Equal(0, row.FullyCoveredHours);
            Assert.Equal(1, row.ExpectedHours);
            var exclusion = Assert.Single(row.Exclusions);
            Assert.Equal("source_policy_mismatch", exclusion.Code);
            Assert.Equal(1, exclusion.Hours);
        }

        var noPolicies = await query.ReadAsync(Input("all", hours, policies: []), TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.All(noPolicies.ReportedVehicleWindows, row =>
        {
            Assert.Null(row.Count);
            Assert.Equal(0, row.ContributingHours);
        });
    }

    [ReportedVehiclePostgresFact]
    public async Task WindowCountsUnionWholeHourMembershipsAndKeepCityPopulationsSeparate()
    {
        await using var database = await ReportedVehicleDatabaseFixture.CreateAsync();
        var first = new DateTime(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc);
        var second = first.AddHours(1);
        var emptyHour = second.AddHours(1);
        var missing = emptyHour.AddHours(1);
        var emptyPartial = Header("atlanta", emptyHour, []);
        emptyPartial.CollectionStatus = ReportedVehicleCollectionStatus.Partial;
        emptyPartial.IncompleteReasons = ["start_boundary_missing"];
        emptyPartial.SourceDiagnosticsJson = Diagnostics(emptyHour, startBoundary: false,
            endBoundary: false, maximumGap: null, reasons: ["start_boundary_missing"]);
        await using (var context = database.CreateDbContext())
        {
            context.CityReportedVehicleHours.AddRange(
                Header("atlanta", first, [
                    Membership("atlanta", first, "bus", "descriptor", "x"),
                    Membership("atlanta", first, "bus", "descriptor", "y")]),
                Header("atlanta", second, [
                    Membership("atlanta", second, "bus", "descriptor", "x"),
                    Membership("atlanta", second, "rail", "agency_train_id", "x")]),
                emptyPartial,
                Header("boston", first, [
                    Membership("boston", first, "bus", "descriptor", "x")]));
            await context.SaveChangesAsync();
        }

        var query = new StatsOverlayQuery(database);
        var cityResult = await query.ReadAsync(Input("city", [
            Hour("atlanta", first), Hour("atlanta", second), Hour("atlanta", emptyHour), Hour("atlanta", missing)], ["no-matching-route"]),
            TimeSpan.FromSeconds(5), CancellationToken.None);
        var city = Assert.Single(cityResult.ReportedVehicleWindows,
            row => row.CitySlug == "atlanta" && row.Window == "today");
        Assert.Equal(3, city.Count); // Distinct tuple union is 3; summing hourly counts would be 4.
        Assert.Equal(4, city.ExpectedHours);
        Assert.Equal(3, city.ContributingHours);
        Assert.Equal(2, city.FullyCoveredHours);
        Assert.Contains(city.Exclusions, exclusion => exclusion.Code == "missing_hour" && exclusion.Hours == 1);

        var emptyResult = await query.ReadAsync(Input("city", [Hour("atlanta", emptyHour)]),
            TimeSpan.FromSeconds(5), CancellationToken.None);
        var empty = Assert.Single(emptyResult.ReportedVehicleWindows, row => row.Window == "today");
        Assert.Equal(0, empty.Count);
        Assert.Equal(1, empty.ContributingHours);
        Assert.Equal(0, empty.FullyCoveredHours);
        Assert.Empty(empty.Exclusions);

        var allResult = await query.ReadAsync(Input("all", [
            Hour("atlanta", first), Hour("atlanta", second), Hour("atlanta", emptyHour),
            Hour("atlanta", missing), Hour("boston", first)]),
            TimeSpan.FromSeconds(5), CancellationToken.None);
        var atlanta = Assert.Single(allResult.ReportedVehicleWindows,
            row => row.CitySlug == "atlanta" && row.Window == "today");
        var boston = Assert.Single(allResult.ReportedVehicleWindows,
            row => row.CitySlug == "boston" && row.Window == "today");
        Assert.Equal(3, atlanta.Count);
        Assert.Equal(1, boston.Count); // Same raw tuple in another city remains a separate city population.
        Assert.Equal(4, atlanta.Count + boston.Count);
    }

    [ReportedVehiclePostgresFact]
    public async Task ReadSeesOnlyThePreOrPostAtomicPopulationDuringAWrite()
    {
        await using var database = await ReportedVehicleDatabaseFixture.CreateAsync();
        var hour = new DateTime(2026, 8, 20, 14, 0, 0, DateTimeKind.Utc);
        await using var writer = database.CreateDbContext();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        writer.CityReportedVehicleHours.Add(Header("atlanta", hour,
            [Membership("atlanta", hour, "bus", "descriptor", "uncommitted")]));
        await writer.SaveChangesAsync();

        var input = Input("city", [Hour("atlanta", hour)]);
        var query = new StatsOverlayQuery(database);
        var beforeCommit = await query.ReadAsync(input, TimeSpan.FromSeconds(5), CancellationToken.None);
        var absent = Assert.Single(beforeCommit.ReportedVehicleWindows, row => row.Window == "today");
        Assert.Null(absent.Count);
        Assert.Equal(0, absent.ContributingHours);
        Assert.Contains(absent.Exclusions, row => row.Code == "missing_hour");

        await transaction.CommitAsync();
        var afterCommit = await query.ReadAsync(input, TimeSpan.FromSeconds(5), CancellationToken.None);
        var visible = Assert.Single(afterCommit.ReportedVehicleWindows, row => row.Window == "today");
        Assert.Equal(1, visible.Count);
        Assert.Equal(1, visible.ContributingHours);
    }

    static StatsOverlayQueryInput Input(string scope, StatsOverlayQueryHour[] hours, string[]? routes = null,
        StatsOverlayReportedVehiclePolicy[]? policies = null) => new()
    {
        Scope = scope,
        SelectedRouteKeys = routes ?? [],
        Hours = hours,
        ReportedVehiclesEnabled = true,
        ReportedVehiclePolicies = policies ?? hours.Select(hour => hour.CitySlug).Distinct(StringComparer.Ordinal)
            .Select(city => new StatsOverlayReportedVehiclePolicy(city, new string('a', 64), new string('b', 64),
                [new StatsOverlayReportedVehicleSource("feed-a", 30)])).ToArray(),
    };

    static StatsOverlayQueryHour Hour(string city, DateTime at) => new()
    {
        CitySlug = city,
        HourStartUtc = at,
        IsToday = true,
        IsLast24 = true,
        ExpectedCadenceSeconds = 30,
    };

    static CityReportedVehicleHour Header(string city, DateTime hour,
        CityReportedVehicleHourMembership[] memberships) => new()
    {
        CitySlug = city,
        HourStartUtc = hour,
        DefinitionVersion = CityReportedVehicleHour.CurrentDefinitionVersion,
        CaptureRunId = Guid.Parse("34fdb6a0-a3ce-4501-9c25-b3f4118f3091"),
        SourceSetFingerprint = new string('a', 64),
        CadencePolicyFingerprint = new string('b', 64),
        CollectionStatus = ReportedVehicleCollectionStatus.Complete,
        IdentityIntegrity = true,
        MembershipCount = memberships.Length,
        SuccessfulPollCount = 1,
        ExpectedSourceCount = 1,
        SourceDiagnosticsJson = Diagnostics(hour),
        ObservedStartUtc = hour,
        ObservedEndUtc = hour.AddHours(1),
        PersistedAtUtc = DateTime.UtcNow,
        Memberships = memberships,
    };

    static string Diagnostics(DateTime hour, bool startBoundary = true, bool endBoundary = true,
        decimal? maximumGap = 20, string[]? reasons = null) => JsonSerializer.Serialize(new[]
    {
        new
        {
            SourceKey = "feed-a",
            SuccessfulPollCount = 1,
            FailedPollCount = 0,
            FirstSuccessfulUtc = hour.AddSeconds(10),
            LastSuccessfulUtc = hour.AddSeconds(50),
            PredecessorWitnessUtc = startBoundary ? hour.AddSeconds(-1) : (DateTime?)null,
            SuccessorWitnessUtc = endBoundary ? hour.AddHours(1).AddSeconds(1) : (DateTime?)null,
            MaximumGapSeconds = maximumGap,
            StartBoundaryOk = startBoundary,
            EndBoundaryOk = endBoundary,
            RawVehicleRecordCount = 2,
            UnidentifiableRecordCount = 0,
            FallbackRecordCount = 0,
            Reasons = reasons ?? [],
        }
    });

    static CityReportedVehicleHourMembership Membership(string city, DateTime hour,
        string identityNamespace, string identityKind, string identityValue) => new()
    {
        CitySlug = city,
        HourStartUtc = hour,
        IdentityNamespace = identityNamespace,
        IdentityKind = identityKind,
        IdentityValue = identityValue,
    };
}
