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
    public string Label { get; init; }
    public string Color { get; init; }
    public bool IsSelected { get; set; }
    public TransitMode Mode { get; init; }
}

public interface IRouteFilterViewModel : IViewModel, IDisposable
{
    IEnumerable<RouteItem> RouteItems { get; }
    void SelectRoute(RouteItem routeItem);
    void ClearSelection(TransitMode mode);
    void SetHoveredRoute(RouteItem? routeItem);
    public bool HasSelection { get; }
    bool HasSelectionFor(TransitMode mode);
    public bool IsSingleSelection { get; }
    public string? SelectedRouteId { get; }
    public IReadOnlyCollection<string> SelectedRouteIds { get; }
    public string? HoveredRouteId { get; }
    public int ActiveBusCount { get; }
    public int ActiveRailCount { get; }
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
    int _activeRailCount;

    [ObservableProperty]
    string? _hoveredRouteId;

    readonly ILogger<RouteFilterViewModel> _logger;
    readonly IToastService _toastService;
    readonly IApplicationViewModel _applicationViewModel;

    // Accumulated distinct non-stale vehicle IDs per route, upserted across every batch
    // (live + cold-start snapshot) — mirrors the server-side LastBatchCache accumulator so
    // the running count reflects all known vehicles, not just the ones that moved this cycle.
    // An empty/all-stale batch adds nothing, so the count holds its last value.
    readonly Dictionary<string, HashSet<string>> _routeVehicles = new(StringComparer.Ordinal);
    // Tracks which vehicle IDs are rail so counts can be split without re-examining route names.
    readonly HashSet<string> _railVehicleIds = new(StringComparer.Ordinal);
    // vehicleId → consecutive batches in which the vehicle was absent from the feed. Reset to 0
    // every batch the vehicle appears in (stale OR live — a stale record still proves it's in the
    // feed). Once a vehicle is absent this many batches in a row it's evicted from the counts, so
    // a vehicle that finishes its run / drops out of GTFS-RT stops inflating the total instead of
    // lingering forever. The threshold is deliberately more lenient than the audio eviction's 3
    // (TransitMap.RouteAudioEvictAfterBatches) so the count is slow to drop and tolerant of feed
    // gaps. Mirrors the absence-tracking pattern in TransitMap.EvictInactiveRouteAudioAsync.
    readonly Dictionary<string, int> _vehicleAbsenceBatches = new(StringComparer.Ordinal);
    const int VehicleEvictAfterBatches = 6;

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
        // Accumulate distinct non-stale vehicles per route across batches (live + the
        // cold-start snapshot fanned through this same event). Stale records (same GPS
        // reading repeated) are excluded from the count's membership but still count as
        // a feed appearance for absence tracking below.
        var changed = false;
        // Vehicles seen anywhere in this batch (stale OR live). A vehicle present here is in
        // the feed this cycle, so its absence streak resets even if its only record was stale.
        var seenThisBatch = new HashSet<string>(StringComparer.Ordinal);
        var sawAnyBatchEvent = false;
        foreach (var envelope in batch)
        {
            if (envelope.Payload is not RouteNearestPointBatchEvent batchEvent) continue;
            sawAnyBatchEvent = true;
            foreach (var record in batchEvent.BatchRecords)
            {
                if (string.IsNullOrEmpty(record.RouteId)) continue;
                seenThisBatch.Add(record.VehicleId);
                if (record.IsStale) continue;
                if (!_routeVehicles.TryGetValue(record.RouteId, out var vehicles))
                    _routeVehicles[record.RouteId] = vehicles = new HashSet<string>(StringComparer.Ordinal);
                changed |= vehicles.Add(record.VehicleId);
                if (record.TransitMode == TransitMode.Rail)
                    _railVehicleIds.Add(record.VehicleId);
            }
        }

        // Age out vehicles that have dropped out of the feed. Only run when this notification
        // actually carried a transit batch — a non-batch envelope (e.g. a settings event fanned
        // through the same bus) must not be mistaken for a cycle in which every vehicle was absent.
        if (sawAnyBatchEvent)
            changed |= EvictAbsentVehicles(seenThisBatch);

        if (!changed) return Task.CompletedTask;

        RouteItems = RouteItems
            .OrderByDescending(x => _routeVehicles.TryGetValue(x.RouteId, out var v) ? v.Count : 0)
            .ToList();

        RecomputeActiveTransitCounts();

        return Task.CompletedTask;
    }

    // Increment the consecutive-absence streak for every counted vehicle missing from this
    // cycle's feed (reset to 0 for those present), then evict any whose streak has reached the
    // threshold. Returns true if any vehicle was removed (so the caller re-sorts and recomputes).
    bool EvictAbsentVehicles(HashSet<string> seenThisBatch)
    {
        // Snapshot the currently-counted vehicle IDs (across all routes) to avoid mutating
        // _routeVehicles while enumerating it.
        var toEvict = new List<string>();
        foreach (var vehicles in _routeVehicles.Values)
        {
            foreach (var vehicleId in vehicles)
            {
                if (seenThisBatch.Contains(vehicleId))
                {
                    _vehicleAbsenceBatches.Remove(vehicleId);
                    continue;
                }

                var absent = _vehicleAbsenceBatches.GetValueOrDefault(vehicleId) + 1;
                _vehicleAbsenceBatches[vehicleId] = absent;
                if (absent >= VehicleEvictAfterBatches)
                    toEvict.Add(vehicleId);
            }
        }

        if (toEvict.Count == 0) return false;

        var evictSet = new HashSet<string>(toEvict, StringComparer.Ordinal);
        foreach (var vehicles in _routeVehicles.Values)
            vehicles.RemoveWhere(evictSet.Contains);
        _railVehicleIds.RemoveWhere(evictSet.Contains);
        foreach (var vehicleId in toEvict)
            _vehicleAbsenceBatches.Remove(vehicleId);

        return true;
    }

    void RecomputeActiveTransitCounts()
    {
        var selected = SelectedRouteIds;
        var hovered = HoveredRouteId;

        // Build the effective emphasis set: union of persistent selection and hover.
        // Empty set means unscoped (all vehicles).
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

        IEnumerable<string> vehicleIds = effectiveIds.Count > 0
            ? effectiveIds.SelectMany(id => _routeVehicles.TryGetValue(id, out var v) ? v : [])
            : _routeVehicles.Values.SelectMany(v => v);

        int busCount = 0, railCount = 0;
        foreach (var id in vehicleIds)
        {
            if (_railVehicleIds.Contains(id)) railCount++;
            else busCount++;
        }

        ActiveBusCount = busCount;
        ActiveRailCount = railCount;
    }

    void BuildRouteItems()
    {
        var previouslySelected = SelectedRouteIds;

        RouteItems = _applicationViewModel.RouteShapes
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrEmpty(x.Properties.RouteShortName) || !string.IsNullOrEmpty(x.Properties.RouteId))
            .Select(x => new RouteItem
            {
                RouteId = x.Properties.RouteShortName ?? x.Properties.RouteId!,
                Label = x.Properties.RouteShortName ?? x.Properties.RouteId!,
                Color = IsVisibleColor(x.Properties.Color) ? x.Properties.Color! : "#CC0000",
                IsSelected = previouslySelected.Contains(x.Properties.RouteShortName ?? x.Properties.RouteId!),
                Mode = x.Properties.Mode,
            })
            .OrderByDescending(x => _routeVehicles.TryGetValue(x.RouteId, out var v) ? v.Count : 0)
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
                Label = x.Label,
                Color = x.Color,
                IsSelected = x.RouteId == routeItem.RouteId ? !x.IsSelected : x.IsSelected,
                Mode = x.Mode,
            })
            .ToList();
        RecomputeActiveTransitCounts();
    }

    public void ClearSelection(TransitMode mode)
    {
        RouteItems = RouteItems
            .Select(x => new RouteItem { RouteId = x.RouteId, Label = x.Label, Color = x.Color, IsSelected = x.Mode == mode ? false : x.IsSelected, Mode = x.Mode, })
            .ToList();
        RecomputeActiveTransitCounts();
    }

    public void SetHoveredRoute(RouteItem? routeItem)
    {
        HoveredRouteId = routeItem?.RouteId;
        RecomputeActiveTransitCounts();
    }

    static bool IsVisibleColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return false;
        var c = color.TrimStart('#').ToUpperInvariant();
        return c is not ("FFFFFF" or "FFF" or "FEFEFE");
    }

    public bool HasSelection => RouteItems.Any(x => x.IsSelected);

    public bool HasSelectionFor(TransitMode mode) => RouteItems.Any(x => x.IsSelected && x.Mode == mode);

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
