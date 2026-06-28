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
    static List<EventEnvelope> MakeBatch(string city, params string[] vehicleIds)
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

    static RouteNearestPointBatchEvent.RouteNearestPointRecord MakeRecord(
        string vehicleId, double currentLat, double currentLon, bool isStale)
        => new(
            vehicleId, "74", 33.75, -84.39, DateTime.UtcNow,
            currentLat, currentLon, DateTime.UtcNow, null, null, isStale);

    static List<EventEnvelope> MakeBatch(string city, params RouteNearestPointBatchEvent.RouteNearestPointRecord[] records)
        => new()
        {
            new EventEnvelope(
                "RouteNearestPointBatchEvent",
                DateTimeOffset.UtcNow,
                new RouteNearestPointBatchEvent(records))
        };

    static IReadOnlyList<RouteNearestPointBatchEvent.RouteNearestPointRecord> RecordsOf(
        IReadOnlyList<EventEnvelope> snapshot)
        => snapshot
            .Select(e => e.Payload)
            .OfType<RouteNearestPointBatchEvent>()
            .SelectMany(p => p.BatchRecords)
            .ToList();

    [Fact]
    public void New_Current_IsEmptyNonNull()
    {
        var cache = new LastBatchCache();
        Assert.NotNull(cache.Current("marta"));
        Assert.Empty(cache.Current("marta"));
    }

    [Fact]
    public void Set_Then_Current_ContainsNonStaleVehicle()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", MakeBatch("marta", "v1"));

        var records = RecordsOf(cache.Current("marta"));
        var v1 = Assert.Single(records);
        Assert.Equal("v1", v1.VehicleId);
        Assert.False(v1.IsStale);
    }

    [Fact]
    public void Set_Twice_MergesBothVehicles()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", MakeBatch("marta", "v1"));
        cache.Set("marta", MakeBatch("marta", "v2"));

        var ids = RecordsOf(cache.Current("marta")).Select(r => r.VehicleId).ToList();
        Assert.Equal(2, ids.Count);
        Assert.Contains("v1", ids);
        Assert.Contains("v2", ids);
    }

    [Fact]
    public void Set_Null_YieldsEmptyNonNull()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", null!);
        Assert.NotNull(cache.Current("marta"));
        Assert.Empty(cache.Current("marta"));
    }

    [Fact]
    public void Concurrent_SetAndRead_NeverTornOrNull()
    {
        var cache = new LastBatchCache();
        var batches = Enumerable.Range(0, 8)
            .Select(i => (IReadOnlyList<EventEnvelope>)MakeBatch("marta", $"v{i}"))
            .ToArray();
        var knownIds = Enumerable.Range(0, 8).Select(i => $"v{i}").ToHashSet();

        Parallel.For(0, 8, i => cache.Set("marta", batches[i]));

        Parallel.For(0, 32, _ =>
        {
            var snapshot = cache.Current("marta");
            Assert.NotNull(snapshot);
            foreach (var rec in RecordsOf(snapshot))
            {
                Assert.False(rec.IsStale);
                Assert.Contains(rec.VehicleId, knownIds);
            }
        });
    }

    // INV-T3 (FR-011): same vehicleId under two cities never collides
    [Fact]
    public void T010_PerCityCacheIsolation_SameVehicleIdNeverCollides()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", MakeBatch("marta", MakeRecord("v1", 33.75, -84.39, false)));
        cache.Set("wmata", MakeBatch("wmata", MakeRecord("v1", 38.90, -77.03, false)));

        var martaRecs = RecordsOf(cache.Current("marta"));
        var wmataRecs = RecordsOf(cache.Current("wmata"));

        Assert.Single(martaRecs);
        Assert.Equal(33.75, martaRecs[0].CurrentNearestLat);

        Assert.Single(wmataRecs);
        Assert.Equal(38.90, wmataRecs[0].CurrentNearestLat);
    }

    [Fact]
    public void T010_Current_UnknownCity_ReturnsEmpty()
    {
        var cache = new LastBatchCache();
        Assert.Empty(cache.Current("unknown-city"));
    }

    [Fact]
    public void US1_MixedBatch_ExcludesStale()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", MakeBatch("marta",
            MakeRecord("v1", 33.751, -84.389, isStale: false),
            MakeRecord("v2", 33.760, -84.380, isStale: true)));

        var records = RecordsOf(cache.Current("marta"));
        var v1 = Assert.Single(records);
        Assert.Equal("v1", v1.VehicleId);
        Assert.DoesNotContain(records, r => r.IsStale);
    }

    [Fact]
    public void US1_AllStaleFirstBatch_YieldsEmpty()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", MakeBatch("marta",
            MakeRecord("v1", 33.751, -84.389, isStale: true),
            MakeRecord("v2", 33.760, -84.380, isStale: true)));

        Assert.Empty(cache.Current("marta"));
    }

    [Fact]
    public void US1_NonEmptyCurrent_HasNoEmptyEnvelope()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", MakeBatch("marta", "v1", "v2"));

        Assert.NotEmpty(cache.Current("marta"));
        foreach (var env in cache.Current("marta"))
        {
            var rnp = Assert.IsType<RouteNearestPointBatchEvent>(env.Payload);
            Assert.NotEmpty(rnp.BatchRecords);
        }
    }

    [Fact]
    public void US3_StaleAfterGood_RetainsNonStalePosition()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", MakeBatch("marta", MakeRecord("v1", 33.751, -84.389, isStale: false)));
        cache.Set("marta", MakeBatch("marta", MakeRecord("v1", 33.900, -84.100, isStale: true)));

        var v1 = Assert.Single(RecordsOf(cache.Current("marta")));
        Assert.Equal("v1", v1.VehicleId);
        Assert.False(v1.IsStale);
        Assert.Equal(33.751, v1.CurrentNearestLat);
        Assert.Equal(-84.389, v1.CurrentNearestLon);
    }

    [Fact]
    public void US3_AllStaleOrEmptyAfterGood_LeavesSnapshotUnchanged()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", MakeBatch("marta", MakeRecord("v1", 33.751, -84.389, isStale: false)));

        cache.Set("marta", MakeBatch("marta",
            MakeRecord("v1", 33.900, -84.100, isStale: true),
            MakeRecord("v2", 33.800, -84.200, isStale: true)));

        var idsAfterStale = RecordsOf(cache.Current("marta")).Select(r => r.VehicleId).ToList();
        Assert.Equal(new[] { "v1" }, idsAfterStale);

        cache.Set("marta", new List<EventEnvelope>());

        var idsAfterEmpty = RecordsOf(cache.Current("marta")).Select(r => r.VehicleId).ToList();
        Assert.Equal(new[] { "v1" }, idsAfterEmpty);
    }

    [Fact]
    public void US3_CrossBatchRetention_KeepsBothVehicles()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", MakeBatch("marta", MakeRecord("v1", 33.751, -84.389, isStale: false)));
        cache.Set("marta", MakeBatch("marta", MakeRecord("v2", 33.760, -84.380, isStale: false)));

        var ids = RecordsOf(cache.Current("marta")).Select(r => r.VehicleId).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "v1", "v2" }, ids);
    }

    [Fact]
    public void US3_LatestNonStaleWins_UpsertsInPlace()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", MakeBatch("marta", MakeRecord("v1", 33.751, -84.389, isStale: false)));
        cache.Set("marta", MakeBatch("marta", MakeRecord("v1", 33.900, -84.100, isStale: false)));

        var v1 = Assert.Single(RecordsOf(cache.Current("marta")));
        Assert.Equal("v1", v1.VehicleId);
        Assert.Equal(33.900, v1.CurrentNearestLat);
        Assert.Equal(-84.100, v1.CurrentNearestLon);
    }
}
