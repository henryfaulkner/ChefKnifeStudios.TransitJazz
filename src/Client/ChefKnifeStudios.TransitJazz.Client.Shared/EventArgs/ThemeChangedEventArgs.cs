using ChefKnifeStudios.TransitJazz.Client.Core.Services;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.EventArgs;

public class ThemeChangedEventArgs : IEventArgs
{
    public bool IsDarkMode { get; set; }
}
