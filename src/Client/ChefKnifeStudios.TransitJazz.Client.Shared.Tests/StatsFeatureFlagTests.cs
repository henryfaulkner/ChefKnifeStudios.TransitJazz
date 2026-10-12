using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Enums;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class StatsFeatureFlagTests
{
    [Fact]
    public void ProductionEntryPolicyIsOffByDefaultAndUsesTheSingleConfiguredFlag()
    {
        var defaultFlags = new FeatureFlagService();
        var enabledFlags = new FeatureFlagService(new Dictionary<FeatureFlags, bool>
        {
            [FeatureFlags.StatsOverlay] = true
        });
        var unrelatedFlags = new FeatureFlagService(new Dictionary<FeatureFlags, bool>
        {
            [FeatureFlags.StatsOverlay] = false
        });

        Assert.False(StatsOverlayFeaturePolicy.IsAvailable(defaultFlags));
        Assert.False(StatsOverlayFeaturePolicy.IsAvailable(unrelatedFlags));
        Assert.True(StatsOverlayFeaturePolicy.IsAvailable(enabledFlags));
    }
}
