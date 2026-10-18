using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.RailRealtime;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;

public class MartaCity(
    IHttpClientFactory httpClientFactory,
    IOptions<RailRealtimeOptions> railOptions,
    ILogger<MartaCity> logger,
    TimeProvider? timeProvider = null,
    ReportedVehicleBufferBudget? reportedVehicleBufferBudget = null,
    ReportedVehicleHistoryOptions? reportedVehicleHistoryOptions = null) : ITransitCity
{
    static readonly JsonSerializerOptions _railJsonOptions = new() { PropertyNameCaseInsensitive = true };
    const string BusUrl = "https://gtfs-rt.itsmarta.com/TMGTFSRealTimeWebService/vehicle/vehiclepositions.pb";
    readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public string Name => CityNames.Marta;

    public async Task<CityFetchResult> FetchVehiclesAsync(CancellationToken ct)
    {
        var sources = new List<CityFetchResult> { await FetchBusFeedAsync(ct) };
        if (railOptions.Value.Enabled)
            sources.Add(await FetchRailEntitiesAsync(ct));
        return CityFetchResult.Combine(sources);
    }

    async Task<CityFetchResult> FetchBusFeedAsync(CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Get, BusUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TransitJazz", "1.0"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

            var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("MARTA bus feed returned {StatusCode}", response.StatusCode);
                return CityFetchResult.FromSources(null, 0, 1, reportedVehicleObservations: FailedBusObservation("source_failure"));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var feed = ProtoBuf.Serializer.Deserialize<FeedMessage>(stream)
                ?? throw new InvalidDataException("MARTA bus feed returned a null protobuf root.");
            var receiptUtc = _timeProvider.GetUtcNow();
            var options = railOptions.Value;
            var source = options.ReportedVehicleCaptureEnabled ? ReportedSource(options.BusReportedVehicleSource) : null;
            var observations = source is null ? [] : new[]
            {
                SuccessfulObservation(source, receiptUtc, VehicleDescriptorIds(feed))
            };
            return CityFetchResult.FromSources(feed, 1, 0, reportedVehicleObservations: observations);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("MARTA bus source failed; exception type {ExceptionType}.",
                StructuredLogRedactor.SafeExceptionType(ex));
            return CityFetchResult.FromSources(null, 0, 1, reportedVehicleObservations: FailedBusObservation("source_failure"));
        }
    }

    async Task<CityFetchResult> FetchRailEntitiesAsync(CancellationToken ct)
    {
        try
        {
            var opts = railOptions.Value;
            if (!opts.Enabled) return CityFetchResult.FromSources(new FeedMessage(), 0, 0);

            var client = httpClientFactory.CreateClient("RailRealtimeApi");
            var requestUri = string.IsNullOrEmpty(opts.ApiKey)
                ? client.BaseAddress!
                : new Uri($"{client.BaseAddress}?apiKey={opts.ApiKey}");

            var json = await client.GetStringAsync(requestUri, ct);
            var arrivals = JsonSerializer.Deserialize<List<RailArrivalDto>>(json, _railJsonOptions)
                           ?? throw new JsonException("MARTA rail feed returned a null root.");
            var receiptUtc = _timeProvider.GetUtcNow();
            var source = opts.ReportedVehicleCaptureEnabled ? ReportedSource(opts.RailReportedVehicleSource) : null;
            var observations = source is null ? [] : new[]
            {
                SuccessfulObservation(source, receiptUtc, ReportedVehicleRailExtractor.TrainIds(arrivals))
            };

            var realtime = arrivals.Where(a =>
                string.Equals(a.IsRealtime?.Trim(), "true", StringComparison.OrdinalIgnoreCase));

            var parsed = new List<(string TrainId, string Line, double Lat, double Lon, string? EventTime)>();
            foreach (var a in realtime)
            {
                if (string.IsNullOrEmpty(a.TrainId) || string.IsNullOrEmpty(a.Line)) continue;
                if (!double.TryParse(a.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                    || !double.TryParse(a.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) continue;
                parsed.Add((a.TrainId, a.Line, lat, lon, a.EventTime));
            }

            var entities = new List<FeedEntity>();
            foreach (var group in parsed.GroupBy(r => r.TrainId))
            {
                var first = group.First();
                ulong? timestamp = null;
                if (first.EventTime is not null
                    && DateTime.TryParseExact(first.EventTime, "MM/dd/yyyy hh:mm:ss tt",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    timestamp = (ulong)new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeSeconds();

                entities.Add(new FeedEntity
                {
                    Id = first.TrainId,
                    Vehicle = new VehiclePosition
                    {
                        Vehicle = new VehicleDescriptor { Id = first.TrainId },
                        Trip = new TripDescriptor { RouteId = first.Line },
                        Position = new Position { Latitude = (float)first.Lat, Longitude = (float)first.Lon },
                        Timestamp = timestamp,
                    }
                });
            }

            logger.LogDebug("MARTA rail source fetched {Count} train entities.", entities.Count);
            return CityFetchResult.FromSources(new FeedMessage { Entities = entities }, 1, 0,
                reportedVehicleObservations: observations);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("MARTA rail source failed; bus path unaffected. Exception type {ExceptionType}.",
                StructuredLogRedactor.SafeExceptionType(ex));
            return CityFetchResult.FromSources(null, 0, 1, reportedVehicleObservations: FailedRailObservation());
        }
    }

    IEnumerable<ReportedVehicleSourceObservation> FailedBusObservation(string code)
    {
        var options = railOptions.Value;
        var source = options.ReportedVehicleCaptureEnabled ? ReportedSource(options.BusReportedVehicleSource) : null;
        return source is null ? [] : [ReportedVehicleSourceObservation.Failed(source, _timeProvider.GetUtcNow(), code)];
    }

    IEnumerable<ReportedVehicleSourceObservation> FailedRailObservation()
    {
        var options = railOptions.Value;
        var source = options.ReportedVehicleCaptureEnabled ? ReportedSource(options.RailReportedVehicleSource) : null;
        return source is null ? [] : [ReportedVehicleSourceObservation.Failed(source, _timeProvider.GetUtcNow(), "parse_failure")];
    }

    static ReportedVehicleSourceDefinition? ReportedSource(ReportedVehicleSourceConfig? config) => config?.ToDefinition();

    ReportedVehicleSourceObservation SuccessfulObservation(ReportedVehicleSourceDefinition source,
        DateTimeOffset receiptUtc, IEnumerable<string?> values) => reportedVehicleBufferBudget is not null
        && reportedVehicleHistoryOptions is not null
            ? ReportedVehicleSourceObservation.Successful(source, receiptUtc, values,
                reportedVehicleHistoryOptions, reportedVehicleBufferBudget)
            : ReportedVehicleSourceObservation.Successful(source, receiptUtc, values);

    static IEnumerable<string?> VehicleDescriptorIds(FeedMessage feed)
    {
        foreach (var entity in feed.Entities)
            if (entity.IsDeleted != true && entity.Vehicle is not null)
                yield return entity.Vehicle.Vehicle?.Id;
    }
}
