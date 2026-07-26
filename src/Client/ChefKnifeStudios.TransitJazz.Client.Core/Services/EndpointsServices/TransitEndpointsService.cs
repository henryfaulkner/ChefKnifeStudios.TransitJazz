using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Client.Core.Enums;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.TransitJazz.Client.Core.Services.EndpointsServices;

public interface ITransitEndpointsService
{
    Task<Result<IEnumerable<EventEnvelope>>> GetLastBatch(CancellationToken ct = default);
}

public class TransitEndpointsService : ITransitEndpointsService
{
    readonly ILogger<TransitEndpointsService> _logger;
    readonly IHttpService _httpService;
    readonly NavigationManager _navigationManager;

    public TransitEndpointsService(
        ILogger<TransitEndpointsService> logger,
        IHttpServiceFactory httpClientFactory,
        NavigationManager navigationManager)
    {
        _logger = logger;
        _httpService = httpClientFactory.Create(nameof(APIs.TransitJazzAPI));
        _navigationManager = navigationManager;
    }

    public async Task<Result<IEnumerable<EventEnvelope>>> GetLastBatch(CancellationToken ct = default)
    {
        try
        {
            var city = _navigationManager.ResolveCity();
            var url = $"{ApiEndpoints.Transit.GetLastBatch}?city={city}";
            var result = await _httpService.GetAsync<IEnumerable<EventEnvelope>>(url, ct);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetLastBatch. TraceIdentifier: {TraceId}", Guid.NewGuid());
            return Result.Error("An unexpected error occurred.");
        }
    }
}
