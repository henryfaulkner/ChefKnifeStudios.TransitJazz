using Azure.Identity;
using Azure.Storage.Blobs;
using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.MartaJazz.Shared;
using ChefKnifeStudios.MartaJazz.Shared.TelemetryData;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Parquet.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.MartaJazz.Server.WebAPI.EndpointGroups;

public static class TelemetryEndpoints
{
    const string CacheKeyPrefix = "telemetry-today:";
    static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public static IEndpointRouteBuilder MapTelemetryEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup(string.Empty)
            .WithName(nameof(ApiEndpoints.Telemetry))
            .WithTags(nameof(ApiEndpoints.Telemetry));

        group.MapGet(ApiEndpoints.Telemetry.GetTodaySummary, async (
            [FromServices] IOptions<LoggingOptions> options,
            [FromServices] IMemoryCache cache,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger(nameof(TelemetryEndpoints));
            var rows = await GetTodayRowsAsync(options.Value, cache, logger, ct);

            var tables = rows
                .GroupBy(r => (r.EventType, r.CityName))
                .Select(g => new TelemetryTableSummaryDto
                {
                    EventType = g.Key.EventType,
                    CityName = g.Key.CityName,
                    TotalCount = g.Count()
                })
                .OrderBy(t => t.EventType)
                .ThenBy(t => t.CityName)
                .ToList();

            return Results.Ok(new TelemetryTodaySummaryDto { Tables = tables });
        })
        .WithName(nameof(ApiEndpoints.Telemetry.GetTodaySummary))
        .Produces<TelemetryTodaySummaryDto>(StatusCodes.Status200OK);

        group.MapGet(ApiEndpoints.Telemetry.GetToday, async (
            [FromQuery] string eventType,
            [FromQuery] string? city,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromQuery] TelemetrySortColumn sortBy,
            [FromQuery] bool sortDesc,
            [FromServices] IOptions<LoggingOptions> options,
            [FromServices] IMemoryCache cache,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger(nameof(TelemetryEndpoints));
            var rows = await GetTodayRowsAsync(options.Value, cache, logger, ct);

            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize <= 0 ? 25 : pageSize, 1, 200);

            var matchingList = Sort(
                rows.Where(r => r.EventType == eventType && r.CityName == city),
                sortBy, sortDesc).ToList();

            var pageItems = matchingList
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Results.Ok(new TelemetryPageDto
            {
                Items = pageItems,
                TotalCount = matchingList.Count,
                Page = page,
                PageSize = pageSize
            });
        })
        .WithName(nameof(ApiEndpoints.Telemetry.GetToday))
        .Produces<TelemetryPageDto>(StatusCodes.Status200OK);

        return builder;
    }

    static IEnumerable<TelemetryEventDto> Sort(IEnumerable<TelemetryEventDto> rows, TelemetrySortColumn sortBy, bool sortDesc) => sortBy switch
    {
        TelemetrySortColumn.EventId => OrderBy(rows, r => r.EventId, sortDesc),
        TelemetrySortColumn.CityName => OrderBy(rows, r => r.CityName, sortDesc),
        TelemetrySortColumn.FeedFreshnessSeconds => OrderBy(rows, r => r.FeedFreshnessSeconds, sortDesc),
        TelemetrySortColumn.CitiesProcessedCount => OrderBy(rows, r => r.CitiesProcessedCount, sortDesc),
        TelemetrySortColumn.CitiesProcessedCsv => OrderBy(rows, r => r.CitiesProcessedCsv, sortDesc),
        TelemetrySortColumn.TimeTakenSeconds => OrderBy(rows, r => r.TimeTakenSeconds, sortDesc),
        TelemetrySortColumn.HealthOk => OrderBy(rows, r => r.HealthOk, sortDesc),
        TelemetrySortColumn.TonesEmitted => OrderBy(rows, r => r.TonesEmitted, sortDesc),
        TelemetrySortColumn.VehiclesProcessed => OrderBy(rows, r => r.VehiclesProcessed, sortDesc),
        TelemetrySortColumn.GcHeapBytes => OrderBy(rows, r => r.GcHeapBytes, sortDesc),
        TelemetrySortColumn.ProcessWorkingSetBytes => OrderBy(rows, r => r.ProcessWorkingSetBytes, sortDesc),
        TelemetrySortColumn.VehicleStateCacheSize => OrderBy(rows, r => r.VehicleStateCacheSize, sortDesc),
        TelemetrySortColumn.CrossingBaselineCacheSize => OrderBy(rows, r => r.CrossingBaselineCacheSize, sortDesc),
        TelemetrySortColumn.RouteIndexSize => OrderBy(rows, r => r.RouteIndexSize, sortDesc),
        TelemetrySortColumn.RouteTriggerPointCacheSize => OrderBy(rows, r => r.RouteTriggerPointCacheSize, sortDesc),
        _ => OrderBy(rows, r => r.ObservationUtc, sortDesc),
    };

    static IEnumerable<TelemetryEventDto> OrderBy<TKey>(IEnumerable<TelemetryEventDto> rows, Func<TelemetryEventDto, TKey> keySelector, bool desc) =>
        desc ? rows.OrderByDescending(keySelector) : rows.OrderBy(keySelector);

    /// <summary>Deserialized rows for "today" (UTC), cached briefly so paging through a table doesn't
    /// re-download/re-parse every blob on each click (FR: server-side paging without a per-page blob read).</summary>
    static Task<List<TelemetryEventDto>> GetTodayRowsAsync(LoggingOptions options, IMemoryCache cache, ILogger logger, CancellationToken ct)
    {
        var cacheKey = $"{CacheKeyPrefix}{DateTime.UtcNow:yyyy-MM-dd}";
        return cache.GetOrCreateAsync(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return ReadTodayAsync(options, logger, ct);
        })!;
    }

    static async Task<List<TelemetryEventDto>> ReadTodayAsync(LoggingOptions options, ILogger logger, CancellationToken ct)
    {
        var results = new List<TelemetryEventDto>();

        if (!options.Enabled)
            return results;

        BlobServiceClient? serviceClient = null;
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            serviceClient = new BlobServiceClient(options.ConnectionString);
        else if (!string.IsNullOrWhiteSpace(options.BlobServiceUri))
            serviceClient = new BlobServiceClient(new Uri(options.BlobServiceUri), new DefaultAzureCredential());

        if (serviceClient is null)
        {
            logger.LogWarning("TelemetryEndpoints: no blob target configured (Logging:Telemetry:BlobServiceUri/ConnectionString).");
            return results;
        }

        var container = serviceClient.GetBlobContainerClient(options.Container);
        var prefix = $"telemetry/dt={DateTime.UtcNow:yyyy-MM-dd}/";

        await foreach (var blobItem in container.GetBlobsAsync(
            traits: Azure.Storage.Blobs.Models.BlobTraits.None,
            states: Azure.Storage.Blobs.Models.BlobStates.None,
            prefix: prefix,
            cancellationToken: ct))
        {
            try
            {
                var blobClient = container.GetBlobClient(blobItem.Name);
                using var stream = new MemoryStream();
                await blobClient.DownloadToAsync(stream, ct);
                stream.Position = 0;

                var rows = await ParquetSerializer.DeserializeAsync<TelemetryEvent>(stream, cancellationToken: ct);
                results.AddRange(rows.Select(ToDto));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "TelemetryEndpoints: failed to read {Blob}, skipping.", blobItem.Name);
            }
        }

        return results;
    }

    static TelemetryEventDto ToDto(TelemetryEvent e) => new()
    {
        EventType = e.event_type,
        EventId = e.event_id,
        ObservationUtc = e.observation_utc,
        CityName = e.city_name,
        FeedFreshnessSeconds = e.feed_freshness_seconds,
        CitiesProcessedCount = e.cities_processed_count,
        CitiesProcessedCsv = e.cities_processed_csv,
        TimeTakenSeconds = e.time_taken_seconds,
        HealthOk = e.health_ok,
        TonesEmitted = e.tones_emitted,
        VehiclesProcessed = e.vehicles_processed,
        GcHeapBytes = e.gc_heap_bytes,
        ProcessWorkingSetBytes = e.process_working_set_bytes,
        VehicleStateCacheSize = e.vehicle_state_cache_size,
        CrossingBaselineCacheSize = e.crossing_baseline_cache_size,
        RouteIndexSize = e.route_index_size,
        RouteTriggerPointCacheSize = e.route_trigger_point_cache_size,
    };
}
