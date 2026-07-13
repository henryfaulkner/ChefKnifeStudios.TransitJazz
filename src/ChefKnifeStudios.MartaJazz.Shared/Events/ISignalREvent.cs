using MessagePack;

namespace ChefKnifeStudios.MartaJazz.Shared.Events;

// [Union] lets MessagePack serialize the polymorphic EventEnvelope.Payload (typed as this
// interface) by writing an integer discriminator. Every concrete ISignalREvent MUST be listed
// here with a STABLE key — reordering or reusing a number is a wire-breaking change. This is
// MessagePack's equivalent of the JSON path's EventEnvelopeConverter "eventType" string.
[Union(0, typeof(RouteNearestPointBatchEvent))]
[Union(1, typeof(RouteCrossingBatchEvent))]
public interface ISignalREvent;
