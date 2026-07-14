using ChefKnifeStudios.MartaJazz.Server.WebAPI.SignalR;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ChefKnifeStudios.MartaJazz.Server.WebAPI.Tests;

public class WorkerTransitHubTests
{
    static List<EventEnvelope> MakeBatch(params string[] vehicleIds)
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
                        "v1", "74", 33.75, -84.39,
                        33.751, -84.389, 10000, null, null, false),
                    new RouteNearestPointBatchEvent.RouteNearestPointRecord(
                        "v2", "74", 33.75, -84.39,
                        33.760, -84.380, 10000, null, null, true)
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

    // --- Fakes ---

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
