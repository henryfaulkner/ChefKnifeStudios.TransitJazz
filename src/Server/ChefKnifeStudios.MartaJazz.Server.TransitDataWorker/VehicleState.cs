namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker;

/// <summary>
/// Tracks a vehicle's most recent nearest route point for delta detection.
/// Stored in a <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/> keyed by vehicle ID.
/// </summary>
/// <param name="NearestLat">Latitude of the nearest route point in degrees.</param>
/// <param name="NearestLon">Longitude of the nearest route point in degrees.</param>
/// <param name="LastUpdated">UTC timestamp of the last state update, used for out-of-order guards and stale pruning.</param>
/// <param name="RouteJoinKey">The route join key (see RouteShapeProperties.JoinKey) the vehicle is currently snapped to.</param>
/// <param name="SpeedMetersPerSec">Vehicle speed from the GTFS-RT feed, if available.</param>
/// <param name="Bearing">Vehicle bearing in degrees from the GTFS-RT feed, if available.</param>
/// <param name="LastSnapDistanceKm">Haversine distance (km) from the vehicle's raw GPS to its nearest route point at the time the state was recorded. Carried for debug output only.</param>
/// <param name="LastRawLat">Raw vehicle latitude reported by the GTFS-RT feed at the time the state was recorded. Carried for debug output only.</param>
/// <param name="LastRawLon">Raw vehicle longitude reported by the GTFS-RT feed at the time the state was recorded. Carried for debug output only.</param>
/// <param name="SnapIndex">Index into the route point array of the last snapped position. Used to constrain the next snap search to a window around this index.</param>
/// <param name="VehicleTimestamp">GTFS-RT per-vehicle sample timestamp (Unix seconds). Used to detect when the upstream feed delivered the same GPS sample on consecutive polls.</param>
public record VehicleState(
    double NearestLat,
    double NearestLon,
    DateTime LastUpdated,
    string RouteJoinKey,
    float? SpeedMetersPerSec,
    float? Bearing,
    double LastSnapDistanceKm,
    double LastRawLat,
    double LastRawLon,
    int SnapIndex,
    ulong? VehicleTimestamp
);
