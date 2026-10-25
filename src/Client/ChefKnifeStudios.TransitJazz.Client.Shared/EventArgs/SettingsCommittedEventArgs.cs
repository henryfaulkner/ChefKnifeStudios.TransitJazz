using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.EventArgs;

public sealed class SettingsCommittedEventArgs : IEventArgs
{
    public required string PropertyName { get; init; }
    public bool IsPersisted { get; init; }
    public SettingsChangeOutcome? ChangeOutcome { get; init; }
}
