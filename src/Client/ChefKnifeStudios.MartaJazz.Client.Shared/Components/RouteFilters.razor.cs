using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Components;

public partial class RouteFilters : ComponentBase, IDisposable
{
    [Inject] ILogger<RouteFilters> _logger { get; set; } = null!;
    [Inject] IRouteFilterViewModel RouteFilterViewModel { get; set; } = null!;

    protected override void OnInitialized()
    {
        RouteFilterViewModel.PropertyChanged += RouteFilterViewModel_PropertyChanged;
    }

    public void Dispose()
    {
        RouteFilterViewModel.PropertyChanged -= RouteFilterViewModel_PropertyChanged;
    }

    void RouteFilterViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IRouteFilterViewModel.RouteItems) or nameof(IRouteFilterViewModel.HasSelection) or nameof(IRouteFilterViewModel.ActiveBusCount))
        {
            InvokeAsync(StateHasChanged);
        }
    }

    void HandleMouseOver(MouseEventArgs args, RouteItem routeItem)
    {
        RouteFilterViewModel.SelectRoute(routeItem);
    }

    void HandleMouseOut(MouseEventArgs args, RouteItem routeItem)
    {
        RouteFilterViewModel.ClearSelection();
    }
}
