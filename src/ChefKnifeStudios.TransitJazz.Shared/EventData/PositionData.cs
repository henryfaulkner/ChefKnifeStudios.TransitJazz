namespace ChefKnifeStudios.TransitJazz.Shared.EventData;

public sealed record PositionData(
    float Latitude,
    float Longitude,
    float? Bearing,
    float? SpeedMetersPerSec,
    double? OdometerMeters,
    long? Timestamp,
    uint? CurrentStopSequence,
    string? CurrentStopId,
    VehicleStopStatus? CurrentStatus,
    CongestionLevel? CongestionLevel
);

public enum VehicleStopStatus
{
    IncomingAt = 0,
    StoppedAt = 1,
    InTransitTo = 2
}

public enum CongestionLevel
{
    UnknownCongestionLevel = 0,
    RunningSmoothly = 1,
    StopAndGo = 2,
    Congestion = 3,
    SevereCongestion = 4
}
