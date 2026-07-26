using ChefKnifeStudios.TransitJazz.Client.Core.Services;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.EventArgs;

public class AudioSettingChangedEventArgs : IEventArgs
{
    public required bool IsAudioEnabled { get; init; }
}
