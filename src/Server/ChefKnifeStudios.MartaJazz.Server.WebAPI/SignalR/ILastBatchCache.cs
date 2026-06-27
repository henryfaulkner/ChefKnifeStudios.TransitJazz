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
        readonly Dictionary<string, RouteNearestPointBatchEvent.RouteNearestPointRecord> _vehicles = new();
        IReadOnlyList<EventEnvelope> _current = Array.Empty<EventEnvelope>();

        public IReadOnlyList<EventEnvelope> Current => _current;

        public void Set(IReadOnlyList<EventEnvelope> batch)
        {
            if (batch is not null)
            {
                foreach (var env in batch)
                {
                    if (env?.Payload is not RouteNearestPointBatchEvent rnp) continue;
                    foreach (var rec in rnp.BatchRecords)
                    {
                        if (rec.IsStale) continue;
                        _vehicles[rec.VehicleId] = rec;
                    }
                }
            }

            _current = _vehicles.Count == 0
                ? Array.Empty<EventEnvelope>()
                : new[]
                {
                    new EventEnvelope(
                        nameof(RouteNearestPointBatchEvent),
                        DateTimeOffset.UtcNow,
                        new RouteNearestPointBatchEvent(_vehicles.Values.ToList()))
                };
        }
    }
}
