using ChefKnifeStudios.MartaJazz.Client.Core.Services;
using ChefKnifeStudios.MartaJazz.Client.Core.Services.EndpointsServices;
using ChefKnifeStudios.MartaJazz.Client.Shared.Components;
using ChefKnifeStudios.MartaJazz.Client.Shared.EventArgs;
using ChefKnifeStudios.MartaJazz.Client.Shared.Models;
using ChefKnifeStudios.MartaJazz.Client.Shared.Services;
using ChefKnifeStudios.MartaJazz.Shared.Models;
using ChefKnifeStudios.MartaJazz.Shared.Services;
using ChefKnifeStudios.MartaJazz.Client.Shared.Services.JsInterop;
using ChefKnifeStudios.MartaJazz.Client.Shared.ViewModels;
using ChefKnifeStudios.MartaJazz.Shared;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using ChefKnifeStudios.MartaJazz.Shared.Geospatial;
using ChefKnifeStudios.MartaJazz.Shared.GtfsData;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
    [Inject] ITransitSynthJsInterop TransitSynth { get; set; } = null!;
    [Inject] IRouteFilterViewModel RouteFilterViewModel { get; set; } = null!;
    [Inject] IApplicationViewModel ApplicationViewModel { get; set; } = null!;
    [Inject] IEventNotificationService EventNotificationService { get; set; } = null!;
    [Inject] ISettingsService SettingsService { get; set; } = null!;
    [Inject] IViewportSizeJsInterop ViewportSize { get; set; } = null!;
    [Inject] ITransitEndpointsService TransitEndpointsService { get; set; } = null!;
    [Inject] IOutsideClickJsInterop OutsideClickJsInterop { get; set; } = null!;
    [Inject] NavigationManager NavigationManager { get; set; } = null!;

    const float MinWidth = 1100;

    readonly string _accordionElementId = $"route-accordion-{Guid.NewGuid()}";
    bool _accordionExpanded;

    IDisposable? _viewportSub;
    bool _isMobile;
    SignalRNotificationHandler? _batchHandler;

    Map? _map;
    bool _mapReady;
    // Batches that arrive before the map is ready (the initial REST snapshot and any
    // SignalR batches that beat OnMapReadyAsync). Accumulate rather than overwrite — a
    // single slot let a small SignalR batch clobber the full initial snapshot, so the
    // snapshot never painted and buses only appeared from the next live batch.
    readonly List<IEnumerable<EventEnvelope>> _pendingBatches = new();

    bool _audioEnabled = true;
    bool _audioUnlocked = false;
    bool _checkpointsVisible = false;
    bool _crossingTrailVisible = true;

    // routeId → GeoJSON string (client-side cache, lives for page lifetime)
    readonly Dictionary<string, RouteShapeFeature> _routeShapeCache = new(StringComparer.Ordinal);
    bool _routesLoaded;
    bool _routesRendered;

    // routeId → consecutive batches with no live vehicle. A route's audio Sampler is
    // disposed once it's been absent this many batches in a row (~tolerates brief feed
    // gaps so we don't re-fetch+decode a route that flickers out for one cycle).
    readonly Dictionary<string, int> _routeAbsenceBatches = new(StringComparer.Ordinal);
    const int RouteAudioEvictAfterBatches = 3;

    static readonly Dictionary<string, (double Lat, double Lon)> _cityCenter = new(StringComparer.OrdinalIgnoreCase)
    {
        [CityNames.Marta] = (33.749, -84.388),
        [CityNames.Wmata] = (38.907, -77.037),
    };

    CameraOptions DefaultCameraOptions
    {
        get
        {
            var city = NavigationManager.ResolveCity();
            var (lat, lon) = _cityCenter.TryGetValue(city, out var c) ? c : _cityCenter[CityNames.Marta];
            return new() { Center = new Position(lat, lon), Zoom = _isMobile ? 6 : 9.5 };
        }
    }

    protected override async Task OnInitializedAsync()
    {
        var uri = new Uri(NavigationManager.Uri);

        // NO HASH SEGMENT BY DEFAULT
        //if (string.IsNullOrEmpty(uri.Fragment))
        //    NavigationManager.NavigateTo(NavigationManager.Uri + "#" + CityNames.Marta, forceLoad: false);

        var settings = SettingsService.GetSettings();
        _audioEnabled = settings.IsAudioEnabled;
        _checkpointsVisible = settings.AreCheckpointsVisible;
        _crossingTrailVisible = settings.IsCrossingTrailVisible;

        RouteFilterViewModel.PropertyChanged += OnRouteFilterPropertyChanged;
        EventNotificationService.EventReceived += HandleSettingsEventReceived;

        // Subscribe FIRST so the immediate initial fire reaches us, then register.
        _viewportSub = ViewportSize.AddViewportSizeChangeCallback(OnViewportChanged);
        await ViewportSize.RegisterViewportSizeAsync();

        _batchHandler = batch => InvokeAsync(() => HandleVehicleBatchAsync(batch));
        NotificationService.NotificationReceived += _batchHandler;

        // NOTE: the cold-start vehicle snapshot is delivered exactly once, by the SignalR
        // JoinCity replay (TransitHub replays LastBatchCache.Current on connect). We do NOT
        // also fetch it over REST here — doing both delivered the identical batch twice,
        // re-anchoring every vehicle's animation back to its start position and re-firing
        // checkpoint crossings (the "rapid pulsing" on load). The SignalR replay also feeds
        // the running count via ApplicationViewModel.NotificationReceived.
        try
        {
            await LoadRoutesAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "TransitMap: Failed to load routes");
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            await OutsideClickJsInterop.AddOutsideClickListenerAsync(
                _accordionElementId, CollapseAccordion);

        if (_mapReady && _routesLoaded && !_routesRendered && _map is not null)
        {
            _routesRendered = true;
            await _map.SetCheckpointVisibilityAsync(false);
            await RenderRoutesAsync();
            _ = ConfigureAllTrackersAsync();
            var settings = SettingsService.GetSettings();
            await _map.SetCheckpointVisibilityAsync(settings.AreCheckpointsVisible);
            await _map.SetCrossingTrailVisibilityAsync(settings.IsCrossingTrailVisible);
            await _map.SetAllCheckpointsVisibilityAsync(settings.AreAllCheckpointsVisible);
            await _map.SetVehiclesVisibleAsync(settings.IsBusesVisible);
        }
    }

    public Task OnCrossingsAsync(CrossingEventDto[] crossings)
    {
        // Capture the route-filter snapshot at batch-receipt time so the gate reflects what
        // was selected when the crossings arrived, not whatever changes during the spread.
        var selected = RouteFilterViewModel.SelectedRouteIds;
        var hovered = RouteFilterViewModel.HoveredRouteId;
        var effectiveIds = selected.Count > 0 || hovered is not null
            ? selected.Concat(hovered is not null ? [hovered] : []).ToHashSet(StringComparer.Ordinal)
            : null;

        foreach (var crossing in crossings)
        {
            if (effectiveIds is not null && !effectiveIds.Contains(crossing.RouteId)) continue;

            // Server stamps each crossing with where in the cycle window it was actually
            // crossed (OffsetMs). Fire each on its own delayed continuation so a burst of
            // checkpoints spreads out in true crossing order instead of sounding all at once.
            // Fire-and-forget: we must not block the SignalR batch handler for ~8s.
            var c = crossing;
            _ = FireCrossingDelayedAsync(c);
        }

        return Task.CompletedTask;
    }

    async Task FireCrossingDelayedAsync(CrossingEventDto crossing)
    {
        try
        {
            if (crossing.OffsetMs > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(crossing.OffsetMs));

            // Re-check live settings at fire time (a setting may have flipped mid-spread).
            // Pulse (expanding ring) is gated on its own checkpoint-visibility setting.
            if (_checkpointsVisible && _map is not null)
            {
                try { await _map.PulseCheckpointAsync(crossing.RouteId, crossing.TriggerIndex); }
                catch (Exception ex) { Logger.LogWarning(ex, "PulseCheckpointAsync failed for {RouteId}/{Idx}", crossing.RouteId, crossing.TriggerIndex); }
            }

            // Trail is gated on its own setting, independent of the pulse AND of audio
            // mute/lock state (FR-001).
            if (_crossingTrailVisible && _map is not null)
            {
                try
                {
                    var durationSec = await TransitSynth.DurationSecondsForAsync(crossing.VehicleId, crossing.RouteId);
                    await _map.StartCrossingTrailAsync(crossing.RouteId, crossing.VehicleId, crossing.TriggerIndex, durationSec);
                }
                catch (Exception ex) { Logger.LogWarning(ex, "StartCrossingTrailAsync failed for {RouteId}/{Idx}", crossing.RouteId, crossing.TriggerIndex); }
            }

            if (_audioEnabled)
            {
                try { await TransitSynth.TriggerNoteAsync(crossing.RouteId, crossing.VehicleId, crossing.TriggerIndex, crossing.TotalTriggers); }
                catch (Exception ex) { Logger.LogWarning(ex, "TransitMap.OnCrossingsAsync: TriggerNoteAsync failed for vehicle {VehicleId} on route {RouteId}", crossing.VehicleId, crossing.RouteId); }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "TransitMap.FireCrossingDelayedAsync: failed for vehicle {VehicleId} on route {RouteId}", crossing.VehicleId, crossing.RouteId);
        }
    }

    // Unlocks the Web Audio context from a real user gesture (required by mobile
    // browser autoplay policies — Tone.start() is a no-op outside a gesture stack),
    // then dismisses the overlay. After this one tap, every later crossing plays
    // without further interaction.
    async Task EnableAudioAsync()
    {
        try
        {
            await TransitSynth.UnlockAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "TransitMap.EnableAudioAsync: failed to unlock audio context");
        }
        finally
        {
            _audioUnlocked = true;
            StateHasChanged();
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
            _checkpointsVisible = checkpoint.AreCheckpointsVisible;
            InvokeAsync(async () =>
            {
                if (_map is not null)
                    await _map.SetCheckpointVisibilityAsync(checkpoint.AreCheckpointsVisible);
            });
            return;
        }

        if (e is CrossingTrailVisibilityChangedEventArgs trail)
        {
            _crossingTrailVisible = trail.IsCrossingTrailVisible;
            InvokeAsync(async () =>
            {
                if (_map is not null)
                    // false → clears active trails immediately (FR-006).
                    await _map.SetCrossingTrailVisibilityAsync(trail.IsCrossingTrailVisible);
            });
            return;
        }

        if (e is BusVisibilitySettingChangedEventArgs buses)
        {
            InvokeAsync(async () =>
            {
                if (_map is not null)
                    await _map.SetVehiclesVisibleAsync(buses.IsBusesVisible);
            });
            return;
        }

        if (e is AllCheckpointsVisibilityChangedEventArgs allCheckpoints)
        {
            InvokeAsync(async () =>
            {
                if (_map is not null)
                    await _map.SetAllCheckpointsVisibilityAsync(allCheckpoints.AreAllCheckpointsVisible);
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

                // Re-apply focus (hover preview or persistent selection) after the basemap swap resets all layers.
                ApplyMapFocus();

                // Restore checkpoint visibility to match the current setting.
                var settings = SettingsService.GetSettings();
                await _map.SetCheckpointVisibilityAsync(settings.AreCheckpointsVisible);
                await _map.SetCrossingTrailVisibilityAsync(settings.IsCrossingTrailVisible);
                await _map.SetAllCheckpointsVisibilityAsync(settings.AreAllCheckpointsVisible);
                await _map.SetVehiclesVisibleAsync(settings.IsBusesVisible);
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

    void CollapseAccordion()
    {
        _accordionExpanded = false;
        InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        RouteFilterViewModel.PropertyChanged -= OnRouteFilterPropertyChanged;
        EventNotificationService.EventReceived -= HandleSettingsEventReceived;
        if (_batchHandler != null)
            NotificationService.NotificationReceived -= _batchHandler;
        _viewportSub?.Dispose();
        await OutsideClickJsInterop.RemoveOutsideClickListenerAsync(_accordionElementId);
    }

    void OnRouteFilterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(IRouteFilterViewModel.RouteItems)
                                or nameof(IRouteFilterViewModel.HasSelection)
                                or nameof(IRouteFilterViewModel.HoveredRouteId)))
            return;

        if (!_mapReady || _map is null) return;

        ApplyMapFocus();
    }

    void ApplyMapFocus()
    {
        if (_map is null) return;

        var selected = RouteFilterViewModel.SelectedRouteIds;
        var hovered = RouteFilterViewModel.HoveredRouteId;

        if (hovered is null && selected.Count == 0)
        {
            InvokeAsync(() => _map.ClearRouteFocusAsync());
            return;
        }

        // Emphasize the union of persistently selected routes and the hovered route.
        var focusSet = hovered is null
            ? selected
            : selected.Contains(hovered)
                ? selected
                : selected.Concat([hovered]).ToList();

        InvokeAsync(() => _map.FocusRoutesAsync(focusSet));
    }

    async Task OnMapReadyAsync(Map map)
    {
        _map = map;
        _mapReady = true;

        await InvokeAsync(StateHasChanged);

        if (_pendingBatches.Count > 0)
        {
            var batches = _pendingBatches.ToArray();
            _pendingBatches.Clear();
            Logger.LogInformation("TransitMap.OnMapReadyAsync: replaying {Count} pending batch(es)", batches.Length);
            foreach (var batch in batches)
                await HandleVehicleBatchAsync(batch);
        }
        else
        {
            Logger.LogInformation("TransitMap.OnMapReadyAsync: no pending batch to replay");
        }
    }

    async Task RenderRoutesAsync()
    {
        Logger.LogDebug("TransitMap.RenderRoutesAsync: pushing {Count} cached routes to map", _routeShapeCache.Count);

        if (_routeShapeCache.Count == 0)
        {
            Logger.LogWarning("TransitMap.RenderRoutesAsync: route cache is empty — routes will not render");
            return;
        }

        var payload = _routeShapeCache
            .Where(kvp => kvp.Value.Geometry?.Coordinates is { Length: > 0 })
            .Select(kvp => (object)new
            {
                routeId = kvp.Key,
                color = kvp.Value.Properties?.Color ?? "#6b7280",
                coordinates = kvp.Value.Geometry!.Coordinates
            })
            .ToArray();

        if (_map is not null)
            await _map.AddAllRoutesAsync(payload);

        Logger.LogDebug("TransitMap.RenderRoutesAsync: route geometry push complete");
    }

    async Task ConfigureAllTrackersAsync()
    {
        await Task.Yield();
        foreach (var (routeId, feature) in _routeShapeCache)
            await ConfigureTrackerForRouteAsync(routeId, feature);
        if (_map is not null)
            await _map.FlushTriggerPointsAsync();
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
                cumDist[i] = cumDist[i - 1] + HaversineCalculator.DistanceMeters(coords[i - 1][1], coords[i - 1][0], coords[i][1], coords[i][0]);

            var triggerPoints = TriggerPointGenerator.Generate(coords, cumDist);
            Logger.LogDebug("TransitMap: route {RouteId} → {Count} trigger points", routeId, triggerPoints.Count);

            if (_map is not null)
            {
                var jsPoints = triggerPoints.Select(p => (object)new { index = p.Index, alongDistanceM = p.AlongDistanceM }).ToArray();
                await _map.AddTriggerPointMarkersAsync(routeId, jsPoints, coords);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "TransitMap: failed to configure tracker for route {RouteId}", routeId);
        }
    }

    async Task HandleVehicleBatchAsync(IEnumerable<EventEnvelope> batch)
    {
        if (!_mapReady || _map is null)
        {
            // Bound the buffer: if the map never readies these would grow unbounded,
            // each holding a full EventEnvelope list. Keep the oldest (the initial REST
            // snapshot, added first) plus the most recent live batches.
            const int MaxPendingBatches = 8;
            if (_pendingBatches.Count >= MaxPendingBatches)
                _pendingBatches.RemoveAt(1);
            _pendingBatches.Add(batch);
            return;
        }

        // Handle RouteNearestPointBatchEvent — animated path-following. Build the JS
        // payload in a single pass (per-batch, every 10s) rather than materializing an
        // intermediate record array then projecting again — transient .NET allocation
        // churn is the lever that raises the never-returned WASM heap high-water.
        var records = new List<object>();
        var activeRoutes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var envelope in batch)
        {
            if (envelope.Payload is not RouteNearestPointBatchEvent e) continue;
            foreach (var r in e.BatchRecords)
            {
                activeRoutes.Add(r.RouteId);
                records.Add(new
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
                    isStale = r.IsStale,
                    transitMode = r.TransitMode.ToString().ToLowerInvariant()
                });
            }
        }

        if (records.Count > 0)
        {
            await _map.ProcessNearestPointBatchAsync(records);
            await EvictInactiveRouteAudioAsync(activeRoutes);
        }

        // Handle RouteCrossingBatchEvent — server-authoritative checkpoint crossings
        var crossings = batch
            .Select(e => e.Payload)
            .OfType<RouteCrossingBatchEvent>()
            .SelectMany(e => e.BatchRecords)
            .Select(r => new CrossingEventDto(r.VehicleId, r.RouteId, r.TriggerIndex, r.TotalTriggers, r.OffsetMs))
            .ToArray();

        if (crossings.Length > 0)
            await OnCrossingsAsync(crossings);

        StateHasChanged();
    }

    // Frees audio Samplers for routes whose vehicles have left the feed, so decoded PCM
    // doesn't accumulate over a long session. A route must be absent for several batches
    // in a row before eviction, so a one-cycle feed gap doesn't force a re-fetch+decode.
    async Task EvictInactiveRouteAudioAsync(HashSet<string> activeRoutes)
    {
        foreach (var routeId in activeRoutes)
            _routeAbsenceBatches.Remove(routeId);

        var stale = false;
        foreach (var routeId in _routeShapeCache.Keys)
        {
            if (activeRoutes.Contains(routeId)) continue;
            var count = _routeAbsenceBatches.GetValueOrDefault(routeId) + 1;
            _routeAbsenceBatches[routeId] = count;
            if (count >= RouteAudioEvictAfterBatches) stale = true;
        }

        if (stale)
            await TransitSynth.DisposeInactiveRoutesAsync(activeRoutes);
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
        foreach (var routeShapeFeature in allFeatures)
        {
            var key = routeShapeFeature.Properties?.RouteShortName ?? routeShapeFeature.Properties?.RouteId ?? "(null)";
            _routeShapeCache[key] = routeShapeFeature;
            Logger.LogDebug("TransitMap.LoadRoutesAsync: cached key={Key} RouteId={RouteId} CoordCount={CoordCount} Geometry={GeomNull}",
                key,
                routeShapeFeature.Properties?.RouteId,
                routeShapeFeature.Geometry?.Coordinates?.Length ?? -1,
                routeShapeFeature.Geometry is null ? "NULL" : "ok");
        }

        Logger.LogDebug("TransitMap.LoadRoutesAsync: cache populated — {Cached} cached",
            _routeShapeCache.Count);

        _routesLoaded = true;
        await InvokeAsync(StateHasChanged);

        _ = TransitSynth.PreloadAsync(_routeShapeCache.Keys);
    }

    public record CrossingEventDto(string VehicleId, string RouteId, int TriggerIndex, int TotalTriggers, double OffsetMs);
}
