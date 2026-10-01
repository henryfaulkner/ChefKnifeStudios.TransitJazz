using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class CityMinuteStatisticsQueryTests
{
    [Fact]
    public void SourceContractTreatsGaugeValuesAsSamplesNotDailyTotals()
    {
        var first = Sample(new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc), 12);
        var second = Sample(new DateTime(2026, 9, 20, 0, 1, 0, DateTimeKind.Utc), 12);

        Assert.Equal(24, new[] { first, second }.Sum(row => row.VehiclesProcessed!.Value));
        Assert.Equal(12, new[] { first, second }.Max(row => row.VehiclesProcessed!.Value));
    }

    static CityMinuteStatistic Sample(DateTime minute, long vehicles) => new()
    {
        CitySlug = "atlanta",
        StatMinuteUtc = minute,
        CollectionStatus = CollectionStatus.Partial,
        VehiclesProcessed = vehicles,
    };
}
