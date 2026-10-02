using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class CategoryCadenceQueryTests
{
    [CategoryPostgresFact]
    public async Task SuccessfullyPublishedBusAndRailTotalsAreFortyAndFifteen()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await CategoryQueryData.WriteHourAsync(database, crossings: 40);
        await CategoryQueryData.WriteHourAsync(database, category: "rail", crossings: 15);

        var bus = Assert.Single(await CategoryQueryFixture.ReadAsync(database, "cadence-insights.sql"));
        var rail = Assert.Single(await CategoryQueryFixture.ReadAsync(database, "cadence-insights.sql", category: "rail"));

        Assert.Equal(40m, bus["crossings_published_count"]);
        Assert.Equal(15m, rail["crossings_published_count"]);
        Assert.Equal(40m, bus["published_opportunities_per_complete_hour"]);
        Assert.Equal(15m, rail["published_opportunities_per_complete_hour"]);
        Assert.Equal(1L, bus["complete_hour_count"]);
        Assert.Equal("published opportunities per complete observed hour", bus["unit"]);
    }

    [CategoryPostgresFact]
    public async Task CompleteZeroIsAvailableAndMissingHistoryIsUnavailable()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await CategoryQueryData.WriteHourAsync(database, crossings: 0);

        var covered = Assert.Single(await CategoryQueryFixture.ReadAsync(database, "cadence-insights.sql"));
        var missing = Assert.Single(await CategoryQueryFixture.ReadAsync(database, "cadence-insights.sql", category: "rail"));

        Assert.Equal(0m, covered["published_opportunities_per_complete_hour"]);
        Assert.Equal(1L, covered["complete_hour_count"]);
        Assert.Null(missing["published_opportunities_per_complete_hour"]);
        Assert.Equal(0L, missing["complete_hour_count"]);
    }

    [CategoryPostgresFact]
    public async Task FailedPublicationMinuteExcludesTheHourFromCadence()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await CategoryQueryData.WriteHourAsync(database, crossings: 40);
        await using var context = database.CreateDbContext();
        await context.CityCategoryMinuteStatistics.Where(x => x.StatMinuteUtc == CategoryStatisticsTestDatabase.HourStart)
            .ExecuteUpdateAsync(x => x.SetProperty(row => row.CollectionStatus, ChefKnifeStudios.TransitJazz.Server.Data.Models.CategoryCollectionStatus.Partial)
                .SetProperty(row => row.ValidPublishCycleCount, 0L).SetProperty(row => row.FailedCycleCount, 1L)
                .SetProperty(row => row.CrossingsPublishedCount, 0L));

        var result = Assert.Single(await CategoryQueryFixture.ReadAsync(database, "cadence-insights.sql"));

        Assert.Equal(0L, result["complete_hour_count"]);
        Assert.Null(result["published_opportunities_per_complete_hour"]);
    }
}
