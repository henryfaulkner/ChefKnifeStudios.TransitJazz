using ChefKnifeStudios.TransitJazz.Client.Shared.Models;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class StatsDisplayUnitsTests
{
    [Fact]
    public void MetricIsIdentityAndImperialUsesExactMileConversion()
    {
        Assert.Equal(1.609344m, StatsDisplayUnits.ConvertDistance(1.609344m, StatsUnitSystem.Metric));
        Assert.Equal(1m, StatsDisplayUnits.ConvertDistance(1.609344m, StatsUnitSystem.Imperial));
        Assert.Equal(10m, StatsDisplayUnits.ConvertDistance(16.09344m, StatsUnitSystem.Imperial));
    }

    [Theory]
    [InlineData(StatsUnitSystem.Metric)]
    [InlineData(StatsUnitSystem.Imperial)]
    public void NullAndZeroRemainNullAndZero(StatsUnitSystem units)
    {
        Assert.Null(StatsDisplayUnits.ConvertDistance(null, units));
        Assert.Equal(0m, StatsDisplayUnits.ConvertDistance(0m, units));
    }

    [Fact]
    public void ImperialConversionRetainsFullDecimalPrecision()
    {
        Assert.Equal(1m / 1.609344m, StatsDisplayUnits.ConvertDistance(1m, StatsUnitSystem.Imperial));
    }

    [Theory]
    [InlineData(StatsUnitSystem.Metric, StatsUnitSystem.Metric)]
    [InlineData(StatsUnitSystem.Imperial, StatsUnitSystem.Imperial)]
    [InlineData((StatsUnitSystem)999, StatsUnitSystem.Metric)]
    [InlineData((StatsUnitSystem)(-1), StatsUnitSystem.Metric)]
    public void NormalizeFallsBackToMetricForUndefinedValues(StatsUnitSystem input, StatsUnitSystem expected) =>
        Assert.Equal(expected, StatsDisplayUnits.Normalize(input));
}
