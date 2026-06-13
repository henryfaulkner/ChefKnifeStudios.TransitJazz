using ChefKnifeStudios.MartaJazz.Client.Shared.Services;
using ChefKnifeStudios.MartaJazz.Client.Shared.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Components;

public class RouteItem
{
    public string RouteId { get; init; }
    public string Color { get; init; }
    public bool IsSelected { get; set; }
}

public interface IRouteFilterViewModel : IViewModel, IDisposable
{
    IEnumerable<RouteItem> RouteItems { get; }
    void SelectRoute(RouteItem routeItem);
    void ClearSelection();
    public bool HasSelection { get; }
    public string? SelectedRouteId { get; }
}

public partial class RouteFilterViewModel : BaseViewModel, IRouteFilterViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    IEnumerable<RouteItem> _routeItems = [];

    readonly ILogger<RouteFilterViewModel> _logger;
    readonly IToastService _toastService;
    readonly IApplicationViewModel _applicationViewModel;

    public RouteFilterViewModel(
        ILogger<RouteFilterViewModel> logger,
        IToastService toastService,
        IApplicationViewModel applicationViewModel)
    {
        _logger = logger;
        _toastService = toastService;
        _applicationViewModel = applicationViewModel;

        // RouteShapes are loaded asynchronously by ApplicationViewModel, so the cache
        // may still be empty when this VM is constructed. Build whatever is available
        // now, then rebuild when RoutesLoaded flips so the filters appear once loading
        // completes (mirrors RouteFilters' own PropertyChanged subscription).
        _applicationViewModel.PropertyChanged += OnApplicationViewModelPropertyChanged;
        BuildRouteItems();
    }

    void OnApplicationViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IApplicationViewModel.RoutesLoaded))
            BuildRouteItems();
    }

    void BuildRouteItems()
    {
        RouteItems = _applicationViewModel.RouteShapes
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrEmpty(x.Properties.RouteShortName))
            .Select(x => new RouteItem
            {
                RouteId = x.Properties.RouteShortName!,
                Color = x.Properties.Color ?? "#888888",
                IsSelected = false,
            })
            .ToList();

        _logger.LogDebug("RouteFilterViewModel.BuildRouteItems: built {Count} route items", RouteItems.Count());
    }

    public void SelectRoute(RouteItem routeItem)
    {
        // Assign through the generated RouteItems property — mutating items
        // in place bypasses the setter, so PropertyChanged never fires.
        RouteItems = RouteItems
            .Select(x => new RouteItem { RouteId = x.RouteId, Color = x.Color, IsSelected = x.RouteId == routeItem.RouteId, })
            .ToList();
    }

    public void ClearSelection()
    {
        RouteItems = RouteItems
            .Select(x => new RouteItem { RouteId = x.RouteId, Color = x.Color, IsSelected = false, })
            .ToList();
    }

    public bool HasSelection => RouteItems.Any(x => x.IsSelected);

    public string? SelectedRouteId => RouteItems.FirstOrDefault(x => x.IsSelected)?.RouteId;

    public void Dispose()
    {
        _applicationViewModel.PropertyChanged -= OnApplicationViewModelPropertyChanged;
    }
}
