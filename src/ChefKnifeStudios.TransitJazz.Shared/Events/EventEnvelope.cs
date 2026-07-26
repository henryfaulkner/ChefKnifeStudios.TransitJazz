using System;
using MessagePack;

namespace ChefKnifeStudios.TransitJazz.Shared.Events;

[MessagePackObject]
public sealed record EventEnvelope(
    [property: Key(0)] string EventType,
    [property: Key(1)] DateTimeOffset Timestamp,
    [property: Key(2)] ISignalREvent Payload
);
