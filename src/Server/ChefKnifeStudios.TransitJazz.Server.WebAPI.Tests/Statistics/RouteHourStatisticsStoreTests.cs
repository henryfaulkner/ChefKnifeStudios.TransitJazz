using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class RouteHourStatisticsStoreTests
{
    [Fact]
    public void EfModelIncludesOneSchemaOnlyRouteHourMigration()
    {
        var options = new DbContextOptionsBuilder<ChefKnifeStudios.TransitJazz.Server.Data.AppDbContext>()
            .UseNpgsql("Host=localhost;Database=transitjazz")
            .Options;
        using var context = new ChefKnifeStudios.TransitJazz.Server.Data.AppDbContext(options);

        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_CreateCityRouteHourStatistics", StringComparison.Ordinal));
        Assert.Equal("city_route_hour_statistics", context.Model.FindEntityType(typeof(CityRouteHourStatistic))!.GetTableName());
    }

    [RouteHourPostgresFact]
    public async Task WholeEnvelopeInsertAndIdenticalRetryRetainOneSortedCohort()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        var rows = new[] { Row("route-b"), Row("route-a") };

        var inserted = await database.Store.WriteAsync(rows);
        var retry = await database.Store.WriteAsync(rows.Reverse().ToArray());

        Assert.Equal(RouteHourStatisticsWriteOutcome.Inserted, inserted.Outcome);
        Assert.Equal(RouteHourStatisticsWriteOutcome.Unchanged, retry.Outcome);
        await using var context = database.CreateDbContext();
        var retained = await context.CityRouteHourStatistics.OrderBy(row => row.RouteJoinKey).ToListAsync();
        Assert.Equal(["route-a", "route-b"], retained.Select(row => row.RouteJoinKey));
        Assert.All(retained, row => Assert.False(row.HasConflict));
        Assert.All(retained, row => Assert.NotEqual(default, row.PersistedAtUtc));
    }

    [RouteHourPostgresFact]
    public async Task DifferentCohortQuarantinesEveryOriginalRowAndIdenticalRetryCannotClearIt()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        var original = new[] { Row("route-a"), Row("route-b") };
        await database.Store.WriteAsync(original);

        var conflict = await database.Store.WriteAsync([Row("route-a"), Row("route-c")]);
        var retry = await database.Store.WriteAsync(original);

        Assert.Equal(RouteHourStatisticsWriteOutcome.Quarantined, conflict.Outcome);
        Assert.Equal(RouteHourStatisticsWriteOutcome.Unchanged, retry.Outcome);
        await using var context = database.CreateDbContext();
        var retained = await context.CityRouteHourStatistics.OrderBy(row => row.RouteJoinKey).ToListAsync();
        Assert.Equal(["route-a", "route-b"], retained.Select(row => row.RouteJoinKey));
        Assert.All(retained, row => Assert.True(row.HasConflict));
    }

    [RouteHourPostgresFact]
    public async Task ConcurrentIdenticalEnvelopesSerializeAndRetainOneCohort()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        var rows = new[] { Row("route-a"), Row("route-b") };

        var outcomes = await Task.WhenAll(database.Store.WriteAsync(rows), database.Store.WriteAsync(rows));

        Assert.Equal(1, outcomes.Count(result => result.Outcome == RouteHourStatisticsWriteOutcome.Inserted));
        Assert.Equal(1, outcomes.Count(result => result.Outcome == RouteHourStatisticsWriteOutcome.Unchanged));
        await using var context = database.CreateDbContext();
        Assert.Equal(2, await context.CityRouteHourStatistics.CountAsync());
    }

    [RouteHourPostgresFact]
    public async Task ConcurrentDifferentRunCohortsQuarantineTheSingleCommittedEnvelope()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        var first = new[] { Row("route-a"), Row("route-b") };
        var second = new[] { Row("route-a"), Row("route-b") };
        foreach (var row in second) row.CaptureRunId = Guid.Parse("4d6150e0-b01d-4c16-8cf1-5c06826b1b1a");

        var outcomes = await Task.WhenAll(database.Store.WriteAsync(first), database.Store.WriteAsync(second));

        Assert.Equal(1, outcomes.Count(result => result.Outcome == RouteHourStatisticsWriteOutcome.Inserted));
        Assert.Equal(1, outcomes.Count(result => result.Outcome == RouteHourStatisticsWriteOutcome.Quarantined));
        await using var context = database.CreateDbContext();
        var rows = await context.CityRouteHourStatistics.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.True(row.HasConflict));
        var retainedRun = Assert.Single(rows.Select(row => row.CaptureRunId).Distinct());
        Assert.Contains(retainedRun, new[]
        {
            Guid.Parse("ed6150e0-b01d-4c16-8cf1-5c06826b1b1a"),
            Guid.Parse("4d6150e0-b01d-4c16-8cf1-5c06826b1b1a"),
        });
    }

    [RouteHourPostgresFact]
    public async Task LaterChunkConstraintFailureRollsBackTheWholeEnvelope()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        await using (var context = database.CreateDbContext())
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE public.city_route_hour_statistics ADD CONSTRAINT ck_route_hour_test_failure CHECK (route_short_name <> 'force-fail')");

        var rows = new[] { Row("route-a"), Row("route-b") };
        rows[1].RouteShortName = "force-fail";
        await Assert.ThrowsAsync<DbUpdateException>(() => database.Store.WriteAsync(rows, maxRowsPerCommand: 1));

        await using var verification = database.CreateDbContext();
        Assert.Empty(await verification.CityRouteHourStatistics.ToListAsync());
    }

    [RouteHourPostgresFact]
    public async Task V2JsonPropertyOrderIsCanonicalAndEveryEvidenceChangeQuarantinesTheOriginal()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        var firstHour = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
        var original = V2Row("v2-order", firstHour);
        Assert.Equal(RouteHourStatisticsWriteOutcome.Inserted, (await database.Store.WriteAsync([original])).Outcome);
        var reordered = V2Row("v2-order", firstHour);
        reordered.SourceEvidence = ReorderJsonObject(reordered.SourceEvidence!);
        Assert.Equal(RouteHourStatisticsWriteOutcome.Unchanged, (await database.Store.WriteAsync([reordered])).Outcome);

        var mutations = new Func<DateTime, string, CityRouteHourStatistic>[]
        {
            (hour, key) => V2Row(key, hour, sourceKey: "marta-rail", fingerprintFill: 'd'),
            (hour, key) => V2Row(key, hour, successfulPollCount: 2),
            (hour, key) => V2Row(key, hour, startBoundaryOk: false),
            (hour, key) => V2Row(key, hour, recoveredDuringHour: true, sourceReasons: ["source_recovered"]),
            (hour, key) => V2Row(key, hour, distanceValid: false, distanceReasons: ["source_failure"]),
            (hour, key) => V2Row(key, hour, activityValid: false, activityReasons: ["unaligned_activity_samples"]),
            (hour, key) => V2Row(key, hour, publicationValid: true, publicationReasons: []),
            (hour, key) => V2Row(key, hour, distanceReasons: ["boundary_unproven"]),
            (hour, key) => V2Row(key, hour, activityReasons: ["unknown_source_provenance"]),
            (hour, key) => V2Row(key, hour, publicationReasons: ["source_failure"]),
            (hour, key) => V2Row(key, hour, fingerprintFill: 'd'),
            (hour, key) => V2Row(key, hour, supportFill: 'd')
        };

        for (var index = 0; index < mutations.Length; index++)
        {
            var hour = firstHour.AddHours(index + 1);
            var key = $"v2-change-{index:D2}";
            var baseline = V2Row(key, hour);
            var changed = mutations[index](hour, key);
            Assert.Equal(RouteHourStatisticsWriteOutcome.Inserted, (await database.Store.WriteAsync([baseline])).Outcome);
            Assert.Equal(RouteHourStatisticsWriteOutcome.Quarantined, (await database.Store.WriteAsync([changed])).Outcome);
            var retained = await ReadOneAsync(database, key);
            Assert.True(retained.HasConflict);
            using var expectedEvidence = JsonDocument.Parse(baseline.SourceEvidence!);
            using var retainedEvidence = JsonDocument.Parse(retained.SourceEvidence!);
            Assert.True(JsonElement.DeepEquals(expectedEvidence.RootElement, retainedEvidence.RootElement));
            Assert.Equal(baseline.SourceSetFingerprint, retained.SourceSetFingerprint);
            Assert.Equal(baseline.ActivitySupportFingerprint, retained.ActivitySupportFingerprint);
        }
    }

    [RouteHourPostgresFact]
    public async Task ConcurrentMixedVersionWritersShareTheSameHourIdentityAndStickyConflict()
    {
        await using var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        var hour = new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
        var legacy = Row("mixed-version", hour);
        var versioned = V2Row("mixed-version", hour);

        var outcomes = await Task.WhenAll(database.Store.WriteAsync([legacy]), database.Store.WriteAsync([versioned]));

        Assert.Equal(1, outcomes.Count(outcome => outcome.Outcome == RouteHourStatisticsWriteOutcome.Inserted));
        Assert.Equal(1, outcomes.Count(outcome => outcome.Outcome == RouteHourStatisticsWriteOutcome.Quarantined));
        Assert.True((await ReadOneAsync(database, "mixed-version")).HasConflict);
    }

    static async Task<CityRouteHourStatistic> ReadOneAsync(RouteHourStatisticsDatabaseFixture database, string key)
    {
        await using var context = database.CreateDbContext();
        return await context.CityRouteHourStatistics.AsNoTracking().SingleAsync(row => row.RouteJoinKey == key);
    }

    static string ReorderJsonObject(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject().Reverse())
            {
                writer.WritePropertyName(property.Name);
                property.Value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    static CityRouteHourStatistic V2Row(
        string key,
        DateTime hour,
        string sourceKey = "marta-bus",
        long successfulPollCount = 1,
        bool startBoundaryOk = true,
        bool recoveredDuringHour = false,
        string[]? sourceReasons = null,
        bool distanceValid = true,
        bool activityValid = true,
        bool publicationValid = false,
        string[]? distanceReasons = null,
        string[]? activityReasons = null,
        string[]? publicationReasons = null,
        char fingerprintFill = 'b',
        char supportFill = 'c')
    {
        var fingerprint = new string(fingerprintFill, 64);
        sourceReasons ??= [];
        distanceReasons ??= [];
        activityReasons ??= [];
        publicationReasons ??= ["publication_unavailable"];
        var receipt = hour.AddMinutes(5);
        var row = Row(key, hour);
        row.DefinitionVersion = CityRouteHourStatistic.SourceEvidenceDefinitionVersion;
        row.SourceSetFingerprint = fingerprint;
        row.DistanceValid = distanceValid;
        row.ActivityValid = activityValid;
        row.PublicationValid = publicationValid;
        row.DistanceReasons = distanceReasons;
        row.ActivityReasons = activityReasons;
        row.PublicationReasons = publicationReasons;
        row.ActivitySupportFingerprint = new string(supportFill, 64);
        row.SourceEvidence = JsonSerializer.Serialize(new
        {
            schemaVersion = "route-hour-source-evidence-v1",
            sourceSetFingerprint = fingerprint,
            expectedSourceKeys = new[] { sourceKey },
            contributingSourceKeys = new[] { sourceKey },
            membershipKnown = true,
            sources = new[]
            {
                new
                {
                    sourceKey,
                    successfulPollCount,
                    emptyPollCount = 0,
                    failedPollCount = 0,
                    disabledPollCount = 0,
                    firstSuccessfulReceiptUtc = receipt,
                    lastSuccessfulReceiptUtc = receipt,
                    maxSuccessfulGapSeconds = 10,
                    startBoundaryOk,
                    endBoundaryOk = true,
                    positionedCoverageValid = true,
                    recoveredDuringHour,
                    reasons = sourceReasons
                }
            }
        });
        row.Validate();
        return row;
    }

    static CityRouteHourStatistic Row(string key, DateTime? hourStartUtc = null) => new()
    {
        CitySlug = "atlanta",
        RouteJoinKey = key,
        HourStartUtc = hourStartUtc ?? new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
        RouteShortName = key,
        StaticRouteId = "shape-" + key,
        Category = "bus",
        RouteCatalogFingerprint = new string('a', 64),
        DefinitionVersion = CityRouteHourStatistic.CurrentDefinitionVersion,
        CaptureRunId = Guid.Parse("ed6150e0-b01d-4c16-8cf1-5c06826b1b1a"),
        CollectionStatus = RouteHourCollectionStatus.Partial,
        IncompleteReasons = ["boundary_unproven"],
        HealthyCadenceLimitSeconds = 30,
        ObservedCycleCount = 1,
        ValidActiveSampleCount = 1,
        ValidPublishCycleCount = 1,
        FirstCycleUtc = (hourStartUtc ?? new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)).AddMinutes(5),
        LastCycleUtc = (hourStartUtc ?? new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)).AddMinutes(5),
        MaxObservationGapSeconds = 10,
        ActiveVehicleCountSum = 0,
        PeakActiveVehicleCount = 0,
        DistinctActiveVehicleCount = 0,
    };
}
