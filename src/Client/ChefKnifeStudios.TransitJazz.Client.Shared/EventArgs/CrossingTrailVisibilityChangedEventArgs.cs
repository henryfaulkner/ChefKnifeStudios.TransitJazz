using ChefKnifeStudios.TransitJazz.Client.Core.Services;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.EventArgs;

public class CrossingTrailVisibilityChangedEventArgs : IEventArgs
{
    public required bool IsCrossingTrailVisible { get; init; }
}
