using ChefKnifeStudios.MartaJazz.Shared;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker;

public sealed class SignalRHubPublisher : ITransitHubPublisher, IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly ILogger<SignalRHubPublisher> _logger;
    // Serializes on-demand reconnect attempts from PublishBatchAsync so concurrent ticks
    // can't stack multiple StartAsync calls on the same disconnected connection.
    private readonly SemaphoreSlim _reconnectGate = new(1, 1);

    public SignalRHubPublisher(
        IConfiguration configuration,
        ILogger<SignalRHubPublisher> logger)
    {
        _logger = logger;

        var apiBaseUrl = configuration["services:apiservice:https:0"];
        var hubUrl = apiBaseUrl != null
            ? $"{apiBaseUrl}/hubs/worker-transit"
            : configuration["SignalR:HubUrl"]
              ?? throw new InvalidOperationException("Missing SignalR:HubUrl");

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, opts =>
            {
                // opts.AccessTokenProvider = async () => await tokenProvider.GetAccessTokenAsync() ?? string.Empty;
            })
            // Unbounded backoff: the default WithAutomaticReconnect() gives up after ~30s
            // (0s/2s/10s/30s) and permanently Closes the connection — after which nothing
            // re-arms it and the worker publishes nothing until the container is restarted.
            // This worker's only job is to push batches, so retry forever, capped at 30s.
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            // MessagePack instead of JSON: the polymorphic payload is handled by [Union] on
            // ISignalREvent + [Key] on every contract (see Shared/Events). All three hops
            // (this worker, the hub, the WASM client) MUST use MessagePack — a MessagePack
            // producer cannot talk to a JSON consumer, so deploy them together.
            .AddMessagePackProtocol()
            .Build();

        _connection.Reconnecting += ex =>
        {
            _logger.LogWarning("Hub connection lost, reconnecting... {ex}", ex);
            return Task.CompletedTask;
        };

        _connection.Reconnected += id =>
        {
            _logger.LogInformation("Reconnected to hub, connectionId={id}", id);
            return Task.CompletedTask;
        };

        _connection.Closed += ex =>
        {
            // Reaching Closed means automatic reconnect gave up (or StartAsync was never
            // called). We don't restart here — PublishBatchAsync re-arms on the next tick,
            // which avoids racing the reconnect machinery from inside its own callback.
            _logger.LogError("Hub connection closed; will attempt restart on next publish. {ex}", ex);
            return Task.CompletedTask;
        };
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        try
        {
            await _connection.StartAsync(ct);
            _logger.LogInformation("Connected to WorkerTransitHub");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalR client failed to connect to hub.");
        }
    }

    public async Task<bool> PublishBatchAsync(string city, List<EventEnvelope> batch, CancellationToken ct = default)
    {
        // Disconnected is terminal for the connection's own reconnect machinery — only a
        // fresh StartAsync recovers it. Reconnecting/Connecting are transient; skip this
        // tick and let auto-reconnect finish rather than fighting it.
        if (_connection.State == HubConnectionState.Disconnected)
            await TryReconnectAsync(ct);

        if (_connection.State != HubConnectionState.Connected)
        {
            _logger.LogWarning("Cannot publish batch: hub not connected (state={state})", _connection.State);
            return false;
        }

        await _connection.InvokeAsync(HubMethods.PublishBatch, city, batch, ct);
        return true;
    }

    async Task TryReconnectAsync(CancellationToken ct)
    {
        // Single-flight: if another tick is already restarting, don't pile on.
        if (!await _reconnectGate.WaitAsync(0, ct))
            return;
        try
        {
            // Re-check under the gate — a concurrent caller may have already reconnected.
            if (_connection.State != HubConnectionState.Disconnected)
                return;

            _logger.LogInformation("Hub disconnected; attempting to restart connection.");
            await _connection.StartAsync(ct);
            _logger.LogInformation("Reconnected to WorkerTransitHub.");
        }
        catch (Exception ex)
        {
            // Swallow: the next tick will try again. Don't let a failed restart kill the tick.
            _logger.LogWarning(ex, "Hub restart attempt failed; will retry next publish.");
        }
        finally
        {
            _reconnectGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _reconnectGate.Dispose();
        await _connection.DisposeAsync();
    }

    /// <summary>Reconnect forever with exponential backoff capped at 30s. Returning a non-null
    /// delay from <see cref="NextRetryDelay"/> keeps the built-in reconnect loop alive
    /// indefinitely (the default policy returns null after ~30s, permanently closing).</summary>
    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            var seconds = Math.Min(30, Math.Pow(2, Math.Min(retryContext.PreviousRetryCount, 5)));
            return TimeSpan.FromSeconds(seconds);
        }
    }
}
