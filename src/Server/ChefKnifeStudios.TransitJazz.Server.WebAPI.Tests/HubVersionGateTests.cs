using ChefKnifeStudios.TransitJazz.Server.WebAPI.SignalR;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests;

// P3-S1 (051 US4): the version gate, proven against a REAL MessagePack HubConnection.
//
// This is the one behaviour the unit layer cannot reach. MessagePack's ReadDouble accepts int
// encodings, so a stale client bundle decoding v2's scaled-int coordinates as v1 doubles would
// NOT throw — it would silently misrender every vehicle on the map. Renaming the hub method to
// "JoinCityV2" converts that silent corruption into a loud failure at join time. Asserting it
// requires the real hub dispatcher (unknown-method handling lives there, not in the Hub class),
// hence the TestHost escalation the test-plan authorizes for exactly this case.
public class HubVersionGateTests : IAsyncLifetime
{
    IHost _host = null!;
    FakeLastBatchCache _cache = null!;

    public async Task InitializeAsync()
    {
        _cache = new FakeLastBatchCache();
        var builder = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddSignalR().AddMessagePackProtocol();
                    services.AddSingleton<ILastBatchCache>(_cache);
                    services.AddLogging();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapHub<TransitHub>("/transithub"));
                });
            });

        _host = await builder.StartAsync();
    }

    public async Task DisposeAsync() => await _host.StopAsync();

    HubConnection MakeClient()
    {
        var server = _host.GetTestServer();
        return new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "transithub"), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
            })
            .AddMessagePackProtocol()
            .Build();
    }

    [Fact]
    public async Task LegacyJoinCity_Faults_AndDeliversNoBatch()
    {
        _cache.Set("marta", MakeSnapshot("v1"));
        var received = new List<object>();
        await using var connection = MakeClient();
        connection.On<List<EventEnvelope>>(HubMethods.ReceiveBatch, b => received.Add(b));
        await connection.StartAsync();

        // The pre-051 client bundle invokes the old name.
        var ex = await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("JoinCity", "marta"));

        // Pins the message text SignalRNotificationService.InitAsync filters on to distinguish a
        // version mismatch from an ordinary connection failure. If a future SignalR release reworded
        // this, that targeted catch would silently fall through to the generic handler and the
        // mismatch would once again surface only as an unexplained empty map.
        Assert.Contains("Method does not exist", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(received);
    }

    [Fact]
    public async Task JoinCityV2_Succeeds_AndReplaysCachedSnapshot()
    {
        _cache.Set("marta", MakeSnapshot("v1"));
        var replayed = new TaskCompletionSource<List<EventEnvelope>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = MakeClient();
        connection.On<List<EventEnvelope>>(HubMethods.ReceiveBatch, b => replayed.TrySetResult(b));
        await connection.StartAsync();

        await connection.InvokeAsync(HubMethods.JoinCity, "marta");

        // Event-based sync, not a sleep: the replay is pushed from the server on join.
        var batch = await replayed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var record = batch.Select(e => e.Payload)
            .OfType<RouteNearestPointBatchEvent>()
            .SelectMany(p => p.BatchRecords)
            .Single();
        Assert.Equal("v1", record.VehicleId);
    }

    static List<EventEnvelope> MakeSnapshot(string vehicleId) => new()
    {
        new EventEnvelope(
            "RouteNearestPointBatchEvent",
            DateTimeOffset.UnixEpoch,
            new RouteNearestPointBatchEvent(new[]
            {
                new RouteNearestPointBatchEvent.RouteNearestPointRecord(
                    vehicleId, "74", null, null, 3_375_100, -8_438_900,
                    10000, null, null, IsStale: false, Category: null)
            }))
    };

    sealed class FakeLastBatchCache : ILastBatchCache
    {
        readonly Dictionary<string, IReadOnlyList<EventEnvelope>> _store = new();
        public IReadOnlyList<EventEnvelope> Current(string city)
            => _store.TryGetValue(city, out var v) ? v : Array.Empty<EventEnvelope>();
        public void Set(string city, IReadOnlyList<EventEnvelope> batch) => _store[city] = batch;
    }
}
