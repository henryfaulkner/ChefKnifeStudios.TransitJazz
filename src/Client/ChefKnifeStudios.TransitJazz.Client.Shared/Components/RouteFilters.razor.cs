using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;
using System;
using System.ComponentModel;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components;

public partial class RouteFilters : ComponentBase, IDisposable
{
    [Inject] IRouteFilterViewModel RouteFilterViewModel { get; set; } = null!;

    protected override void OnInitialized()
    {
        _isDark = SettingsService.GetSettings().IsDarkModeEnabled;
        EventNotificationService.EventReceived += HandleDarkEvent;
        RouteFilterViewModel.PropertyChanged += RouteFilterViewModel_PropertyChanged;
    }

    public void Dispose()
    {
        EventNotificationService.EventReceived -= HandleDarkEvent;
        RouteFilterViewModel.PropertyChanged -= RouteFilterViewModel_PropertyChanged;
    }

    void RouteFilterViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IRouteFilterViewModel.RouteItems) or nameof(IRouteFilterViewModel.HasSelection)
            or nameof(IRouteFilterViewModel.ActiveCountsByCategory) or nameof(IRouteFilterViewModel.HoveredRouteJoinKey)
            or nameof(IRouteFilterViewModel.HoveredCategory))
            _ = InvokeAsync(StateHasChanged);
    }
}
