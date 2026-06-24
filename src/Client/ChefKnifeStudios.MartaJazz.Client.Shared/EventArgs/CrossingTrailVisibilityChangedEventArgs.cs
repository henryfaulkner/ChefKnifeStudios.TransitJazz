using ChefKnifeStudios.MartaJazz.Client.Core.Services;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.EventArgs;

public class CrossingTrailVisibilityChangedEventArgs : IEventArgs
{
    public required bool IsCrossingTrailVisible { get; init; }
}
