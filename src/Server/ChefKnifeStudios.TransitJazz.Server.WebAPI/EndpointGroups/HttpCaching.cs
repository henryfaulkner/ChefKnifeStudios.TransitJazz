using Microsoft.Extensions.Primitives;
using System.Linq;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.EndpointGroups;

/// <summary>
/// Pure conditional-GET decision (If-None-Match vs ETag -> 304?), extracted so the full
/// boundary matrix (exact/absent/stale/multi-value/wildcard) is provable at unit cost
/// (test-plan P1-U3; plan's binding seam #2).
/// </summary>
public static class HttpCaching
{
    public static bool IsNotModified(StringValues ifNoneMatch, string etag)
    {
        if (ifNoneMatch.Count == 0) return false;

        return ifNoneMatch
            .SelectMany(v => (v ?? string.Empty).Split(','))
            .Select(v => v.Trim())
            .Any(v => v == "*" || v == etag);
    }
}
