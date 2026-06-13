using ChefKnifeStudios.MartaJazz.Client.Core.Services;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.EventArgs;

public class BladeEventArgs : IEventArgs
{
    public enum Types { Close, Settings }
    public required Types Type { get; init; }
    public object? Data { get; init; }
}
