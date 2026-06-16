using ChefKnifeStudios.MartaJazz.Shared.Events;
using System;
using System.Collections.Generic;
using System.Threading;

namespace ChefKnifeStudios.MartaJazz.Server.WebAPI.SignalR;

public interface ILastBatchCache
{
    IReadOnlyList<EventEnvelope> Current { get; }
    void Set(IReadOnlyList<EventEnvelope> batch);
}

public sealed class LastBatchCache : ILastBatchCache
{
    private IReadOnlyList<EventEnvelope> _current = Array.Empty<EventEnvelope>();

    public IReadOnlyList<EventEnvelope> Current => Volatile.Read(ref _current);

    public void Set(IReadOnlyList<EventEnvelope> batch)
        => Volatile.Write(ref _current, batch ?? Array.Empty<EventEnvelope>());
}
