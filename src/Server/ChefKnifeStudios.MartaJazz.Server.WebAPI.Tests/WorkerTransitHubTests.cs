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
                    id, "74", 33.75, -84.39, DateTime.UtcNow,
                    33.751, -84.389, DateTime.UtcNow, null, null, false)
            })
        )).ToList();

    [Fact]
    public async Task PublishBatch_CachesBatch()
    {
        var fakeCache = new FakeLastBatchCache();
        var fakeHub = new FakeHubContext();
        var hub = new WorkerTransitHub(fakeHub, NullLogger<WorkerTransitHub>.Instance, fakeCache);
        var batch = MakeBatch("v1");

        await hub.PublishBatch(batch);

        Assert.Equal(1, fakeCache.WriteCount);
        Assert.Equal(batch, fakeCache.LastWritten);
    }

    [Fact]
    public async Task PublishBatch_StillRelays()
    {
        var fakeHub = new FakeHubContext();
        var hub = new WorkerTransitHub(fakeHub, NullLogger<WorkerTransitHub>.Instance, new FakeLastBatchCache());
        var batch = MakeBatch("v1");

        await hub.PublishBatch(batch);

        Assert.Equal(1, fakeHub.AllProxy.SendAsyncCallCount);
        Assert.Equal("ReceiveBatch", fakeHub.AllProxy.LastMethodCalled);
    }

    // T011 (US2): a batch with both non-stale and stale records is relayed in FULL (stale included);
    // the relayed argument is the same instance as the input, proving filtering touches only the cache.
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
                        "v1", "74", 33.75, -84.39, DateTime.UtcNow,
                        33.751, -84.389, DateTime.UtcNow, null, null, false),
                    new RouteNearestPointBatchEvent.RouteNearestPointRecord(
                        "v2", "74", 33.75, -84.39, DateTime.UtcNow,
                        33.760, -84.380, DateTime.UtcNow, null, null, true)
                }))
        };

        await hub.PublishBatch(batch);

        var relayed = Assert.IsAssignableFrom<IReadOnlyList<EventEnvelope>>(fakeHub.AllProxy.LastArgs![0]);
        Assert.Same(batch, relayed);
        var relayedRecords = relayed
            .Select(e => e.Payload)
            .OfType<RouteNearestPointBatchEvent>()
            .SelectMany(p => p.BatchRecords)
            .ToList();
        Assert.Equal(2, relayedRecords.Count);
        Assert.Contains(relayedRecords, r => r.IsStale);
    }

    [Fact]
    public async Task PublishBatch_CachesEvenIfEmpty()
    {
        var fakeCache = new FakeLastBatchCache();
        var fakeHub = new FakeHubContext();
        var hub = new WorkerTransitHub(fakeHub, NullLogger<WorkerTransitHub>.Instance, fakeCache);

        await hub.PublishBatch(new List<EventEnvelope>());

        Assert.Equal(1, fakeCache.WriteCount);
        Assert.Equal(1, fakeHub.AllProxy.SendAsyncCallCount);
    }

    // --- Fakes ---

    sealed class FakeLastBatchCache : ILastBatchCache
    {
        public int WriteCount { get; private set; }
        public IReadOnlyList<EventEnvelope>? LastWritten { get; private set; }

        public IReadOnlyList<EventEnvelope> Current => LastWritten ?? Array.Empty<EventEnvelope>();

        public void Set(IReadOnlyList<EventEnvelope> batch)
        {
            LastWritten = batch;
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

        public IClientProxy All => AllProxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => AllProxy;
        public IClientProxy Client(string connectionId) => AllProxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => AllProxy;
        public IClientProxy Group(string groupName) => AllProxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => AllProxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => AllProxy;
        public IClientProxy User(string userId) => AllProxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => AllProxy;
    }

    sealed class FakeHubContext : IHubContext<TransitHub>
    {
        private readonly FakeHubClients _clients = new();

        public FakeClientProxy AllProxy => _clients.AllProxy;

        public IHubClients Clients => _clients;
        public IGroupManager Groups => throw new NotImplementedException();
    }
}
