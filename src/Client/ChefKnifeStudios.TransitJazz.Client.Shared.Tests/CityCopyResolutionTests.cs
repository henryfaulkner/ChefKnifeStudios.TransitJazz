using ChefKnifeStudios.TransitJazz.Client.Shared.Resources;
using ChefKnifeStudios.TransitJazz.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

// 052-city-slug-migration SC-005: every city's resx-backed copy must resolve after the 30
// keys are re-prefixed in lockstep with the AudioUnlockOverlay/InfoFab switch expressions —
// a half-renamed prefix means IStringLocalizer falls back to returning the key name itself
// (NotFound), which these tests catch.
public class CityCopyResolutionTests
{
    static IStringLocalizer<RouteFilterResources> BuildLocalizer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        return services.BuildServiceProvider().GetRequiredService<IStringLocalizer<RouteFilterResources>>();
    }

    // Mirrors AudioUnlockOverlay.razor's OnInitialized switch verbatim — Atlanta is the
    // unprefixed default arm.
    static string AudioOverlayPrefix(string citySlug) => citySlug switch
    {
        var s when s == CityNames.Wmata => "WashingtonDcAudioOverlay",
        var s when s == CityNames.Mbta => "BostonAudioOverlay",
        var s when s == CityNames.Nymta => "NewYorkCityAudioOverlay",
        var s when s == CityNames.Ttc => "TorontoAudioOverlay",
        var s when s == CityNames.Septa => "PhiladelphiaAudioOverlay",
        var s when s == CityNames.Rtd => "DenverAudioOverlay",
        _ => "AudioOverlay",
    };

    // Mirrors InfoFab.razor's OnInitialized switch verbatim.
    static string InfoOverlayPrefix(string citySlug) => citySlug switch
    {
        var s when s == CityNames.Wmata => "WashingtonDcOverlay",
        var s when s == CityNames.Mbta => "BostonOverlay",
        var s when s == CityNames.Nymta => "NewYorkCityOverlay",
        var s when s == CityNames.Ttc => "TorontoOverlay",
        var s when s == CityNames.Septa => "PhiladelphiaOverlay",
        var s when s == CityNames.Rtd => "DenverOverlay",
        _ => "InfoOverlay",
    };

    static string[] AllSlugs => new[]
    {
        CityNames.Marta, CityNames.Wmata, CityNames.Mbta, CityNames.Nymta,
        CityNames.Ttc, CityNames.Septa, CityNames.Rtd,
    };

    [Fact]
    public void every_city_resolves_audio_overlay_copy()
    {
        var loc = BuildLocalizer();
        foreach (var slug in AllSlugs)
        {
            var prefix = AudioOverlayPrefix(slug);
            foreach (var suffix in new[] { "Header", "Paragraph1", "Paragraph2", "Paragraph3" })
            {
                var key = prefix + suffix;
                var value = loc[key];
                Assert.False(value.ResourceNotFound, $"Missing resx key '{key}' for city '{slug}'.");
                Assert.False(string.IsNullOrWhiteSpace(value.Value), $"Resx key '{key}' for city '{slug}' resolved to empty.");
                Assert.NotEqual(key, value.Value);
            }
        }
    }

    [Fact]
    public void every_city_resolves_info_overlay_copy()
    {
        var loc = BuildLocalizer();
        foreach (var slug in AllSlugs)
        {
            var key = InfoOverlayPrefix(slug) + "Paragraph1";
            var value = loc[key];
            Assert.False(value.ResourceNotFound, $"Missing resx key '{key}' for city '{slug}'.");
            Assert.False(string.IsNullOrWhiteSpace(value.Value), $"Resx key '{key}' for city '{slug}' resolved to empty.");
            Assert.NotEqual(key, value.Value);
        }
    }
}
