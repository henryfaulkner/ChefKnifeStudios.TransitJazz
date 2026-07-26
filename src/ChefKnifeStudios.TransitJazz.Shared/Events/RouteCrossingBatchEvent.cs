using System.Collections.Generic;
using MessagePack;

namespace ChefKnifeStudios.TransitJazz.Shared.Events;

[MessagePackObject]
public sealed record RouteCrossingBatchEvent(
    [property: Key(0)] IEnumerable<RouteCrossingBatchEvent.RouteCrossingRecord> BatchRecords
) : ISignalREvent
{
    [MessagePackObject]
    public sealed record RouteCrossingRecord(
        [property: Key(0)] string VehicleId,
        [property: Key(1)] string RouteJoinKey,
        [property: Key(2)] int TriggerIndex,
        [property: Key(3)] int TotalTriggers,
        // Absolute distance (metres) of this checkpoint along the route polyline. The CLIENT
        // computes the tone's fire delay from this against the DOT'S OWN animated motion —
        // time-to-reach = (AlongDistanceM − dotDistanceNow) / empiricalSpeed — so the tone fires
        // exactly when the animated dot arrives, tracking the extrapolated dot rather than the
        // server's snapped along-route estimate. (Sending a server-baked offset/frac left a
        // speed-scaled residual lead because the snap runs ahead of the client's extrapolation.)
        [property: Key(4)] double AlongDistanceM
    );
}
