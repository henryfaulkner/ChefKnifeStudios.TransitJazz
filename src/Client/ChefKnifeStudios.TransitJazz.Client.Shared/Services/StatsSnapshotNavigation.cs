using System;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using ChefKnifeStudios.TransitJazz.Client.Core.Services;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services;

/// <summary>Converts route changes into canonical city signals for the app-owned Stats store.</summary>
public sealed class StatsSnapshotNavigation : IStatsSnapshotNavigation, IDisposable
{
    readonly NavigationManager navigation;
    bool disposed;

    public StatsSnapshotNavigation(NavigationManager navigation)
    {
        this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        this.navigation.LocationChanged += HandleLocationChanged;
    }

    public event Action<string>? CityChanged;

    void HandleLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        if (!disposed) CityChanged?.Invoke(navigation.ResolveCity());
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        navigation.LocationChanged -= HandleLocationChanged;
    }
}
