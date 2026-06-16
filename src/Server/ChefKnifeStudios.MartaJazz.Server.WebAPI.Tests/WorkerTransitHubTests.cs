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

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            SendAsyncCallCount++;
            LastMethodCalled = method;
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
