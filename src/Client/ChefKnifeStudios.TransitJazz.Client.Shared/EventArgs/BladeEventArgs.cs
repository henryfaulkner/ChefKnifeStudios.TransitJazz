using ChefKnifeStudios.TransitJazz.Client.Core.Services;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.EventArgs;

public class BladeEventArgs : IEventArgs
{
    public enum Types { Close, Settings, Stats, Info }
    public required Types Type { get; init; }
    public object? Data { get; init; }
}
