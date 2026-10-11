using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class CategoryLocalHourQueryTests
{
    [CategoryPostgresFact]
    public async Task SameLocalHourAcrossDaysUsesCompleteHourCountAsCadenceDenominator()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        var first = CategoryStatisticsTestDatabase.HourStart;
        await CategoryQueryData.WriteHourAsync(database, start: first, crossings: 50);
        await CategoryQueryData.WriteHourAsync(database, start: first.AddDays(1), crossings: 70);

        var results = await CategoryQueryFixture.ReadAsync(database, "local-hour-cadence.sql", fromUtc: first,
            toUtc: first.AddDays(1).AddHours(1), timeZoneId: "America/New_York");

        Assert.Equal(2, results.Count);
        Assert.All(results, row =>
        {
            Assert.Equal(8, row["local_hour"]);
            Assert.Equal(-14400, row["utc_offset_seconds"]);
            Assert.Equal(2L, row["complete_hour_count"]);
            Assert.Equal(2L, row["contributing_day_count"]);
            Assert.Equal(120m, row["crossings_published_count"]);
            Assert.Equal(60m, row["published_opportunities_per_complete_hour"]);
        });
        Assert.Equal(2, results.Select(x => x["hour_start_utc"]).Distinct().Count());
    }

    [CategoryPostgresFact]
    public async Task RepeatedDstHourRetainsBothUtcIdentitiesAndOffsets()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        var first = new DateTime(2026, 11, 1, 5, 0, 0, DateTimeKind.Utc);
        await CategoryQueryData.WriteHourAsync(database, start: first, crossings: 50);
        await CategoryQueryData.WriteHourAsync(database, start: first.AddHours(1), crossings: 70);

        var results = await CategoryQueryFixture.ReadAsync(database, "local-hour-cadence.sql", fromUtc: first,
            toUtc: first.AddHours(2), timeZoneId: "America/New_York");

        Assert.Equal(2, results.Count);
        Assert.All(results, row =>
        {
            Assert.Equal(1, row["local_hour"]);
            Assert.Equal(2L, row["complete_hour_count"]);
            Assert.Equal(1L, row["contributing_day_count"]);
            Assert.Equal(60m, row["published_opportunities_per_complete_hour"]);
        });
        Assert.Equal(new[] { -18000, -14400 }, results.Select(x => (int)x["utc_offset_seconds"]!).Order());
        Assert.Equal(new[] { first, first.AddHours(1) }, results.Select(x => (DateTime)x["hour_start_utc"]!));
    }

    [CategoryPostgresFact]
    public async Task SpringTransitionDoesNotInventTheSkippedLocalHour()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        var first = new DateTime(2026, 3, 8, 6, 0, 0, DateTimeKind.Utc);
        await CategoryQueryData.WriteHourAsync(database, start: first);
        await CategoryQueryData.WriteHourAsync(database, start: first.AddHours(1));

        var results = await CategoryQueryFixture.ReadAsync(database, "local-hour-cadence.sql", fromUtc: first,
            toUtc: first.AddHours(2), timeZoneId: "America/New_York");

        Assert.Equal(new[] { 1, 3 }, results.Select(x => (int)x["local_hour"]!));
        Assert.DoesNotContain(results, row => Equals(row["local_hour"], 2));
    }

    [CategoryPostgresFact]
    public async Task ConfiguredNamedZoneControlsLocalGrouping()
    {
        await using var database = await CategoryStatisticsTestDatabase.CreateAsync();
        await CategoryQueryData.WriteHourAsync(database);

        var result = Assert.Single(await CategoryQueryFixture.ReadAsync(database, "local-hour-cadence.sql", timeZoneId: "America/Denver"));

        Assert.Equal("America/Denver", result["time_zone_id"]);
        Assert.Equal(6, result["local_hour"]);
        Assert.Equal(-21600, result["utc_offset_seconds"]);
    }
}
