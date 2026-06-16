using Ardalis.Result;
using ChefKnifeStudios.MartaJazz.Client.Core.Enums;
using ChefKnifeStudios.MartaJazz.Shared;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.MartaJazz.Client.Core.Services.EndpointsServices;

public interface ITransitEndpointsService
{
    Task<Result<IEnumerable<EventEnvelope>>> GetLastBatch(CancellationToken ct = default);
}

public class TransitEndpointsService : ITransitEndpointsService
{
    readonly ILogger<TransitEndpointsService> _logger;
    readonly IHttpService _httpService;

    public TransitEndpointsService(
        ILogger<TransitEndpointsService> logger,
        IHttpServiceFactory httpClientFactory)
    {
        _logger = logger;
        _httpService = httpClientFactory.Create(nameof(APIs.TransitJazzAPI));
    }

    public async Task<Result<IEnumerable<EventEnvelope>>> GetLastBatch(CancellationToken ct = default)
    {
        try
        {
            var result = await _httpService.GetAsync<IEnumerable<EventEnvelope>>(ApiEndpoints.Transit.GetLastBatch, ct);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetLastBatch. TraceIdentifier: {TraceId}", Guid.NewGuid());
            return Result.Error("An unexpected error occurred.");
        }
    }
}
