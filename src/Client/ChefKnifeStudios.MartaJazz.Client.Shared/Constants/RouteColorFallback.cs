namespace ChefKnifeStudios.MartaJazz.Client.Shared.Constants;

// GTFS route colors that are too close to the current theme's background are swapped for a
// fallback everywhere a route color is rendered — the pill swatches in RouteFilters and the
// route line layer on the map (Map.razor / map-interop.js) must agree, since a route can
// otherwise render invisibly in one place and visibly in the other. Light theme washes out
// white/light-grey; dark theme washes out black/dark-grey — so the check is theme-aware.
public static class RouteColorFallback
{
    public const string FallbackColor = "#CC0000";

    public static bool IsVisibleColor(string? color, bool isDarkMode)
    {
        if (string.IsNullOrWhiteSpace(color)) return false;
        var c = color.TrimStart('#').ToUpperInvariant();

        if (!isDarkMode && c is "FFFFFF" or "FFF" or "FEFEFE") return false;
        if (isDarkMode && c is "000000" or "000" or "010101") return false;

        return !IsWashedOut(c, isDarkMode);
    }

    public static string ResolveColor(string? color, bool isDarkMode) =>
        IsVisibleColor(color, isDarkMode) ? color! : FallbackColor;

    // A low-saturation color reads as washed out against its theme's background when it's also
    // close to that background's brightness extreme — near-white on the light theme, near-black
    // on the dark theme. Treated as washed out when R, G, B are within a few units of each other
    // (low saturation) and sit past the relevant brightness threshold for the active theme.
    static bool IsWashedOut(string hex, bool isDarkMode)
    {
        if (hex.Length != 6 || !TryParseHex(hex, out var r, out var g, out var b)) return false;

        var max = System.Math.Max(r, System.Math.Max(g, b));
        var min = System.Math.Min(r, System.Math.Min(g, b));

        const int SaturationTolerance = 10;
        if (max - min > SaturationTolerance) return false;

        const int LightBrightnessFloor = 200;
        const int DarkBrightnessCeiling = 55;

        return isDarkMode ? max <= DarkBrightnessCeiling : min >= LightBrightnessFloor;
    }

    static bool TryParseHex(string hex, out int r, out int g, out int b)
    {
        r = g = b = 0;
        return int.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out r)
            && int.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out g)
            && int.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out b);
    }
}
