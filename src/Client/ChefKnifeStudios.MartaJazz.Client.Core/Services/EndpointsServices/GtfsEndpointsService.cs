using Ardalis.Result;
using ChefKnifeStudios.MartaJazz.Client.Core.Enums;
using ChefKnifeStudios.MartaJazz.Shared;
using ChefKnifeStudios.MartaJazz.Shared.GtfsData;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.MartaJazz.Client.Core.Services.EndpointsServices;

public interface IGtfsEndpointsService
{
    Task<Result<RouteShapeFeature>> GetRouteShape(string routeId, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<RouteShapeFeature>>> GetAllRouteShapes(CancellationToken cancellationToken = default);
}

public class GtfsEndpointsService : IGtfsEndpointsService
{
    readonly ILogger<GtfsEndpointsService> _logger;
    readonly IHttpService _httpService;
    readonly NavigationManager _navigationManager;

    public GtfsEndpointsService(
        ILogger<GtfsEndpointsService> logger,
        IHttpServiceFactory httpClientFactory,
        NavigationManager navigationManager)
    {
        _logger = logger;
        _httpService = httpClientFactory.Create(nameof(APIs.TransitJazzAPI));
        _navigationManager = navigationManager;
    }

    // Parse #city from the current URL hash, defaulting to "marta" (FR-004)
    string ResolveCity()
    {
        try
        {
            var uri = new Uri(_navigationManager.Uri);
            var fragment = uri.Fragment.TrimStart('#');
            if (!string.IsNullOrWhiteSpace(fragment))
                return Uri.UnescapeDataString(fragment).ToLowerInvariant();
        }
        catch { }
        return "marta";
    }

    public async Task<Result<RouteShapeFeature>> GetRouteShape(string routeId, CancellationToken cancellationToken = default)
    {
        try
        {
            var city = ResolveCity();
            var url = ApiEndpoints.Gtfs.GetRouteShape.Replace("{routeId}", routeId) + $"?city={city}";
            var result = await _httpService.GetAsync<RouteShapeFeature>(url, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetRouteShape. TraceIdentifier: {TraceId}", Guid.NewGuid());
            return Result.Error("An unexpected error occurred.");
        }
    }

    public async Task<Result<IEnumerable<RouteShapeFeature>>> GetAllRouteShapes(CancellationToken cancellationToken = default)
    {
        try
        {
            var city = ResolveCity();
            var url = $"{ApiEndpoints.Gtfs.GetAllRouteShapes}?city={city}";
            _logger.LogDebug("GtfsEndpointsService.GetAllRouteShapes: requesting {Url}", url);
            var result = await _httpService.GetAsync<IEnumerable<RouteShapeFeature>>(url, cancellationToken);

            if (result.IsSuccess)
            {
                var list = result.Value?.ToList();
                _logger.LogDebug("GtfsEndpointsService.GetAllRouteShapes: deserialized {Count} features", list?.Count ?? 0);
            }
            else
            {
                _logger.LogWarning("GtfsEndpointsService.GetAllRouteShapes: non-success result — Status={Status} Errors={Errors}",
                    result.Status, string.Join("; ", result.Errors));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetAllRouteShapes. TraceIdentifier: {TraceId}", Guid.NewGuid());
            return Result.Error("An unexpected error occurred.");
        }
    }
}
