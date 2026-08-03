using ChefKnifeStudios.TransitJazz.Server.WebAPI.SignalR;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests;

public class WorkerTransitHubTests
{
    static List<EventEnvelope> MakeBatch(params string[] vehicleIds)
        => vehicleIds.Select(id => new EventEnvelope(
            "RouteNearestPointBatchEvent",
            DateTimeOffset.UtcNow,
            new RouteNearestPointBatchEvent(new[]
            {
                new RouteNearestPointBatchEvent.RouteNearestPointRecord(
                    id, "74", 3_375_000, -8_439_000,
                    3_375_100, -8_438_900, 10000, null, null, false, null)
            })
        )).ToList();

    // INV-T1: PublishBatch(city, batch) routes to Clients.Group(city), never Clients.All
    [Fact]
    public async Task T011_PublishBatch_RoutesToCityGroup_NeverAll()
    {
        var fakeCache = new FakeLastBatchCache();
        var fakeHub = new FakeHubContext();
        var hub = new WorkerTransitHub(fakeHub, NullLogger<WorkerTransitHub>.Instance, fakeCache);
        var batch = MakeBatch("v1");

        await hub.PublishBatch("marta", batch);

        Assert.Equal(1, fakeHub.GroupProxy.SendAsyncCallCount);
        Assert.Equal("ReceiveBatch", fakeHub.GroupProxy.LastMethodCalled);
        Assert.Equal("marta", fakeHub.LastGroupName);
        Assert.Equal(0, fakeHub.AllProxy.SendAsyncCallCount);
    }

    [Fact]
    public async Task T011_PublishBatch_SetsCacheWithCity()
    {
        var fakeCache = new FakeLastBatchCache();
        var fakeHub = new FakeHubContext();
        var hub = new WorkerTransitHub(fakeHub, NullLogger<WorkerTransitHub>.Instance, fakeCache);
        var batch = MakeBatch("v1");

        await hub.PublishBatch("marta", batch);

        Assert.Equal("marta", fakeCache.LastCity);
        Assert.Equal(1, fakeCache.WriteCount);
        Assert.Equal(batch, fakeCache.LastWritten);
    }

    [Fact]
    public async Task T011_PublishBatch_DifferentCities_RoutedToCorrectGroups()
    {
        var fakeCache = new FakeLastBatchCache();
        var fakeHub = new FakeHubContext();
        var hub = new WorkerTransitHub(fakeHub, NullLogger<WorkerTransitHub>.Instance, fakeCache);

        await hub.PublishBatch("marta", MakeBatch("v1"));
        await hub.PublishBatch("wmata", MakeBatch("v2"));

        Assert.Equal(2, fakeHub.GroupProxy.SendAsyncCallCount);
        Assert.Equal("wmata", fakeHub.LastGroupName); // last call was wmata
    }

    [Fact]
    public async Task PublishBatch_RelaysFullBatchIncludingStale()
    {
        var fakeHub = new FakeHubContext();
        var hub = new WorkerTransitHub(fakeHub, NullLogger<WorkerTransitHub>.Instance, new FakeLastBatchCache());
        var batch = new List<EventEnvelope>
        {
            new EventEnvelope(
                "RouteNearestPointBatchEvent",
                DateTimeOffset.UtcNow,
                new RouteNearestPointBatchEvent(new[]
                {
                    new RouteNearestPointBatchEvent.RouteNearestPointRecord(
                        "v1", "74", 3_375_000, -8_439_000,
                        3_375_100, -8_438_900, 10000, null, null, false, null),
                    new RouteNearestPointBatchEvent.RouteNearestPointRecord(
                        "v2", "74", 3_375_000, -8_439_000,
                        3_376_000, -8_438_000, 10000, null, null, true, null)
                }))
        };

        await hub.PublishBatch("marta", batch);

        var relayed = Assert.IsAssignableFrom<IReadOnlyList<EventEnvelope>>(fakeHub.GroupProxy.LastArgs![0]);
        Assert.Same(batch, relayed);
        var relayedRecords = relayed
            .Select(e => e.Payload)
            .OfType<RouteNearestPointBatchEvent>()
            .SelectMany(p => p.BatchRecords)
            .ToList();
        Assert.Equal(2, relayedRecords.Count);
        Assert.Contains(relayedRecords, r => r.IsStale);
    }

    // --- 052-city-slug-migration: TransitHub.JoinCityV2 version-gate tests ---

    // contract C2/C4: group name == the slug passed, verbatim — the publish/join symmetry
    // that fails silently in production if it ever drifts (signalr-cutover.md C1).
    [Fact]
    public async Task join_city_v2_adds_connection_to_group_named_by_slug()
    {
        var fakeGroups = new FakeGroupManager();
        var hub = new TransitHub(new FakeLastBatchCache(), NullLogger<TransitHub>.Instance)
        {
            Groups = fakeGroups,
            Context = new FakeHubCallerContext("conn-1"),
        };

        await hub.JoinCityV2("atlanta");

        Assert.Equal("conn-1", fakeGroups.LastConnectionId);
        Assert.Equal("atlanta", fakeGroups.LastGroupName);
    }

    // contract C2: no unversioned JoinCity shim — reintroducing one recreates the exact
    // silent-failure hazard the version gate exists to prevent.
    [Fact]
    public void legacy_join_city_method_is_absent()
    {
        var method = typeof(TransitHub).GetMethod("JoinCity", BindingFlags.Public | BindingFlags.Instance);
        Assert.Null(method);
    }

    // Existing replay behaviour (cached batch sent to the joining caller) preserved under
    // the new method name.
    [Fact]
    public async Task join_city_v2_replays_cached_batch_for_that_city()
    {
        var fakeCache = new FakeLastBatchCache();
        var batch = MakeBatch("v1");
        fakeCache.Set("atlanta", batch);

        var fakeGroups = new FakeGroupManager();
        var fakeClients = new FakeCallerHubClients();
        var hub = new TransitHub(fakeCache, NullLogger<TransitHub>.Instance)
        {
            Groups = fakeGroups,
            Context = new FakeHubCallerContext("conn-1"),
            Clients = fakeClients,
        };

        await hub.JoinCityV2("atlanta");

        Assert.Equal(1, fakeClients.CallerProxy.SendAsyncCallCount);
        Assert.Equal(HubMethods.ReceiveBatch, fakeClients.CallerProxy.LastMethodCalled);
        Assert.Same(batch, fakeClients.CallerProxy.LastArgs![0]);
    }

    // --- Fakes ---

    sealed class FakeGroupManager : IGroupManager
    {
        public string? LastConnectionId { get; private set; }
        public string? LastGroupName { get; private set; }

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            LastConnectionId = connectionId;
            LastGroupName = groupName;
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    sealed class FakeHubCallerContext(string connectionId) : HubCallerContext
    {
        public override string ConnectionId { get; } = connectionId;
        public override string? UserIdentifier => null;
        public override System.Security.Claims.ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override Microsoft.AspNetCore.Http.Features.IFeatureCollection Features { get; } = new Microsoft.AspNetCore.Http.Features.FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    sealed class FakeCallerHubClients : IHubCallerClients
    {
        public FakeClientProxy CallerProxy { get; } = new();
        public FakeClientProxy AllProxy { get; } = new();

        public IClientProxy Caller => CallerProxy;
        public IClientProxy Others => AllProxy;
        public IClientProxy All => AllProxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => AllProxy;
        public IClientProxy Client(string connectionId) => AllProxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => AllProxy;
        public IClientProxy Group(string groupName) => AllProxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => AllProxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => AllProxy;
        public IClientProxy OthersInGroup(string groupName) => AllProxy;
        public IClientProxy User(string userId) => AllProxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => AllProxy;
        public IClientProxy OthersInGroups(IReadOnlyList<string> groupNames) => AllProxy;
    }

    sealed class FakeLastBatchCache : ILastBatchCache
    {
        public int WriteCount { get; private set; }
        public string? LastCity { get; private set; }
        public IReadOnlyList<EventEnvelope>? LastWritten { get; private set; }
        readonly Dictionary<string, IReadOnlyList<EventEnvelope>> _store = new();

        public IReadOnlyList<EventEnvelope> Current(string city)
            => _store.TryGetValue(city, out var v) ? v : Array.Empty<EventEnvelope>();

        public void Set(string city, IReadOnlyList<EventEnvelope> batch)
        {
            LastCity = city;
            LastWritten = batch;
            _store[city] = batch;
            WriteCount++;
        }
    }

    sealed class FakeClientProxy : IClientProxy
    {
        public int SendAsyncCallCount { get; private set; }
        public string? LastMethodCalled { get; private set; }
        public object?[]? LastArgs { get; private set; }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            SendAsyncCallCount++;
            LastMethodCalled = method;
            LastArgs = args;
            return Task.CompletedTask;
        }
    }

    sealed class FakeHubClients : IHubClients
    {
        public FakeClientProxy AllProxy { get; } = new();
        public FakeClientProxy GroupProxy { get; } = new();
        public string? LastGroupName { get; private set; }

        public IClientProxy All => AllProxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => AllProxy;
        public IClientProxy Client(string connectionId) => AllProxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => AllProxy;
        public IClientProxy Group(string groupName) { LastGroupName = groupName; return GroupProxy; }
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => GroupProxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => GroupProxy;
        public IClientProxy User(string userId) => AllProxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => AllProxy;
    }

    sealed class FakeHubContext : IHubContext<TransitHub>
    {
        private readonly FakeHubClients _clients = new();

        public FakeClientProxy AllProxy => _clients.AllProxy;
        public FakeClientProxy GroupProxy => _clients.GroupProxy;
        public string? LastGroupName => _clients.LastGroupName;

        public IHubClients Clients => _clients;
        public IGroupManager Groups => throw new NotImplementedException();
    }
}
