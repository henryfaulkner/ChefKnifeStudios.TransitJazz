using ChefKnifeStudios.TransitJazz.Client.Core.Services;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.EventArgs;

public class CheckpointVisibilityChangedEventArgs : IEventArgs
{
    public required bool AreCheckpointsVisible { get; init; }
}
