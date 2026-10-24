using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using ChefKnifeStudios.TransitJazz.Shared.Geospatial;
using ChefKnifeStudios.TransitJazz.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests;

public sealed class RouteIndexStartupTests
{
    [Fact]
    public async Task Startup_WaitsForStaticLoaderThenBuildsTheRouteIndex()
    {
        var source = new InMemoryRouteShapeSource();
        var worker = new Worker(
            NullLogger<Worker>.Instance,
            new NullPublisher(),
            Array.Empty<ITransitCity>(),
            new TriggerPointGenerator(NullLogger<TriggerPointGenerator>.Instance),
            routeShapeSource: source);

        var initialization = worker.InitializeRouteIndexAsync(CancellationToken.None);
        Assert.False(initialization.IsCompleted);
        Assert.False(worker.IsReady);

        source.Publish([Shape()]);
        await initialization.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(worker.IsReady);
    }

    [Fact]
    public async Task Refresh_RebuildsImmediatelyAfterTheLoaderPublishesANewerGeneration()
    {
        var source = new InMemoryRouteShapeSource();
        var events = new RouteIndexEventSpy(expectedRouteCount: 2);
        var worker = new Worker(
            NullLogger<Worker>.Instance,
            new NullPublisher(),
            Array.Empty<ITransitCity>(),
            new TriggerPointGenerator(NullLogger<TriggerPointGenerator>.Instance),
            structuredEventLogger: events,
            routeShapeSource: source);

        source.Publish([Shape("first")]);
        await worker.InitializeRouteIndexAsync(CancellationToken.None);

        using var cancellation = new CancellationTokenSource();
        var refresh = worker.RefreshRouteIndexAsync(cancellation.Token);
        source.Publish([Shape("second"), Shape("third")]);

        await events.ExpectedLoad.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
    }

    [Theory]
    [InlineData("512")]
    [InlineData("512-alias")]
    public async Task SeattleRoute512NeverPublishesEvenWhenStaticGeometryExists(string feedRoute)
    {
        var publisher = new CountingPublisher();
        var city = new SeattleTestCity();
        var worker = new Worker(NullLogger<Worker>.Instance, publisher, [city],
            new TriggerPointGenerator(NullLogger<TriggerPointGenerator>.Instance));
        var feed = new FeedMessage
        {
            Entities = [SeattleTestFixtures.Vehicle("st-512", feedRoute, timestamp: SeattleTestFixtures.ReceiptUtc)],
        };
        var futureGeometry = new Dictionary<string, RoutePoint[]>(StringComparer.Ordinal)
        {
            [feedRoute] = [new RoutePoint("512", 47.60, -122.33), new RoutePoint("512", 47.61, -122.32)],
        };

        try
        {
            var result = await worker.ProcessSpatialReconciliationAsync(city, feed, futureGeometry,
                new Dictionary<string, string> { [feedRoute] = "bus" }, CancellationToken.None);
            Assert.Equal(0, result.VehiclesProcessed);
            Assert.Equal(0, result.TonesEmitted);
            Assert.False(result.PublishAttempted);
            Assert.Equal(0, publisher.PublishCount);
        }
        finally
        {
            worker.Dispose();
        }
    }

    [Fact]
    public async Task SeattleDirectRouteIdAndDisplayAliasesResolveToTheSameGeometry()
    {
        var source = new InMemoryRouteShapeSource();
        var worker = new Worker(NullLogger<Worker>.Instance, new NullPublisher(), [],
            new TriggerPointGenerator(NullLogger<TriggerPointGenerator>.Instance), routeShapeSource: source);
        source.Publish(
        [
            SeattleShape("100451", "556"),
            SeattleShape("102767", "547"),
            SeattleShape("100479", "1 Line"),
            SeattleShape("2LINE", "2 Line"),
        ]);

        try
        {
            await worker.InitializeRouteIndexAsync(CancellationToken.None);
            var field = typeof(Worker).GetField("_routeIndex", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var cities = (Dictionary<string, IReadOnlyDictionary<string, RoutePoint[]>>)field.GetValue(worker)!;
            var index = cities[CityNames.Seattle];

            Assert.Equal(new[] { "100451", "102767", "100479", "2LINE", "556", "547", "1 Line", "2 Line" }
                .Order(StringComparer.Ordinal), index.Keys.Order(StringComparer.Ordinal));
            foreach (var (rawId, display) in new[]
                     { ("100451", "556"), ("102767", "547"), ("100479", "1 Line"), ("2LINE", "2 Line") })
                Assert.Equal(index[display], index[rawId]);
            Assert.DoesNotContain("512", index.Keys);
        }
        finally
        {
            worker.Dispose();
        }
    }

    static RouteShapeFeature Shape(string routeId = "1") => new(
        "Feature",
        new RouteShapeGeometry("LineString", [[-84.4, 33.7], [-84.3, 33.8]]),
        new RouteShapeProperties(routeId, routeId, "#000000", "#ffffff", City: "atlanta"));

    static RouteShapeFeature SeattleShape(string routeId, string display) => new(
        "Feature",
        new RouteShapeGeometry("LineString", [[-122.33, 47.6], [-122.32, 47.61]]),
        new RouteShapeProperties(routeId, display, "#000000", "#ffffff", Category: "bus", City: CityNames.Seattle));

    sealed class NullPublisher : ITransitHubPublisher
    {
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> PublishBatchAsync(string city, List<EventEnvelope> batch, CancellationToken ct = default) => Task.FromResult(true);
    }

    sealed class CountingPublisher : ITransitHubPublisher
    {
        public int PublishCount { get; private set; }
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> PublishBatchAsync(string city, List<EventEnvelope> batch, CancellationToken ct = default)
        {
            PublishCount++;
            return Task.FromResult(true);
        }
    }

    sealed class SeattleTestCity : ITransitCity
    {
        public string Name => CityNames.Seattle;
        public Task<CityFetchResult> FetchVehiclesAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    sealed class RouteIndexEventSpy(int expectedRouteCount) : IWorkerStructuredEventLogger
    {
        public TaskCompletionSource<bool> ExpectedLoad { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Emit(StructuredLogEvent logEvent)
        {
            if (logEvent.EventName == nameof(StructuredLogEventName.RouteIndexLoaded)
                && logEvent.RouteCount == expectedRouteCount)
                ExpectedLoad.TrySetResult(true);
        }

        public void EmitRecovery(StructuredLogEvent recoveryEvent, string recoveredEventName, string? recoveredReasonCode) { }
    }
}
