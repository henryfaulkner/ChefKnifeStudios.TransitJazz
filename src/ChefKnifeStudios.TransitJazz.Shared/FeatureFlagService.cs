using ChefKnifeStudios.TransitJazz.Shared.Enums;
using System.Collections.Generic;

namespace ChefKnifeStudios.TransitJazz.Shared;

public interface IFeatureFlagService
{
    bool IsEnabled(FeatureFlags flag);
}

public class FeatureFlagService : IFeatureFlagService
{
    private readonly Dictionary<FeatureFlags, bool> _flags;

    public FeatureFlagService(Dictionary<FeatureFlags, bool>? flags = null)
    {
        _flags = flags ?? new Dictionary<FeatureFlags, bool>();
    }

    public bool IsEnabled(FeatureFlags flag)
    {
        return _flags.TryGetValue(flag, out var enabled) && enabled;
    }
}
