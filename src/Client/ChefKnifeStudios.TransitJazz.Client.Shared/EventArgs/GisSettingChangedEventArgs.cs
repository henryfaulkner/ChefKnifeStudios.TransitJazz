using ChefKnifeStudios.TransitJazz.Client.Core.Services;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.EventArgs;

public class GisSettingChangedEventArgs : IEventArgs
{
    public required bool IsStreetMapEnabled { get; init; }
}
