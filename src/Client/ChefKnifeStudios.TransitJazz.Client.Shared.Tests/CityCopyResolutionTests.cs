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
        var s when s == CityNames.Seattle => "SeattleAudioOverlay",
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
        var s when s == CityNames.Seattle => "SeattleOverlay",
        _ => "InfoOverlay",
    };

    static string[] AllSlugs => new[]
    {
        CityNames.Marta, CityNames.Wmata, CityNames.Mbta, CityNames.Nymta,
        CityNames.Ttc, CityNames.Septa, CityNames.Rtd, CityNames.Seattle,
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

    [Fact]
    public void seattle_copy_covers_the_listening_instructions_sources_and_supported_map_scope()
    {
        var loc = BuildLocalizer();
        var prefix = AudioOverlayPrefix(CityNames.Seattle);
        var header = loc[prefix + "Header"].Value;
        var paragraphs = Enumerable.Range(1, 3).Select(i => loc[$"{prefix}Paragraph{i}"].Value).ToArray();
        var info = loc[InfoOverlayPrefix(CityNames.Seattle) + "Paragraph1"].Value;

        Assert.StartsWith("KING COUNTY METRO + SOUND TRANSIT - Seattle", header, StringComparison.Ordinal);
        Assert.StartsWith("<strong>This is an audio experience — turn your device's sound on.</strong>", paragraphs[1], StringComparison.Ordinal);
        Assert.Contains("<strong>A note lands where the vehicle happens to be along its route</strong>", paragraphs[1], StringComparison.Ordinal);
        Assert.EndsWith("What is playing right now has not played before.", paragraphs[1], StringComparison.Ordinal);
        Assert.Contains("Sound Transit route 512 is omitted", paragraphs[2], StringComparison.Ordinal);
        Assert.Contains("terms of use", paragraphs[2], StringComparison.Ordinal);
        Assert.Contains("streetcar and water-taxi", paragraphs[2], StringComparison.Ordinal);
        Assert.Contains("Sound Transit route 512", info, StringComparison.Ordinal);
    }
}
