using ChefKnifeStudios.MartaJazz.Shared;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker;

public sealed class SignalRHubPublisher : ITransitHubPublisher, IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly ILogger<SignalRHubPublisher> _logger;

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
            .WithAutomaticReconnect()
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
            _logger.LogError("Hub connection closed permanently {ex}", ex);
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
        if (_connection.State != HubConnectionState.Connected)
        {
            _logger.LogWarning("Cannot publish batch: hub not connected (state={state})", _connection.State);
            return false;
        }

        await _connection.InvokeAsync(HubMethods.PublishBatch, city, batch, ct);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
