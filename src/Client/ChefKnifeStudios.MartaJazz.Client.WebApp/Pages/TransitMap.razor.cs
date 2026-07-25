using ChefKnifeStudios.MartaJazz.Client.Core.Services;
using ChefKnifeStudios.MartaJazz.Client.Core.Services.EndpointsServices;
using ChefKnifeStudios.MartaJazz.Client.Shared.Components;
using ChefKnifeStudios.MartaJazz.Client.Shared.Constants;
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
using Microsoft.JSInterop;
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
    [Inject] IJSRuntime JS { get; set; } = null!;

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

    // routeJoinKey → GeoJSON string (client-side cache, lives for page lifetime)
    readonly Dictionary<string, RouteShapeFeature> _routeShapeCache = new(StringComparer.Ordinal);
    bool _routesLoaded;
    bool _routesRendered;

    // routeJoinKey → consecutive batches with no live vehicle. A route's audio Sampler is
    // disposed once it's been absent this many batches in a row (~tolerates brief feed
    // gaps so we don't re-fetch+decode a route that flickers out for one cycle).
    readonly Dictionary<string, int> _routeAbsenceBatches = new(StringComparer.Ordinal);
    const int RouteAudioEvictAfterBatches = 3;

    static readonly Dictionary<string, (double Lat, double Lon)> _cityCenter = new(StringComparer.OrdinalIgnoreCase)
    {
        [CityNames.Marta] = (33.749, -84.388),
        [CityNames.Wmata] = (38.907, -77.037),
        [CityNames.Mbta]  = (42.361, -71.057),
        [CityNames.Nymta] = (40.7580, -73.9855),
        [CityNames.Ttc]   = (43.6532, -79.3832),
        [CityNames.Septa] = (39.9526, -75.1652),
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
        if (string.IsNullOrEmpty(uri.Fragment))
            NavigationManager.NavigateTo(NavigationManager.Uri + "#" + CityNames.Marta, forceLoad: false);

        var settings = SettingsService.GetSettings();
        _audioEnabled = settings.IsAudioEnabled;
        _checkpointsVisible = settings.AreCheckpointsVisible;
        _crossingTrailVisible = settings.IsCrossingTrailVisible;
        _ = TransitSynth.SetAudioEnabledAsync(_audioEnabled);

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
        {
            await OutsideClickJsInterop.AddOutsideClickListenerAsync(
                _accordionElementId, CollapseAccordion);

            // Tag the Umami pageview with the resolved city (from the URL hash). Each city
            // switch does a full reload, so first render fires once per city view.
            try { await JS.InvokeVoidAsync("trackCityView", NavigationManager.ResolveCity()); }
            catch (Exception ex) { Logger.LogWarning(ex, "TransitMap: city view tracking failed"); }
        }

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
            // Seed the dispatcher's live mute flag from the persisted setting (default is true).
            await _map.SetCrossingAudioEnabledAsync(settings.IsAudioEnabled);
        }
    }

    public async Task OnCrossingsAsync(CrossingEventDto[] crossings)
    {
        if (_map is null) return;

        // Capture the route-filter snapshot at batch-receipt time so the gate reflects what
        // was selected when the crossings arrived, not whatever changes during the spread.
        var selected = RouteFilterViewModel.SelectedRouteJoinKeys;
        var hovered = RouteFilterViewModel.HoveredRouteJoinKey;
        var effectiveIds = selected.Count > 0 || hovered is not null
            ? selected.Concat(hovered is not null ? [hovered] : []).ToHashSet(StringComparer.Ordinal)
            : null;

        // Project to the dispatcher's payload shape (camelCase → JS). The server stamps each
        // crossing with AlongDistanceM — the checkpoint's absolute distance along the route; the
        // JS dispatcher asks the animator how long until the dot reaches it and fires each
        // crossing's pulse+trail+note TOGETHER off a single setTimeout. One interop call for the
        // whole batch replaces the old per-crossing Task.Delay + 4-interop fan-out that queued
        // on WASM's single thread and desynced at scale.
        var payload = new List<object>(crossings.Length);
        foreach (var crossing in crossings)
        {
            if (effectiveIds is not null && !effectiveIds.Contains(crossing.RouteJoinKey)) continue;
            payload.Add(new
            {
                routeJoinKey = crossing.RouteJoinKey,
                vehicleId = crossing.VehicleId,
                triggerIndex = crossing.TriggerIndex,
                totalTriggers = crossing.TotalTriggers,
                alongDistanceM = crossing.AlongDistanceM
            });
        }

        if (payload.Count == 0) return;

        // Gating flags captured now; each is honored per-effect in JS (pulse ← checkpoints,
        // trail ← trail setting). Audio is NOT captured here — the dispatcher re-checks a live
        // mute flag at fire time (SetCrossingAudioEnabledAsync), so a crossing scheduled while
        // muted still sounds if the user unmutes before it fires (extended-mute fix).
        var flags = new
        {
            checkpointsVisible = _checkpointsVisible,
            crossingTrailVisible = _crossingTrailVisible
        };

        await _map.DispatchCrossingsAsync(payload, flags);
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
            _ = TransitSynth.SetAudioEnabledAsync(_audioEnabled);
            // Also push the live flag to the crossing dispatcher so crossings already scheduled
            // during a muted window sound the instant they fire once unmuted, rather than waiting
            // out the ~10s scheduling horizon (the extended-mute bug).
            InvokeAsync(async () =>
            {
                if (_map is not null)
                    await _map.SetCrossingAudioEnabledAsync(_audioEnabled);
            });
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
                var settings = SettingsService.GetSettings();
                var shade = settings.IsDarkModeEnabled ? "Dark" : "Light";
                var on    = gis.IsStreetMapEnabled ? "On" : "Off";
                var key   = $"MapTiler:StyleUrls:{shade}{on}";
                var url   = Configuration.GetValue<string>(key)
                            ?? Configuration.GetValue<string>("MapTiler:StyleUrl")
                            ?? string.Empty;
                if (string.IsNullOrEmpty(url)) return;

                // Await style.load completion, then re-render routes from cache (no re-fetch).
                var result = await _map.SetBasemapStyleAsync(url);
                await RenderRoutesAsync();

                // Re-apply focus (hover preview or persistent selection) after the basemap swap resets all layers.
                ApplyMapFocus();

                // Restore checkpoint visibility to match the current setting.
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
                                or nameof(IRouteFilterViewModel.HoveredRouteJoinKey)))
            return;

        if (!_mapReady || _map is null) return;

        ApplyMapFocus();
    }

    void ApplyMapFocus()
    {
        if (_map is null) return;

        var selected = RouteFilterViewModel.SelectedRouteJoinKeys;
        var hovered = RouteFilterViewModel.HoveredRouteJoinKey;

        // Keep the crossing dispatcher's live filter in sync with the current selection ∪ hover so
        // its already-scheduled timers re-check it at fire time (immediate filtering, no ~10s lag).
        // null when nothing is selected/hovered → dispatcher lets all routes through.
        var effectiveIds = hovered is null && selected.Count == 0
            ? null
            : hovered is null || selected.Contains(hovered)
                ? (IEnumerable<string>)selected
                : selected.Concat([hovered]).ToList();
        InvokeAsync(() => _map.SetCrossingFilterAsync(effectiveIds));

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
                routeJoinKey = kvp.Key,
                color = RouteColorFallback.ResolveColor(kvp.Value.Properties?.Color),
                category = kvp.Value.Properties?.Category ?? "bus",
                coordinates = kvp.Value.Geometry!.Coordinates
            })
            .ToArray();

        if (_map is not null)
            await _map.AddAllRoutesAsync(payload, SettingsService.GetSettings().IsDarkModeEnabled);

        Logger.LogDebug("TransitMap.RenderRoutesAsync: route geometry push complete");
    }

    async Task ConfigureAllTrackersAsync()
    {
        await Task.Yield();
        foreach (var (routeJoinKey, feature) in _routeShapeCache)
            await ConfigureTrackerForRouteAsync(routeJoinKey, feature);
        if (_map is not null)
            await _map.FlushTriggerPointsAsync();
    }

    async Task ConfigureTrackerForRouteAsync(string routeJoinKey, RouteShapeFeature feature)
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
            Logger.LogDebug("TransitMap: route {RouteJoinKey} → {Count} trigger points", routeJoinKey, triggerPoints.Count);

            if (_map is not null)
            {
                var jsPoints = triggerPoints.Select(p => (object)new { index = p.Index, alongDistanceM = p.AlongDistanceM }).ToArray();
                await _map.AddTriggerPointMarkersAsync(routeJoinKey, jsPoints, coords);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "TransitMap: failed to configure tracker for route {RouteJoinKey}", routeJoinKey);
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
                activeRoutes.Add(r.RouteJoinKey);
                records.Add(new
                {
                    vehicleId = r.VehicleId,
                    routeJoinKey = r.RouteJoinKey,
                    priorLon = r.PriorNearestLon,
                    priorLat = r.PriorNearestLat,
                    currentLon = r.CurrentNearestLon,
                    currentLat = r.CurrentNearestLat,
                    durationMs = r.DurationMs,
                    speed = r.SpeedMetersPerSec,
                    bearing = r.Bearing,
                    isStale = r.IsStale,
                    category = r.Category
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
            .Select(r => new CrossingEventDto(r.VehicleId, r.RouteJoinKey, r.TriggerIndex, r.TotalTriggers, r.AlongDistanceM))
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
        foreach (var routeJoinKey in activeRoutes)
            _routeAbsenceBatches.Remove(routeJoinKey);

        var stale = false;
        foreach (var routeJoinKey in _routeShapeCache.Keys)
        {
            if (activeRoutes.Contains(routeJoinKey)) continue;
            var count = _routeAbsenceBatches.GetValueOrDefault(routeJoinKey) + 1;
            _routeAbsenceBatches[routeJoinKey] = count;
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
        // Route shapes are fetched ONCE per app lifetime by ApplicationViewModel (kicked off
        // in App.razor, in parallel with the SignalR connect). Await that shared load instead
        // of issuing a second GetAllRouteShapes call — the duplicate fetch doubled the shape
        // payload (140 KB for MARTA, multi-MB for NYMTA) on the startup critical path. The
        // InitializeAsync call is idempotent insurance in case this page ever renders before
        // App.razor's fire-and-forget kick-off.
        _ = ApplicationViewModel.InitializeAsync(ct);
        var loaded = await ApplicationViewModel.RoutesLoadedTask;
        if (!loaded)
        {
            Logger.LogError("TransitMap.LoadRoutesAsync: shared route-shape load failed — routes will not render");
            return;
        }

        _routeShapeCache.Clear();
        foreach (var (key, feature) in ApplicationViewModel.RouteShapes)
            _routeShapeCache[key] = feature;

        Logger.LogDebug("TransitMap.LoadRoutesAsync: cache populated — {Cached} cached",
            _routeShapeCache.Count);

        _routesLoaded = true;
        await InvokeAsync(StateHasChanged);

        _ = TransitSynth.PreloadAsync(_routeShapeCache.Keys);
    }

    public record CrossingEventDto(string VehicleId, string RouteJoinKey, int TriggerIndex, int TotalTriggers, double AlongDistanceM);
}
