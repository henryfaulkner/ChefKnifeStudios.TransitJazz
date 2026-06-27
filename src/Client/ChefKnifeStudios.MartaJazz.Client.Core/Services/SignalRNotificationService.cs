using ChefKnifeStudios.MartaJazz.Client.Core.Enums;
using ChefKnifeStudios.MartaJazz.Shared;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace ChefKnifeStudios.MartaJazz.Client.Core.Services;

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
        ILogger<SignalRNotificationService> logger) : ISignalRNotificationService
{
    private HubConnection? _hubConnection;
    private string _city = "marta";
    public event SignalRNotificationHandler? NotificationReceived;

    public async Task InitAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Starting SignalRNotificationService.InitAsync");
        if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
            return;

        try
        {
            CloseConnection();

            _city = ResolveCity();

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
                    .AddJsonProtocol(options =>
                    {
                        JsonSettings.ApplyTo(options.PayloadSerializerOptions);
                    })
                    .Build();

                logger.LogInformation("Connecting to SignalR hub: {host}", baseUri.Host);

                _hubConnection.On<List<EventEnvelope>>("ReceiveBatch", batch =>
                {
                    logger.LogInformation("[SignalR] ReceiveBatch fired: {Count} events, hubState={State}", batch.Count, _hubConnection?.State);
                    NotificationReceived?.Invoke(batch);
                    logger.LogInformation("[SignalR] ReceiveBatch: NotificationReceived invoke returned");
                });

                _hubConnection.Reconnected += async _ =>
                {
                    logger.LogInformation("Reconnected; rejoining city group {City}", _city);
                    await _hubConnection.InvokeAsync("JoinCity", _city);
                };

                await _hubConnection.StartAsync(ct);
                await _hubConnection.InvokeAsync("JoinCity", _city, ct);

                logger.LogInformation("Joined city group {City}", _city);
            }
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

    // Read #city from the current browser URL hash; default "marta" (FR-004)
    string ResolveCity()
    {
        try
        {
            var uri = new Uri(navigationManager.Uri);
            var fragment = uri.Fragment.TrimStart('#');
            if (!string.IsNullOrWhiteSpace(fragment))
                return Uri.UnescapeDataString(fragment).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not parse city from URL; defaulting to marta.");
        }
        return "marta";
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
