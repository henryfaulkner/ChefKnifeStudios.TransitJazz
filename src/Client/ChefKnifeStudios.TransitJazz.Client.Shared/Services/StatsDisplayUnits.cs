using ChefKnifeStudios.TransitJazz.Client.Shared.Models;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services;

public static class StatsDisplayUnits
{
    const decimal KilometersPerMile = 1.609344m;

    public static StatsUnitSystem Normalize(StatsUnitSystem units) =>
        units is StatsUnitSystem.Metric or StatsUnitSystem.Imperial ? units : StatsUnitSystem.Metric;

    public static decimal? ConvertDistance(decimal? kilometers, StatsUnitSystem units) =>
        Normalize(units) == StatsUnitSystem.Imperial && kilometers is { } value
            ? value / KilometersPerMile
            : kilometers;
}
