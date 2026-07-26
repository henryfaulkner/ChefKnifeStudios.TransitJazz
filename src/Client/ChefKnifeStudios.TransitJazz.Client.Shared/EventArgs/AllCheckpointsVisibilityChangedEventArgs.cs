using ChefKnifeStudios.TransitJazz.Client.Core.Services;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.EventArgs;

public class AllCheckpointsVisibilityChangedEventArgs : IEventArgs
{
    public required bool AreAllCheckpointsVisible { get; init; }
}
