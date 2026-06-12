using ChefKnifeStudios.MartaJazz.Client.Shared.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Components;

public class RouteItem 
{
    public string RouteId { get; init; }
    public bool IsSelected { get; set; }
}

public interface IRouteFilterViewModel : IViewModel
{
    IEnumerable<RouteItem> RouteItems { get; }
    Task LoadAsync(CancellationToken ct = default);
    void SelectRoute(string routeId);
    public bool HasSelection { get; }
}

public partial class RouteFilterViewModel : BaseViewModel, IRouteFilterViewModel
{
    [ObservableProperty]
    public IEnumerable<RouteItem> _routeItems = [
        new () { RouteId = "001", IsSelected = false, },
        new () { RouteId = "002", IsSelected = false, },
        new () { RouteId = "003", IsSelected = false, },
        new () { RouteId = "004", IsSelected = false, },
        new () { RouteId = "005", IsSelected = false, },
        new () { RouteId = "006", IsSelected = false, },
        new () { RouteId = "007", IsSelected = false, },
        new () { RouteId = "008", IsSelected = false, },
        new () { RouteId = "009", IsSelected = false, },
    ];

    readonly ILogger<RouteFilterViewModel> _logger;
    readonly IToastService _toastService;

    public RouteFilterViewModel(
        ILogger<RouteFilterViewModel> logger,
        IToastService toastService)
    {
        _logger = logger;
        _toastService = toastService;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        // Add service call to populate the RouteFilters
        // Toast warning if the RouteFilter fails to load
        // Log error if the RouteFilter fails to load
    }

    public void SelectRoute(string routeId)
    {
        _toastService.ShowSuccess($"Route {routeId} selected!"); // TODO remove
        foreach (var routeItem in RouteItems)
        {
            routeItem.IsSelected = routeItem.RouteId == routeId;  
        }
    }

    public bool HasSelection => RouteItems.Any(x => x.IsSelected);
}
