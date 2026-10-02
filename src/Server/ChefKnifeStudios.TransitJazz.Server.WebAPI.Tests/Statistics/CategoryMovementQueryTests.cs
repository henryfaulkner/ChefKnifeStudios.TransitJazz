using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class CategoryMovementQueryTests
{
    [CategoryPostgresFact]
    public async Task ExactHourPopulationAndAcceptedIntervalsProduceTheirOwnMeans()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        var minutes = CategoryStatisticsTestDatabase.Minutes();
        foreach (var minute in minutes)
        {
            minute.DistanceMetersSum = minute.StatMinuteUtc.Minute < 2 ? 600m : 0m;
            minute.DistanceIntervalCount = minute.StatMinuteUtc.Minute < 2 ? 1 : 0;
        }
        var hour = CategoryStatisticsTestDatabase.Hour();
        hour.DistanceIntervalCount = 2;
        await database.Store.WriteAsync(minutes, [hour]);

        var result = Assert.Single(await CategoryQueryFixture.ReadAsync(database, "hourly-insights.sql"));

        Assert.Equal(1200m, result["distance_meters_sum"]);
        Assert.Equal(3L, result["distinct_active_vehicle_count"]);
        Assert.Equal(2L, result["distance_interval_count"]);
        Assert.Equal(400m, result["avg_meters_per_observed_vehicle_hour"]);
        Assert.Equal(600m, result["avg_meters_per_vehicle_update"]);
        Assert.Equal(60, result["covered_minutes"]);
        Assert.Equal(30, result["healthy_cadence_limit_seconds"]);
    }

    [CategoryPostgresFact]
    public async Task EligibleVehiclesWithoutMovementStillContributeToVehicleHourDenominator()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        var minutes = CategoryStatisticsTestDatabase.Minutes();
        foreach (var minute in minutes) { minute.DistanceMetersSum = 0; minute.DistanceIntervalCount = 0; }
        var hour = CategoryStatisticsTestDatabase.Hour();
        hour.DistanceMetersSum = 0;
        hour.DistanceIntervalCount = 0;
        await database.Store.WriteAsync(minutes, [hour]);

        var result = Assert.Single(await CategoryQueryFixture.ReadAsync(database, "hourly-insights.sql"));

        Assert.Equal(3L, result["distinct_active_vehicle_count"]);
        Assert.Equal(0m, result["avg_meters_per_observed_vehicle_hour"]);
        Assert.Null(result["avg_meters_per_vehicle_update"]);
    }

    [CategoryPostgresFact]
    public async Task CoveredEmptyHourHasAvailableZeroActivityAndUnavailableDistanceMeans()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        var minutes = CategoryStatisticsTestDatabase.Minutes();
        foreach (var minute in minutes)
        {
            minute.ActiveVehicleCountSum = 0; minute.DistanceMetersSum = 0;
            minute.DistanceIntervalCount = 0; minute.CrossingsPublishedCount = 0;
        }
        var hour = CategoryStatisticsTestDatabase.Hour();
        hour.ActiveVehicleCountSum = 0; hour.DistanceMetersSum = 0;
        hour.DistanceIntervalCount = 0; hour.CrossingsPublishedCount = 0; hour.DistinctActiveVehicleCount = 0;
        await database.Store.WriteAsync(minutes, [hour]);

        var result = Assert.Single(await CategoryQueryFixture.ReadAsync(database, "hourly-insights.sql"));

        Assert.Equal(0m, result["avg_active_vehicles"]);
        Assert.Equal(0L, result["published_crossings_per_hour"]);
        Assert.Equal(60L, result["valid_active_sample_count"]);
        Assert.Null(result["avg_meters_per_observed_vehicle_hour"]);
        Assert.Null(result["avg_meters_per_vehicle_update"]);
    }

    [CategoryPostgresFact]
    public async Task MissingBackingMinuteExcludesPreviouslyStoredCompleteHour()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await database.Store.WriteAsync(CategoryStatisticsTestDatabase.Minutes(), [CategoryStatisticsTestDatabase.Hour()]);
        await using var context = database.CreateDbContext();
        await context.CityCategoryMinuteStatistics.Where(x => x.StatMinuteUtc == CategoryStatisticsTestDatabase.HourStart).ExecuteDeleteAsync();

        Assert.Empty(await CategoryQueryFixture.ReadAsync(database, "hourly-insights.sql"));
    }

    [CategoryPostgresFact]
    public async Task MinuteConflictExcludesHourEvenBeforeParentQuarantinePropagation()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await database.Store.WriteAsync(CategoryStatisticsTestDatabase.Minutes(), [CategoryStatisticsTestDatabase.Hour()]);
        await using var context = database.CreateDbContext();
        await context.CityCategoryMinuteStatistics.Where(x => x.StatMinuteUtc == CategoryStatisticsTestDatabase.HourStart).ExecuteUpdateAsync(x => x.SetProperty(row => row.HasConflict, true));
        Assert.False((await context.CityCategoryHourStatistics.SingleAsync()).HasConflict);

        Assert.Empty(await CategoryQueryFixture.ReadAsync(database, "hourly-insights.sql"));
    }

    [CategoryPostgresFact]
    public async Task AdditiveMismatchExcludesHourInTheQuerySnapshot()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await database.Store.WriteAsync(CategoryStatisticsTestDatabase.Minutes(), [CategoryStatisticsTestDatabase.Hour()]);
        await using var context = database.CreateDbContext();
        await context.CityCategoryMinuteStatistics.Where(x => x.StatMinuteUtc == CategoryStatisticsTestDatabase.HourStart).ExecuteUpdateAsync(x => x.SetProperty(row => row.DistanceMetersSum, 21m));

        Assert.Empty(await CategoryQueryFixture.ReadAsync(database, "hourly-insights.sql"));
    }

    [CategoryPostgresFact]
    public async Task IncompatibleMinuteCadenceCannotSupportCompleteHour()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await database.Store.WriteAsync(CategoryStatisticsTestDatabase.Minutes(), [CategoryStatisticsTestDatabase.Hour()]);
        await using var context = database.CreateDbContext();
        await context.CityCategoryMinuteStatistics.Where(x => x.StatMinuteUtc == CategoryStatisticsTestDatabase.HourStart).ExecuteUpdateAsync(x => x.SetProperty(row => row.HealthyCadenceLimitSeconds, 20));

        Assert.Empty(await CategoryQueryFixture.ReadAsync(database, "hourly-insights.sql"));
    }

    [CategoryPostgresFact]
    public async Task RequestedPartialHourCannotContributeDefinitiveHourlyMean()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await database.Store.WriteAsync(CategoryStatisticsTestDatabase.Minutes(), [CategoryStatisticsTestDatabase.Hour()]);

        Assert.Empty(await CategoryQueryFixture.ReadAsync(database, "hourly-insights.sql",
            fromUtc: CategoryStatisticsTestDatabase.HourStart.AddSeconds(1)));
    }
}

internal static class CategoryQueryFixture
{
    public static async Task<List<Dictionary<string, object?>>> ReadAsync(CategoryStatisticsTestDatabase database,
        string fileName, string category = "bus", DateTime? fromUtc = null, DateTime? toUtc = null,
        string? timeZoneId = null, string? definitionVersion = CityCategoryMinuteStatistic.CurrentDefinitionVersion,
        bool explain = false)
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "contracts", fileName));
        await using var context = database.CreateDbContext();
        await context.Database.OpenConnectionAsync();
        await using var command = (NpgsqlCommand)context.Database.GetDbConnection().CreateCommand();
        command.CommandText = explain ? "EXPLAIN (FORMAT TEXT) " + sql : sql;
        command.CommandTimeout = 10;
        command.Parameters.AddWithValue("city_slug", "atlanta");
        command.Parameters.AddWithValue("category", category);
        command.Parameters.Add("definition_version", NpgsqlTypes.NpgsqlDbType.Text).Value = (object?)definitionVersion ?? DBNull.Value;
        command.Parameters.AddWithValue("from_utc", fromUtc ?? CategoryStatisticsTestDatabase.HourStart);
        command.Parameters.AddWithValue("to_utc", toUtc ?? CategoryStatisticsTestDatabase.HourStart.AddHours(1));
        if (timeZoneId is not null) command.Parameters.AddWithValue("time_zone_id", timeZoneId);
        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < reader.FieldCount; i++) row.Add(reader.GetName(i), await reader.IsDBNullAsync(i) ? null : reader.GetValue(i));
            rows.Add(row);
        }
        return rows;
    }
}
