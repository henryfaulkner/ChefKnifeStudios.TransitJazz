using System.Text.Json;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class ReportedVehicleCoverageQueryTests
{
    [ReportedVehiclePostgresFact]
    public async Task ExclusionsAndContributingLimitationsStaySeparateFromPopulationAndDenominator()
    {
        await using var database = await ReportedVehicleDatabaseFixture.CreateAsync();
        var first = new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc);
        var rows = new List<CityReportedVehicleHour>
        {
            Header(first),
            Header(first.AddHours(1), status: ReportedVehicleCollectionStatus.Partial,
                incomplete: ["start_boundary_missing"], startBoundary: false, endBoundary: false,
                reasons: ["start_boundary_missing"]),
            Header(first.AddHours(2), status: ReportedVehicleCollectionStatus.NoData,
                successful: 0, failed: 1, incomplete: ["source_failure"], startBoundary: false,
                endBoundary: false, reasons: ["source_failure"]),
            Header(first.AddHours(3), definition: "unsupported-definition"),
            Header(first.AddHours(4), sourceFingerprint: new string('c', 64)),
            Header(first.AddHours(5), cadenceFingerprint: new string('d', 64)),
            Header(first.AddHours(6), status: ReportedVehicleCollectionStatus.Partial, conflict: true),
            Header(first.AddHours(7), status: ReportedVehicleCollectionStatus.Partial, integrity: false),
            Header(first.AddHours(8), status: ReportedVehicleCollectionStatus.Partial, diagnostics: "[]"),
            Header(first.AddHours(9), status: ReportedVehicleCollectionStatus.Partial,
                failed: 1, incomplete: ["cadence_gap", "source_failure"], reasons: ["cadence_gap", "source_failure"]),
            Header(first.AddHours(10), status: ReportedVehicleCollectionStatus.Partial,
                incomplete: ["cadence_gap"], maximumGap: 90, reasons: ["cadence_gap"]),
            Header(first.AddHours(11), status: ReportedVehicleCollectionStatus.Partial,
                unidentifiable: 1, incomplete: ["missing_identity"], reasons: ["missing_identity"]),
        };
        var membershipMismatch = Header(first.AddHours(12));
        membershipMismatch.MembershipCount = 1;
        rows.Add(membershipMismatch);
        rows[0].Memberships.Add(Membership(first, "observed"));
        rows[0].MembershipCount = 1;
        rows[9].Memberships.Add(Membership(first.AddHours(9), "source-failure-partial"));
        rows[9].MembershipCount = 1;
        rows[10].Memberships.Add(Membership(first.AddHours(10), "cadence-gap-partial"));
        rows[10].MembershipCount = 1;
        rows[11].Memberships.Add(Membership(first.AddHours(11), "known-among-missing-ids"));
        rows[11].MembershipCount = 1;

        await using (var context = database.CreateDbContext())
        {
            context.CityReportedVehicleHours.AddRange(rows);
            await context.SaveChangesAsync();
        }

        var hours = Enumerable.Range(0, 14).Select(offset => new StatsOverlayQueryHour
        {
            CitySlug = "atlanta",
            HourStartUtc = first.AddHours(offset),
            IsToday = true,
            IsLast24 = true,
            ExpectedCadenceSeconds = 30,
        }).ToArray();
        var input = new StatsOverlayQueryInput
        {
            Scope = "city",
            Hours = hours,
            ReportedVehiclesEnabled = true,
            ReportedVehiclePolicies = [new StatsOverlayReportedVehiclePolicy(
                "atlanta", new string('a', 64), new string('b', 64), [new("feed-a", 30)])],
        };

        var result = await new StatsOverlayQuery(database).ReadAsync(input,
            TimeSpan.FromSeconds(5), CancellationToken.None);
        var today = Assert.Single(result.ReportedVehicleWindows, row => row.Window == "today");

        Assert.Equal(14, today.ExpectedHours);
        Assert.Equal(5, today.ContributingHours);
        Assert.Equal(1, today.FullyCoveredHours);
        Assert.Equal(4, today.Count);
        Assert.Empty(result.RouteMeasures); // Feed counts remain available without route-hour evidence.
        Assert.Equal(1, today.UnidentifiableRecordCount);
        Assert.Equal(9, today.Exclusions.Sum(row => row.Hours)); // One primary exclusion per excluded hour.
        Assert.Equal(new Dictionary<string, int>
        {
            ["cadence_policy_mismatch"] = 1,
            ["conflict"] = 1,
            ["identity_loss"] = 3,
            ["missing_hour"] = 1,
            ["no_usable_observation"] = 1,
            ["source_policy_mismatch"] = 1,
            ["unsupported_definition"] = 1,
        }, today.Exclusions.ToDictionary(row => row.Code, row => row.Hours, StringComparer.Ordinal));
        Assert.Contains("cadence_gap", today.Limitations);
        Assert.Contains("missing_identity", today.Limitations);
        Assert.Contains("source_failure", today.Limitations);
        Assert.Contains("start_boundary_missing", today.Limitations);
    }

    [ReportedVehiclePostgresFact]
    public async Task MissingSeattlePolicyKeepsItsHoursInTheDenominatorAsUnavailable()
    {
        await using var database = await ReportedVehicleDatabaseFixture.CreateAsync();
        var first = new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc);
        var hours = new[] { "atlanta", "seattle" }.SelectMany(city => Enumerable.Range(0, 2).Select(offset =>
            new StatsOverlayQueryHour
            {
                CitySlug = city,
                HourStartUtc = first.AddHours(offset),
                IsToday = true,
                IsLast24 = true,
                ExpectedCadenceSeconds = 30,
            })).ToArray();
        var input = new StatsOverlayQueryInput
        {
            Scope = "all",
            Hours = hours,
            ReportedVehiclesEnabled = true,
            ReportedVehiclePolicies = [new StatsOverlayReportedVehiclePolicy(
                "atlanta", new string('a', 64), new string('b', 64), [new("feed-a", 30)])],
        };

        var result = await new StatsOverlayQuery(database).ReadAsync(input,
            TimeSpan.FromSeconds(5), CancellationToken.None);
        var seattle = Assert.Single(result.ReportedVehicleWindows,
            row => row.CitySlug == "seattle" && row.Window == "today");

        Assert.Null(seattle.Count);
        Assert.Equal(2, seattle.ExpectedHours);
        Assert.Equal(0, seattle.ContributingHours);
        Assert.Equal(2, Assert.Single(seattle.Exclusions, exclusion => exclusion.Code == "source_policy_mismatch").Hours);
    }

    static CityReportedVehicleHour Header(DateTime hour,
        ReportedVehicleCollectionStatus status = ReportedVehicleCollectionStatus.Complete,
        string? definition = null, string? sourceFingerprint = null, string? cadenceFingerprint = null,
        bool conflict = false, bool integrity = true, int successful = 1, int failed = 0,
        long unidentifiable = 0, string[]? incomplete = null, bool startBoundary = true,
        bool endBoundary = true, decimal? maximumGap = 20, string[]? reasons = null, string? diagnostics = null)
    {
        var header = new CityReportedVehicleHour
        {
            CitySlug = "atlanta",
            HourStartUtc = hour,
            DefinitionVersion = definition ?? CityReportedVehicleHour.CurrentDefinitionVersion,
            CaptureRunId = Guid.NewGuid(),
            SourceSetFingerprint = sourceFingerprint ?? new string('a', 64),
            CadencePolicyFingerprint = cadenceFingerprint ?? new string('b', 64),
            CollectionStatus = status,
            IdentityIntegrity = integrity,
            HasConflict = conflict,
            SuccessfulPollCount = successful,
            FailedPollCount = failed,
            UnidentifiableRecordCount = unidentifiable,
            ExpectedSourceCount = 1,
            SourceDiagnosticsJson = diagnostics ?? SourceDiagnostic(hour, successful, failed,
                unidentifiable, startBoundary, endBoundary, maximumGap, reasons ?? []),
            IncompleteReasons = incomplete ?? [],
            PersistedAtUtc = DateTime.UtcNow,
        };
        header.MembershipCount = 0;
        return header;
    }

    static string SourceDiagnostic(DateTime hour, long successful, long failed, long unidentifiable,
        bool startBoundary, bool endBoundary, decimal? maximumGap, string[] reasons) =>
        JsonSerializer.Serialize(new[]
        {
            new
            {
                SourceKey = "feed-a",
                SuccessfulPollCount = successful,
                FailedPollCount = failed,
                FirstSuccessfulUtc = successful == 0 ? (DateTime?)null : hour.AddSeconds(10),
                LastSuccessfulUtc = successful == 0 ? (DateTime?)null : hour.AddSeconds(50),
                PredecessorWitnessUtc = startBoundary ? hour.AddSeconds(-1) : (DateTime?)null,
                SuccessorWitnessUtc = endBoundary ? hour.AddHours(1).AddSeconds(1) : (DateTime?)null,
                MaximumGapSeconds = maximumGap,
                StartBoundaryOk = startBoundary,
                EndBoundaryOk = endBoundary,
                RawVehicleRecordCount = checked(successful + unidentifiable),
                UnidentifiableRecordCount = unidentifiable,
                FallbackRecordCount = 0,
                Reasons = reasons,
            }
        });

    static CityReportedVehicleHourMembership Membership(DateTime hour, string value) => new()
    {
        CitySlug = "atlanta",
        HourStartUtc = hour,
        IdentityNamespace = "bus",
        IdentityKind = "descriptor",
        IdentityValue = value,
    };
}
