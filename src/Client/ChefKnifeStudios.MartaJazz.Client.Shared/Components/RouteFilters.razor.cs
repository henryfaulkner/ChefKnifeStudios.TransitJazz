using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Components;

public partial class RouteFilters : ComponentBase, IDisposable
{
    [Inject] ILogger<RouteFilters> _logger { get; set; } = null!;
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
        if (e.PropertyName is nameof(IRouteFilterViewModel.RouteItems) or nameof(IRouteFilterViewModel.HasSelection) or nameof(IRouteFilterViewModel.ActiveCountsByCategory) or nameof(IRouteFilterViewModel.HoveredRouteJoinKey) or nameof(IRouteFilterViewModel.HoveredCategory))
        {
            InvokeAsync(StateHasChanged);
        }
    }

    void HandleMouseOver(MouseEventArgs args, RouteItem routeItem)
    {
        RouteFilterViewModel.SetHoveredRoute(routeItem);
    }

    void HandleMouseOut(MouseEventArgs args, RouteItem routeItem)
    {
        RouteFilterViewModel.SetHoveredRoute(null);
    }

    void HandleSectionMouseOver(MouseEventArgs args, string category)
    {
        RouteFilterViewModel.SetHoveredCategory(category);
    }

    void HandleSectionMouseOut(MouseEventArgs args, string category)
    {
        RouteFilterViewModel.SetHoveredCategory(null);
    }

    void HandleSelect(RouteItem routeItem)
    {
        RouteFilterViewModel.SelectRoute(routeItem);
    }

    void HandleClearSelections(string category)
    {
        RouteFilterViewModel.ClearSelection(category);
    }

    void HandleSelectAll(string category)
    {
        RouteFilterViewModel.SelectAll(category);
    }
}
