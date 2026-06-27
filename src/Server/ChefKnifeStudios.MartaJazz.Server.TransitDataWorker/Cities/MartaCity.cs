using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.RailRealtime;
using ChefKnifeStudios.MartaJazz.Shared.GtfsData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Cities;

public class MartaCity(
    IHttpClientFactory httpClientFactory,
    IOptions<RailRealtimeOptions> railOptions,
    ILogger<MartaCity> logger) : ITransitCity
{
    static readonly JsonSerializerOptions _railJsonOptions = new() { PropertyNameCaseInsensitive = true };
    const string BusUrl = "https://gtfs-rt.itsmarta.com/TMGTFSRealTimeWebService/vehicle/vehiclepositions.pb";

    public string Name => "marta";
    public bool EmitsTelemetry => true;

    public async Task<FeedMessage> FetchVehiclesAsync(CancellationToken ct)
    {
        var busFeed = await FetchBusFeedAsync(ct);
        var railEntities = await FetchRailEntitiesAsync(ct);

        var merged = busFeed ?? new FeedMessage();
        merged.Entities.AddRange(railEntities);
        return merged;
    }

    async Task<FeedMessage?> FetchBusFeedAsync(CancellationToken ct)
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
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return ProtoBuf.Serializer.Deserialize<FeedMessage>(stream);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch MARTA bus GTFS-RT feed.");
            return null;
        }
    }

    async Task<IReadOnlyList<FeedEntity>> FetchRailEntitiesAsync(CancellationToken ct)
    {
        try
        {
            var opts = railOptions.Value;
            if (!opts.Enabled) return Array.Empty<FeedEntity>();

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

            logger.LogInformation("MARTA rail: fetched {Count} train entities.", entities.Count);
            return entities;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MARTA rail fetch failed; bus path unaffected.");
            return Array.Empty<FeedEntity>();
        }
    }
}
