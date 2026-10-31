using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Client.Core.Enums;
using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.TransitJazz.Client.Core.Services.EndpointsServices;

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

    public static bool IsUsableRouteShape(RouteShapeFeature? feature, string expectedCity)
    {
        var properties = feature?.Properties;
        var geometry = feature?.Geometry;
        if (properties is null || geometry is null
            || string.IsNullOrWhiteSpace(properties.JoinKey)
            || !string.Equals(feature!.Type, "Feature", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(geometry.Type, "LineString", StringComparison.OrdinalIgnoreCase)
            || (properties.City is { Length: > 0 } city
                && !string.Equals(city, expectedCity, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var coordinates = geometry.Coordinates;
        if (coordinates is null || coordinates.Length < 2)
        {
            return false;
        }

        (double Longitude, double Latitude)? firstPoint = null;
        var distinctPointFound = false;
        foreach (var point in coordinates)
        {
            if (point is null || point.Length < 2)
            {
                return false;
            }

            var longitude = point[0];
            var latitude = point[1];
            if (!double.IsFinite(longitude) || !double.IsFinite(latitude)
                || longitude is < -180 or > 180
                || latitude is < -90 or > 90)
            {
                return false;
            }

            var coordinate = (longitude, latitude);
            if (firstPoint is null)
            {
                firstPoint = coordinate;
            }
            else if (coordinate != firstPoint.Value)
            {
                distinctPointFound = true;
            }
        }

        return distinctPointFound;
    }

    public async Task<Result<RouteShapeFeature>> GetRouteShape(string routeId, CancellationToken cancellationToken = default)
    {
        try
        {
            var city = _navigationManager.ResolveCity();
            var url = ApiEndpoints.Gtfs.GetRouteShape.Replace("{routeId}", routeId) + $"?city={city}";
            var result = await _httpService.GetAsync<RouteShapeFeature>(url, cancellationToken);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetRouteShape. TraceIdentifier: {TraceId}", Guid.NewGuid());
            return Result.Error("request_failed");
        }
    }

    public async Task<Result<IEnumerable<RouteShapeFeature>>> GetAllRouteShapes(CancellationToken cancellationToken = default)
    {
        try
        {
            var city = _navigationManager.ResolveCity();
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
                _logger.LogWarning("GtfsEndpointsService.GetAllRouteShapes: non-success result — Status={Status}",
                    result.Status);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetAllRouteShapes. TraceIdentifier: {TraceId}", Guid.NewGuid());
            return Result.Error("request_failed");
        }
    }
}
