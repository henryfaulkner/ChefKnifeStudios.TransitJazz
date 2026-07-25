namespace ChefKnifeStudios.MartaJazz.Client.Shared.Constants;

// GTFS route colors that are too close to the map/pill background to read (white and light
// grey) are swapped for this fallback everywhere a route color is rendered — the pill swatches
// in RouteFilters and the route line layer on the map (Map.razor / map-interop.js) must agree,
// since a route can otherwise render invisibly in one place and visibly in the other.
public static class RouteColorFallback
{
    public const string FallbackColor = "#CC0000";

    public static bool IsVisibleColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return false;
        var c = color.TrimStart('#').ToUpperInvariant();
        if (c is "FFFFFF" or "FFF" or "FEFEFE") return false;
        return !IsLightGrey(c);
    }

    public static string ResolveColor(string? color) => IsVisibleColor(color) ? color! : FallbackColor;

    // A light grey/near-white grey reads as washed-out against the light map basemap and pill
    // background alike. Treated as "light grey" when R, G, B are all within a few units of each
    // other (low saturation) and all above a brightness floor.
    static bool IsLightGrey(string hex)
    {
        if (hex.Length != 6 || !TryParseHex(hex, out var r, out var g, out var b)) return false;

        var max = System.Math.Max(r, System.Math.Max(g, b));
        var min = System.Math.Min(r, System.Math.Min(g, b));

        const int SaturationTolerance = 10;
        const int BrightnessFloor = 200;

        return (max - min) <= SaturationTolerance && min >= BrightnessFloor;
    }

    static bool TryParseHex(string hex, out int r, out int g, out int b)
    {
        r = g = b = 0;
        return int.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out r)
            && int.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out g)
            && int.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out b);
    }
}
