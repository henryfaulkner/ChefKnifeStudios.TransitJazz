using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Audio;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.EventArgs;

public sealed class AudioStopOutcomeChangedEventArgs : IEventArgs
{
    public AudioStopOutcome? Outcome { get; init; }
}
