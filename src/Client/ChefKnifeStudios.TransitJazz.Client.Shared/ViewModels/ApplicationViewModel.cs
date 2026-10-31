using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Client.Core.Services.EndpointsServices;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
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
    RouteLoadOutcome RouteLoadOutcome { get; }
    LiveDeliveryState LiveDeliveryState { get; }
    NotificationScope NotificationScope { get; }
    NotificationIssueCode? LiveIssueCode { get; }
    NotificationIssueCode? LiveProcessingIssueCode { get; }

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

public enum RouteLoadStatus
{
    Pending,
    Usable,
    Unavailable,
    Cancelled
}

public enum RouteLoadFailureKind
{
    None,
    RequestFailed,
    NoUsableCurrentCityData
}

public sealed record RouteLoadOutcome(
    RouteLoadStatus Status,
    int UsableRouteCount,
    RouteLoadFailureKind FailureKind = RouteLoadFailureKind.None)
{
    public bool IsUsable => Status == RouteLoadStatus.Usable && UsableRouteCount > 0;
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
    readonly NavigationManager _navigationManager;

    readonly Dictionary<string, RouteShapeFeature> _routeShapes = new(StringComparer.Ordinal);
    readonly SemaphoreSlim _initGate = new(1, 1);
    readonly TaskCompletionSource<bool> _routesLoadedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool _initialized;
    bool _hasConfirmedLiveReadiness;
    long _liveProcessingGeneration;
    NotificationScope _notificationScope;

    public event SignalRNotificationHandler? NotificationReceived;

    [ObservableProperty]
    bool _isConnected;

    [ObservableProperty]
    bool _routesLoaded;

    [ObservableProperty]
    RouteLoadOutcome _routeLoadOutcome = new(RouteLoadStatus.Pending, 0);

    [ObservableProperty]
    LiveDeliveryState _liveDeliveryState = new(false, true, false, string.Empty, 0, null);

    [ObservableProperty]
    NotificationIssueCode? _liveIssueCode;

    [ObservableProperty]
    NotificationIssueCode? _liveProcessingIssueCode;

    readonly NotificationCoordinator? _notificationCoordinator;

    public ApplicationViewModel(
        ISignalRNotificationService signalRNotificationService,
        IGtfsEndpointsService gtfsEndpointsService,
        ILogger<ApplicationViewModel> logger,
        NavigationManager navigationManager,
        NotificationCoordinator? notificationCoordinator = null)
    {
        _signalRNotificationService = signalRNotificationService;
        _gtfsEndpointsService = gtfsEndpointsService;
        _logger = logger;
        _navigationManager = navigationManager;
        _notificationCoordinator = notificationCoordinator;
    }

    public IReadOnlyDictionary<string, RouteShapeFeature> RouteShapes => _routeShapes;
    public NotificationScope NotificationScope => _notificationScope;

    public Task<bool> RoutesLoadedTask => _routesLoadedTcs.Task;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await _initGate.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var city = _navigationManager.ResolveCity();
            _notificationScope = new NotificationScope(1, city.ToLowerInvariant());
            _notificationCoordinator?.SetScope(_notificationScope);

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
            _signalRNotificationService.DeliveryStateChanged += OnDeliveryStateChanged;
            await _signalRNotificationService.InitAsync(ct);
            OnDeliveryStateChanged(_signalRNotificationService.DeliveryState);
            if (LiveDeliveryState.IsReady)
            {
                _logger.LogInformation("ApplicationViewModel: SignalR connection established");
            }
            else
            {
                _logger.LogWarning("ApplicationViewModel: SignalR delivery is not ready ({FailureReason})",
                    LiveDeliveryState.FailureReason);
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            _logger.LogError(ex, "ApplicationViewModel: failed to connect to SignalR hub");
        }
    }

    void OnDeliveryStateChanged(LiveDeliveryState state)
    {
        LiveDeliveryState = state;
        IsConnected = state.IsReady;
        _notificationCoordinator?.SetDeliveryDesired(state.DeliveryDesired);

        if (_notificationCoordinator is null)
        {
            return;
        }

        if (state.IsReady)
        {
            _hasConfirmedLiveReadiness = true;
            LiveIssueCode = null;
            _ = _notificationCoordinator.ResolveAsync(NotificationCapability.Live);
            return;
        }

        if (!state.DeliveryDesired)
        {
            LiveIssueCode = null;
            return;
        }

        var code = state.FailureReason switch
        {
            LiveDeliveryFailureReason.ConnectionClosed => NotificationIssueCode.LiveStopped,
            null when !state.IsReady && _hasConfirmedLiveReadiness => NotificationIssueCode.LiveInterrupted,
            LiveDeliveryFailureReason reason => MapInitialLiveFailure(reason),
            _ => null
        };

        if (code is not NotificationIssueCode issueCode)
        {
            LiveIssueCode = null;
            return;
        }

        LiveIssueCode = issueCode;
        _notificationCoordinator.Report(new NotificationIssue(
            issueCode,
            _notificationScope,
            state.ConnectionGeneration,
            IsObservedNetworkFailure: issueCode == NotificationIssueCode.LiveInterrupted));
    }

    static NotificationIssueCode? MapInitialLiveFailure(LiveDeliveryFailureReason reason) => reason switch
    {
        LiveDeliveryFailureReason.MissingConfiguration or
        LiveDeliveryFailureReason.InvalidConfiguration or
        LiveDeliveryFailureReason.StartFailed or
        LiveDeliveryFailureReason.OperationTimedOut => NotificationIssueCode.LiveConnectionFailed,
        LiveDeliveryFailureReason.CityJoinFailed => NotificationIssueCode.LiveJoinFailed,
        LiveDeliveryFailureReason.ResumeFailed => NotificationIssueCode.LiveResumeFailed,
        LiveDeliveryFailureReason.ProtocolRejected => NotificationIssueCode.LiveVersionRejected,
        _ => null
    };

    Task OnNotificationReceived(List<EventEnvelope> batch) => ProcessBatchAsync(batch);

    public Task PublishBatch(List<EventEnvelope> batch) => ProcessBatchAsync(batch);

    async Task ProcessBatchAsync(List<EventEnvelope> batch)
    {
        var generation = Interlocked.Increment(ref _liveProcessingGeneration);
        var hasValidTransitWork = IsWellFormedTransitBatch(batch);

        try
        {
            await InvokeNotificationHandlersAsync(batch).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApplicationViewModel: live batch processing failed");
            var delivery = LiveDeliveryState;
            if (hasValidTransitWork
                && delivery.DeliveryDesired
                && delivery.IsReady
                && generation == Volatile.Read(ref _liveProcessingGeneration))
            {
                LiveProcessingIssueCode = NotificationIssueCode.LiveProcessingFailed;
                _notificationCoordinator?.Report(new NotificationIssue(
                    NotificationIssueCode.LiveProcessingFailed,
                    _notificationScope,
                    generation,
                    ConfirmedCause: GetProcessingRootCause()));
            }

            return;
        }

        // A quiet/empty feed is not evidence that processing recovered. Only a
        // successfully completed, well-formed transit batch resolves this issue.
        if (hasValidTransitWork && generation == Volatile.Read(ref _liveProcessingGeneration))
        {
            var hadProcessingFailure = LiveProcessingIssueCode == NotificationIssueCode.LiveProcessingFailed;
            LiveProcessingIssueCode = null;
            if (hadProcessingFailure && _notificationCoordinator is not null)
            {
                await _notificationCoordinator.ResolveIssueAsync(
                    NotificationIssueCode.LiveProcessingFailed,
                    _notificationScope,
                    generation).ConfigureAwait(false);
            }
        }
    }

    async Task InvokeNotificationHandlersAsync(List<EventEnvelope> batch)
    {
        var handlers = NotificationReceived;
        if (handlers is null) return;

        var tasks = new List<Task>();
        var failures = new List<Exception>();
        foreach (var callback in handlers.GetInvocationList())
        {
            try
            {
                var task = ((SignalRNotificationHandler)callback)(batch);
                if (task is null)
                    failures.Add(new InvalidOperationException("A live batch handler returned no task."));
                else
                    tasks.Add(task);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        if (tasks.Count > 0)
        {
            var all = Task.WhenAll(tasks);
            try
            {
                await all.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (all.Exception is { } aggregate)
                    failures.AddRange(aggregate.Flatten().InnerExceptions);
                else
                    failures.Add(ex);
            }
        }

        if (failures.Count > 0)
            throw new AggregateException("One or more live batch handlers failed.", failures);
    }

    NotificationIssueCode? GetProcessingRootCause()
    {
        if (LiveIssueCode is NotificationIssueCode liveIssue)
        {
            return liveIssue;
        }

        return RouteLoadOutcome.FailureKind switch
        {
            RouteLoadFailureKind.RequestFailed => NotificationIssueCode.RouteLoadFailed,
            RouteLoadFailureKind.NoUsableCurrentCityData => NotificationIssueCode.RoutesUnavailable,
            _ => null
        };
    }

    static bool IsWellFormedTransitBatch(List<EventEnvelope> batch)
    {
        var transitEvents = batch
            .Select(envelope => envelope.Payload)
            .OfType<RouteNearestPointBatchEvent>()
            .ToArray();
        if (transitEvents.Length == 0)
        {
            return false;
        }

        foreach (var transitEvent in transitEvents)
        {
            var records = transitEvent.BatchRecords?.ToArray();
            if (records is null || records.Length == 0)
            {
                return false;
            }

            foreach (var record in records)
            {
                if (record is null
                    || string.IsNullOrWhiteSpace(record.VehicleId)
                    || string.IsNullOrWhiteSpace(record.RouteJoinKey)
                    || record.CurrentNearestLatE5 is < -9_000_000 or > 9_000_000
                    || record.CurrentNearestLonE5 is < -18_000_000 or > 18_000_000
                    || (record.PriorNearestLatE5 is null) != (record.PriorNearestLonE5 is null)
                    || record.PriorNearestLatE5 is < -9_000_000 or > 9_000_000
                    || record.PriorNearestLonE5 is < -18_000_000 or > 18_000_000
                    || record.DurationMs < 0
                    || record.SpeedMetersPerSec is float speed && (!float.IsFinite(speed) || speed < 0)
                    || record.Bearing is float bearing && (!float.IsFinite(bearing) || bearing is < 0 or > 360))
                {
                    return false;
                }
            }
        }

        return true;
    }

    async Task LoadRoutesAsync(CancellationToken ct)
    {
        _logger.LogDebug("ApplicationViewModel.LoadRoutesAsync: fetching all route shapes");
        RouteLoadOutcome = new(RouteLoadStatus.Pending, 0);
        RoutesLoaded = false;

        try
        {
            var res = await _gtfsEndpointsService.GetAllRouteShapes(ct);
            if (!res.IsSuccess)
            {
                SetRouteFailure(RouteLoadFailureKind.RequestFailed, NotificationIssueCode.RouteLoadFailed);
                _logger.LogError("ApplicationViewModel.LoadRoutesAsync: GetAllRouteShapes failed — Status={Status}",
                    res.Status);
                return;
            }

            var city = _navigationManager.ResolveCity();
            _routeShapes.Clear();
            foreach (var feature in res.Value ?? [])
            {
                if (!GtfsEndpointsService.IsUsableRouteShape(feature, city))
                {
                    continue;
                }

                _routeShapes[feature.Properties.JoinKey] = feature;
            }

            _logger.LogDebug("ApplicationViewModel.LoadRoutesAsync: cached {Count} route shapes", _routeShapes.Count);
            if (_routeShapes.Count == 0)
            {
                SetRouteFailure(RouteLoadFailureKind.NoUsableCurrentCityData, NotificationIssueCode.RoutesUnavailable);
                return;
            }

            RouteLoadOutcome = new(RouteLoadStatus.Usable, _routeShapes.Count);
            RoutesLoaded = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            RouteLoadOutcome = new(RouteLoadStatus.Cancelled, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApplicationViewModel.LoadRoutesAsync: route-shape load threw");
            SetRouteFailure(RouteLoadFailureKind.RequestFailed, NotificationIssueCode.RouteLoadFailed);
        }
        finally
        {
            // RoutesLoadedTask must ALWAYS complete (success or failure) — TransitMap awaits
            // it instead of fetching shapes itself, and a faulted/never-completing task there
            // would leave the map without routes forever.
            _routesLoadedTcs.TrySetResult(RouteLoadOutcome.IsUsable);
        }
    }

    void SetRouteFailure(RouteLoadFailureKind failureKind, NotificationIssueCode issueCode)
    {
        if (_routeShapes.Count > 0)
        {
            RouteLoadOutcome = new(RouteLoadStatus.Usable, _routeShapes.Count);
            RoutesLoaded = true;
            return;
        }

        RouteLoadOutcome = new(RouteLoadStatus.Unavailable, 0, failureKind);
        _notificationCoordinator?.Report(new NotificationIssue(issueCode, _notificationScope, 1));
    }
}
