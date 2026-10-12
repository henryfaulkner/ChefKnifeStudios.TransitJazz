using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Enums;
using System;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services;

/// <summary>The configured flag controls every Stats launcher and the mounted production overlay.</summary>
public static class StatsOverlayFeaturePolicy
{
    public static bool IsAvailable(IFeatureFlagService featureFlags)
    {
        ArgumentNullException.ThrowIfNull(featureFlags);
        return featureFlags.IsEnabled(FeatureFlags.StatsOverlay);
    }
}
