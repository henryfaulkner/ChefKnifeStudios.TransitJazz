using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Components;

public partial class RouteFilters : ComponentBase
{
    [Inject] ILogger<RouteFilters> _logger { get; set; } = null!;
    [Inject] IRouteFilterViewModel RouteFilterViewModel { get; set; } = null!;

    void HandleMouseOver(MouseEventArgs args, RouteItem routeItem)
    {
        _logger.LogInformation("Mouse over route filter");
        RouteFilterViewModel.SelectRoute(routeItem.RouteId);
    }

    void HandleMouseOut(MouseEventArgs args, RouteItem routeItem)
    {
        _logger.LogInformation("Mouse out of route filter");
    }
}
