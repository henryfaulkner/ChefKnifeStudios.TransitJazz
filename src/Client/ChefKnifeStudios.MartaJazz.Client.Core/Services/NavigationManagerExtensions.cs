using ChefKnifeStudios.MartaJazz.Shared;
using Microsoft.AspNetCore.Components;

namespace ChefKnifeStudios.MartaJazz.Client.Core.Services;

public static class NavigationManagerExtensions
{
    public static string ResolveCity(this NavigationManager nav)
    {
        try
        {
            var fragment = new Uri(nav.Uri).Fragment.TrimStart('#');
            if (!string.IsNullOrWhiteSpace(fragment))
                return Uri.UnescapeDataString(fragment).ToLowerInvariant();
        }
        catch { }
        return CityNames.Marta;
    }
}
