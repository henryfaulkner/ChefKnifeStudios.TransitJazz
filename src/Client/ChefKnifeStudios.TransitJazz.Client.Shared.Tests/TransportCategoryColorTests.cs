using ChefKnifeStudios.TransitJazz.Client.Shared.Components.Charts;
using ChefKnifeStudios.TransitJazz.Client.Shared.Constants;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class TransportCategoryColorTests
{
    [Theory]
    [InlineData("bus", "#006400", "#66BB6A")]
    [InlineData("rail", "#1A237E", "#7986CB")]
    [InlineData("train", "#1A237E", "#7986CB")]
    [InlineData("streetcar", "#B71C1C", "#E57373")]
    [InlineData("tram", "#B71C1C", "#E57373")]
    [InlineData("ferry", "#616161", "#9E9E9E")]
    [InlineData("future-category", "#616161", "#9E9E9E")]
    public void ChartCategoryKeysMatchTheRunningLabelPaletteInBothThemes(string category, string light, string dark)
    {
        Assert.Equal(light, TransportCategoryColors.Get(category, isDark: false));
        Assert.Equal(dark, TransportCategoryColors.Get(category, isDark: true));
        Assert.Equal(light, ChartPresentation.GetSeriesStyle("category:" + category, isDark: false).Color);
        Assert.Equal(dark, ChartPresentation.GetSeriesStyle("category:" + category, isDark: true).Color);
        Assert.Equal(light, ChartPresentation.GetRouteColor("atlanta", "missing-route", category));
        Assert.Equal(dark, ChartPresentation.GetRouteColor("atlanta", "missing-route", category, isDark: true));
    }
}
