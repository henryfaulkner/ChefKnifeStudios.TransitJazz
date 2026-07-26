namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;

/// <summary>
/// Sink abstraction consumed by <see cref="LogEventWorker"/>.
/// Accumulates event-args rows in memory and flushes them to parquet/blob on demand.
/// </summary>
public interface ILoggingService
{
    /// <summary>Route an event to its in-memory buffer. Must be cheap (no I/O).</summary>
    void Accumulate(IEventArgs e);

    /// <summary>Serialize and upload all non-empty buffers; clear them on success.</summary>
    Task FlushAsync(CancellationToken ct);

    /// <summary>Running count of events dropped by load-shedding (DropWrite overflow).</summary>
    long DroppedRecords { get; }

    /// <summary>Running count of blob-upload failures swallowed by the sidecar.</summary>
    long PersistFailures { get; }
}
