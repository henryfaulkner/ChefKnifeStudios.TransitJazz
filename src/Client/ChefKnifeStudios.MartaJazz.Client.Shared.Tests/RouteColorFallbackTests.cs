using ChefKnifeStudios.MartaJazz.Client.Shared.Constants;
using Xunit;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Tests;

// Route colors that wash out against the current theme's background are swapped for a
// fallback everywhere a route color is rendered — RouteFilters pills and the map route-line
// layer must agree. Light theme washes out white/light-grey only. Dark theme washes out BOTH
// ends: white/light-grey (still a weak color) → red, and black/dark-grey (invisible against
// the dark background) → blue.
public class RouteColorFallbackTests
{
    [Theory]
    [InlineData("#FFFFFF")]
    [InlineData("#FFF")]
    [InlineData("#FEFEFE")]
    [InlineData("#F0F0F0")]
    [InlineData(null)]
    [InlineData("")]
    public void ResolveColor_LightMode_FallsBackRedForWhiteAndLightGrey(string? color)
    {
        Assert.Equal(RouteColorFallback.WhiteFallbackColor, RouteColorFallback.ResolveColor(color, isDarkMode: false));
    }

    [Theory]
    [InlineData("#FFFFFF")]
    [InlineData("#FFF")]
    [InlineData("#FEFEFE")]
    [InlineData("#F0F0F0")]
    [InlineData(null)]
    [InlineData("")]
    public void ResolveColor_DarkMode_FallsBackRedForWhiteAndLightGrey(string? color)
    {
        Assert.Equal(RouteColorFallback.WhiteFallbackColor, RouteColorFallback.ResolveColor(color, isDarkMode: true));
    }

    [Theory]
    [InlineData("#000000")]
    [InlineData("#000")]
    [InlineData("#010101")]
    [InlineData("#1A1A1A")]
    public void ResolveColor_DarkMode_FallsBackBlueForBlackAndDarkGrey(string color)
    {
        Assert.Equal(RouteColorFallback.BlackFallbackColor, RouteColorFallback.ResolveColor(color, isDarkMode: true));
    }

    [Fact]
    public void ResolveColor_LightMode_KeepsBlack_NotWashedOutOnLightBackground()
    {
        Assert.Equal("#000000", RouteColorFallback.ResolveColor("#000000", isDarkMode: false));
    }

    [Theory]
    [InlineData("#CC0000")]
    [InlineData("#1F77B4")]
    [InlineData("#22AA55")]
    public void ResolveColor_SaturatedColor_PassesThroughInBothModes(string color)
    {
        Assert.Equal(color, RouteColorFallback.ResolveColor(color, isDarkMode: false));
        Assert.Equal(color, RouteColorFallback.ResolveColor(color, isDarkMode: true));
    }
}
