using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
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

    static RouteShapeFeature Shape(string routeId = "1") => new(
        "Feature",
        new RouteShapeGeometry("LineString", [[-84.4, 33.7], [-84.3, 33.8]]),
        new RouteShapeProperties(routeId, routeId, "#000000", "#ffffff", City: "atlanta"));

    sealed class NullPublisher : ITransitHubPublisher
    {
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> PublishBatchAsync(string city, List<EventEnvelope> batch, CancellationToken ct = default) => Task.FromResult(true);
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
