using System;
using System.Collections.Generic;

namespace ChefKnifeStudios.MartaJazz.Shared.Events;

public enum TransitMode { Bus = 0, Rail = 1 }

/// <summary>
/// SignalR event containing a batch of vehicles that moved to a different nearest route point.
/// Emitted once per poll cycle by the V2 spatial reconciliation pass.
/// </summary>
/// <param name="BatchRecords">The set of vehicle route-point transitions detected in this cycle.</param>
public sealed record RouteNearestPointBatchEvent(
    IEnumerable<RouteNearestPointBatchEvent.RouteNearestPointRecord> BatchRecords
) : ISignalREvent
{
    /// <summary>
    /// A single vehicle's transition from one nearest route point to another.
    /// </summary>
    /// <param name="VehicleId">GTFS vehicle identifier.</param>
    /// <param name="RouteJoinKey">The route join key (see RouteShapeProperties.JoinKey) the vehicle is currently snapped to.</param>
    /// <param name="PriorNearestLat">Latitude of the previous nearest route point.</param>
    /// <param name="PriorNearestLon">Longitude of the previous nearest route point.</param>
    /// <param name="PriorUtcNow">UTC timestamp when the previous nearest point was recorded.</param>
    /// <param name="CurrentNearestLat">Latitude of the current nearest route point.</param>
    /// <param name="CurrentNearestLon">Longitude of the current nearest route point.</param>
    /// <param name="CurrentUtcNow">UTC timestamp of this observation.</param>
    /// <param name="SpeedMetersPerSec">Vehicle speed from the GTFS-RT feed, if available.</param>
    /// <param name="Bearing">Vehicle bearing in degrees (0-360) from the GTFS-RT feed, if available.</param>
    /// <param name="IsStale">True when this record reflects an upstream GTFS-RT sample whose per-vehicle timestamp matches the prior observation — i.e. the feed delivered the same GPS reading twice. Clients should keep extrapolating from the last empirical speed but should NOT append this snap to their motion history.</param>
    public sealed record RouteNearestPointRecord(
        string VehicleId,
        string RouteJoinKey,
        double PriorNearestLat,
        double PriorNearestLon,
        DateTime PriorUtcNow,
        double CurrentNearestLat,
        double CurrentNearestLon,
        DateTime CurrentUtcNow,
        float? SpeedMetersPerSec,
        float? Bearing,
        bool IsStale,
        TransitMode TransitMode = TransitMode.Bus
    );
}
