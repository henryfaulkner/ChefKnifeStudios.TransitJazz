using ChefKnifeStudios.MartaJazz.Client.Shared.Resources;
using Microsoft.Extensions.Localization;
using System.Collections.Generic;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Data;

public interface IRouteBlurbStore
{
    RouteBlurb GetForRoute(string routeJoinKey);
}

public sealed class RouteBlurbStore : IRouteBlurbStore
{
    readonly IStringLocalizer<RouteFilterResources> _localizer;

    static readonly Dictionary<string, RouteBlurb> _authored = new(System.StringComparer.Ordinal)
    {
        // Authored entries added here as neighborhood blurbs are written.
    };

    public RouteBlurbStore(IStringLocalizer<RouteFilterResources> localizer)
    {
        _localizer = localizer;
    }

    public RouteBlurb GetForRoute(string routeJoinKey)
    {
        if (_authored.TryGetValue(routeJoinKey ?? string.Empty, out var blurb))
            return blurb;

        var placeholder = string.Format(_localizer["RouteBlurbPlaceholder"], routeJoinKey ?? string.Empty);
        return new RouteBlurb(routeJoinKey ?? string.Empty, placeholder, placeholder, IsPlaceholder: true);
    }
}
