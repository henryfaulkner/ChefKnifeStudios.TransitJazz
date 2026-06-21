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
    // Existing factory: one non-stale envelope per vehicle id. Kept for untouched tests.
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

    // Build a single explicit record with controllable id, position, and staleness.
    static RouteNearestPointBatchEvent.RouteNearestPointRecord MakeRecord(
        string vehicleId, double currentLat, double currentLon, bool isStale)
        => new(
            vehicleId, "74", 33.75, -84.39, DateTime.UtcNow,
            currentLat, currentLon, DateTime.UtcNow, null, null, isStale);

    // Wrap explicit records into a one-envelope batch (mirrors what the worker publishes).
    static List<EventEnvelope> MakeBatch(params RouteNearestPointBatchEvent.RouteNearestPointRecord[] records)
        => new()
        {
            new EventEnvelope(
                "RouteNearestPointBatchEvent",
                DateTimeOffset.UtcNow,
                new RouteNearestPointBatchEvent(records))
        };

    // Helper: flatten the snapshot to its records.
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
        Assert.NotNull(cache.Current);
        Assert.Empty(cache.Current);
    }

    // Rewritten (T018): Current is a rebuilt envelope, so assert v1 is present and non-stale (content, not reference).
    [Fact]
    public void Set_Then_Current_ContainsNonStaleVehicle()
    {
        var cache = new LastBatchCache();
        cache.Set(MakeBatch("v1"));

        var records = RecordsOf(cache.Current);
        var v1 = Assert.Single(records);
        Assert.Equal("v1", v1.VehicleId);
        Assert.False(v1.IsStale);
    }

    // Rewritten (T019): merge semantics — Set(v1) then Set(v2) retains BOTH (not replacement).
    [Fact]
    public void Set_Twice_MergesBothVehicles()
    {
        var cache = new LastBatchCache();
        cache.Set(MakeBatch("v1"));
        cache.Set(MakeBatch("v2"));

        var ids = RecordsOf(cache.Current).Select(r => r.VehicleId).ToList();
        Assert.Equal(2, ids.Count);
        Assert.Contains("v1", ids);
        Assert.Contains("v2", ids);
    }

    [Fact]
    public void Set_Null_YieldsEmptyNonNull()
    {
        var cache = new LastBatchCache();
        cache.Set(null!);
        Assert.NotNull(cache.Current);
        Assert.Empty(cache.Current);
    }

    // Rewritten (T020): each read non-null; every record non-stale and from some written batch (merged snapshot may span batches).
    [Fact]
    public void Concurrent_SetAndRead_NeverTornOrNull()
    {
        var cache = new LastBatchCache();
        var batches = Enumerable.Range(0, 8)
            .Select(i => (IReadOnlyList<EventEnvelope>)MakeBatch($"v{i}"))
            .ToArray();
        var knownIds = Enumerable.Range(0, 8).Select(i => $"v{i}").ToHashSet();

        Parallel.For(0, 8, i => cache.Set(batches[i]));

        Parallel.For(0, 32, _ =>
        {
            var snapshot = cache.Current;
            Assert.NotNull(snapshot);
            foreach (var rec in RecordsOf(snapshot))
            {
                Assert.False(rec.IsStale);
                Assert.Contains(rec.VehicleId, knownIds);
            }
        });
    }

    // --- User Story 1: cold-start snapshot is stale-free and never an empty envelope ---

    // T006 / vector D: mixed batch -> only the non-stale record survives.
    [Fact]
    public void US1_MixedBatch_ExcludesStale()
    {
        var cache = new LastBatchCache();
        cache.Set(MakeBatch(
            MakeRecord("v1", 33.751, -84.389, isStale: false),
            MakeRecord("v2", 33.760, -84.380, isStale: true)));

        var records = RecordsOf(cache.Current);
        var v1 = Assert.Single(records);
        Assert.Equal("v1", v1.VehicleId);
        Assert.DoesNotContain(records, r => r.IsStale);
    }

    // T007 / vector C: all-stale first batch (nothing seen non-stale) -> empty snapshot.
    [Fact]
    public void US1_AllStaleFirstBatch_YieldsEmpty()
    {
        var cache = new LastBatchCache();
        cache.Set(MakeBatch(
            MakeRecord("v1", 33.751, -84.389, isStale: true),
            MakeRecord("v2", 33.760, -84.380, isStale: true)));

        Assert.Empty(cache.Current);
    }

    // T008 / vector J: any non-empty Current has no empty BatchRecords envelope.
    [Fact]
    public void US1_NonEmptyCurrent_HasNoEmptyEnvelope()
    {
        var cache = new LastBatchCache();
        cache.Set(MakeBatch("v1", "v2"));

        Assert.NotEmpty(cache.Current);
        foreach (var env in cache.Current)
        {
            var rnp = Assert.IsType<RouteNearestPointBatchEvent>(env.Payload);
            Assert.NotEmpty(rnp.BatchRecords);
        }
    }

    // --- User Story 3: per-vehicle retention across batches ---

    // T013 / vector E: Set(v1 non-stale) then Set(v1 stale) -> v1 retained at non-stale position.
    [Fact]
    public void US3_StaleAfterGood_RetainsNonStalePosition()
    {
        var cache = new LastBatchCache();
        cache.Set(MakeBatch(MakeRecord("v1", 33.751, -84.389, isStale: false)));
        cache.Set(MakeBatch(MakeRecord("v1", 33.900, -84.100, isStale: true)));

        var v1 = Assert.Single(RecordsOf(cache.Current));
        Assert.Equal("v1", v1.VehicleId);
        Assert.False(v1.IsStale);
        Assert.Equal(33.751, v1.CurrentNearestLat);
        Assert.Equal(-84.389, v1.CurrentNearestLon);
    }

    // T014 / vectors I & H: all-stale-after-good and empty-after-good both leave the snapshot unchanged.
    [Fact]
    public void US3_AllStaleOrEmptyAfterGood_LeavesSnapshotUnchanged()
    {
        var cache = new LastBatchCache();
        cache.Set(MakeBatch(MakeRecord("v1", 33.751, -84.389, isStale: false)));

        // all-stale batch (vector I)
        cache.Set(MakeBatch(
            MakeRecord("v1", 33.900, -84.100, isStale: true),
            MakeRecord("v2", 33.800, -84.200, isStale: true)));

        var idsAfterStale = RecordsOf(cache.Current).Select(r => r.VehicleId).ToList();
        Assert.Equal(new[] { "v1" }, idsAfterStale);

        // empty batch (vector H)
        cache.Set(new List<EventEnvelope>());

        var idsAfterEmpty = RecordsOf(cache.Current).Select(r => r.VehicleId).ToList();
        Assert.Equal(new[] { "v1" }, idsAfterEmpty);
    }

    // T015 / vector G: cross-batch retention — Set(v1) then Set(v2) -> both present.
    [Fact]
    public void US3_CrossBatchRetention_KeepsBothVehicles()
    {
        var cache = new LastBatchCache();
        cache.Set(MakeBatch(MakeRecord("v1", 33.751, -84.389, isStale: false)));
        cache.Set(MakeBatch(MakeRecord("v2", 33.760, -84.380, isStale: false)));

        var ids = RecordsOf(cache.Current).Select(r => r.VehicleId).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "v1", "v2" }, ids);
    }

    // T016 / vector F: latest-non-stale-wins — v1 @posA then v1 @posB -> @posB exactly once.
    [Fact]
    public void US3_LatestNonStaleWins_UpsertsInPlace()
    {
        var cache = new LastBatchCache();
        cache.Set(MakeBatch(MakeRecord("v1", 33.751, -84.389, isStale: false)));
        cache.Set(MakeBatch(MakeRecord("v1", 33.900, -84.100, isStale: false)));

        var v1 = Assert.Single(RecordsOf(cache.Current));
        Assert.Equal("v1", v1.VehicleId);
        Assert.Equal(33.900, v1.CurrentNearestLat);
        Assert.Equal(-84.100, v1.CurrentNearestLon);
    }
}
