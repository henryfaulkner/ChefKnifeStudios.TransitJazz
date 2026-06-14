using ChefKnifeStudios.MartaJazz.Client.Core.Services;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.EventArgs;

public class GisSettingChangedEventArgs : IEventArgs
{
    public required bool IsStreetMapEnabled { get; init; }
}
