using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Subway;
using ChefKnifeStudios.MartaJazz.Shared;
using ChefKnifeStudios.MartaJazz.Shared.EventData;
using ChefKnifeStudios.MartaJazz.Shared.GtfsData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Cities;

// Bespoke ITransitCity for NYC subway (sibling of MartaCity). NYC subway GTFS-RT never
// carries Position.lat/lon — this adapter synthesizes one per train from the stop-arrival
// fields (route/stop_id/current_status/timestamp) before the entity ever reaches Worker,
// so the shared loop and every downstream stage treat it exactly like a bus.
public class NymtaCity(
    IHttpClientFactory httpClientFactory,
    IOptions<SubwaySynthesisOptions> options,
    ILogger<NymtaCity> logger) : ITransitCity
{
    static readonly TimeSpan TableTtl = TimeSpan.FromHours(24);

    volatile StopOffsetTable? _table;
    DateTime _fetchedAtUtc;

    public string Name => CityNames.Nymta;
    public bool EmitsTelemetry => false;

    public async Task<FeedMessage> FetchVehiclesAsync(CancellationToken ct)
    {
        await EnsureTableAsync(ct);

        var table = _table;
        if (table is null)
            return new FeedMessage();

        var merged = new FeedMessage();
        int synthesizedStopped = 0, synthesizedInTransit = 0, skippedUnknownStation = 0;
        var nominalRunSeconds = options.Value.NominalRunSeconds;

        foreach (var url in options.Value.GtfsRtUrls)
        {
            try
            {
                var feed = await FetchFeedAsync(url, ct);
                if (feed is null) continue;

                foreach (var entity in feed.Entities)
                {
                    var (synthesized, outcome) = SynthesizeEntity(table, entity, nominalRunSeconds);
                    switch (outcome)
                    {
                        case SynthesisOutcome.Stopped:
                            synthesizedStopped++;
                            merged.Entities.Add(synthesized!);
                            break;
                        case SynthesisOutcome.InTransit:
                            synthesizedInTransit++;
                            merged.Entities.Add(synthesized!);
                            break;
                        default:
                            skippedUnknownStation++;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "NymtaCity: failed to fetch/synthesize GTFS-RT from {Url}.", url);
            }
        }

        logger.LogInformation(
            "NymtaCity: synthesizedStopped={Stopped}, synthesizedInTransit={InTransit}, skippedUnknownStation={Skipped}.",
            synthesizedStopped, synthesizedInTransit, skippedUnknownStation);

        return merged;
    }

    (FeedEntity? Entity, SynthesisOutcome Outcome) SynthesizeEntity(StopOffsetTable table, FeedEntity entity, double nominalRunSeconds)
    {
        var route = entity.Vehicle?.Trip?.RouteId;
        var target = entity.Vehicle?.StopId;
        if (string.IsNullOrEmpty(route) || string.IsNullOrEmpty(target))
            return (null, SynthesisOutcome.SkippedUnknownStation);

        var status = entity.Vehicle?.CurrentStatus;
        var result = ShapeInterpolator.Synthesize(
            table, route, target, status, entity.Vehicle?.Timestamp,
            DateTimeOffset.UtcNow, nominalRunSeconds);

        if (!result.Placed) return (null, result.Outcome);

        var trainId = entity.Vehicle?.Vehicle?.Id ?? entity.Id;
        var synthesized = new FeedEntity
        {
            Id = trainId,
            Vehicle = new VehiclePosition
            {
                Vehicle = new VehicleDescriptor { Id = trainId },
                Trip = new TripDescriptor { RouteId = route },
                Position = new Position { Latitude = (float)result.Lat, Longitude = (float)result.Lon },
                Timestamp = entity.Vehicle?.Timestamp,
                CurrentStatus = status,
            }
        };
        return (synthesized, result.Outcome);
    }

    async Task EnsureTableAsync(CancellationToken ct)
    {
        if (_table is not null && DateTime.UtcNow - _fetchedAtUtc <= TableTtl) return;

        try
        {
            var client = httpClientFactory.CreateClient("RouteShapeApi");
            var response = await client.GetAsync(
                $"{ApiEndpoints.Gtfs.GetSubwayStopOffsets}?city={CityNames.Nymta}", ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var sets = JsonSerializer.Deserialize<List<SubwayStopOffsetSet>>(json, JsonOptions.Get());

            if (sets is null || sets.Count == 0)
            {
                logger.LogWarning("NymtaCity: stop-offsets endpoint returned no data; keeping existing cache.");
                return;
            }

            _table = new StopOffsetTable(sets);
            _fetchedAtUtc = DateTime.UtcNow;
            logger.LogInformation("NymtaCity: fetched subway stop-offsets ({Count} sets).", sets.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "NymtaCity: failed to fetch subway stop-offsets; keeping existing cache if any.");
        }
    }

    async Task<FeedMessage?> FetchFeedAsync(string url, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TransitJazz", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("NymtaCity: GTFS-RT feed {Url} returned {StatusCode}.", url, response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return ProtoBuf.Serializer.Deserialize<FeedMessage>(stream);
    }
}
