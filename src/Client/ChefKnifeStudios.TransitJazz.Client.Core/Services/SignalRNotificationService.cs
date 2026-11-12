#nullable enable
using ChefKnifeStudios.TransitJazz.Client.Core.Enums;
using ChefKnifeStudios.TransitJazz.Client.Core.ClientExperience;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.ClientExperience;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.TransitJazz.Client.Core.Services;

public delegate Task SignalRNotificationHandler(List<EventEnvelope> batch);

public interface ISignalRNotificationService
{
    event SignalRNotificationHandler? NotificationReceived;
    event Action<LiveDeliveryState>? DeliveryStateChanged;
    LiveDeliveryState DeliveryState { get; }
    Task InitAsync(CancellationToken ct = default);
}

/// <summary>The narrow SignalR boundary used to drive production lifecycle handlers in tests.</summary>
public interface ILiveDeliveryConnection : IAsyncDisposable
{
    HubConnectionState State { get; }
    event Func<Exception?, Task>? Reconnecting;
    event Func<string?, Task>? Reconnected;
    event Func<Exception?, Task>? Closed;
    IDisposable OnNotificationReceived(SignalRNotificationHandler handler);
    Task StartAsync(CancellationToken cancellationToken);
    Task JoinCityAsync(string city, CancellationToken cancellationToken);
    Task LeaveCityAsync(string city, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface ILiveDeliveryConnectionFactory
{
    ILiveDeliveryConnection Create(Uri hubUri);
}

public sealed class SignalRLiveDeliveryConnectionFactory : ILiveDeliveryConnectionFactory
{
    public ILiveDeliveryConnection Create(Uri hubUri)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUri)
            .WithAutomaticReconnect()
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Debug))
            .AddMessagePackProtocol()
            .Build();

        return new HubLiveDeliveryConnection(connection);
    }

    sealed class HubLiveDeliveryConnection(HubConnection connection) : ILiveDeliveryConnection
    {
        public HubConnectionState State => connection.State;

        public event Func<Exception?, Task>? Reconnecting
        {
            add => connection.Reconnecting += value;
            remove => connection.Reconnecting -= value;
        }

        public event Func<string?, Task>? Reconnected
        {
            add => connection.Reconnected += value;
            remove => connection.Reconnected -= value;
        }

        public event Func<Exception?, Task>? Closed
        {
            add => connection.Closed += value;
            remove => connection.Closed -= value;
        }

        public IDisposable OnNotificationReceived(SignalRNotificationHandler handler)
            => connection.On<List<EventEnvelope>>(HubMethods.ReceiveBatch, batch => _ = handler(batch));

        public Task StartAsync(CancellationToken cancellationToken) => connection.StartAsync(cancellationToken);
        public Task JoinCityAsync(string city, CancellationToken cancellationToken)
            => connection.InvokeAsync(HubMethods.JoinCity, city, cancellationToken);
        public Task LeaveCityAsync(string city, CancellationToken cancellationToken)
            => connection.InvokeAsync(HubMethods.LeaveCity, city, cancellationToken);
        public Task StopAsync(CancellationToken cancellationToken = default) => connection.StopAsync(cancellationToken);
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}

public sealed class SignalRNotificationService : ISignalRNotificationService, IDeliveryControl, IAsyncDisposable
{
    static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(15);

    readonly IConfiguration _configuration;
    readonly IWebAssemblyHostEnvironment _hostEnvironment;
    readonly NavigationManager _navigationManager;
    readonly ILogger<SignalRNotificationService> _logger;
    readonly ILiveDeliveryConnectionFactory _connectionFactory;
    readonly TimeProvider _timeProvider;
    readonly IClientExperienceCapture? _clientExperienceCapture;
    readonly IClientExperienceAttemptCapture? _attemptCapture;
    readonly object _stateLock = new();

    ILiveDeliveryConnection? _connection;
    IDisposable? _notificationRegistration;
    ConnectionHandlers? _handlers;
    LiveDeliveryState _deliveryState = new(false, true, false, CityNames.Marta, 0, null);
    string _city = CityNames.Marta;
    volatile bool _desiredDelivery = true;
    long _connectionGeneration;
    bool _disposed;
    Guid _liveEpisodeId;
    string? _lastLiveExperienceState;

    public SignalRNotificationService(
        IConfiguration configuration,
        IWebAssemblyHostEnvironment hostEnvironment,
        NavigationManager navigationManager,
        ILogger<SignalRNotificationService> logger,
        ILiveDeliveryConnectionFactory? connectionFactory = null,
        TimeProvider? timeProvider = null,
        IClientExperienceCapture? clientExperienceCapture = null,
        IClientExperienceAttemptCapture? attemptCapture = null)
    {
        _configuration = configuration;
        _hostEnvironment = hostEnvironment;
        _navigationManager = navigationManager;
        _logger = logger;
        _connectionFactory = connectionFactory ?? new SignalRLiveDeliveryConnectionFactory();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _clientExperienceCapture = clientExperienceCapture;
        _attemptCapture = attemptCapture ?? clientExperienceCapture as IClientExperienceAttemptCapture;
    }

    public event SignalRNotificationHandler? NotificationReceived;
    public event Action<LiveDeliveryState>? DeliveryStateChanged;

    public LiveDeliveryState DeliveryState
    {
        get
        {
            lock (_stateLock)
            {
                return _deliveryState;
            }
        }
    }

    public bool IsJoined => DeliveryState.CityJoined;

    public bool DesiredDelivery
    {
        set
        {
            _desiredDelivery = value;
            var current = DeliveryState;
            Publish(current with { DeliveryDesired = value });
        }
    }

    public async Task InitAsync(CancellationToken ct = default)
    {
        if (_disposed)
        {
            return;
        }

        await ReplaceConnectionAsync();
        _city = _navigationManager.ResolveCity();
        var generation = Interlocked.Increment(ref _connectionGeneration);
        var attemptId = _desiredDelivery
            ? _attemptCapture?.StartAttempt(_city.ToLowerInvariant(), "live_start", 30_000) ?? Guid.Empty
            : Guid.Empty;

        var setting = _configuration.GetSection("AppSettings:ExternalApis")
            .GetChildren()
            .FirstOrDefault(item => item.GetValue<string>("Name") == nameof(APIs.TransitJazzSignalR));
        var baseUrl = setting?.GetValue<string>("BaseUri");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            FinishLiveAttempt(attemptId, "live_start", "failed", "owner_failed");
            Publish(new(false, _desiredDelivery, false, _city, generation, LiveDeliveryFailureReason.MissingConfiguration));
            return;
        }

        if (!TryCreateHubUri(baseUrl, out var hubUri))
        {
            FinishLiveAttempt(attemptId, "live_start", "failed", "owner_failed");
            Publish(new(false, _desiredDelivery, false, _city, generation, LiveDeliveryFailureReason.InvalidConfiguration));
            return;
        }

        var connection = _connectionFactory.Create(hubUri);
        _connection = connection;
        AttachHandlers(connection, generation);
        Publish(new(false, _desiredDelivery, false, _city, generation, null));

        try
        {
            await AwaitOperationAsync(token => connection.StartAsync(token), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            FailIfCurrent(connection, generation, LiveDeliveryFailureReason.Cancelled, attemptId, "live_start");
            return;
        }
        catch (TimeoutException)
        {
            FailIfCurrent(connection, generation, LiveDeliveryFailureReason.OperationTimedOut, attemptId, "live_start");
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalR connection start failed for city {City}", _city);
            FailIfCurrent(connection, generation, LiveDeliveryFailureReason.StartFailed, attemptId, "live_start");
            return;
        }

        if (!IsCurrent(connection, generation))
        {
            return;
        }

        if (connection.State != HubConnectionState.Connected)
        {
            FailIfCurrent(connection, generation, LiveDeliveryFailureReason.StartFailed, attemptId, "live_start");
            return;
        }

        Publish(new(true, _desiredDelivery, false, _city, generation, null));
        if (_desiredDelivery)
        {
            await JoinCityAsync(connection, generation, ct, resume: false, attemptId);
        }
    }

    public async Task PauseAsync()
    {
        var connection = _connection;
        var state = DeliveryState;
        if (connection is null || connection.State != HubConnectionState.Connected || !state.CityJoined)
        {
            Publish(state with { DeliveryDesired = false, CityJoined = false });
            return;
        }

        var generation = state.ConnectionGeneration;
        try
        {
            await AwaitOperationAsync(token => connection.LeaveCityAsync(state.City, token), CancellationToken.None);
            if (IsCurrent(connection, generation))
            {
                Publish(new(true, _desiredDelivery, false, state.City, generation, null));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving city group {City}", state.City);
            if (IsCurrent(connection, generation))
            {
                Publish(new(connection.State == HubConnectionState.Connected, _desiredDelivery, true,
                    state.City, generation, null));
            }
        }
    }

    public async Task ResumeAsync()
    {
        var connection = _connection;
        var state = DeliveryState;
        if (connection is null || !_desiredDelivery || connection.State != HubConnectionState.Connected)
        {
            Publish(state with { DeliveryDesired = _desiredDelivery });
            return;
        }

        var attemptId = _attemptCapture?.StartAttempt(state.City.ToLowerInvariant(), "live_resume", 30_000) ?? Guid.Empty;
        await JoinCityAsync(connection, state.ConnectionGeneration, CancellationToken.None, resume: true, attemptId);
    }

    async Task JoinCityAsync(
        ILiveDeliveryConnection connection,
        long generation,
        CancellationToken cancellationToken,
        bool resume,
        Guid attemptId = default)
    {
        var state = DeliveryState;
        try
        {
            await AwaitOperationAsync(token => connection.JoinCityAsync(state.City, token), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            FailIfCurrent(connection, generation, LiveDeliveryFailureReason.Cancelled, attemptId, resume ? "live_resume" : "live_start");
            return;
        }
        catch (TimeoutException)
        {
            FailIfCurrent(connection, generation, LiveDeliveryFailureReason.OperationTimedOut, attemptId, resume ? "live_resume" : "live_start");
            return;
        }
        catch (HubException ex) when (ex.Message.Contains("Method does not exist", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(ex, "SignalR city join rejected by the server protocol for city {City}", state.City);
            FailIfCurrent(connection, generation, LiveDeliveryFailureReason.ProtocolRejected, attemptId, resume ? "live_resume" : "live_start");
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalR city join failed for city {City}", state.City);
            FailIfCurrent(connection, generation,
                resume ? LiveDeliveryFailureReason.ResumeFailed : LiveDeliveryFailureReason.CityJoinFailed,
                attemptId, resume ? "live_resume" : "live_start");
            return;
        }

        if (!IsCurrent(connection, generation))
        {
            return;
        }

        if (!_desiredDelivery)
        {
            try
            {
                await connection.LeaveCityAsync(state.City, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Late city join could not be undone for city {City}", state.City);
            }

            if (IsCurrent(connection, generation))
            {
                FinishLiveAttempt(attemptId, resume ? "live_resume" : "live_start", "cancelled", "user_cancelled");
                Publish(new(connection.State == HubConnectionState.Connected, false, false, state.City, generation, null));
            }

            return;
        }

        if (connection.State == HubConnectionState.Connected)
        {
            FinishLiveAttempt(attemptId, resume ? "live_resume" : "live_start", "succeeded", "owner_confirmed");
            Publish(new(true, true, true, state.City, generation, null));
        }
        else
        {
            FailIfCurrent(connection, generation, LiveDeliveryFailureReason.ConnectionClosed, attemptId, resume ? "live_resume" : "live_start");
        }
    }

    void AttachHandlers(ILiveDeliveryConnection connection, long generation)
    {
        Func<Exception?, Task> reconnecting = error => OnReconnectingAsync(connection, generation, error);
        Func<string?, Task> reconnected = connectionId => OnReconnectedAsync(connection, generation, connectionId);
        Func<Exception?, Task> closed = error => OnClosedAsync(connection, generation, error);
        connection.Reconnecting += reconnecting;
        connection.Reconnected += reconnected;
        connection.Closed += closed;
        _handlers = new(reconnecting, reconnected, closed);
        _notificationRegistration = connection.OnNotificationReceived(batch =>
            IsCurrent(connection, generation) && DeliveryState.IsReady
                ? NotificationReceived?.Invoke(batch) ?? Task.CompletedTask
                : Task.CompletedTask);
    }

    Task OnReconnectingAsync(ILiveDeliveryConnection connection, long generation, Exception? exception)
    {
        if (IsCurrent(connection, generation))
        {
            Publish(new(false, _desiredDelivery, false, _city, generation, null));
            _logger.LogWarning(exception, "SignalR reconnecting for city {City}", _city);
        }

        return Task.CompletedTask;
    }

    async Task OnReconnectedAsync(ILiveDeliveryConnection connection, long generation, string? connectionId)
    {
        if (!IsCurrent(connection, generation))
        {
            return;
        }

        Publish(new(connection.State == HubConnectionState.Connected, _desiredDelivery, false, _city, generation, null));
        if (_desiredDelivery && connection.State == HubConnectionState.Connected)
        {
            await JoinCityAsync(connection, generation, CancellationToken.None, resume: true);
        }
    }

    Task OnClosedAsync(ILiveDeliveryConnection connection, long generation, Exception? exception)
    {
        if (IsCurrent(connection, generation))
        {
            LiveDeliveryFailureReason? reason = _disposed || !_desiredDelivery
                ? null
                : LiveDeliveryFailureReason.ConnectionClosed;
            Publish(new(false, _desiredDelivery, false, _city, generation, reason));
            _logger.LogWarning(exception, "SignalR closed for city {City}", _city);
        }

        return Task.CompletedTask;
    }

    async Task AwaitOperationAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(OperationTimeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            await operation(linked.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException();
        }
    }

    void FailIfCurrent(ILiveDeliveryConnection connection, long generation, LiveDeliveryFailureReason reason,
        Guid attemptId = default, string journey = "live_start")
    {
        if (IsCurrent(connection, generation))
        {
            FinishLiveAttempt(attemptId, journey,
                reason == LiveDeliveryFailureReason.Cancelled ? "cancelled" : "failed",
                reason == LiveDeliveryFailureReason.Cancelled ? "user_cancelled" : "owner_failed");
            Publish(new(connection.State == HubConnectionState.Connected, _desiredDelivery, false, _city, generation, reason));
        }
    }

    void FinishLiveAttempt(Guid attemptId, string journey, string outcome, string reason)
    {
        if (attemptId == Guid.Empty) return;
        try { _attemptCapture?.FinishAttempt(attemptId, _city.ToLowerInvariant(), journey, outcome, reason); }
        catch { /* Capture is passive and cannot affect live delivery. */ }
    }

    bool IsCurrent(ILiveDeliveryConnection connection, long generation)
        => ReferenceEquals(_connection, connection)
            && generation == Volatile.Read(ref _connectionGeneration)
            && !_disposed;

    void Publish(LiveDeliveryState state)
    {
        LiveDeliveryState previous;
        string currentExperienceState;
        Guid episodeId;
        bool reportExperience;
        lock (_stateLock)
        {
            previous = _deliveryState;
            _deliveryState = state;
            currentExperienceState = !state.DeliveryDesired ? "not_requested"
                : state.IsReady ? "healthy"
                : state.FailureReason is not null ? previous.IsReady ? "degraded" : "unavailable"
                : "pending";
            var scopeChanged = previous.ConnectionGeneration != state.ConnectionGeneration
                || !string.Equals(previous.City, state.City, StringComparison.OrdinalIgnoreCase);
            if (scopeChanged || _liveEpisodeId == Guid.Empty)
                _liveEpisodeId = Guid.NewGuid();
            episodeId = _liveEpisodeId;
            reportExperience = scopeChanged || !string.Equals(_lastLiveExperienceState, currentExperienceState, StringComparison.Ordinal);
            _lastLiveExperienceState = currentExperienceState;
        }

        if (reportExperience) CaptureLiveExperienceState(previous, state, currentExperienceState, episodeId);
        DeliveryStateChanged?.Invoke(state);
    }

    void CaptureLiveExperienceState(LiveDeliveryState previous, LiveDeliveryState current, string currentState, Guid episodeId)
    {
        if (_clientExperienceCapture is null || current.ConnectionGeneration <= 0) return;
        var previousState = !previous.DeliveryDesired ? "not_requested"
            : previous.IsReady ? "healthy"
            : previous.FailureReason is not null ? "unavailable"
            : "pending";
        var reason = currentState switch
        {
            "healthy" => "owner_confirmed",
            "unavailable" or "degraded" => "owner_failed",
            "not_requested" => "not_requested",
            _ => "unknown"
        };
        try
        {
            _clientExperienceCapture.TryCapture(new ClientExperienceEvent
            {
                SchemaVersion = ClientExperienceCatalog.SchemaVersion,
                MeasurementVersion = ClientExperienceCatalog.MeasurementVersion,
                EventKind = "capability_changed",
                EventId = Guid.NewGuid(),
                PageSessionId = Guid.Empty,
                CityActivationId = null,
                Sequence = 0,
                OccurredAtUtc = DateTimeOffset.UtcNow,
                PageElapsedMs = 0,
                CitySlug = string.IsNullOrWhiteSpace(current.City) ? "unknown" : current.City.ToLowerInvariant(),
                ClientRelease = "unknown",
                Capability = "live_delivery",
                PreviousState = previousState,
                CurrentState = currentState,
                EpisodeId = episodeId,
                OwnerGeneration = Math.Max(0, current.ConnectionGeneration),
                InitialSnapshot = false,
                Reason = reason
            });
        }
        catch { /* Telemetry never participates in connection or join acceptance. */ }
    }

    bool TryCreateHubUri(string baseUrl, out Uri hubUri)
    {
        hubUri = default!;
        try
        {
            Uri baseUri;
            if (Uri.TryCreate(baseUrl.TrimEnd('/'), UriKind.Absolute, out var absolute))
            {
                baseUri = absolute;
            }
            else if (Uri.TryCreate(baseUrl, UriKind.Relative, out var relative))
            {
                baseUri = new Uri(new Uri(_hostEnvironment.BaseAddress, UriKind.Absolute), relative);
            }
            else
            {
                return false;
            }

            if (baseUri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(baseUri.Host))
            {
                return false;
            }

            hubUri = new Uri($"{baseUri.ToString().TrimEnd('/')}/hubs/transit", UriKind.Absolute);
            return true;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    async Task ReplaceConnectionAsync()
    {
        var previous = _connection;
        var handlers = _handlers;
        var notificationRegistration = _notificationRegistration;
        _connection = null;
        _handlers = null;
        _notificationRegistration = null;

        if (previous is null)
        {
            return;
        }

        if (handlers is not null)
        {
            previous.Reconnecting -= handlers.Reconnecting;
            previous.Reconnected -= handlers.Reconnected;
            previous.Closed -= handlers.Closed;
        }
        notificationRegistration?.Dispose();

        try
        {
            await previous.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error stopping replaced SignalR connection");
        }

        try
        {
            await previous.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error disposing replaced SignalR connection");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Interlocked.Increment(ref _connectionGeneration);
        await ReplaceConnectionAsync();
        var state = DeliveryState;
        Publish(new(false, false, false, state.City, state.ConnectionGeneration, null));
    }

    sealed record ConnectionHandlers(
        Func<Exception?, Task> Reconnecting,
        Func<string?, Task> Reconnected,
        Func<Exception?, Task> Closed);
}
