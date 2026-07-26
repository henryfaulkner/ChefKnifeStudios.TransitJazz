using System;
using System.Collections.Generic;
using MessagePack;

namespace ChefKnifeStudios.TransitJazz.Shared.Events;

/// <summary>
/// SignalR event containing a batch of vehicles that moved to a different nearest route point.
/// Emitted once per poll cycle by the V2 spatial reconciliation pass.
/// </summary>
/// <param name="BatchRecords">The set of vehicle route-point transitions detected in this cycle.</param>
[MessagePackObject]
public sealed record RouteNearestPointBatchEvent(
    [property: Key(0)] IEnumerable<RouteNearestPointBatchEvent.RouteNearestPointRecord> BatchRecords
) : ISignalREvent
{
    /// <summary>
    /// A single vehicle's transition from one nearest route point to another.
    /// </summary>
    /// <param name="VehicleId">GTFS vehicle identifier.</param>
    /// <param name="RouteJoinKey">The route join key (see RouteShapeProperties.JoinKey) the vehicle is currently snapped to.</param>
    /// <param name="PriorNearestLat">Latitude of the previous nearest route point. Rounded to 5 decimals (~1.1 m) on the wire.</param>
    /// <param name="PriorNearestLon">Longitude of the previous nearest route point. Rounded to 5 decimals (~1.1 m) on the wire.</param>
    /// <param name="CurrentNearestLat">Latitude of the current nearest route point. Rounded to 5 decimals (~1.1 m) on the wire.</param>
    /// <param name="CurrentNearestLon">Longitude of the current nearest route point. Rounded to 5 decimals (~1.1 m) on the wire.</param>
    /// <param name="DurationMs">Elapsed milliseconds between the prior and current observation — the client tween length. Replaces the two full-precision UTC timestamps the client only ever subtracted (payload thinning; see feature 040). 0 on a vehicle's first observation, which the client renders as an instant snap-into-place.</param>
    /// <param name="SpeedMetersPerSec">Vehicle speed from the GTFS-RT feed, if available.</param>
    /// <param name="Bearing">Vehicle bearing in degrees (0-360) from the GTFS-RT feed, if available.</param>
    /// <param name="IsStale">True when this record reflects an upstream GTFS-RT sample whose per-vehicle timestamp matches the prior observation — i.e. the feed delivered the same GPS reading twice. Clients should keep extrapolating from the last empirical speed but should NOT append this snap to their motion history.</param>
    /// <param name="Category">The vehicle's per-city category key (e.g. "bus", "rail", "streetcar", "unknown"), classified by WebAPI from GTFS route_type and carried transitively through the Worker's route index.</param>
    [MessagePackObject]
    public sealed record RouteNearestPointRecord(
        [property: Key(0)] string VehicleId,
        [property: Key(1)] string RouteJoinKey,
        [property: Key(2)] double PriorNearestLat,
        [property: Key(3)] double PriorNearestLon,
        [property: Key(4)] double CurrentNearestLat,
        [property: Key(5)] double CurrentNearestLon,
        [property: Key(6)] int DurationMs,
        [property: Key(7)] float? SpeedMetersPerSec,
        [property: Key(8)] float? Bearing,
        [property: Key(9)] bool IsStale,
        [property: Key(10)] string Category = "bus"
    );
}
