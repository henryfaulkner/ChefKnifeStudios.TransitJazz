namespace ChefKnifeStudios.MartaJazz.Client.Shared.Constants;

// GTFS route colors that are too close to the current theme's background are swapped for a
// fallback everywhere a route color is rendered — the pill swatches in RouteFilters and the
// route line layer on the map (Map.razor / map-interop.js) must agree, since a route can
// otherwise render invisibly in one place and visibly in the other. Light theme washes out
// white/light-grey only (dark text/lines stay readable on a light background). Dark theme
// washes out BOTH ends — white/light-grey still reads as a blank pill/line, and black/dark-grey
// disappears into the dark background — so each extreme gets its own fallback color.
public static class RouteColorFallback
{
    public const string WhiteFallbackColor = "#CC0000";
    public const string BlackFallbackColor = "#3366FF";

    // Backward-compatible alias for the white/light-grey fallback.
    public const string FallbackColor = WhiteFallbackColor;

    public static bool IsVisibleColor(string? color, bool isDarkMode) =>
        Classify(color, isDarkMode) is Washout.None;

    public static string ResolveColor(string? color, bool isDarkMode) => Classify(color, isDarkMode) switch
    {
        Washout.White => WhiteFallbackColor,
        Washout.Black => BlackFallbackColor,
        _ => color!,
    };

    enum Washout { None, White, Black }

    static Washout Classify(string? color, bool isDarkMode)
    {
        if (string.IsNullOrWhiteSpace(color)) return Washout.White;
        var c = color.TrimStart('#').ToUpperInvariant();

        if (c is "FFFFFF" or "FFF" or "FEFEFE") return Washout.White;
        if (isDarkMode && c is "000000" or "000" or "010101") return Washout.Black;

        return IsWashedOutGrey(c, isDarkMode);
    }

    // A low-saturation color reads as washed out against its theme's background when it's also
    // close to a brightness extreme. Light-grey washes out on both themes (it's always a weak
    // pill/line color); dark-grey washes out only on the dark theme, where it blends into the
    // dark background. Treated as washed out when R, G, B are within a few units of each other
    // (low saturation) and sit past the relevant brightness threshold.
    static Washout IsWashedOutGrey(string hex, bool isDarkMode)
    {
        if (hex.Length != 6 || !TryParseHex(hex, out var r, out var g, out var b)) return Washout.None;

        var max = System.Math.Max(r, System.Math.Max(g, b));
        var min = System.Math.Min(r, System.Math.Min(g, b));

        const int SaturationTolerance = 10;
        if (max - min > SaturationTolerance) return Washout.None;

        const int LightBrightnessFloor = 200;
        const int DarkBrightnessCeiling = 55;

        if (min >= LightBrightnessFloor) return Washout.White;
        if (isDarkMode && max <= DarkBrightnessCeiling) return Washout.Black;

        return Washout.None;
    }

    static bool TryParseHex(string hex, out int r, out int g, out int b)
    {
        r = g = b = 0;
        return int.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out r)
            && int.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out g)
            && int.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out b);
    }
}
