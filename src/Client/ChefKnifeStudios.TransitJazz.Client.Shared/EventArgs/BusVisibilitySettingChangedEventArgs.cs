using ChefKnifeStudios.TransitJazz.Client.Core.Services;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.EventArgs;

public class BusVisibilitySettingChangedEventArgs : IEventArgs
{
    public required bool IsBusesVisible { get; init; }
}
