#nullable enable

using ChefKnifeStudios.TransitJazz.Client.Shared.Resources;
using System.Globalization;
using Microsoft.Extensions.Localization;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services;

/// <summary>
/// Resolves chart strings and formats numbers in English, independent of the
/// renderer's captured ExecutionContext culture.
/// </summary>
public sealed class StatsChartText(IStringLocalizer<RouteFilterResources> localizer)
{
    public string Get(string key, params object?[] arguments)
    {
        var culture = CultureInfo.GetCultureInfo("en");
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return arguments.Length == 0 ? localizer[key].Value : localizer[key, arguments].Value;
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    public string Format(double? value, string format = "0.##") => value is { } number
        ? number.ToString(format, CultureInfo.GetCultureInfo("en"))
        : Get("StatsUnavailable");

    public string Format(long? value) => value is { } number
        ? number.ToString(CultureInfo.GetCultureInfo("en"))
        : Get("StatsUnavailable");
}
