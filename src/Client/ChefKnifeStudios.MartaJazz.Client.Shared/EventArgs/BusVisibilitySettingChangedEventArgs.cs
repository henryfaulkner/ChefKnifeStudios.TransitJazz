using ChefKnifeStudios.MartaJazz.Client.Core.Services;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.EventArgs;

public class BusVisibilitySettingChangedEventArgs : IEventArgs
{
    public required bool IsBusesVisible { get; init; }
}
