using ChefKnifeStudios.MartaJazz.Client.Shared.Services;
using ChefKnifeStudios.MartaJazz.Client.Shared.ViewModels;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

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
    void SetHoveredRoute(RouteItem? routeItem);
    public bool HasSelection { get; }
    public bool IsSingleSelection { get; }
    public string? SelectedRouteId { get; }
    public IReadOnlyCollection<string> SelectedRouteIds { get; }
    public string? HoveredRouteId { get; }
    public int ActiveBusCount { get; }
}

public partial class RouteFilterViewModel : BaseViewModel, IRouteFilterViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(IsSingleSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedRouteId))]
    [NotifyPropertyChangedFor(nameof(SelectedRouteIds))]
    IEnumerable<RouteItem> _routeItems = [];

    [ObservableProperty]
    int _activeBusCount;

    [ObservableProperty]
    string? _hoveredRouteId;

    readonly ILogger<RouteFilterViewModel> _logger;
    readonly IToastService _toastService;
    readonly IApplicationViewModel _applicationViewModel;

    // Retained per-route running counts from the last batch so ActiveBusCount
    // can recompute on selection change without waiting for a new batch.
    Dictionary<string, int> _lastBatchRouteCounts = new(StringComparer.Ordinal);

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
        _applicationViewModel.NotificationReceived += OnNotificationReceived;
        BuildRouteItems();
    }

    void OnApplicationViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IApplicationViewModel.RoutesLoaded))
            BuildRouteItems();
    }

    Task OnNotificationReceived(List<EventEnvelope> batch)
    {
        // Count distinct active vehicles per route from RouteNearestPointBatchEvent —
        // the authoritative per-cycle view that drives animation. Stale records
        // (same GPS reading repeated) are excluded so only moving buses count.
        var routeVehicles = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var envelope in batch)
        {
            if (envelope.Payload is not RouteNearestPointBatchEvent batchEvent) continue;
            foreach (var record in batchEvent.BatchRecords)
            {
                if (record.IsStale || string.IsNullOrEmpty(record.RouteId)) continue;
                if (!routeVehicles.TryGetValue(record.RouteId, out var vehicles))
                    routeVehicles[record.RouteId] = vehicles = new HashSet<string>(StringComparer.Ordinal);
                vehicles.Add(record.VehicleId);
            }
        }

        if (routeVehicles.Count == 0) return Task.CompletedTask;

        _lastBatchRouteCounts = routeVehicles.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Count,
            StringComparer.Ordinal);
        RecomputeActiveBusCount();

        return Task.CompletedTask;
    }

    void RecomputeActiveBusCount()
    {
        var selected = SelectedRouteIds;
        var hovered = HoveredRouteId;

        // Build the effective emphasis set: union of persistent selection and hover.
        // Empty set means unscoped (all buses).
        IReadOnlyCollection<string> effectiveIds;
        if (selected.Count == 0 && hovered is null)
        {
            effectiveIds = [];
        }
        else if (hovered is null || selected.Contains(hovered))
        {
            effectiveIds = selected;
        }
        else
        {
            effectiveIds = [.. selected, hovered];
        }

        int count = effectiveIds.Count > 0
            ? effectiveIds.Sum(id => _lastBatchRouteCounts.TryGetValue(id, out var c) ? c : 0)
            : _lastBatchRouteCounts.Values.Sum();

        ActiveBusCount = count;
    }

    void BuildRouteItems()
    {
        var previouslySelected = SelectedRouteIds;

        RouteItems = _applicationViewModel.RouteShapes
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrEmpty(x.Properties.RouteShortName))
            .Select(x => new RouteItem
            {
                RouteId = x.Properties.RouteShortName!,
                Color = x.Properties.Color ?? "#888888",
                IsSelected = previouslySelected.Contains(x.Properties.RouteShortName!),
            })
            .ToList();

        _logger.LogDebug("RouteFilterViewModel.BuildRouteItems: built {Count} route items", RouteItems.Count());
    }

    public void SelectRoute(RouteItem routeItem)
    {
        // Assign through the generated RouteItems property — mutating items
        // in place bypasses the setter, so PropertyChanged never fires.
        RouteItems = RouteItems
            .Select(x => new RouteItem
            {
                RouteId = x.RouteId,
                Color = x.Color,
                IsSelected = x.RouteId == routeItem.RouteId ? !x.IsSelected : x.IsSelected,
            })
            .ToList();
        RecomputeActiveBusCount();
    }

    public void ClearSelection()
    {
        RouteItems = RouteItems
            .Select(x => new RouteItem { RouteId = x.RouteId, Color = x.Color, IsSelected = false, })
            .ToList();
        RecomputeActiveBusCount();
    }

    public void SetHoveredRoute(RouteItem? routeItem)
    {
        HoveredRouteId = routeItem?.RouteId;
        RecomputeActiveBusCount();
    }

    public bool HasSelection => RouteItems.Any(x => x.IsSelected);

    public bool IsSingleSelection => SelectedRouteIds.Count == 1;

    public IReadOnlyCollection<string> SelectedRouteIds =>
        RouteItems.Where(x => x.IsSelected).Select(x => x.RouteId).ToList();

    public string? SelectedRouteId => IsSingleSelection ? SelectedRouteIds.First() : null;

    public void Dispose()
    {
        _applicationViewModel.PropertyChanged -= OnApplicationViewModelPropertyChanged;
        _applicationViewModel.NotificationReceived -= OnNotificationReceived;
    }
}
