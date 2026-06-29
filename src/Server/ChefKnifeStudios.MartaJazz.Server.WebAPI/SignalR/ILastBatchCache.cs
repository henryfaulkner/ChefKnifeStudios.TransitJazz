using ChefKnifeStudios.MartaJazz.Shared.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ChefKnifeStudios.MartaJazz.Server.WebAPI.SignalR;

public interface ILastBatchCache
{
    IReadOnlyList<EventEnvelope> Current(string city);
    void Set(string city, IReadOnlyList<EventEnvelope> batch);
}

public sealed class LastBatchCache : ILastBatchCache
{
    readonly object _gate = new();
    readonly Dictionary<string, CityCache> _cities = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<EventEnvelope> Current(string city)
    {
        lock (_gate)
            return _cities.TryGetValue(city, out var c) ? c.Current : Array.Empty<EventEnvelope>();
    }

    public void Set(string city, IReadOnlyList<EventEnvelope> batch)
    {
        lock (_gate)
        {
            if (!_cities.TryGetValue(city, out var c))
                _cities[city] = c = new CityCache();
            c.Set(batch);
        }
    }

    sealed class CityCache
    {
        // A vehicle is evicted once it hasn't appeared in any batch for this many
        // data-carrying cycles. Keeps the cold-start snapshot tracking the live fleet so
        // it can't carry minutes-old ghosts of vehicles that left the feed.
        const int EvictAfterCycles = 3;

        readonly Dictionary<string, Entry> _vehicles = new();
        // Monotonic counter advanced once per batch that carries vehicle data. We age out
        // by cycle count rather than wall-clock so a slow/stalled worker can't skew the TTL,
        // and an empty (feed-hiccup) batch never ages anything out — it just holds.
        long _cycle;
        IReadOnlyList<EventEnvelope> _current = Array.Empty<EventEnvelope>();

        public IReadOnlyList<EventEnvelope> Current => _current;

        readonly record struct Entry(RouteNearestPointBatchEvent.RouteNearestPointRecord Record, long LastSeenCycle);

        public void Set(IReadOnlyList<EventEnvelope> batch)
        {
            var records = batch?
                .Select(e => e?.Payload)
                .OfType<RouteNearestPointBatchEvent>()
                .SelectMany(p => p.BatchRecords)
                .ToList();

            // Only advance the cycle clock (and therefore age out vehicles) on a batch that
            // actually carries data. An empty publish during a feed gap leaves the snapshot
            // untouched rather than blanking the map.
            if (records is { Count: > 0 })
            {
                _cycle++;

                foreach (var rec in records)
                {
                    // Upsert the latest record per vehicle INCLUDING stale ones. Dropping
                    // stale records made the snapshot synthetically "all moving": every
                    // retained record carried a prior!=current motion segment, so on load the
                    // client replayed motion for vehicles that were actually stopped. The
                    // client's cold-start path idles a stale vehicle correctly — but only if
                    // the snapshot tells the truth about staleness, so preserve it. A stale
                    // record still refreshes LastSeenCycle: the vehicle is still being
                    // reported, just without a new GPS fix, so it must not be evicted.
                    _vehicles[rec.VehicleId] = new Entry(rec, _cycle);
                }

                // Evict vehicles not seen in the last EvictAfterCycles data-carrying batches.
                var toEvict = _vehicles
                    .Where(kvp => _cycle - kvp.Value.LastSeenCycle >= EvictAfterCycles)
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (var id in toEvict)
                    _vehicles.Remove(id);
            }

            _current = _vehicles.Count == 0
                ? Array.Empty<EventEnvelope>()
                : new[]
                {
                    new EventEnvelope(
                        nameof(RouteNearestPointBatchEvent),
                        DateTimeOffset.UtcNow,
                        new RouteNearestPointBatchEvent(_vehicles.Values.Select(e => e.Record).ToList()))
                };
        }
    }
}
