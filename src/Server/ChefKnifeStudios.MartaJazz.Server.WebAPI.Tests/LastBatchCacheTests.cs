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
                    id, "74", 33.75, -84.39,
                    33.751, -84.389, 10000, null, null, false)
            })
        )).ToList();

    static RouteNearestPointBatchEvent.RouteNearestPointRecord MakeRecord(
        string vehicleId, double currentLat, double currentLon, bool isStale)
        => new(
            vehicleId, "74", 33.75, -84.39,
            currentLat, currentLon, 10000, null, null, isStale);

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
                Assert.Contains(rec.VehicleId, knownIds);
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

    // The snapshot preserves staleness so the client can idle a stopped/stale vehicle on
    // load instead of replaying its last motion segment. Stale records are NOT dropped.
    [Fact]
    public void US1_MixedBatch_IncludesStaleWithFlagPreserved()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", MakeBatch("marta",
            MakeRecord("v1", 33.751, -84.389, isStale: false),
            MakeRecord("v2", 33.760, -84.380, isStale: true)));

        var records = RecordsOf(cache.Current("marta"));
        Assert.Equal(2, records.Count);
        Assert.False(records.Single(r => r.VehicleId == "v1").IsStale);
        Assert.True(records.Single(r => r.VehicleId == "v2").IsStale);
    }

    [Fact]
    public void US1_AllStaleFirstBatch_RetainsStaleVehicles()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", MakeBatch("marta",
            MakeRecord("v1", 33.751, -84.389, isStale: true),
            MakeRecord("v2", 33.760, -84.380, isStale: true)));

        var ids = RecordsOf(cache.Current("marta")).Select(r => r.VehicleId).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "v1", "v2" }, ids);
        Assert.All(RecordsOf(cache.Current("marta")), r => Assert.True(r.IsStale));
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

    // Latest record per vehicle wins, even when the latest is stale: a vehicle that goes
    // stale must read as stale in the snapshot (so the client idles it) rather than keeping
    // its last moving record forever and animating a stop that never happens.
    [Fact]
    public void US3_StaleAfterGood_LatestStaleWins()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", MakeBatch("marta", MakeRecord("v1", 33.751, -84.389, isStale: false)));
        cache.Set("marta", MakeBatch("marta", MakeRecord("v1", 33.900, -84.100, isStale: true)));

        var v1 = Assert.Single(RecordsOf(cache.Current("marta")));
        Assert.Equal("v1", v1.VehicleId);
        Assert.True(v1.IsStale);
        Assert.Equal(33.900, v1.CurrentNearestLat);
        Assert.Equal(-84.100, v1.CurrentNearestLon);
    }

    [Fact]
    public void US3_EmptyBatchAfterGood_LeavesSnapshotUnchanged()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", MakeBatch("marta", MakeRecord("v1", 33.751, -84.389, isStale: false)));

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

    // TTL eviction: a vehicle absent for EvictAfterCycles (3) data-carrying batches is
    // dropped, so the cold-start snapshot can't carry ghosts of vehicles that left the feed.
    [Fact]
    public void Eviction_VehicleAbsentForThreeCycles_IsEvicted()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", MakeBatch("marta", MakeRecord("v1", 33.75, -84.39, isStale: false))); // cycle 1
        cache.Set("marta", MakeBatch("marta", MakeRecord("other", 33.76, -84.38, isStale: false))); // cycle 2
        cache.Set("marta", MakeBatch("marta", MakeRecord("other", 33.76, -84.38, isStale: false))); // cycle 3
        cache.Set("marta", MakeBatch("marta", MakeRecord("other", 33.76, -84.38, isStale: false))); // cycle 4 → v1 evicted

        var ids = RecordsOf(cache.Current("marta")).Select(r => r.VehicleId).ToList();
        Assert.DoesNotContain("v1", ids);
        Assert.Contains("other", ids);
    }

    [Fact]
    public void Eviction_VehicleStillWithinTtl_Survives()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", MakeBatch("marta", MakeRecord("v1", 33.75, -84.39, isStale: false))); // cycle 1
        cache.Set("marta", MakeBatch("marta", MakeRecord("other", 33.76, -84.38, isStale: false))); // cycle 2
        cache.Set("marta", MakeBatch("marta", MakeRecord("other", 33.76, -84.38, isStale: false))); // cycle 3 → v1 age 2 < 3

        Assert.Contains("v1", RecordsOf(cache.Current("marta")).Select(r => r.VehicleId));
    }

    // A stale re-report still proves the vehicle is being observed, so it refreshes the
    // eviction clock and keeps the vehicle alive (marked stale).
    [Fact]
    public void Eviction_StaleReportRefreshesTtl()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", MakeBatch("marta", MakeRecord("v1", 33.75, -84.39, isStale: false))); // cycle 1
        cache.Set("marta", MakeBatch("marta", MakeRecord("v1", 33.75, -84.39, isStale: true)));  // cycle 2 (refresh)
        cache.Set("marta", MakeBatch("marta", MakeRecord("other", 33.76, -84.38, isStale: false))); // cycle 3
        cache.Set("marta", MakeBatch("marta", MakeRecord("other", 33.76, -84.38, isStale: false))); // cycle 4 → v1 age 2 < 3

        Assert.Contains("v1", RecordsOf(cache.Current("marta")).Select(r => r.VehicleId));
    }

    // Empty batches (feed hiccups) must not advance the eviction clock, or a brief silence
    // would blank the whole snapshot.
    [Fact]
    public void Eviction_EmptyBatchesDoNotAgeOutVehicles()
    {
        var cache = new LastBatchCache();
        cache.Set("marta", MakeBatch("marta", MakeRecord("v1", 33.75, -84.39, isStale: false))); // cycle 1
        for (var i = 0; i < 5; i++)
            cache.Set("marta", new List<EventEnvelope>()); // empty — no cycle advance

        Assert.Contains("v1", RecordsOf(cache.Current("marta")).Select(r => r.VehicleId));
    }
}
