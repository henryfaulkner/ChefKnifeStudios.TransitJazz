using ChefKnifeStudios.TransitJazz.Shared.Events;
using MessagePack;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;

/// <summary>
/// Measures the exact MessagePack-serialized size of the envelope batch about to be published,
/// for the batch_wire_bytes telemetry column (research D1). A pure function so it is provable at
/// unit cost; the Worker calls it immediately before PublishBatchAsync on the identical batch.
/// </summary>
public static class WireSize
{
    // One reused buffer per thread avoids a per-tick LOH allocation for large (e.g. NYMTA) batches
    // while staying safe under the Worker's single-threaded-per-tick call pattern. Each call clears
    // and re-serializes fresh — callers must read the returned count before the pool-backed source
    // batch (e.g. a RecyclableList-derived envelope) goes out of scope, same as any other read of it.
    static readonly ThreadLocal<ArrayBufferWriter<byte>> Writer = new(() => new ArrayBufferWriter<byte>());

    public static long Measure(List<EventEnvelope> batch)
    {
        var writer = Writer.Value!;
        writer.Clear();
        MessagePackSerializer.Serialize(writer, batch);
        return writer.WrittenCount;
    }
}
