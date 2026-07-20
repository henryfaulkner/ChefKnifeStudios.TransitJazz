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
    // Public alias of CityCache.CrossingAgeCap for test/verification code outside this
    // assembly's internals (see the const's own doc comment for the tuning rationale).
    public static readonly TimeSpan CrossingAgeCap = CityCache.CrossingAgeCap;

    readonly object _gate = new();
    readonly Dictionary<string, CityCache> _cities = new(StringComparer.OrdinalIgnoreCase);
    readonly TimeProvider _timeProvider;

    public LastBatchCache() : this(TimeProvider.System) { }

    public LastBatchCache(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public IReadOnlyList<EventEnvelope> Current(string city)
    {
        lock (_gate)
            return _cities.TryGetValue(city, out var c) ? c.Current(_timeProvider.GetUtcNow()) : Array.Empty<EventEnvelope>();
    }

    public void Set(string city, IReadOnlyList<EventEnvelope> batch)
    {
        lock (_gate)
        {
            if (!_cities.TryGetValue(city, out var c))
                _cities[city] = c = new CityCache();
            c.Set(batch, _timeProvider.GetUtcNow());
        }
    }

    sealed class CityCache
    {
        // A vehicle is evicted once it hasn't appeared in any batch for this many
        // data-carrying cycles. Keeps the cold-start snapshot tracking the live fleet so
        // it can't carry minutes-old ghosts of vehicles that left the feed.
        const int EvictAfterCycles = 3;

        // Feature 045 (D5): max age of a replayed crossing on join. Chosen as one Worker
        // tick (~10s, matching Worker.cs's PeriodicTimer(TimeSpan.FromSeconds(10)) publish
        // cadence) — a crossing older than one tick is from a superseded position snapshot
        // and would replay against a dot position the client has already moved past. The
        // valid range is (0, ~one tick]: larger admits stale dot positions the client drops
        // anyway (net waste, and re-opens the "rapid pulsing" regression this guards
        // against — LastBatchCacheCrossingExclusionTests); smaller risks admitting nothing
        // on a fast join, defeating the point of replay (SC-004).
        public static readonly TimeSpan CrossingAgeCap = TimeSpan.FromSeconds(10);

        readonly Dictionary<string, Entry> _vehicles = new();
        // Monotonic counter advanced once per batch that carries vehicle data. We age out
        // by cycle count rather than wall-clock so a slow/stalled worker can't skew the TTL,
        // and an empty (feed-hiccup) batch never ages anything out — it just holds.
        long _cycle;
        IReadOnlyList<EventEnvelope> _positionEnvelopes = Array.Empty<EventEnvelope>();
        // Recent crossing records, each stamped with the wall-clock time it was Set(). Pruned
        // to CrossingAgeCap on every Set AND re-filtered at read time in Current(), since a
        // record can age out between two Set calls (e.g. a quiet cycle with no crossings).
        readonly List<(RouteCrossingBatchEvent.RouteCrossingRecord Record, DateTimeOffset At)> _recentCrossings = new();

        readonly record struct Entry(RouteNearestPointBatchEvent.RouteNearestPointRecord Record, long LastSeenCycle);

        public IReadOnlyList<EventEnvelope> Current(DateTimeOffset now)
        {
            var survivors = _recentCrossings
                .Where(c => now - c.At <= CrossingAgeCap)
                .Select(c => c.Record)
                // Canonical ordering — matches the live publish path (Worker.cs) so client
                // dispatch is deterministic whether crossings arrive live or replayed.
                .OrderBy(r => r.RouteJoinKey, StringComparer.Ordinal)
                .ThenBy(r => r.VehicleId, StringComparer.Ordinal)
                .ThenBy(r => r.TriggerIndex)
                .ToList();

            // Never send an empty crossing envelope — omit it entirely, exactly as the
            // Worker's live publish path does when crossingRecords.Count == 0.
            if (survivors.Count == 0) return _positionEnvelopes;

            var crossingEnvelope = new EventEnvelope(
                nameof(RouteCrossingBatchEvent),
                now,
                new RouteCrossingBatchEvent(survivors));

            return [.. _positionEnvelopes, crossingEnvelope];
        }

        public void Set(IReadOnlyList<EventEnvelope> batch, DateTimeOffset now)
        {
            var positionRecords = batch?
                .Select(e => e?.Payload)
                .OfType<RouteNearestPointBatchEvent>()
                .SelectMany(p => p.BatchRecords)
                .ToList();

            // Only advance the cycle clock (and therefore age out vehicles) on a batch that
            // actually carries data. An empty publish during a feed gap leaves the snapshot
            // untouched rather than blanking the map.
            if (positionRecords is { Count: > 0 })
            {
                _cycle++;

                foreach (var rec in positionRecords)
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

            var crossingRecords = batch?
                .Select(e => e?.Payload)
                .OfType<RouteCrossingBatchEvent>()
                .SelectMany(p => p.BatchRecords);
            if (crossingRecords != null)
            {
                foreach (var rec in crossingRecords)
                    _recentCrossings.Add((rec, now));
            }
            _recentCrossings.RemoveAll(c => now - c.At > CrossingAgeCap);

            _positionEnvelopes = _vehicles.Count == 0
                ? Array.Empty<EventEnvelope>()
                : new[]
                {
                    new EventEnvelope(
                        nameof(RouteNearestPointBatchEvent),
                        now,
                        new RouteNearestPointBatchEvent(_vehicles.Values.Select(e => e.Record).ToList()))
                };
        }
    }
}
