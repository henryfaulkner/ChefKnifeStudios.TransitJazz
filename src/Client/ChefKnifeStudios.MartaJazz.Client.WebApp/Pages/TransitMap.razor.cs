using ChefKnifeStudios.MartaJazz.Client.Core.Services;
using ChefKnifeStudios.MartaJazz.Client.Core.Services.EndpointsServices;
using ChefKnifeStudios.MartaJazz.Client.Shared.Components;
using ChefKnifeStudios.MartaJazz.Client.Shared.EventArgs;
using ChefKnifeStudios.MartaJazz.Client.Shared.Models;
using ChefKnifeStudios.MartaJazz.Client.Shared.Services;
using ChefKnifeStudios.MartaJazz.Client.Shared.Services.JsInterop;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using ChefKnifeStudios.MartaJazz.Shared.GtfsData;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.MartaJazz.Client.WebApp.Pages;

public partial class TransitMap : ComponentBase, IAsyncDisposable
{
    [Inject] ISignalRNotificationService NotificationService { get; set; } = null!;
    [Inject] IConfiguration Configuration { get; set; } = null!;
    [Inject] ILogger<TransitMap> Logger { get; set; } = null!;
    [Inject] IGtfsEndpointsService GtfsEndpointsService { get; set; } = null!;
    [Inject] ITriggerPointGenerator TriggerPointGenerator { get; set; } = null!;
    [Inject] ICheckpointTrackerJsInterop CheckpointTracker { get; set; } = null!;
    [Inject] ITransitSynthJsInterop TransitSynth { get; set; } = null!;
    [Inject] IRouteFilterViewModel RouteFilterViewModel { get; set; } = null!;
    [Inject] IEventNotificationService EventNotificationService { get; set; } = null!;
    [Inject] ISettingsService SettingsService { get; set; } = null!;
    [Inject] IViewportSizeJsInterop ViewportSize { get; set; } = null!;

    const float MinWidth = 1100;

    IDisposable? _viewportSub;
    bool _isMobile;

    Map? _map;
    bool _mapReady;
    IEnumerable<EventEnvelope>? _pendingBatch;

    bool _audioEnabled = true;
    DotNetObjectReference<object>? _dotNetRef;

    // vehicleId → routeId, updated on every VehiclePositionUpdatedEvent
    readonly Dictionary<string, string> _vehicleRouteMap = new();

    // routeId → GeoJSON string (client-side cache, lives for page lifetime)
    readonly Dictionary<string, RouteShapeFeature> _routeShapeCache = new(StringComparer.Ordinal);
    bool _routesLoaded;
    bool _routesRendered;

    static CameraOptions DefaultCameraOptions
        => new() { Center = new Position(33.749, -84.388), Zoom = 10 };

    protected override async Task OnInitializedAsync()
    {
        _dotNetRef = DotNetObjectReference.Create((object)this);

        var settings = SettingsService.GetSettings();
        _audioEnabled = settings.IsAudioEnabled;

        RouteFilterViewModel.PropertyChanged += OnRouteFilterPropertyChanged;
        EventNotificationService.EventReceived += HandleSettingsEventReceived;

        // Subscribe FIRST so the immediate initial fire reaches us, then register.
        _viewportSub = ViewportSize.AddViewportSizeChangeCallback(OnViewportChanged);
        await ViewportSize.RegisterViewportSizeAsync();

        try
        {
            await NotificationService.InitAsync();
            NotificationService.NotificationReceived += HandleVehicleBatchAsync;

            await LoadRoutesAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "TransitMap: Failed to connect to SignalR hub");
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_mapReady && _routesLoaded && !_routesRendered && _map is not null)
        {
            _routesRendered = true;
            await _map.SetCheckpointVisibilityAsync(false);
            await RenderRoutesAsync();
            var settings = SettingsService.GetSettings();
            await _map.SetCheckpointVisibilityAsync(settings.AreCheckpointsVisible);
            await _map.SetVehiclesVisibleAsync(true);
        }
    }

    [JSInvokable]
    public async Task OnCrossingsAsync(CrossingEventDto[] crossings)
    {
        if (!_audioEnabled) return;
        foreach (var crossing in crossings)
        {
            try
            {
                await TransitSynth.TriggerNoteAsync(crossing.RouteId, crossing.VehicleId);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "TransitMap.OnCrossingsAsync: TriggerNoteAsync failed for vehicle {VehicleId} on route {RouteId}", crossing.VehicleId, crossing.RouteId);
            }
        }
    }

    void HandleSettingsEventReceived(object sender, IEventArgs e)
    {
        if (e is AudioSettingChangedEventArgs audio)
        {
            _audioEnabled = audio.IsAudioEnabled;
            return;
        }

        if (e is CheckpointVisibilityChangedEventArgs checkpoint)
        {
            InvokeAsync(async () =>
            {
                if (_map is not null)
                    await _map.SetCheckpointVisibilityAsync(checkpoint.AreCheckpointsVisible);
            });
            return;
        }

        if (e is GisSettingChangedEventArgs gis)
        {
            InvokeAsync(async () =>
            {
                if (_map is null) return;
                var key = gis.IsStreetMapEnabled ? "MapTiler:StyleUrls:LightOn" : "MapTiler:StyleUrls:LightOff";
                var url = Configuration.GetValue<string>(key)
                          ?? Configuration.GetValue<string>("MapTiler:StyleUrl")
                          ?? string.Empty;
                if (string.IsNullOrEmpty(url)) return;

                // Await style.load completion, then re-render routes from cache (no re-fetch).
                var result = await _map.SetBasemapStyleAsync(url);
                await RenderRoutesAsync();

                // Restore checkpoint visibility to match the current setting.
                var settings = SettingsService.GetSettings();
                await _map.SetCheckpointVisibilityAsync(settings.AreCheckpointsVisible);
                await _map.SetVehiclesVisibleAsync(true);
            });
        }
    }

    void OnViewportChanged(Vector2 size)
    {
        bool isMobile = size.X < MinWidth;
        if (isMobile == _isMobile) return;

        _isMobile = isMobile;
        // The callback arrives off a JS interop continuation — marshal back
        // to the renderer's sync context before touching component state.
        InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        RouteFilterViewModel.PropertyChanged -= OnRouteFilterPropertyChanged;
        EventNotificationService.EventReceived -= HandleSettingsEventReceived;
        NotificationService.NotificationReceived -= HandleVehicleBatchAsync;
        _viewportSub?.Dispose();
        await CheckpointTracker.ClearAsync();
        _dotNetRef?.Dispose();
    }

    void OnRouteFilterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(IRouteFilterViewModel.RouteItems) or nameof(IRouteFilterViewModel.HasSelection)))
            return;

        if (!_mapReady || _map is null) return;

        if (RouteFilterViewModel.SelectedRouteId is { } id)
            InvokeAsync(() => _map.FocusRouteAsync(id));
        else
            InvokeAsync(() => _map.ClearRouteFocusAsync());
    }

    async Task OnMapReadyAsync(Map map)
    {
        _map = map;
        _mapReady = true;

        await InvokeAsync(StateHasChanged);

        if (_pendingBatch is not null)
        {
            var batch = _pendingBatch;
            _pendingBatch = null;
            await HandleVehicleBatchAsync(batch);
        }
    }

    async Task RenderRoutesAsync()
    {
        Logger.LogDebug("TransitMap.RenderRoutesAsync: pushing {Count} cached routes to map", _routeShapeCache.Count);

        if (_routeShapeCache.Count == 0)
            Logger.LogWarning("TransitMap.RenderRoutesAsync: route cache is empty — routes will not render");

        foreach (var (routeId, feature) in _routeShapeCache)
        {
            var coordCount = feature.Geometry?.Coordinates?.Length ?? -1;
            Logger.LogDebug("TransitMap.RenderRoutesAsync: rendering routeId={RouteId} CoordCount={CoordCount} Color={Color}",
                routeId, coordCount, feature.Properties?.Color);

            if (feature.Geometry?.Coordinates is null || feature.Geometry.Coordinates.Length == 0)
            {
                Logger.LogWarning("TransitMap.RenderRoutesAsync: skipping routeId={RouteId} — Geometry.Coordinates is null or empty", routeId);
                continue;
            }

            await _map!.AddRouteShapeFeatureAsync(routeId, feature.Geometry.Coordinates, feature.Properties.Color);
            await _map!.LoadRouteGeometryForAnimationAsync(routeId, feature.Geometry.Coordinates);
            await ConfigureTrackerForRouteAsync(routeId, feature);
        }

        Logger.LogDebug("TransitMap.RenderRoutesAsync: route geometry push complete");
    }

    async Task ConfigureTrackerForRouteAsync(string routeId, RouteShapeFeature feature)
    {
        try
        {
            var coords = feature.Geometry.Coordinates;

            // Build cumulative distances mirroring ChefMapAnimator.buildCumulativeDistances
            var cumDist = new double[coords.Length];
            cumDist[0] = 0;
            for (var i = 1; i < coords.Length; i++)
                cumDist[i] = cumDist[i - 1] + HaversineMeters(coords[i - 1], coords[i]);

            var triggerPoints = TriggerPointGenerator.Generate(coords, cumDist);
            Logger.LogDebug("TransitMap: route {RouteId} → {Count} trigger points", routeId, triggerPoints.Count);

            if (_map is not null)
            {
                var jsPoints = triggerPoints.Select(p => (object)new { index = p.Index, alongDistanceM = p.AlongDistanceM }).ToArray();
                await _map.AddTriggerPointMarkersAsync(routeId, jsPoints, coords);
            }

            if (_dotNetRef is not null)
                await CheckpointTracker.ConfigureRouteAsync(routeId, triggerPoints.ToArray(), _dotNetRef);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "TransitMap: failed to configure tracker for route {RouteId}", routeId);
        }
    }

    static double HaversineMeters(double[] p1, double[] p2)
    {
        const double R = 6371000;
        const double toRad = Math.PI / 180;
        var dLat = (p2[1] - p1[1]) * toRad;
        var dLon = (p2[0] - p1[0]) * toRad;
        var lat1 = p1[1] * toRad;
        var lat2 = p2[1] * toRad;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    async Task HandleVehicleBatchAsync(IEnumerable<EventEnvelope> batch)
    {
        if (!_mapReady || _map is null)
        {
            _pendingBatch = batch;
            return;
        }

        // Handle RouteNearestPointBatchEvent — animated path-following
        var nearestPointRecords = batch
            .Where(x => x.Payload is RouteNearestPointBatchEvent)
            .Select(x => (RouteNearestPointBatchEvent)x.Payload)
            .SelectMany(e => e.BatchRecords)
            .ToArray();

        Logger.LogDebug("TransitMap: batch contains {NearestCount} nearest-point records, {PosCount} position events",
            nearestPointRecords.Length,
            batch.Count(x => x.Payload is VehiclePositionUpdatedEvent));

        nearestPointRecords = nearestPointRecords.Where(r => IsAllowedRoute(r.RouteId)).ToArray();

        if (nearestPointRecords.Length > 0)
        {
            var records = nearestPointRecords.Select(r => (object)new
            {
                vehicleId = r.VehicleId,
                routeId = r.RouteId,
                priorLon = r.PriorNearestLon,
                priorLat = r.PriorNearestLat,
                currentLon = r.CurrentNearestLon,
                currentLat = r.CurrentNearestLat,
                durationMs = (r.CurrentUtcNow - r.PriorUtcNow).TotalMilliseconds,
                speed = r.SpeedMetersPerSec,
                bearing = r.Bearing,
                isStale = r.IsStale
            }).ToArray();

            Logger.LogDebug("TransitMap: forwarding {Count} nearest-point records to animator", records.Length);
            await _map.ProcessNearestPointBatchAsync(records);
        }

        // V1 fallback: only plot raw positions when no nearest-point events arrived this batch.
        // If the animator is active, PlotVehiclesAsync clears the datasource and wipes animator-managed features.
        if (nearestPointRecords.Length == 0)
        {
            var payloadBatch = batch
                .Where(x => x.Payload is VehiclePositionUpdatedEvent)
                .Select(x => x.Payload as VehiclePositionUpdatedEvent)
                .Where(x => x?.Trip?.RouteId is { } rid && IsAllowedRoute(rid));

            var featureCollection = new
            {
                type = "FeatureCollection",
                features = payloadBatch
                    .Select(x => new
                    {
                        type = "Feature",
                        id = $"vehicle-{x.Vehicle.Id}",
                        properties = new
                        {
                            vehicleId = x.Vehicle.Id,
                            vehicleName = x.Vehicle.LicensePlate,
                            pinIcon = "stop-pin-green",
                        },
                        geometry = new
                        {
                            type = "Point",
                            coordinates = new[] { x.Position.Longitude, x.Position.Latitude }
                        }
                    }).ToArray(),
            };

            foreach (var payload in payloadBatch)
            {
                if (payload.Position is null) continue;
                if (float.IsNaN(payload.Position.Latitude) || float.IsNaN(payload.Position.Longitude))
                {
                    Logger.LogDebug("TransitMap: Skipping vehicle {VehicleId} — invalid coordinates", payload.Vehicle.Id);
                    continue;
                }

                if (payload.Trip?.RouteId is { } routeId)
                    _vehicleRouteMap[payload.Vehicle.Id] = routeId;
            }

            Logger.LogDebug("TransitMap: no nearest-point records, falling back to V1 plot");
            await _map.PlotVehiclesAsync(featureCollection, true);
        }

        await InvokeAsync(StateHasChanged);
    }

    async Task OnVehicleMarkerClickedAsync((Map Map, string VehicleId) args)
    {
        return;
    }

    async Task OnMapBodyClickedAsync(Map map)
    {
        return;
    }

    async Task LoadRoutesAsync(CancellationToken ct = default)
    {
        Logger.LogDebug("TransitMap.LoadRoutesAsync: fetching all route shapes");
        var res = await GtfsEndpointsService.GetAllRouteShapes(ct);
        if (!res.IsSuccess)
        {
            Logger.LogError("TransitMap.LoadRoutesAsync: GetAllRouteShapes failed — Status={Status} Errors={Errors}",
                res.Status, string.Join("; ", res.Errors));
            return;
        }

        var allFeatures = res.Value?.ToList() ?? [];
        Logger.LogDebug("TransitMap.LoadRoutesAsync: received {Total} features from API", allFeatures.Count);

        _routeShapeCache.Clear();
        var skipped = 0;
        foreach (var routeShapeFeature in allFeatures)
        {
            var key = routeShapeFeature.Properties?.RouteShortName ?? routeShapeFeature.Properties?.RouteId ?? "(null)";
            if (IsAllowedRoute(key))
            {
                _routeShapeCache[key] = routeShapeFeature;
                Logger.LogDebug("TransitMap.LoadRoutesAsync: cached key={Key} RouteId={RouteId} CoordCount={CoordCount} Geometry={GeomNull}",
                    key,
                    routeShapeFeature.Properties?.RouteId,
                    routeShapeFeature.Geometry?.Coordinates?.Length ?? -1,
                    routeShapeFeature.Geometry is null ? "NULL" : "ok");
            }
            else
            {
                skipped++;
            }
        }

        Logger.LogDebug("TransitMap.LoadRoutesAsync: cache populated — {Cached} cached, {Skipped} skipped by IsAllowedRoute",
            _routeShapeCache.Count, skipped);

        _routesLoaded = true;
        await InvokeAsync(StateHasChanged);
    }

    // Returns true for routes that should render and produce audio.
    // Restrict to a subset for focused testing; return true unconditionally for all routes.
    static bool IsAllowedRoute(string routeKey) => true;

    public record CrossingEventDto(string VehicleId, string RouteId, int TriggerIndex);
}
