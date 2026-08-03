using ChefKnifeStudios.TransitJazz.Shared;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

// 052-city-slug-migration FR-014: map origins must survive the rename unchanged. Pins the
// exact pre-migration coordinates captured from TransitMap.razor.cs's _cityCenter before any
// slug moved (tasks.md T003), keyed by the (now-renamed) CityNames.* constants.
public class CityMapOriginTests
{
    // Mirrors TransitMap.razor.cs's private _cityCenter — that dictionary isn't public, so this
    // asserts the source file's literal content directly rather than reflecting into it.
    static readonly Dictionary<string, (double Lat, double Lon)> ExpectedOrigins = new(StringComparer.OrdinalIgnoreCase)
    {
        [CityNames.Marta] = (33.749, -84.388),
        [CityNames.Wmata] = (38.907, -77.037),
        [CityNames.Mbta] = (42.361, -71.057),
        [CityNames.Nymta] = (40.7580, -73.9855),
        [CityNames.Ttc] = (43.6532, -79.3832),
        [CityNames.Septa] = (39.9526, -75.1652),
        [CityNames.Rtd] = (39.7539, -105.0009),
    };

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException("Could not locate repo root (no ancestor containing 'src') from " + AppContext.BaseDirectory);

        return dir.FullName;
    }

    [Fact]
    public void every_city_has_a_map_origin()
    {
        var path = Path.Combine(RepoRoot(),
            "src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs");
        var source = File.ReadAllText(path);

        foreach (var (slug, (lat, lon)) in ExpectedOrigins)
        {
            // Compares by parsed value, not exact source text — the source may format a
            // coordinate with a trailing zero (40.7580) that round-trips to the same double
            // as "40.758", and this test cares about the value surviving the rename, not the
            // literal's formatting.
            var pattern = new System.Text.RegularExpressions.Regex(
                @"\((-?\d+\.\d+),\s*(-?\d+\.\d+)\)");
            var found = pattern.Matches(source).Any(m =>
                double.TryParse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var mLat) &&
                double.TryParse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture, out var mLon) &&
                mLat == lat && mLon == lon);

            Assert.True(found,
                $"_cityCenter is missing the pre-migration coordinate pair ({lat}, {lon}) for slug '{slug}'.");
        }
    }
}
