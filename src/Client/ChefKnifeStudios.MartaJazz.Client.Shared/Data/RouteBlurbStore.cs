using ChefKnifeStudios.MartaJazz.Client.Shared.Resources;
using Microsoft.Extensions.Localization;
using System.Collections.Generic;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Data;

public interface IRouteBlurbStore
{
    RouteBlurb GetForRoute(string routeId);
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

    public RouteBlurb GetForRoute(string routeId)
    {
        if (_authored.TryGetValue(routeId ?? string.Empty, out var blurb))
            return blurb;

        var placeholder = string.Format(_localizer["RouteBlurbPlaceholder"], routeId ?? string.Empty);
        return new RouteBlurb(routeId ?? string.Empty, placeholder, placeholder, IsPlaceholder: true);
    }
}
