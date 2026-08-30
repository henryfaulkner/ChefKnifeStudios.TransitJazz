using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.RailRealtime;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
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
    ILogger<MartaCity> logger) : ITransitCity
{
    static readonly JsonSerializerOptions _railJsonOptions = new() { PropertyNameCaseInsensitive = true };
    const string BusUrl = "https://gtfs-rt.itsmarta.com/TMGTFSRealTimeWebService/vehicle/vehiclepositions.pb";

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
                return CityFetchResult.FromSources(null, 0, 1);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return CityFetchResult.FromSources(ProtoBuf.Serializer.Deserialize<FeedMessage>(stream), 1, 0);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("MARTA bus source failed; exception type {ExceptionType}.",
                StructuredLogRedactor.SafeExceptionType(ex));
            return CityFetchResult.FromSources(null, 0, 1);
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
                           ?? new List<RailArrivalDto>();

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
            return CityFetchResult.FromSources(new FeedMessage { Entities = entities }, 1, 0);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("MARTA rail source failed; bus path unaffected. Exception type {ExceptionType}.",
                StructuredLogRedactor.SafeExceptionType(ex));
            return CityFetchResult.FromSources(null, 0, 1);
        }
    }
}
