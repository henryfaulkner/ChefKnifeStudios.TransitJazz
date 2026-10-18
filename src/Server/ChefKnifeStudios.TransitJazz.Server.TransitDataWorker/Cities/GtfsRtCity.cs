using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;

public class GtfsRtCity(
    CityConfig config,
    IHttpClientFactory httpClientFactory,
    ILogger<GtfsRtCity> logger,
    TimeProvider? timeProvider = null,
    bool captureReportedVehicles = false,
    ReportedVehicleBufferBudget? reportedVehicleBufferBudget = null,
    ReportedVehicleHistoryOptions? reportedVehicleHistoryOptions = null) : ITransitCity
{
    readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    public string Name => config.Name;

    public async Task<CityFetchResult> FetchVehiclesAsync(CancellationToken ct)
    {
        var apiKey = config.ApiKeyEnvVar is not null
            ? Environment.GetEnvironmentVariable(config.ApiKeyEnvVar)
            : null;

        var merged = new FeedMessage();
        var successfulSources = 0;
        var failedSources = 0;
        var observations = new List<ReportedVehicleSourceObservation>();

        if (captureReportedVehicles && config.ReportedVehicleSources.Length != 0
            && config.ReportedVehicleSources.Length != config.GtfsRtUrls.Length)
            throw new InvalidOperationException($"Reported-vehicle source policy count does not match the GTFS endpoint count for {config.Name}.");

        for (var index = 0; index < config.GtfsRtUrls.Length; index++)
        {
            var url = config.GtfsRtUrls[index];
            var source = captureReportedVehicles && index < config.ReportedVehicleSources.Length
                ? config.ReportedVehicleSources[index].ToDefinition() : null;
            try
            {
                var feed = await FetchFeedAsync(url, apiKey, ct);
                if (feed != null)
                {
                    var receiptUtc = _timeProvider.GetUtcNow();
                    if (source is not null)
                        observations.Add(CreateSuccessfulObservation(source, receiptUtc, feed));
                    successfulSources++;
                    merged.Entities.AddRange(feed.Entities);
                    merged.Header ??= feed.Header;
                }
                else
                {
                    failedSources++;
                    if (source is not null) observations.Add(ReportedVehicleSourceObservation.Failed(source, _timeProvider.GetUtcNow()));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning("City {City}: GTFS-RT source failed at {Endpoint}; exception type {ExceptionType}.",
                    config.Name, StructuredLogRedactor.SafeEndpointIdentity(url), StructuredLogRedactor.SafeExceptionType(ex));
                failedSources++;
                if (source is not null) observations.Add(ReportedVehicleSourceObservation.Failed(source, _timeProvider.GetUtcNow(), "parse_failure"));
            }
        }

        ApplyRailRouteIdMap(merged);
        ApplyRouteIdNormalization(merged);
        return CityFetchResult.FromSources(merged, successfulSources, failedSources,
            reportedVehicleObservations: observations);
    }

    ReportedVehicleSourceObservation CreateSuccessfulObservation(ReportedVehicleSourceDefinition source,
        DateTimeOffset receiptUtc, FeedMessage feed) => reportedVehicleBufferBudget is not null
        && reportedVehicleHistoryOptions is not null
            ? ReportedVehicleSourceObservation.Successful(source, receiptUtc, VehicleDescriptorIds(feed),
                reportedVehicleHistoryOptions, reportedVehicleBufferBudget)
            : ReportedVehicleSourceObservation.Successful(source, receiptUtc, VehicleDescriptorIds(feed));

    static IEnumerable<string?> VehicleDescriptorIds(FeedMessage feed)
    {
        foreach (var entity in feed.Entities)
            if (entity.IsDeleted != true && entity.Vehicle is not null)
                yield return entity.Vehicle.Vehicle?.Id;
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
            logger.LogWarning("City {City}: GTFS-RT source {Endpoint} returned {StatusCode}.",
                config.Name, StructuredLogRedactor.SafeEndpointIdentity(url), (int)response.StatusCode);
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
