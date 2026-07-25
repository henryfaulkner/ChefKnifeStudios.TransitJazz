using ChefKnifeStudios.MartaJazz.Client.Shared.Constants;
using Xunit;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Tests;

// Route colors that are white/light-grey or black/dark-grey are swapped for a fallback
// everywhere a route color is rendered — RouteFilters pills and the map route-line layer
// must agree, since a route can otherwise render invisibly in one place and visibly in the
// other. White/light-grey always falls back to red; black/dark-grey always falls back to
// blue — independent of light/dark theme.
public class RouteColorFallbackTests
{
    [Theory]
    [InlineData("#FFFFFF")]
    [InlineData("#FFF")]
    [InlineData("#FEFEFE")]
    [InlineData("#F0F0F0")]
    [InlineData(null)]
    [InlineData("")]
    public void ResolveColor_FallsBackRedForWhiteAndLightGrey(string? color)
    {
        Assert.Equal(RouteColorFallback.WhiteFallbackColor, RouteColorFallback.ResolveColor(color));
    }

    [Theory]
    [InlineData("#000000")]
    [InlineData("#000")]
    [InlineData("#010101")]
    [InlineData("#1A1A1A")]
    public void ResolveColor_FallsBackBlueForBlackAndDarkGrey(string color)
    {
        Assert.Equal(RouteColorFallback.BlackFallbackColor, RouteColorFallback.ResolveColor(color));
    }

    [Theory]
    [InlineData("#CC0000")]
    [InlineData("#1F77B4")]
    [InlineData("#22AA55")]
    public void ResolveColor_SaturatedColor_PassesThrough(string color)
    {
        Assert.Equal(color, RouteColorFallback.ResolveColor(color));
    }
}
