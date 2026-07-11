using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using Parquet;
using Parquet.Serialization;
using System.Collections.Concurrent;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;

/// <summary>
/// ILoggingService implementation: accumulates <see cref="TelemetryEvent"/> rows in memory,
/// then serialises to parquet (Snappy) and uploads one immutable part-file to Azure Blob
/// on each non-empty flush.
/// </summary>
public sealed class ParquetLoggingService : ILoggingService
{
    readonly LoggingOptions _options;
    readonly ILogger<ParquetLoggingService> _logger;
    readonly BlobContainerClient? _container;

    readonly ConcurrentBag<TelemetryEvent> _buffer = new();

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
        if (e is TelemetryEvent telemetryEvent)
            _buffer.Add(telemetryEvent);
    }

    public async Task FlushAsync(CancellationToken ct)
    {
        var rows = Drain(_buffer);
        if (rows.Count > 0) await FlushAsync(rows, ct);
    }

    static List<T> Drain<T>(ConcurrentBag<T> bag)
    {
        var list = new List<T>();
        while (bag.TryTake(out var item)) list.Add(item);
        return list;
    }

    async Task FlushAsync(List<TelemetryEvent> rows, CancellationToken ct)
    {
        try
        {
            using var ms = new MemoryStream();
            await ParquetSerializer.SerializeAsync(rows, ms, new ParquetSerializerOptions
            {
                CompressionMethod = CompressionMethod.Snappy
            }, ct);

            await UploadAsync(ms, rows.Count, ct);
        }
        catch (Exception ex) { RecordPersistFailure(ex); }
    }

    async Task UploadAsync(MemoryStream ms, int rowCount, CancellationToken ct)
    {
        var blobPath = BuildBlobPath();
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
            _logger.LogInformation("Flushed {Count} telemetry rows to {BlobPath}", rowCount, blobPath);
        }
        else
        {
            _logger.LogDebug("Sidecar disabled or no BlobServiceUri — {Count} telemetry rows not uploaded", rowCount);
        }
    }

    void RecordPersistFailure(Exception ex)
    {
        Interlocked.Increment(ref _persistFailures);
        _logger.LogError(ex, "Sidecar persist failure — swallowed (sidecar_persist_failures={Count})", PersistFailures);
    }

    static string BuildBlobPath()
    {
        var now = DateTime.UtcNow;
        var shortGuid = Guid.NewGuid().ToString("N")[..8];
        // "telemetry/" is the dataset's virtual directory inside the (general-purpose)
        // blob container — the container itself is not necessarily named "telemetry"
        // (prod's Container is "parquet"), so this prefix must stay literal.
        return $"telemetry/dt={now:yyyy-MM-dd}/part-{now:yyyyMMddTHHmmssfffZ}-{shortGuid}.parquet";
    }
}
