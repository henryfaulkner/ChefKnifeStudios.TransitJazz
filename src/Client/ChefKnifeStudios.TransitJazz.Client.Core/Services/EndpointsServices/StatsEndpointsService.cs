using ChefKnifeStudios.TransitJazz.Client.Core.Enums;
using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Stats;

namespace ChefKnifeStudios.TransitJazz.Client.Core.Services.EndpointsServices;

public interface IStatsEndpointsService
{
    Task<HttpServiceResponse<StatsOverlayResponse>> QueryAsync(
        StatsOverlayRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class StatsEndpointsService : IStatsEndpointsService
{
    readonly IHttpService httpService;

    public StatsEndpointsService(IHttpServiceFactory httpServiceFactory) =>
        httpService = httpServiceFactory.Create(nameof(APIs.TransitJazzAPI));

    public Task<HttpServiceResponse<StatsOverlayResponse>> QueryAsync(
        StatsOverlayRequest request,
        CancellationToken cancellationToken = default) =>
        httpService.PostWithMetadataAsync<StatsOverlayRequest, StatsOverlayResponse>(
            ApiEndpoints.Stats.Query, request, cancellationToken);
}
