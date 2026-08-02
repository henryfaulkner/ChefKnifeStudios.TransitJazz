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
    /// <param name="PriorNearestLatE5">Latitude of the previous nearest route point, in degrees x 1e5. <see langword="null"/> on steady-state records — the client animates from its own retained last-known position instead. Non-null only on a vehicle's first observation or when its <paramref name="RouteJoinKey"/> changed. Atomic with <paramref name="PriorNearestLonE5"/>: both null or both non-null, never one.</param>
    /// <param name="PriorNearestLonE5">Longitude of the previous nearest route point, in degrees x 1e5. Null exactly when <paramref name="PriorNearestLatE5"/> is null.</param>
    /// <param name="CurrentNearestLatE5">Latitude of the current nearest route point, in degrees x 1e5 — i.e. <c>(int)Math.Round(lat * 100_000)</c>. Exact for the 5-decimal (~1.1 m) precision the wire already carried as a double. Range +/-9,000,000.</param>
    /// <param name="CurrentNearestLonE5">Longitude of the current nearest route point, in degrees x 1e5. Range +/-18,000,000.</param>
    /// <param name="DurationMs">Elapsed milliseconds between the prior and current observation — the client tween length. Replaces the two full-precision UTC timestamps the client only ever subtracted (payload thinning; see feature 040). 0 on a vehicle's first observation, which the client renders as an instant snap-into-place.</param>
    /// <param name="SpeedMetersPerSec">Vehicle speed from the GTFS-RT feed, if available.</param>
    /// <param name="Bearing">Vehicle bearing in degrees (0-360) from the GTFS-RT feed, if available.</param>
    /// <param name="IsStale">True when this record reflects an upstream GTFS-RT sample whose per-vehicle timestamp matches the prior observation — i.e. the feed delivered the same GPS reading twice. Clients should keep extrapolating from the last empirical speed but should NOT append this snap to their motion history.</param>
    /// <param name="Category">The vehicle's per-city category key. <see langword="null"/> in the normal case — the client resolves the category from its route catalog by <paramref name="RouteJoinKey"/>. Non-null ONLY to carry the <c>"unknown"</c> data-quality signal when the Worker could not classify the route. Has no default: omitting it is a deliberate wire decision, not a fallback.</param>
    /// <remarks>
    /// Wire revision v2 (feature 051, US4). Coordinates are scaled ints, the prior pair is
    /// omitted on steady-state records, and Category is omitted unless it signals "unknown" —
    /// together ~38.7% smaller per record than v1. Because MessagePack's ReadDouble silently
    /// accepts int encodings, a stale client would MISRENDER rather than fail; the rename of
    /// HubMethods.JoinCity to "JoinCityV2" is the gate that makes that failure clean and loud.
    /// Key slots are never renumbered.
    /// </remarks>
    [MessagePackObject]
    public sealed record RouteNearestPointRecord(
        [property: Key(0)] string VehicleId,
        [property: Key(1)] string RouteJoinKey,
        [property: Key(2)] int? PriorNearestLatE5,
        [property: Key(3)] int? PriorNearestLonE5,
        [property: Key(4)] int CurrentNearestLatE5,
        [property: Key(5)] int CurrentNearestLonE5,
        [property: Key(6)] int DurationMs,
        [property: Key(7)] float? SpeedMetersPerSec,
        [property: Key(8)] float? Bearing,
        [property: Key(9)] bool IsStale,
        [property: Key(10)] string? Category
    );
}
