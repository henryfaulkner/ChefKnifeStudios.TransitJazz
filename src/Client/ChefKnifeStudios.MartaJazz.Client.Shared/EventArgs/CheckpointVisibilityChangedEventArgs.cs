using ChefKnifeStudios.MartaJazz.Client.Core.Services;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.EventArgs;

public class CheckpointVisibilityChangedEventArgs : IEventArgs
{
    public required bool AreCheckpointsVisible { get; init; }
}
