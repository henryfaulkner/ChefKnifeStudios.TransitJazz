using System.Collections.Generic;
using MessagePack;

namespace ChefKnifeStudios.MartaJazz.Shared.Events;

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
        // Milliseconds into the batch window at which this checkpoint was actually crossed,
        // derived from where the trigger point sits in the vehicle's prior→current travel
        // span this cycle. Lets the client spread a burst of crossings out in true crossing
        // order instead of firing them all at once on batch receipt. 0 = fire immediately.
        [property: Key(4)] double OffsetMs
    );
}
