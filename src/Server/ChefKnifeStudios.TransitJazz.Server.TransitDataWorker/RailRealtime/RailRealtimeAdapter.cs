using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.RailRealtime;

public interface IRailRealtimeAdapter
{
    /// <summary>Best-effort: never throws; returns empty on failure or when disabled.</summary>
    Task<IReadOnlyList<FeedEntity>> FetchAsync(CancellationToken ct);
}

public class RailRealtimeAdapter(
    IHttpClientFactory httpClientFactory,
    IOptions<RailRealtimeOptions> options,
    ILogger<RailRealtimeAdapter> logger) : IRailRealtimeAdapter
{
    static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<FeedEntity>> FetchAsync(CancellationToken ct)
    {
        try
        {
            var opts = options.Value;
            if (!opts.Enabled)
                return Array.Empty<FeedEntity>();

            var client = httpClientFactory.CreateClient("RailRealtimeApi");
            // BaseAddress is set in Program.cs; append key as query string only if present.
            var requestUri = string.IsNullOrEmpty(opts.ApiKey)
                ? client.BaseAddress!
                : new Uri($"{client.BaseAddress}?apiKey={opts.ApiKey}");
            logger.LogInformation("Rail adapter fetching from {RequestUri}", requestUri);
            var json = await client.GetStringAsync(requestUri, ct);
            var arrivals = JsonSerializer.Deserialize<List<RailArrivalDto>>(json, _jsonOptions)
                           ?? new List<RailArrivalDto>();

            // Realtime filter (FR-004): drop IS_REALTIME != "true" before dedup
            var realtime = arrivals.Where(a =>
                string.Equals(a.IsRealtime?.Trim(), "true", StringComparison.OrdinalIgnoreCase));

            // Parse/skip: drop rows with bad lat/lon or missing TrainId/Line
            int skipped = 0;
            var parsed = new List<(string TrainId, string Line, double Lat, double Lon, string? EventTime)>();
            foreach (var a in realtime)
            {
                if (string.IsNullOrEmpty(a.TrainId) || string.IsNullOrEmpty(a.Line))
                {
                    skipped++;
                    continue;
                }
                if (!double.TryParse(a.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                    || !double.TryParse(a.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                {
                    skipped++;
                    continue;
                }
                parsed.Add((a.TrainId, a.Line, lat, lon, a.EventTime));
            }

            if (skipped > 0)
                logger.LogDebug("Rail adapter skipped {Count} rows with unparseable lat/lon or missing TrainId/Line.", skipped);

            // De-dup + contract guard (FR-003, FR-013): group by TrainId, assert single coord
            var grouped = parsed.GroupBy(r => r.TrainId);
            var entities = new List<FeedEntity>();
            foreach (var group in grouped)
            {
                var first = group.First();
                var distinct = group.Select(r => (r.Lat, r.Lon)).Distinct().ToList();
                if (distinct.Count > 1)
                    logger.LogWarning("Rail live-position contract violated for TRAIN_ID {TrainId}: {Count} distinct coordinates found.", group.Key, distinct.Count);

                // RailTrain → FeedEntity mapping (Entity 3)
                ulong? timestamp = null;
                if (first.EventTime is not null
                    && DateTime.TryParseExact(first.EventTime, "MM/dd/yyyy hh:mm:ss tt",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    timestamp = (ulong)new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeSeconds();
                }

                entities.Add(new FeedEntity
                {
                    Id = first.TrainId,
                    Vehicle = new VehiclePosition
                    {
                        Vehicle = new VehicleDescriptor { Id = first.TrainId },
                        Trip = new TripDescriptor { RouteId = first.Line },
                        Position = new Position
                        {
                            Latitude = (float)first.Lat,
                            Longitude = (float)first.Lon,
                        },
                        Timestamp = timestamp,
                    }
                });
            }

            logger.LogInformation("Rail adapter fetched {EntityCount} train entities from {RowCount} rows ({Skipped} skipped).", entities.Count, arrivals.Count, skipped);
            return entities;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Rail realtime fetch failed; bus path unaffected.");
            return Array.Empty<FeedEntity>();
        }
    }
}
