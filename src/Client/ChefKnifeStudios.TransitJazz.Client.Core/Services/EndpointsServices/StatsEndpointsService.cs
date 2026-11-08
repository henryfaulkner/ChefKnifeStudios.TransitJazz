using ChefKnifeStudios.TransitJazz.Client.Core.Enums;
using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using System.Collections.Generic;

namespace ChefKnifeStudios.TransitJazz.Client.Core.Services.EndpointsServices;

public interface IStatsEndpointsService
{
    Task<HttpServiceResponse<StatsOverlayResponse>> QueryAsync(
        StatsOverlayRequest request,
        CancellationToken cancellationToken = default);
    Task<HttpServiceResponse<StatsOverlayResponse>> QueryAsync(
        StatsOverlayRequest request,
        string contractVersion,
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

    public Task<HttpServiceResponse<StatsOverlayResponse>> QueryAsync(
        StatsOverlayRequest request,
        string contractVersion,
        CancellationToken cancellationToken = default)
    {
        if (contractVersion is not (StatsOverlayVocabulary.ContractVersion or StatsOverlayVocabulary.ContractVersionV2))
            throw new ArgumentOutOfRangeException(nameof(contractVersion), "Unsupported statistics contract version.");
        return httpService.PostWithMetadataAsync<StatsOverlayRequest, StatsOverlayResponse>(
            ApiEndpoints.Stats.Query, request,
            new Dictionary<string, string> { ["X-TransitJazz-Stats-Contract"] = contractVersion }, cancellationToken);
    }
}
