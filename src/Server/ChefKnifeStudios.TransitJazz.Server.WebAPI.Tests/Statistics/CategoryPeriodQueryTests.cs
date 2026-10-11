using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class CategoryPeriodQueryTests
{
    [CategoryPostgresFact]
    public async Task EveryBoundRecipeHasAnExecutablePostgresQueryPlan()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await CategoryQueryData.WriteHourAsync(database);
        await using (var context = database.CreateDbContext())
        {
            await context.Database.ExecuteSqlRawAsync("ANALYZE public.city_category_minute_statistics");
            await context.Database.ExecuteSqlRawAsync("ANALYZE public.city_category_hour_statistics");
        }
        var plans = new List<string>();
        foreach (var file in new[] { "hourly-insights.sql", "activity-insights.sql", "cadence-insights.sql", "period-insights.sql", "local-hour-cadence.sql", "minute-coverage.sql" })
        {
            var rows = await CategoryQueryFixture.ReadAsync(database, file, timeZoneId: "America/New_York", explain: true);
            Assert.NotEmpty(rows);
            plans.Add(file + Environment.NewLine + string.Join(Environment.NewLine, rows.Select(row => (string)row["QUERY PLAN"]!)));
        }
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "category-insights-query-plans.txt"), string.Join(Environment.NewLine + Environment.NewLine, plans));
    }

    [CategoryPostgresFact]
    public async Task PeriodCombinesUnderlyingDenominatorsInsteadOfDisplayedMeans()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await CategoryQueryData.WriteHourAsync(database, distance: 1200, intervals: 2, distinctVehicles: 3, crossings: 50);
        await CategoryQueryData.WriteHourAsync(database, start: CategoryStatisticsTestDatabase.HourStart.AddHours(1),
            samplesPerMinute: 3, activeCountSumPerMinute: 12, distance: 1800, intervals: 3, distinctVehicles: 2, crossings: 70);

        var result = Assert.Single(await CategoryQueryFixture.ReadAsync(database, "period-insights.sql",
            toUtc: CategoryStatisticsTestDatabase.HourStart.AddHours(2)));

        Assert.Equal(3000m, result["distance_meters_sum"]);
        Assert.Equal(5m, result["observed_vehicle_hour_count"]);
        Assert.Equal(600m, result["avg_meters_per_observed_vehicle_hour"]);
        Assert.Equal(5m, result["distance_interval_count"]);
        Assert.Equal(600m, result["avg_meters_per_vehicle_update"]);
        Assert.Equal(240m, result["valid_active_sample_count"]);
        Assert.Equal(3.5m, result["avg_active_vehicles"]);
        Assert.Equal(2L, result["complete_hour_count"]);
        Assert.Equal(60m, result["published_opportunities_per_complete_hour"]);
    }

    [CategoryPostgresFact]
    public async Task PartialBoundaryHoursAreExcludedAndContributingBoundsRemainActual()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await CategoryQueryData.WriteHourAsync(database);
        await CategoryQueryData.WriteHourAsync(database, start: CategoryStatisticsTestDatabase.HourStart.AddHours(1), distance: 1800, distinctVehicles: 2);
        var requestedFrom = CategoryStatisticsTestDatabase.HourStart.AddMinutes(30);
        var requestedTo = CategoryStatisticsTestDatabase.HourStart.AddHours(2).AddMinutes(15);

        var result = Assert.Single(await CategoryQueryFixture.ReadAsync(database, "period-insights.sql", fromUtc: requestedFrom, toUtc: requestedTo));

        Assert.Equal(1L, result["complete_hour_count"]);
        Assert.Equal(900m, result["avg_meters_per_observed_vehicle_hour"]);
        Assert.Equal(requestedFrom, result["requested_from_utc"]);
        Assert.Equal(requestedTo, result["requested_to_utc"]);
        Assert.Equal(CategoryStatisticsTestDatabase.HourStart.AddHours(1), result["contributing_from_utc"]);
        Assert.Equal(CategoryStatisticsTestDatabase.HourStart.AddHours(2), result["contributing_to_utc"]);
    }

    [CategoryPostgresFact]
    public async Task DifferentCadencePoliciesReturnSeparateGroups()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await CategoryQueryData.WriteHourAsync(database, cadence: 30);
        await CategoryQueryData.WriteHourAsync(database, start: CategoryStatisticsTestDatabase.HourStart.AddHours(1), cadence: 20);

        var results = await CategoryQueryFixture.ReadAsync(database, "period-insights.sql", toUtc: CategoryStatisticsTestDatabase.HourStart.AddHours(2));

        Assert.Equal(2, results.Count);
        Assert.Equal(new[] { 20, 30 }, results.Select(x => (int)x["healthy_cadence_limit_seconds"]!).Order());
        Assert.All(results, x => Assert.Equal(1L, x["complete_hour_count"]));
    }

    [CategoryPostgresFact]
    public async Task AllVersionRequestReturnsSeparateGroupsWithoutSilentlyPooling()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await CategoryQueryData.WriteHourAsync(database);
        await CategoryQueryData.WriteHourAsync(database, start: CategoryStatisticsTestDatabase.HourStart.AddHours(1), definitionVersion: "observed-city-category-statistics-v2");

        var results = await CategoryQueryFixture.ReadAsync(database, "period-insights.sql",
            toUtc: CategoryStatisticsTestDatabase.HourStart.AddHours(2), definitionVersion: null);

        Assert.Equal(2, results.Count);
        Assert.All(results, x => Assert.Equal(1L, x["complete_hour_count"]));
        Assert.Equal(2, results.Select(x => x["definition_version"]).Distinct().Count());
    }

    [CategoryPostgresFact]
    public async Task PartialHourKeepsFirstRetainedDateButCannotProduceDefinitiveMeans()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await CategoryQueryData.WriteHourAsync(database);
        await using var context = database.CreateDbContext();
        await context.CityCategoryHourStatistics.ExecuteUpdateAsync(x => x.SetProperty(row => row.CollectionStatus,
            ChefKnifeStudios.TransitJazz.Server.Data.Models.CategoryCollectionStatus.Partial));

        var result = Assert.Single(await CategoryQueryFixture.ReadAsync(database, "period-insights.sql"));

        Assert.Equal(0L, result["complete_hour_count"]);
        Assert.Equal(CategoryStatisticsTestDatabase.HourStart, result["first_collectible_utc"]);
        Assert.Null(result["avg_meters_per_observed_vehicle_hour"]);
        Assert.Null(result["avg_meters_per_vehicle_update"]);
        Assert.Null(result["avg_active_vehicles"]);
        Assert.Null(result["published_opportunities_per_complete_hour"]);
    }
}
