using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Subway;
using ChefKnifeStudios.MartaJazz.Shared;
using ChefKnifeStudios.MartaJazz.Shared.EventData;
using ChefKnifeStudios.MartaJazz.Shared.GtfsData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Cities;

// Bespoke ITransitCity for NYC — one city, two GTFS-RT feeds merged into a single tick.
// Subway GTFS-RT never carries Position.lat/lon, so line-group feeds are synthesized from
// stop-arrival fields (route/stop_id/current_status/timestamp) via ShapeInterpolator. Bus
// GTFS-RT is ordinary real-GPS protobuf, fetched/normalized via an internal GtfsRtCity built
// from the bus half of CityConfig. Both feeds' entities land in one merged FeedMessage before
// the shared loop and every downstream stage ever sees them — NYC rail and bus are not
// separate cities.
public class NymtaCity(
    IHttpClientFactory httpClientFactory,
    IOptions<SubwaySynthesisOptions> options,
    CityConfig busConfig,
    ILogger<NymtaCity> logger,
    ILogger<GtfsRtCity> busLogger) : ITransitCity
{
    static readonly TimeSpan TableTtl = TimeSpan.FromHours(24);

    readonly GtfsRtCity _busCity = new(busConfig, httpClientFactory, busLogger);

    // Per-train last synthesized distance-along-shape + OUR wall-clock time of that
    // synthesis, so movement is paced by our own tick cadence rather than the RT feed's
    // timestamp (see ShapeInterpolator's doc comment — the feed's timestamp doesn't refresh
    // every poll, so pacing off it could still collapse to a teleport on the first tick a
    // target/status changes). Keyed by the same trainId used to build the synthesized
    // FeedEntity below. Unbounded growth is not a concern at NYC subway's train count (~500
    // in service); entries for trains that stop reporting simply go stale and are naturally
    // overwritten if the ID is reused, never read otherwise.
    readonly Dictionary<string, (double DistanceM, DateTimeOffset AtUtc)> _lastSynthesized = new(StringComparer.Ordinal);

    volatile StopOffsetTable? _table;
    DateTime _fetchedAtUtc;

    public string Name => CityNames.Nymta;
    public bool EmitsTelemetry => true;

    public async Task<FeedMessage> FetchVehiclesAsync(CancellationToken ct)
    {
        var merged = await FetchAndSynthesizeSubwayAsync(ct);

        try
        {
            var busFeed = await _busCity.FetchVehiclesAsync(ct);
            merged.Entities.AddRange(busFeed.Entities);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "NymtaCity: failed to fetch bus GTFS-RT.");
        }

        return merged;
    }

    async Task<FeedMessage> FetchAndSynthesizeSubwayAsync(CancellationToken ct)
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

        var trainId = entity.Vehicle?.Vehicle?.Id ?? entity.Id;
        var status = entity.Vehicle?.CurrentStatus;
        var now = DateTimeOffset.UtcNow;
        var hasPrior = _lastSynthesized.TryGetValue(trainId, out var prior);
        var result = ShapeInterpolator.Synthesize(
            table, route, target, status, entity.Vehicle?.Timestamp,
            now, nominalRunSeconds,
            lastKnownDistanceM: hasPrior ? prior.DistanceM : null,
            lastSynthesizedAtUtc: hasPrior ? prior.AtUtc : null);

        if (!result.Placed) return (null, result.Outcome);

        _lastSynthesized[trainId] = (result.DistanceAlongShapeMeters, now);

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
