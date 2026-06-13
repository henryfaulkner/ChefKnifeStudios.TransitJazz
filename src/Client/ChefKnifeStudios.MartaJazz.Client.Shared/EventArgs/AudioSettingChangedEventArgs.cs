using ChefKnifeStudios.MartaJazz.Client.Core.Services;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.EventArgs;

public class AudioSettingChangedEventArgs : IEventArgs
{
    public required bool IsAudioEnabled { get; init; }
}
