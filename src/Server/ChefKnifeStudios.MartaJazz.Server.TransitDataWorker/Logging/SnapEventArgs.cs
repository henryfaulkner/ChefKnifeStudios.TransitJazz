namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;

/// <summary>Per-vehicle snap decision posted once per entity per cycle (FR-006).</summary>
public sealed class SnapEventArgs : LogEventArgs
{
    public required DateTime ObservationUtc { get; init; }
    public required string VehicleId { get; init; }
    public required string RouteId { get; init; }

    /// <summary>String name of the <see cref="SnapDecision"/> enum member (FR-008).</summary>
    public required string SnapOutcome { get; init; }

    public required double RawLat { get; init; }
    public required double RawLon { get; init; }
    public required double SnappedLat { get; init; }
    public required double SnappedLon { get; init; }
    public required double SnapDistanceKm { get; init; }
    public required int SnapIndex { get; init; }
    public required int RoutePointCount { get; init; }

    public double? SpeedMps { get; init; }
    public double? BearingDeg { get; init; }
    public required bool IsStale { get; init; }
}

/// <summary>Classification of a per-vehicle snap observation.</summary>
public enum SnapDecision
{
    FirstObservation,
    Moved,
    Unchanged,
    Stationary,
    Stale
}
