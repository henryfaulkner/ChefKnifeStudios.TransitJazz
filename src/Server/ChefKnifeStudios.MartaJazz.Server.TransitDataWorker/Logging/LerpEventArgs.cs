namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;

/// <summary>
/// Per-vehicle delta computation posted for vehicles that have a prior observation this cycle (FR-007).
/// Captures the prior state and the four deltas needed by downstream analytics.
/// </summary>
public sealed class LerpEventArgs : LogEventArgs
{
    public required DateTime ObservationUtc { get; init; }
    public required string VehicleId { get; init; }

    // ── Prior state ────────────────────────────────────────────────────────
    public required string PriorRouteId { get; init; }
    public required double PriorSnappedLat { get; init; }
    public required double PriorSnappedLon { get; init; }
    public required DateTime PriorObservationUtc { get; init; }
    public double? PriorSpeedMps { get; init; }
    public double? PriorBearingDeg { get; init; }

    // ── Deltas ────────────────────────────────────────────────────────────
    public required double PosDeltaKm { get; init; }
    public double? SpeedDelta { get; init; }
    public double? BearingDelta { get; init; }
    public required double TimeDeltaSec { get; init; }
}
