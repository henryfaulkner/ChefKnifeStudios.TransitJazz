using ChefKnifeStudios.TransitJazz.Server.WebAPI.SignalR;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests;

// P2-U6 (051 US3): TransitHub.LeaveCity / JoinCity replay, exercised by constructing the real
// Hub subclass directly and substituting its base-class Context/Clients/Groups properties with
// hand-written fakes — the WorkerTransitHubTests fake style, extended with a FakeGroupManager
// since TransitHub (unlike WorkerTransitHub) calls Groups.Add/RemoveFromGroupAsync via the Hub
// base class rather than an injected IHubContext.
public class TransitHubTests
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

    static TransitHub MakeHub(FakeLastBatchCache cache, out FakeGroupManager groups, out FakeCallerClients clients, string connectionId = "conn-1")
    {
        groups = new FakeGroupManager();
        clients = new FakeCallerClients();
        var hub = new TransitHub(cache, NullLogger<TransitHub>.Instance)
        {
            Context = new FakeHubCallerContext(connectionId),
            Groups = groups,
            Clients = clients,
        };
        return hub;
    }

    [Fact]
    public async Task LeaveCity_RemovesConnectionFromCityGroup()
    {
        var hub = MakeHub(new FakeLastBatchCache(), out var groups, out _, connectionId: "conn-1");

        await hub.LeaveCity("marta");

        Assert.Equal(1, groups.RemoveCallCount);
        Assert.Equal("conn-1", groups.LastConnectionId);
        Assert.Equal("marta", groups.LastGroupName);
        Assert.Equal(0, groups.AddCallCount);
    }

    [Fact]
    public async Task JoinCity_ReplaysCachedSnapshotToCaller()
    {
        var cache = new FakeLastBatchCache();
        var batch = MakeBatch("v1");
        cache.Set("marta", batch);
        var hub = MakeHub(cache, out var groups, out var clients);

        await hub.JoinCityV2("marta");

        Assert.Equal(1, groups.AddCallCount);
        Assert.Equal("marta", groups.LastGroupName);
        Assert.Equal(1, clients.CallerProxy.SendAsyncCallCount);
        Assert.Equal("ReceiveBatch", clients.CallerProxy.LastMethodCalled);
        Assert.Same(batch, clients.CallerProxy.LastArgs![0]);
    }

    [Fact]
    public async Task JoinCity_EmptyCache_SendsNothing()
    {
        var hub = MakeHub(new FakeLastBatchCache(), out var groups, out var clients);

        await hub.JoinCityV2("marta");

        Assert.Equal(1, groups.AddCallCount);
        Assert.Equal(0, clients.CallerProxy.SendAsyncCallCount);
    }

    // --- Fakes (WorkerTransitHubTests style: hand-written, call counts + "Last*" captures) ---

    sealed class FakeLastBatchCache : ILastBatchCache
    {
        readonly Dictionary<string, IReadOnlyList<EventEnvelope>> _store = new();

        public IReadOnlyList<EventEnvelope> Current(string city)
            => _store.TryGetValue(city, out var v) ? v : Array.Empty<EventEnvelope>();

        public void Set(string city, IReadOnlyList<EventEnvelope> batch) => _store[city] = batch;
    }

    sealed class FakeGroupManager : IGroupManager
    {
        public int AddCallCount { get; private set; }
        public int RemoveCallCount { get; private set; }
        public string? LastConnectionId { get; private set; }
        public string? LastGroupName { get; private set; }

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
        {
            AddCallCount++;
            LastConnectionId = connectionId;
            LastGroupName = groupName;
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
        {
            RemoveCallCount++;
            LastConnectionId = connectionId;
            LastGroupName = groupName;
            return Task.CompletedTask;
        }
    }

    sealed class FakeClientProxy : ISingleClientProxy
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

        public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    sealed class FakeCallerClients : IHubCallerClients
    {
        public FakeClientProxy CallerProxy { get; } = new();

        public IClientProxy Caller => CallerProxy;
        public IClientProxy Others => CallerProxy;
        public IClientProxy All => CallerProxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => CallerProxy;
        public IClientProxy Client(string connectionId) => CallerProxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => CallerProxy;
        public IClientProxy Group(string groupName) => CallerProxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => CallerProxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => CallerProxy;
        public IClientProxy OthersInGroup(string groupName) => CallerProxy;
        public IClientProxy User(string userId) => CallerProxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => CallerProxy;
    }

    sealed class FakeHubCallerContext(string connectionId) : HubCallerContext
    {
        public override string ConnectionId { get; } = connectionId;
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }
}
