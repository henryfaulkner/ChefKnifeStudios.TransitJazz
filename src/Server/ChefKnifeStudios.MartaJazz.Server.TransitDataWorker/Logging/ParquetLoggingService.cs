using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using System.Collections.Concurrent;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;

/// <summary>
/// ILoggingService implementation: accumulates rows per dataset in memory,
/// then serialises to parquet (Snappy) and uploads one immutable part-file per
/// non-empty dataset to Azure Blob on each flush.
/// </summary>
public sealed class ParquetLoggingService : ILoggingService
{
    readonly LoggingOptions _options;
    readonly ILogger<ParquetLoggingService> _logger;
    readonly BlobContainerClient? _container;

    readonly ConcurrentBag<SnapEventArgs> _snapBuffer = new();
    readonly ConcurrentBag<LerpEventArgs> _lerpBuffer = new();
    readonly ConcurrentBag<CycleEventArgs> _cycleBuffer = new();

    long _persistFailures;
    int _containerEnsured; // 0 = not yet ensured, 1 = ensured (Interlocked guard)

    public long DroppedRecords => 0;
    public long PersistFailures => Interlocked.Read(ref _persistFailures);

    public ParquetLoggingService(IOptions<LoggingOptions> options, ILogger<ParquetLoggingService> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (_options.Enabled)
        {
            BlobServiceClient? serviceClient = null;

            if (!string.IsNullOrWhiteSpace(_options.ConnectionString))
                serviceClient = new BlobServiceClient(_options.ConnectionString);
            else if (!string.IsNullOrWhiteSpace(_options.BlobServiceUri))
                serviceClient = new BlobServiceClient(new Uri(_options.BlobServiceUri), new DefaultAzureCredential());

            _container = serviceClient?.GetBlobContainerClient(_options.Container);

            if (_container == null)
            {
                // Misconfiguration: the sidecar is Enabled but has no blob target.
                // Warn loudly — this is the silent failure mode that produces zero blobs.
                _logger.LogWarning(
                    "Logging sidecar is Enabled but no blob target is configured " +
                    "(set Logging:Telemetry:BlobServiceUri or :ConnectionString). Telemetry will NOT be uploaded.");
            }
        }
    }

    public void Accumulate(IEventArgs e)
    {
        switch (e)
        {
            case SnapEventArgs snap: _snapBuffer.Add(snap); break;
            case LerpEventArgs lerp: _lerpBuffer.Add(lerp); break;
            case CycleEventArgs cycle: _cycleBuffer.Add(cycle); break;
        }
    }

    public async Task FlushAsync(CancellationToken ct)
    {
        var snapRows = Drain(_snapBuffer);
        var lerpRows = Drain(_lerpBuffer);
        var cycleRows = Drain(_cycleBuffer);

        if (snapRows.Count > 0) await FlushSnapAsync(snapRows, ct);
        if (lerpRows.Count > 0) await FlushLerpAsync(lerpRows, ct);
        if (cycleRows.Count > 0) await FlushCycleAsync(cycleRows, ct);
    }

    static List<T> Drain<T>(ConcurrentBag<T> bag)
    {
        var list = new List<T>();
        while (bag.TryTake(out var item)) list.Add(item);
        return list;
    }

    async Task FlushSnapAsync(List<SnapEventArgs> rows, CancellationToken ct)
    {
        try
        {
            var schema = new ParquetSchema(
                new DataField<string>(TelemetryColumns.CycleId),
                new DataField<DateTime>(TelemetryColumns.ObservationUtc),
                new DataField<string>(TelemetryColumns.VehicleId),
                new DataField<string>(TelemetryColumns.RouteId),
                new DataField<string>(TelemetryColumns.SnapOutcome),
                new DataField<double>(TelemetryColumns.RawLat),
                new DataField<double>(TelemetryColumns.RawLon),
                new DataField<double>(TelemetryColumns.SnappedLat),
                new DataField<double>(TelemetryColumns.SnappedLon),
                new DataField<double>(TelemetryColumns.SnapDistanceKm),
                new DataField<int>(TelemetryColumns.SnapIndex),
                new DataField<int>(TelemetryColumns.RoutePointCount),
                new DataField<double?>(TelemetryColumns.SpeedMps),
                new DataField<double?>(TelemetryColumns.BearingDeg),
                new DataField<bool>(TelemetryColumns.IsStale)
            );

            using var ms = new MemoryStream();
            using (var writer = await ParquetWriter.CreateAsync(schema, ms))
            {
                writer.CompressionMethod = CompressionMethod.Snappy;
                using var rg = writer.CreateRowGroup();
                var f = schema.DataFields;
                await rg.WriteColumnAsync(new DataColumn(f[0], rows.Select(r => r.CycleId).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[1], rows.Select(r => r.ObservationUtc).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[2], rows.Select(r => r.VehicleId).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[3], rows.Select(r => r.RouteId).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[4], rows.Select(r => r.SnapOutcome).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[5], rows.Select(r => r.RawLat).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[6], rows.Select(r => r.RawLon).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[7], rows.Select(r => r.SnappedLat).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[8], rows.Select(r => r.SnappedLon).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[9], rows.Select(r => r.SnapDistanceKm).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[10], rows.Select(r => r.SnapIndex).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[11], rows.Select(r => r.RoutePointCount).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[12], rows.Select(r => r.SpeedMps).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[13], rows.Select(r => r.BearingDeg).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[14], rows.Select(r => r.IsStale).ToArray()));
            }

            await UploadAsync("snap", ms, rows.Count, ct);
        }
        catch (Exception ex) { RecordPersistFailure("snap", ex); }
    }

    async Task FlushLerpAsync(List<LerpEventArgs> rows, CancellationToken ct)
    {
        try
        {
            var schema = new ParquetSchema(
                new DataField<string>(TelemetryColumns.CycleId),
                new DataField<DateTime>(TelemetryColumns.ObservationUtc),
                new DataField<string>(TelemetryColumns.VehicleId),
                new DataField<string>(TelemetryColumns.PriorRouteId),
                new DataField<double>(TelemetryColumns.PriorSnappedLat),
                new DataField<double>(TelemetryColumns.PriorSnappedLon),
                new DataField<DateTime>(TelemetryColumns.PriorObservationUtc),
                new DataField<double?>(TelemetryColumns.PriorSpeedMps),
                new DataField<double?>(TelemetryColumns.PriorBearingDeg),
                new DataField<double>(TelemetryColumns.PosDeltaKm),
                new DataField<double?>(TelemetryColumns.SpeedDelta),
                new DataField<double?>(TelemetryColumns.BearingDelta),
                new DataField<double>(TelemetryColumns.TimeDeltaSec)
            );

            using var ms = new MemoryStream();
            using (var writer = await ParquetWriter.CreateAsync(schema, ms))
            {
                writer.CompressionMethod = CompressionMethod.Snappy;
                using var rg = writer.CreateRowGroup();
                var f = schema.DataFields;
                await rg.WriteColumnAsync(new DataColumn(f[0], rows.Select(r => r.CycleId).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[1], rows.Select(r => r.ObservationUtc).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[2], rows.Select(r => r.VehicleId).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[3], rows.Select(r => r.PriorRouteId).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[4], rows.Select(r => r.PriorSnappedLat).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[5], rows.Select(r => r.PriorSnappedLon).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[6], rows.Select(r => r.PriorObservationUtc).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[7], rows.Select(r => r.PriorSpeedMps).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[8], rows.Select(r => r.PriorBearingDeg).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[9], rows.Select(r => r.PosDeltaKm).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[10], rows.Select(r => r.SpeedDelta).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[11], rows.Select(r => r.BearingDelta).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[12], rows.Select(r => r.TimeDeltaSec).ToArray()));
            }

            await UploadAsync("lerp", ms, rows.Count, ct);
        }
        catch (Exception ex) { RecordPersistFailure("lerp", ex); }
    }

    async Task FlushCycleAsync(List<CycleEventArgs> rows, CancellationToken ct)
    {
        try
        {
            var schema = new ParquetSchema(
                new DataField<string>(TelemetryColumns.CycleId),
                new DataField<DateTime>(TelemetryColumns.CycleStartUtc),
                new DataField<DateTime>(TelemetryColumns.CycleEndUtc),
                new DataField<double>(TelemetryColumns.CycleExecutionSeconds),
                new DataField<int>(TelemetryColumns.BusesProcessed),
                new DataField<int>(TelemetryColumns.BusesMoved),
                new DataField<int>(TelemetryColumns.BusesUnchanged),
                new DataField<int>(TelemetryColumns.BusesStationary),
                new DataField<int>(TelemetryColumns.BusesStale),
                new DataField<int>(TelemetryColumns.BusesSkippedNoRouteId),
                new DataField<int>(TelemetryColumns.BusesSkippedUnknownRoute),
                new DataField<long?>(TelemetryColumns.FeedHeaderTs),
                new DataField<bool>(TelemetryColumns.DuplicateFeed),
                new DataField<string>(TelemetryColumns.ActiveRouteIds),
                new DataField<string>(TelemetryColumns.ActiveVehicleIds),
                new DataField<int>(TelemetryColumns.LastUpdateCacheSize),
                new DataField<int>(TelemetryColumns.VehicleStateCacheSize),
                new DataField<int>(TelemetryColumns.SidecarBufferOccupancy),
                new DataField<long>(TelemetryColumns.SidecarDroppedRecords),
                new DataField<long>(TelemetryColumns.SidecarPersistFailures)
            );

            using var ms = new MemoryStream();
            using (var writer = await ParquetWriter.CreateAsync(schema, ms))
            {
                writer.CompressionMethod = CompressionMethod.Snappy;
                using var rg = writer.CreateRowGroup();
                var f = schema.DataFields;
                await rg.WriteColumnAsync(new DataColumn(f[0], rows.Select(r => r.CycleId).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[1], rows.Select(r => r.CycleStartUtc).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[2], rows.Select(r => r.CycleEndUtc).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[3], rows.Select(r => r.CycleExecutionSeconds).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[4], rows.Select(r => r.BusesProcessed).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[5], rows.Select(r => r.BusesMoved).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[6], rows.Select(r => r.BusesUnchanged).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[7], rows.Select(r => r.BusesStationary).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[8], rows.Select(r => r.BusesStale).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[9], rows.Select(r => r.BusesSkippedNoRouteId).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[10], rows.Select(r => r.BusesSkippedUnknownRoute).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[11], rows.Select(r => r.FeedHeaderTs).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[12], rows.Select(r => r.DuplicateFeed).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[13], rows.Select(r => r.ActiveRouteIds).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[14], rows.Select(r => r.ActiveVehicleIds).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[15], rows.Select(r => r.LastUpdateCacheSize).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[16], rows.Select(r => r.VehicleStateCacheSize).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[17], rows.Select(r => r.SidecarBufferOccupancy).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[18], rows.Select(r => r.SidecarDroppedRecords).ToArray()));
                await rg.WriteColumnAsync(new DataColumn(f[19], rows.Select(r => r.SidecarPersistFailures).ToArray()));
            }

            await UploadAsync("cycle", ms, rows.Count, ct);
        }
        catch (Exception ex) { RecordPersistFailure("cycle", ex); }
    }

    async Task UploadAsync(string dataset, MemoryStream ms, int rowCount, CancellationToken ct)
    {
        var blobPath = BuildBlobPath(dataset);
        ms.Position = 0;

        if (_container != null)
        {
            // Create the container on first use so a missing container doesn't throw
            // ContainerNotFound (which would otherwise be swallowed as a persist failure).
            if (Interlocked.CompareExchange(ref _containerEnsured, 1, 0) == 0)
            {
                await _container.CreateIfNotExistsAsync(cancellationToken: ct);
            }

            var blobClient = _container.GetBlobClient(blobPath);
            await blobClient.UploadAsync(ms, overwrite: false, ct);
            _logger.LogInformation("Flushed {Count} {Dataset} rows to {BlobPath}", rowCount, dataset, blobPath);
        }
        else
        {
            _logger.LogDebug("Sidecar disabled or no BlobServiceUri — {Count} {Dataset} rows not uploaded", rowCount, dataset);
        }
    }

    void RecordPersistFailure(string dataset, Exception ex)
    {
        Interlocked.Increment(ref _persistFailures);
        _logger.LogError(ex, "Sidecar persist failure for dataset {Dataset} — swallowed (sidecar_persist_failures={Count})",
            dataset, PersistFailures);
    }

    static string BuildBlobPath(string dataset)
    {
        var now = DateTime.UtcNow;
        var shortGuid = Guid.NewGuid().ToString("N")[..8];
        return $"{dataset}/dt={now:yyyy-MM-dd}/part-{now:yyyyMMddTHHmmssfffZ}-{shortGuid}.parquet";
    }
}
