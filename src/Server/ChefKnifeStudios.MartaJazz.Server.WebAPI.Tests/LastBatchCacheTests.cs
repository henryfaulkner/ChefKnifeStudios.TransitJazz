using ChefKnifeStudios.MartaJazz.Server.WebAPI.SignalR;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace ChefKnifeStudios.MartaJazz.Server.WebAPI.Tests;

public class LastBatchCacheTests
{
    static List<EventEnvelope> MakeBatch(params string[] vehicleIds)
        => vehicleIds.Select(id => new EventEnvelope(
            "RouteNearestPointBatchEvent",
            DateTimeOffset.UtcNow,
            new RouteNearestPointBatchEvent(new[]
            {
                new RouteNearestPointBatchEvent.RouteNearestPointRecord(
                    id, "74", 33.75, -84.39, DateTime.UtcNow,
                    33.751, -84.389, DateTime.UtcNow, null, null, false)
            })
        )).ToList();

    [Fact]
    public void New_Current_IsEmptyNonNull()
    {
        var cache = new LastBatchCache();
        Assert.NotNull(cache.Current);
        Assert.Equal(0, cache.Current.Count);
    }

    [Fact]
    public void Set_Then_Current_ReturnsSameBatch()
    {
        var cache = new LastBatchCache();
        var b1 = MakeBatch("v1");
        cache.Set(b1);
        Assert.Equal(b1, cache.Current);
    }

    [Fact]
    public void Set_Twice_LatestWins()
    {
        var cache = new LastBatchCache();
        var b1 = MakeBatch("v1");
        var b2 = MakeBatch("v2");
        cache.Set(b1);
        cache.Set(b2);
        Assert.Equal(b2, cache.Current);
    }

    [Fact]
    public void Set_Null_YieldsEmptyNonNull()
    {
        var cache = new LastBatchCache();
        cache.Set(null!);
        Assert.NotNull(cache.Current);
        Assert.Equal(0, cache.Current.Count);
    }

    [Fact]
    public void Concurrent_SetAndRead_NeverTornOrNull()
    {
        var cache = new LastBatchCache();
        var batches = Enumerable.Range(0, 8)
            .Select(i => (IReadOnlyList<EventEnvelope>)MakeBatch($"v{i}"))
            .ToArray();

        var writes = Parallel.For(0, 8, i => cache.Set(batches[i]));

        // Each read must return a non-null whole batch that we wrote (not a partial)
        var reads = Parallel.For(0, 32, _ =>
        {
            var snapshot = cache.Current;
            Assert.NotNull(snapshot);
            // Every vehicle ID in the snapshot belongs to a single known batch
            if (snapshot.Count > 0)
                Assert.Contains(batches, b => b.SequenceEqual(snapshot));
        });
    }
}
