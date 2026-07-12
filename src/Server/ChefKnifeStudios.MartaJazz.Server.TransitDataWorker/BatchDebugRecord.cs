namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker;

/// <summary>
/// Per-vehicle debug snapshot of a single spatial-reconciliation cycle, written to disk
/// for hand-checking the algorithm. Captures both the inputs (raw GPS, prior state) and
/// the outputs (snap result, classification) so the snap decision can be verified offline.
/// </summary>
/// <param name="VehicleId">GTFS vehicle identifier.</param>
/// <param name="RouteJoinKey">Route join key (see RouteShapeProperties.JoinKey) the vehicle is currently snapped to.</param>
/// <param name="Outcome">Classification of this observation: FirstObservation, Moved, or Unchanged.</param>
/// <param name="RawLat">Vehicle latitude reported by the GTFS-RT feed this tick.</param>
/// <param name="RawLon">Vehicle longitude reported by the GTFS-RT feed this tick.</param>
/// <param name="SnappedLat">Latitude of the nearest route point chosen by the snapper.</param>
/// <param name="SnappedLon">Longitude of the nearest route point chosen by the snapper.</param>
/// <param name="SnapDistanceKm">Haversine distance (km) from raw GPS to the chosen snap point.</param>
/// <param name="SnapIndex">Index of the chosen point within the route's point array.</param>
/// <param name="RoutePointCount">Total points searched for this route (denominator for SnapIndex).</param>
/// <param name="PriorRawLat">Raw GPS latitude observed on the prior tick. Null for FirstObservation.</param>
/// <param name="PriorRawLon">Raw GPS longitude observed on the prior tick. Null for FirstObservation.</param>
/// <param name="PriorSnappedLat">Nearest route point latitude recorded on the prior tick. Null for FirstObservation.</param>
/// <param name="PriorSnappedLon">Nearest route point longitude recorded on the prior tick. Null for FirstObservation.</param>
/// <param name="PriorSnapDistanceKm">Snap distance recorded on the prior tick. Null for FirstObservation.</param>
/// <param name="PriorRouteJoinKey">Route join key the vehicle was snapped to on the prior tick. Null for FirstObservation.</param>
/// <param name="PriorObservationUtc">UTC timestamp of the prior observation. Null for FirstObservation.</param>
/// <param name="ObservationUtc">UTC timestamp of this observation (the value passed to the publish record).</param>
/// <param name="DeltaFromPriorSnapKm">Haversine distance (km) between the prior and current snap points. Null for FirstObservation. Useful for spotting bogus jumps.</param>
/// <param name="DeltaFromPriorRawKm">Haversine distance (km) between the prior and current raw GPS positions. Null for FirstObservation. Lets you check that snap movement tracks raw movement.</param>
/// <param name="SecondsSincePriorObservation">Seconds elapsed between this observation and the prior one. Null for FirstObservation.</param>
/// <param name="SpeedMetersPerSec">Vehicle speed reported this tick, if available.</param>
/// <param name="Bearing">Vehicle bearing reported this tick, if available.</param>
public sealed record BatchDebugRecord(
    string VehicleId,
    string RouteJoinKey,
    string Outcome,
    double RawLat,
    double RawLon,
    double SnappedLat,
    double SnappedLon,
    double SnapDistanceKm,
    int SnapIndex,
    int RoutePointCount,
    double? PriorRawLat,
    double? PriorRawLon,
    double? PriorSnappedLat,
    double? PriorSnappedLon,
    double? PriorSnapDistanceKm,
    string? PriorRouteJoinKey,
    DateTime? PriorObservationUtc,
    DateTime ObservationUtc,
    double? DeltaFromPriorSnapKm,
    double? DeltaFromPriorRawKm,
    double? SecondsSincePriorObservation,
    float? SpeedMetersPerSec,
    float? Bearing
);
