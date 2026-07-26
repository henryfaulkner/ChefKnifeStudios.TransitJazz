namespace ChefKnifeStudios.TransitJazz.Client.Shared.Constants;

// GTFS route colors that are too close to white or black are swapped for a fallback everywhere
// a route color is rendered — the pill swatches in RouteFilters and the route line layer on the
// map (Map.razor / map-interop.js) must agree, since a route can otherwise render invisibly in
// one place and visibly in the other. White/light-grey always falls back to red; black/dark-grey
// always falls back to blue — independent of light/dark theme.
public static class RouteColorFallback
{
    public const string WhiteFallbackColor = "#CC0000";
    public const string BlackFallbackColor = "#3366FF";

    public static bool IsVisibleColor(string? color) => Classify(color) is Washout.None;

    public static string ResolveColor(string? color) => Classify(color) switch
    {
        Washout.White => WhiteFallbackColor,
        Washout.Black => BlackFallbackColor,
        _ => color!,
    };

    enum Washout { None, White, Black }

    static Washout Classify(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return Washout.White;
        var c = color.TrimStart('#').ToUpperInvariant();

        if (c is "FFFFFF" or "FFF" or "FEFEFE") return Washout.White;
        if (c is "000000" or "000" or "010101") return Washout.Black;

        return IsWashedOutGrey(c);
    }

    // A low-saturation color close to a brightness extreme reads as washed out (a near-white
    // or near-black pill/line is as hard to see as pure white/black). Treated as washed out
    // when R, G, B are within a few units of each other (low saturation) and sit past the
    // relevant brightness threshold.
    static Washout IsWashedOutGrey(string hex)
    {
        if (hex.Length != 6 || !TryParseHex(hex, out var r, out var g, out var b)) return Washout.None;

        var max = System.Math.Max(r, System.Math.Max(g, b));
        var min = System.Math.Min(r, System.Math.Min(g, b));

        const int SaturationTolerance = 10;
        if (max - min > SaturationTolerance) return Washout.None;

        const int LightBrightnessFloor = 200;
        const int DarkBrightnessCeiling = 55;

        if (min >= LightBrightnessFloor) return Washout.White;
        if (max <= DarkBrightnessCeiling) return Washout.Black;

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
