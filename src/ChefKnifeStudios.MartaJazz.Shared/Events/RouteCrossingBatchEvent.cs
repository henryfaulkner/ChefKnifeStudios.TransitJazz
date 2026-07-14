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
        // Fraction (0..1) of the vehicle's prior→current travel span at which this checkpoint
        // sits this cycle. The CLIENT re-bases this onto the dot's actual animation duration
        // (frac × durationMs) so a tone fires when the animated dot reaches the checkpoint —
        // NOT against the server's spread window, which is a different clock (the dot animates
        // over ~30s of interpolation while the server span is ~8s, so a pre-baked offset fired
        // the tone far ahead of the dot). Trigger points are distance-ordered, so fracs come
        // out monotonically increasing within a vehicle. 0 = fire immediately.
        [property: Key(4)] double Frac
    );
}
