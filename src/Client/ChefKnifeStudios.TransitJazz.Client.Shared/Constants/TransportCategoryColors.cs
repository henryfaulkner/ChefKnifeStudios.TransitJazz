namespace ChefKnifeStudios.TransitJazz.Client.Shared.Constants;

/// <summary>Transport-type colors shared by running labels and Stats charts.</summary>
public static class TransportCategoryColors
{
    public static string Get(string? category, bool isDark) => category?.Trim().ToLowerInvariant() switch
    {
        "bus" => isDark ? "#66BB6A" : "#006400",
        "rail" or "train" => isDark ? "#7986CB" : "#1A237E",
        "streetcar" or "tram" => isDark ? "#E57373" : "#B71C1C",
        _ => isDark ? "#9E9E9E" : "#616161"
    };
}
