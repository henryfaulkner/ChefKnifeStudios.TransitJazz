using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class CategoryCoverageQueryTests
{
    [CategoryPostgresFact]
    public async Task MissingMinutesAndHourAreExplicitWithoutInventedValuesOrFirstDate()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();

        var rows = await CategoryQueryFixture.ReadAsync(database, "minute-coverage.sql");

        Assert.Equal(61, rows.Count);
        Assert.Equal(60, rows.Count(row => Equals(row["row_kind"], "minute")));
        Assert.All(rows, row =>
        {
            Assert.True((bool)row["is_missing"]!);
            Assert.Equal("Missing", row["exclusion_reason"]);
            Assert.Null(row["avg_active_vehicles"]);
            Assert.Null(row["crossings_published_count"]);
            Assert.Null(row["first_collectible_utc"]);
        });
    }

    [CategoryPostgresFact]
    public async Task HealthyEmptyHourHasDurableCompleteMinutesAndKnownZeroMeasures()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await CategoryQueryData.WriteHourAsync(database, activeCountSumPerMinute: 0, distance: 0, intervals: 0, crossings: 0, distinctVehicles: 0);

        var rows = await CategoryQueryFixture.ReadAsync(database, "minute-coverage.sql");
        var hour = Assert.Single(rows.Where(row => Equals(row["row_kind"], "hour")));

        Assert.True((bool)hour["is_definitive_complete_hour"]!);
        Assert.Equal(60L, hour["durable_complete_minute_count"]);
        Assert.Equal(60L, hour["compatible_complete_minute_count"]);
        Assert.Equal(0m, hour["avg_active_vehicles"]);
        Assert.Equal(0m, hour["published_opportunities_per_complete_hour"]);
        Assert.Null(hour["avg_meters_per_observed_vehicle_hour"]);
        Assert.Null(hour["exclusion_reason"]);
    }

    [CategoryPostgresFact]
    public async Task SubminuteRequestKeepsWholeMinuteContextWithoutProrating()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await CategoryQueryData.WriteHourAsync(database);
        var from = CategoryStatisticsTestDatabase.HourStart.AddSeconds(15);
        var to = CategoryStatisticsTestDatabase.HourStart.AddMinutes(1).AddSeconds(45);

        var rows = await CategoryQueryFixture.ReadAsync(database, "minute-coverage.sql", fromUtc: from, toUtc: to);
        var minutes = rows.Where(row => Equals(row["row_kind"], "minute")).ToArray();
        var hour = Assert.Single(rows.Where(row => Equals(row["row_kind"], "hour")));

        Assert.Equal(2, minutes.Length);
        Assert.All(minutes, row =>
        {
            Assert.True((bool)row["is_partial_boundary"]!);
            Assert.Equal("PartialBoundary", row["exclusion_reason"]);
            Assert.Equal(2L, row["active_vehicle_count_sum"]);
            Assert.Equal(1L, row["valid_active_sample_count"]);
        });
        Assert.Equal(from, minutes[0]["requested_segment_from_utc"]);
        Assert.Equal(to, minutes[1]["requested_segment_to_utc"]);
        Assert.False((bool)hour["is_definitive_complete_hour"]!);
        Assert.Equal("PartialBoundary", hour["exclusion_reason"]);
        Assert.Null(hour["avg_meters_per_observed_vehicle_hour"]);
        Assert.Equal(60L, hour["compatible_complete_minute_count"]);
    }

    [CategoryPostgresFact]
    public async Task NoDataCanRetainFailureAndRejectionDiagnosticsWithoutZeroMeasures()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        var minute = CategoryStatisticsTestDatabase.Minute(0);
        minute.CollectionStatus = CategoryCollectionStatus.NoData;
        minute.ValidActiveSampleCount = minute.ValidPublishCycleCount = minute.ActiveVehicleCountSum = 0;
        minute.DistanceMetersSum = 0; minute.DistanceIntervalCount = 0; minute.CrossingsPublishedCount = 0;
        minute.FailedCycleCount = 1; minute.DistanceRejectedCount = 1;
        await database.Store.WriteAsync([minute], []);

        var row = Assert.Single((await CategoryQueryFixture.ReadAsync(database, "minute-coverage.sql"))
            .Where(row => Equals(row["row_kind"], "minute") && Equals(row["window_start_utc"], CategoryStatisticsTestDatabase.HourStart)));

        Assert.Equal("NoData", row["exclusion_reason"]);
        Assert.Equal(1L, row["failed_cycle_count"]);
        Assert.Equal(1L, row["distance_rejected_count"]);
        Assert.Equal(0L, row["valid_active_sample_count"]);
        Assert.Null(row["avg_active_vehicles"]);
        Assert.Null(row["distance_meters_sum"]);
        Assert.Null(row["crossings_published_count"]);
    }

    [CategoryPostgresFact]
    public async Task ConflictMinuteQuarantinesMeasuresAndExplainsTheExcludedHour()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await CategoryQueryData.WriteHourAsync(database);
        await using var context = database.CreateDbContext();
        await context.CityCategoryMinuteStatistics.Where(row => row.StatMinuteUtc == CategoryStatisticsTestDatabase.HourStart)
            .ExecuteUpdateAsync(x => x.SetProperty(row => row.HasConflict, true));

        var rows = await CategoryQueryFixture.ReadAsync(database, "minute-coverage.sql");
        var hour = Assert.Single(rows.Where(row => Equals(row["row_kind"], "hour")));
        var minute = Assert.Single(rows.Where(row => Equals(row["row_kind"], "minute") && Equals(row["window_start_utc"], CategoryStatisticsTestDatabase.HourStart)));

        Assert.Equal("Conflict", minute["exclusion_reason"]);
        Assert.Null(minute["avg_active_vehicles"]);
        Assert.Equal("Conflict", hour["exclusion_reason"]);
        Assert.Equal(59L, hour["durable_complete_minute_count"]);
        Assert.False((bool)hour["is_definitive_complete_hour"]!);
        Assert.Null(hour["published_opportunities_per_complete_hour"]);
    }

    [CategoryPostgresFact]
    public async Task HourAdditiveMismatchIsExposedWithoutDefinitiveValues()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await CategoryQueryData.WriteHourAsync(database);
        await using var context = database.CreateDbContext();
        await context.CityCategoryMinuteStatistics.Where(row => row.StatMinuteUtc == CategoryStatisticsTestDatabase.HourStart)
            .ExecuteUpdateAsync(x => x.SetProperty(row => row.DistanceMetersSum, 21m));

        var hour = Assert.Single((await CategoryQueryFixture.ReadAsync(database, "minute-coverage.sql"))
            .Where(row => Equals(row["row_kind"], "hour")));

        Assert.Equal("AdditiveMismatch", hour["exclusion_reason"]);
        Assert.Null(hour["avg_meters_per_observed_vehicle_hour"]);
        Assert.Equal(60L, hour["compatible_complete_minute_count"]);
    }

    [CategoryPostgresFact]
    public async Task IncompatibleDefinitionIsVisibleAndCannotMasqueradeAsMissingOrComplete()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await CategoryQueryData.WriteHourAsync(database);
        await using var context = database.CreateDbContext();
        await context.CityCategoryMinuteStatistics.Where(row => row.StatMinuteUtc == CategoryStatisticsTestDatabase.HourStart)
            .ExecuteUpdateAsync(x => x.SetProperty(row => row.DefinitionVersion, "observed-city-category-statistics-v2"));

        var rows = await CategoryQueryFixture.ReadAsync(database, "minute-coverage.sql");
        var hour = Assert.Single(rows.Where(row => Equals(row["row_kind"], "hour")));
        var minute = Assert.Single(rows.Where(row => Equals(row["row_kind"], "minute") && Equals(row["window_start_utc"], CategoryStatisticsTestDatabase.HourStart)));

        Assert.Equal("DefinitionMismatch", minute["exclusion_reason"]);
        Assert.Equal("observed-city-category-statistics-v2", minute["stored_definition_version"]);
        Assert.False((bool)minute["is_missing"]!);
        Assert.Equal("DefinitionMismatch", hour["exclusion_reason"]);
        Assert.Equal(59L, hour["compatible_complete_minute_count"]);
    }

    [CategoryPostgresFact]
    public async Task UnknownHistoryBeginsAtObservedActivationWithoutEarlierZeroBackfill()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        var first = CategoryStatisticsTestDatabase.Minute(10);
        first.Category = "unknown"; first.CollectionStatus = CategoryCollectionStatus.Partial;
        first.DistanceMetersSum = 0; first.DistanceIntervalCount = 0; first.DistanceRejectedCount = 1;
        var laterZero = CategoryStatisticsTestDatabase.Minute(11);
        laterZero.Category = "unknown"; laterZero.CollectionStatus = CategoryCollectionStatus.Partial;
        laterZero.ActiveVehicleCountSum = 0; laterZero.DistanceMetersSum = 0; laterZero.DistanceIntervalCount = 0;
        laterZero.CrossingsPublishedCount = 0;
        await database.Store.WriteAsync([first, laterZero], []);

        var rows = await CategoryQueryFixture.ReadAsync(database, "minute-coverage.sql", category: "unknown");
        var early = Assert.Single(rows.Where(row => Equals(row["row_kind"], "minute") && Equals(row["window_start_utc"], CategoryStatisticsTestDatabase.HourStart)));
        var zero = Assert.Single(rows.Where(row => Equals(row["row_kind"], "minute") && Equals(row["window_start_utc"], CategoryStatisticsTestDatabase.HourStart.AddMinutes(11))));

        Assert.Equal("Missing", early["exclusion_reason"]);
        Assert.Null(early["avg_active_vehicles"]);
        Assert.Equal(CategoryStatisticsTestDatabase.HourStart.AddMinutes(10), early["first_collectible_utc"]);
        Assert.Equal("Partial", zero["exclusion_reason"]);
        Assert.Equal(0m, zero["avg_active_vehicles"]);
        Assert.Equal(0L, zero["crossings_published_count"]);
    }
}
