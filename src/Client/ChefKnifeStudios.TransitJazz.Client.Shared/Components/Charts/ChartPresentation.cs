#nullable enable

using System;
using System.Collections.Generic;
using ChefKnifeStudios.TransitJazz.Client.Shared.Constants;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components.Charts;

/// <summary>Stable key-based series colors and theme colors shared by chart mappers.</summary>
public static class ChartPresentation
{
    static readonly string[] LightSeriesPalette =
    ["#1565C0", "#7B1FA2", "#2E7D32", "#C62828", "#EF6C00", "#00838F", "#AD1457", "#5D4037"];

    static readonly string[] DarkSeriesPalette =
    ["#90CAF9", "#CE93D8", "#A5D6A7", "#EF9A9A", "#FFCC80", "#80DEEA", "#F48FB1", "#BCAAA4"];

    static readonly HashSet<string> TransportCategories = new(StringComparer.OrdinalIgnoreCase)
    { "bus", "rail", "train", "streetcar", "tram", "ferry", "other", "unknown" };

    static readonly IReadOnlyDictionary<string, int> CityPalette = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["atlanta"] = 0,
        ["boston"] = 1,
        ["new-york-city"] = 2,
        ["toronto"] = 3,
        ["philadelphia"] = 4,
        ["washington-dc"] = 5,
        ["denver"] = 6
    };

    public static ChartThemeColors GetThemeColors(bool isDark) => isDark
        ? new(ColorConstants.Dark.OnSurface, ColorConstants.Dark.OutlineVariant, ColorConstants.Dark.Surface)
        : new(ColorConstants.Light.OnSurface, ColorConstants.Light.OutlineVariant, ColorConstants.Light.Surface);

    public static string GetRouteColor(string citySlug, string routeJoinKey, string categoryKey,
        IReadOnlyDictionary<string, RouteShapeFeature>? routeShapes = null, bool isDark = false) =>
        routeShapes is not null && routeShapes.TryGetValue(routeJoinKey, out var shape)
            && (shape.Properties.City is null || string.Equals(shape.Properties.City, citySlug, StringComparison.Ordinal))
            ? RouteColorFallback.ResolveColor(shape.Properties.Color)
            : TransportCategoryColors.Get(categoryKey, isDark);

    public static ChartSeriesStyle GetSeriesStyle(string key, bool isDark)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A stable series key is required.", nameof(key));

        var palette = isDark ? DarkSeriesPalette : LightSeriesPalette;
        var stableKey = key.StartsWith("city:", StringComparison.Ordinal) ? key[5..] : key;
        var index = CityPalette.TryGetValue(stableKey, out var cityIndex) ? cityIndex
            : StableHash(key) % palette.Length;
        var theme = GetThemeColors(isDark);
        var color = key.StartsWith("category:", StringComparison.Ordinal)
            ? TransportCategoryColors.Get(key[9..], isDark)
            : TransportCategories.Contains(key) ? TransportCategoryColors.Get(key, isDark) : palette[index];
        return new ChartSeriesStyle(color, theme.TextColor, theme.GridColor);
    }

    static int StableHash(string key)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var character in key)
            {
                hash ^= character;
                hash *= 16777619;
            }
            return (int)(hash % 8);
        }
    }
}

public sealed record ChartThemeColors(string TextColor, string GridColor, string SurfaceColor);
public sealed record ChartSeriesStyle(string Color, string TextColor, string GridColor);
