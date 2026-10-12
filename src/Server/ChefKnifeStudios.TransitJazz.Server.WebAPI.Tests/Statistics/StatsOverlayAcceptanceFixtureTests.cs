using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class StatsOverlayAcceptanceFixtureTests
{
    [Fact]
    public void IndependentFixturesReconcileCoverageAndBubbleRatios()
    {
        var selected = StatsOverlayAcceptanceFixtures.SelectedRouteHours;
        Assert.Equal(160m, selected.Sum(row => row.Kilometers));
        Assert.Equal(16, selected.Count);
        Assert.Equal(20, 2 * 10);
        Assert.Equal(5m, StatsOverlayAcceptanceFixtures.RatioHours.Sum(row => row.Kilometers)
            / StatsOverlayAcceptanceFixtures.RatioHours.Sum(row => row.RouteVehicleHours));
        Assert.Equal(15m, StatsOverlayAcceptanceFixtures.RatioHours.Sum(row => row.Kilometers)
            / StatsOverlayAcceptanceFixtures.RatioHours.Count(row => row.ActivitySamples > 0));
        Assert.Equal(1.5m, (decimal)StatsOverlayAcceptanceFixtures.RatioHours.Sum(row => row.ActivitySamples)
            / StatsOverlayAcceptanceFixtures.RatioHours.Sum(row => row.SampleDenominator));
        Assert.Equal(2, StatsOverlayAcceptanceFixtures.Cities.Count);
        Assert.Equal(3, StatsOverlayAcceptanceFixtures.Categories.Count);
        Assert.Equal(140m, StatsOverlayAcceptanceFixtures.GapPrefix[^1].CumulativeObservedKilometers);
    }
}
