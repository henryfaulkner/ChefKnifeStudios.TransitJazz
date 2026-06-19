using ChefKnifeStudios.MartaJazz.Client.Core.Services;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.EventArgs;

public class AllCheckpointsVisibilityChangedEventArgs : IEventArgs
{
    public required bool AreAllCheckpointsVisible { get; init; }
}
