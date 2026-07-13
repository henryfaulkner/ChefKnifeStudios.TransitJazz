using System.Text.RegularExpressions;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Cities;

public static partial class RouteIdNormalizer
{
    public static string Apply(string routeId, IReadOnlyList<string> steps)
    {
        var result = routeId;
        foreach (var step in steps)
        {
            result = step switch
            {
                "uppercase" => result.ToUpperInvariant(),
                "plusToSbs" => ApplyPlusToSbs(result),
                "stripLeadingZeros" => ApplyStripLeadingZeros(result),
                _ => result,
            };
        }
        return result;
    }

    static string ApplyPlusToSbs(string routeId) =>
        routeId.EndsWith('+') ? routeId[..^1] + "-SBS" : routeId;

    static string ApplyStripLeadingZeros(string routeId)
    {
        var match = StripLeadingZerosRegex().Match(routeId);
        return match.Success ? match.Groups[1].Value + match.Groups[2].Value : routeId;
    }

    [GeneratedRegex(@"^([A-Z]+)0*(\d.*)$")]
    private static partial Regex StripLeadingZerosRegex();
}
