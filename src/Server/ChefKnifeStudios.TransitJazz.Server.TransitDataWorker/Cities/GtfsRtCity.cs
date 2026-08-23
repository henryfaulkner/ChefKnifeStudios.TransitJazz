using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;

public class GtfsRtCity(
    CityConfig config,
    IHttpClientFactory httpClientFactory,
    ILogger<GtfsRtCity> logger) : ITransitCity
{
    public string Name => config.Name;
    public bool EmitsTelemetry => config.EmitsTelemetry;

    public async Task<CityFetchResult> FetchVehiclesAsync(CancellationToken ct)
    {
        var apiKey = config.ApiKeyEnvVar is not null
            ? Environment.GetEnvironmentVariable(config.ApiKeyEnvVar)
            : null;

        var merged = new FeedMessage();
        var successfulSources = 0;
        var failedSources = 0;

        foreach (var url in config.GtfsRtUrls)
        {
            try
            {
                var feed = await FetchFeedAsync(url, apiKey, ct);
                if (feed != null)
                {
                    successfulSources++;
                    merged.Entities.AddRange(feed.Entities);
                    merged.Header ??= feed.Header;
                }
                else
                {
                    failedSources++;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "City {City}: failed to fetch GTFS-RT from {Url}.", config.Name, url);
                failedSources++;
            }
        }

        ApplyRailRouteIdMap(merged);
        ApplyRouteIdNormalization(merged);
        return CityFetchResult.FromSources(merged, successfulSources, failedSources);
    }

    async Task<FeedMessage?> FetchFeedAsync(string url, string? apiKey, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        var requestUrl = apiKey is not null ? $"{url}?{config.ApiKeyQueryParam}={apiKey}" : url;
        var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TransitJazz", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("City {City}: GTFS-RT feed {Url} returned {StatusCode}.", config.Name, url, response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return ProtoBuf.Serializer.Deserialize<FeedMessage>(stream);
    }

    void ApplyRailRouteIdMap(FeedMessage feed)
    {
        if (config.RailRouteIdMap is null || config.RailRouteIdMap.Count == 0) return;

        foreach (var entity in feed.Entities)
        {
            if (entity.Vehicle?.Trip?.RouteId is not null
                && config.RailRouteIdMap.TryGetValue(entity.Vehicle.Trip.RouteId, out var mapped))
                entity.Vehicle.Trip.RouteId = mapped;
        }
    }

    void ApplyRouteIdNormalization(FeedMessage feed)
    {
        if (config.RouteIdNormalization is not { Length: > 0 }) return;

        foreach (var entity in feed.Entities)
        {
            if (entity.Vehicle?.Trip?.RouteId is not null)
                entity.Vehicle.Trip.RouteId = RouteIdNormalizer.Apply(entity.Vehicle.Trip.RouteId, config.RouteIdNormalization);
        }
    }
}
