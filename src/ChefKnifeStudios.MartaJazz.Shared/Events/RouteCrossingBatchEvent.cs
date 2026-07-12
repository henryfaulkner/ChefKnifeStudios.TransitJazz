using System.Collections.Generic;

namespace ChefKnifeStudios.MartaJazz.Shared.Events;

public sealed record RouteCrossingBatchEvent(
    IEnumerable<RouteCrossingBatchEvent.RouteCrossingRecord> BatchRecords
) : ISignalREvent
{
    public sealed record RouteCrossingRecord(
        string VehicleId,
        string RouteJoinKey,
        int TriggerIndex,
        int TotalTriggers,
        // Milliseconds into the batch window at which this checkpoint was actually crossed,
        // derived from where the trigger point sits in the vehicle's prior→current travel
        // span this cycle. Lets the client spread a burst of crossings out in true crossing
        // order instead of firing them all at once on batch receipt. 0 = fire immediately.
        double OffsetMs
    );
}
