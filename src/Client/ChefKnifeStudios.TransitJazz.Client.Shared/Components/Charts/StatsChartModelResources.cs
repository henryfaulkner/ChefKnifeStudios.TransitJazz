using System.Globalization;
using System.Resources;
using ChefKnifeStudios.TransitJazz.Client.Shared.Resources;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components.Charts;

internal static class StatsChartModelResources
{
    static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    static readonly ResourceManager Resources = new(typeof(RouteFilterResources).FullName!, typeof(RouteFilterResources).Assembly);

    public static string Get(string key) => Resources.GetString(key, English) ?? key;

    public static string Format(string key, params object[] arguments) =>
        string.Format(English, Get(key), arguments);
}
