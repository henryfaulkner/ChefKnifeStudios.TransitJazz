namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;

/// <summary>
/// Telemetry event posted once per completed spatial-reconciliation cycle.
/// Carries per-cycle vehicle-outcome counts, feed metadata, cache sizes,
/// and sidecar self-health counters (FR-005, FR-012).
/// </summary>
public sealed class CycleEventArgs : LogEventArgs
{
    public required DateTime CycleStartUtc { get; init; }
    public required DateTime CycleEndUtc { get; init; }
    public required double CycleExecutionSeconds { get; init; }

    public required int BusesProcessed { get; init; }
    public required int BusesMoved { get; init; }
    public required int BusesUnchanged { get; init; }
    public required int BusesStationary { get; init; }
    public required int BusesStale { get; init; }
    public required int BusesSkippedNoRouteId { get; init; }
    public required int BusesSkippedUnknownRoute { get; init; }

    /// <summary>GTFS-RT feed header timestamp (Unix seconds), or null if absent.</summary>
    public long? FeedHeaderTs { get; init; }
    public required bool DuplicateFeed { get; init; }

    public required int LastUpdateCacheSize { get; init; }
    public required int VehicleStateCacheSize { get; init; }

    /// <summary>Comma-separated distinct route display IDs seen in this cycle (skipped routes excluded).</summary>
    public required string ActiveRouteIds { get; init; }
    /// <summary>Comma-separated distinct vehicle display IDs processed in this cycle (skipped vehicles excluded).</summary>
    public required string ActiveVehicleIds { get; init; }

    // ── Sidecar self-health ────────────────────────────────────────────────
    public required int SidecarBufferOccupancy { get; init; }
    public required long SidecarDroppedRecords { get; init; }
    public required long SidecarPersistFailures { get; init; }
}
