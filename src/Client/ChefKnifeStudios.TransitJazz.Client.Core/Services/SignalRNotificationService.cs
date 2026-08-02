using ChefKnifeStudios.TransitJazz.Client.Core.Enums;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ChefKnifeStudios.TransitJazz.Client.Core.Services;

namespace ChefKnifeStudios.TransitJazz.Client.Core.Services;

public delegate Task SignalRNotificationHandler(List<EventEnvelope> batch);

public interface ISignalRNotificationService
{
    event SignalRNotificationHandler? NotificationReceived;

    Task InitAsync(CancellationToken ct = default);
}

public class SignalRNotificationService(
        IConfiguration configuration,
        IWebAssemblyHostEnvironment hostEnvironment,
        NavigationManager navigationManager,
        ILogger<SignalRNotificationService> logger) : ISignalRNotificationService, IDeliveryControl
{
    private HubConnection? _hubConnection;
    private string _city = CityNames.Marta;
    // Hidden-tab pause (feature 051, US3): the Reconnected handler reads this local flag
    // rather than referencing AttentionGate — one-directional dependency (data-model §4).
    // AttentionGate pushes it via DesiredDelivery before every Pause/Resume call.
    private volatile bool _desiredDelivery = true;
    public event SignalRNotificationHandler? NotificationReceived;

    public bool DesiredDelivery
    {
        set => _desiredDelivery = value;
    }

    // Pause = leave the city group (zero fan-out egress); the connection stays open — a
    // parked WebSocket costs only keepalive pings (research D5).
    public async Task PauseAsync()
    {
        if (_hubConnection is null || _hubConnection.State != HubConnectionState.Connected)
            return;

        try
        {
            await _hubConnection.InvokeAsync(HubMethods.LeaveCity, _city);
            logger.LogInformation("Left city group {City} (hidden-tab pause)", _city);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error leaving city group {City}", _city);
        }
    }

    // Resume = rejoin; JoinCity's existing LastBatchCache replay is the catch-up path — no
    // new server logic needed (research D5).
    public async Task ResumeAsync()
    {
        if (_hubConnection is null || _hubConnection.State != HubConnectionState.Connected)
            return;

        try
        {
            await _hubConnection.InvokeAsync(HubMethods.JoinCity, _city);
            logger.LogInformation("Rejoined city group {City} (hidden-tab resume)", _city);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error rejoining city group {City}", _city);
        }
    }

    public async Task InitAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Starting SignalRNotificationService.InitAsync");
        if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
            return;

        try
        {
            CloseConnection();

            _city = navigationManager.ResolveCity();

            var apis = configuration.GetSection("AppSettings:ExternalApis");
            var itemArray = apis.GetChildren();

            var setting = itemArray.FirstOrDefault(a =>
                a.GetValue<string>("Name") == nameof(APIs.TransitJazzSignalR));

            if (setting != null)
            {
                var baseUrl = setting.GetValue("BaseUri", string.Empty)?.TrimEnd('/');
                if (baseUrl is null)
                {
                    string errMsg = "BaseUrl for PokerAttackSignalR API config is null.";
                    logger.LogCritical(errMsg);
                    throw new ApplicationException(errMsg);
                }

                Uri baseUri;
                if (Uri.IsWellFormedUriString(baseUrl, UriKind.Absolute))
                {
                    baseUri = new Uri(baseUrl);
                }
                else
                {
                    var hostUri = new Uri(hostEnvironment.BaseAddress, UriKind.Absolute);
                    var relativeUri = new Uri(baseUrl, UriKind.Relative);
                    baseUri = new Uri(hostUri, relativeUri);
                }

                var url = $"{baseUri.ToString().TrimEnd('/')}/hubs/transit";

                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(url)
                    .WithAutomaticReconnect()
                    .ConfigureLogging(logging =>
                    {
                        logging.SetMinimumLevel(LogLevel.Debug);
                    })
                    // MessagePack must match the hub — the server registers ONLY MessagePack,
                    // so a JSON client would be rejected at negotiation. Polymorphic payloads
                    // ride the [Union]/[Key] contracts in Shared/Events.
                    .AddMessagePackProtocol()
                    .Build();

                logger.LogInformation("Connecting to SignalR hub: {host}", baseUri.Host);

                _hubConnection.On<List<EventEnvelope>>(HubMethods.ReceiveBatch, batch =>
                {
                    NotificationReceived?.Invoke(batch);
                });

                _hubConnection.Reconnected += async _ =>
                {
                    // Contract C3: a reconnect while paused (hidden+muted) must not blind-rejoin
                    // — read the local flag AttentionGate last pushed, never reference the gate.
                    if (!_desiredDelivery)
                    {
                        logger.LogInformation("Reconnected while paused; not rejoining city group {City}", _city);
                        return;
                    }

                    logger.LogInformation("Reconnected; rejoining city group {City}", _city);
                    await _hubConnection.InvokeAsync(HubMethods.JoinCity, _city);
                };

                await _hubConnection.StartAsync(ct);
                await _hubConnection.InvokeAsync(HubMethods.JoinCity, _city, ct);

                logger.LogInformation("Joined city group {City}", _city);
            }
        }
        catch (HubException ex) when (ex.Message.Contains("Method does not exist", StringComparison.OrdinalIgnoreCase))
        {
            // Wire version gate (feature 051, US4). HubMethods.JoinCity is "JoinCityV2"; a server
            // that still exposes the v1 "JoinCity" rejects the invoke here. This is the gate WORKING
            // — it exists because MessagePack's ReadDouble accepts int encodings, so a stale peer
            // would otherwise decode v2's scaled-int coordinates as garbage and silently misrender
            // every vehicle. The generic handler below reported this as an unremarkable init error,
            // which left the only visible symptom as an empty map with no live data.
            logger.LogError(ex,
                "SignalR join REJECTED — client/server wire version mismatch. This client invokes " +
                "'{JoinMethod}' but the server does not expose it, so no vehicle batches or checkpoint " +
                "crossings will arrive. Deploy the server+worker container BEFORE the client (the " +
                "client must never lead the server). City={City}",
                HubMethods.JoinCity, _city);
            _hubConnection = null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error initializing SignalR Notification Hub");
            _hubConnection = null;
        }
        finally
        {
            logger.LogInformation("Ending SignalRNotificationService.InitAsync");
        }
    }

    public void Dispose()
    {
        try
        {
            CloseConnection();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error disposing SignalR connection");
            throw;
        }
    }

    private void CloseConnection()
    {
        try
        {
            if (_hubConnection == null) return;
            _ = _hubConnection.StopAsync();
            _hubConnection = null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error closing SignalR connection");
            throw;
        }
    }
}
