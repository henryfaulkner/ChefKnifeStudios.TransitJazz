using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Client.Core.Services.EndpointsServices;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.ViewModels;

public interface IApplicationViewModel : IViewModel
{
    /// <summary>Raised for every batch received from the SignalR hub.</summary>
    event SignalRNotificationHandler? NotificationReceived;

    /// <summary>
    /// Fan an out-of-band batch (e.g. the cold-start REST snapshot) through the same
    /// <see cref="NotificationReceived"/> path the live SignalR stream uses, so every
    /// subscriber (running-count, etc.) sees it uniformly.
    /// </summary>
    Task PublishBatch(List<EventEnvelope> batch);

    /// <summary>routeJoinKey → route shape, loaded once for the app lifetime.</summary>
    IReadOnlyDictionary<string, RouteShapeFeature> RouteShapes { get; }

    bool IsConnected { get; }
    bool RoutesLoaded { get; }

    /// <summary>
    /// Completes when the one-time route-shape load finishes: true when shapes were
    /// cached, false when the load failed. Never faults, so consumers can await it
    /// without a try/catch. Lets pages consume the shared load instead of issuing a
    /// duplicate GetAllRouteShapes fetch on the startup critical path.
    /// </summary>
    Task<bool> RoutesLoadedTask { get; }

    /// <summary>
    /// Connect to SignalR and load route shapes. Idempotent — safe to call multiple
    /// times; the underlying work runs at most once for the app lifetime.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);
}

/// <summary>
/// Application-lifetime singleton store for in-memory state and long-lived processes:
/// owns the SignalR hub connection and the loaded route shapes. Registered as a singleton
/// and initialized as early as possible in app startup.
/// </summary>
public partial class ApplicationViewModel : BaseViewModel, IApplicationViewModel
{
    readonly ISignalRNotificationService _signalRNotificationService;
    readonly IGtfsEndpointsService _gtfsEndpointsService;
    readonly ILogger<ApplicationViewModel> _logger;

    readonly Dictionary<string, RouteShapeFeature> _routeShapes = new(StringComparer.Ordinal);
    readonly SemaphoreSlim _initGate = new(1, 1);
    readonly TaskCompletionSource<bool> _routesLoadedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool _initialized;

    public event SignalRNotificationHandler? NotificationReceived;

    [ObservableProperty]
    bool _isConnected;

    [ObservableProperty]
    bool _routesLoaded;

    public ApplicationViewModel(
        ISignalRNotificationService signalRNotificationService,
        IGtfsEndpointsService gtfsEndpointsService,
        ILogger<ApplicationViewModel> logger)
    {
        _signalRNotificationService = signalRNotificationService;
        _gtfsEndpointsService = gtfsEndpointsService;
        _logger = logger;
    }

    public IReadOnlyDictionary<string, RouteShapeFeature> RouteShapes => _routeShapes;

    public Task<bool> RoutesLoadedTask => _routesLoadedTcs.Task;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await _initGate.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            // The hub connection and the route-shape fetch are independent — run them in
            // PARALLEL so RoutesLoaded (what the route filters render from) no longer waits
            // behind the full SignalR handshake (negotiate + WS upgrade + JoinCity).
            await Task.WhenAll(ConnectSignalRAsync(ct), LoadRoutesAsync(ct));

            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    async Task ConnectSignalRAsync(CancellationToken ct)
    {
        try
        {
            _signalRNotificationService.NotificationReceived += OnNotificationReceived;
            await _signalRNotificationService.InitAsync(ct);
            IsConnected = true;
            _logger.LogInformation("ApplicationViewModel: SignalR connection established");
        }
        catch (Exception ex)
        {
            IsConnected = false;
            _logger.LogError(ex, "ApplicationViewModel: failed to connect to SignalR hub");
        }
    }

    Task OnNotificationReceived(List<EventEnvelope> batch)
        => NotificationReceived?.Invoke(batch) ?? Task.CompletedTask;

    public Task PublishBatch(List<EventEnvelope> batch)
        => NotificationReceived?.Invoke(batch) ?? Task.CompletedTask;

    async Task LoadRoutesAsync(CancellationToken ct)
    {
        _logger.LogDebug("ApplicationViewModel.LoadRoutesAsync: fetching all route shapes");

        try
        {
            var res = await _gtfsEndpointsService.GetAllRouteShapes(ct);
            if (!res.IsSuccess)
            {
                _logger.LogError("ApplicationViewModel.LoadRoutesAsync: GetAllRouteShapes failed — Status={Status} Errors={Errors}",
                    res.Status, string.Join("; ", res.Errors));
                return;
            }

            _routeShapes.Clear();
            foreach (var feature in res.Value ?? [])
            {
                var key = feature.Properties?.JoinKey;
                if (string.IsNullOrEmpty(key))
                {
                    _logger.LogWarning("ApplicationViewModel.LoadRoutesAsync: skipping feature with no RouteShortName or RouteId to derive a join key from");
                    continue;
                }

                _routeShapes[key] = feature;
            }

            _logger.LogDebug("ApplicationViewModel.LoadRoutesAsync: cached {Count} route shapes", _routeShapes.Count);
            RoutesLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApplicationViewModel.LoadRoutesAsync: route-shape load threw");
        }
        finally
        {
            // RoutesLoadedTask must ALWAYS complete (success or failure) — TransitMap awaits
            // it instead of fetching shapes itself, and a faulted/never-completing task there
            // would leave the map without routes forever.
            _routesLoadedTcs.TrySetResult(RoutesLoaded);
        }
    }
}
