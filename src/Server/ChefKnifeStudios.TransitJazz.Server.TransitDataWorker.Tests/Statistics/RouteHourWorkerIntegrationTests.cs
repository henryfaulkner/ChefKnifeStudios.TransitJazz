using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Checkpoints;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;
using ChefKnifeStudios.TransitJazz.Shared.Geospatial;
using ChefKnifeStudios.TransitJazz.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class RouteHourWorkerIntegrationTests
{
    [Fact]
    public async Task AliasedDuplicateFeedCountsOneActiveRepresentativeButKeepsEveryProcessedObservation()
    {
        using var fixture = await WorkerFixture.CreateAsync();
        var cycle = fixture.Capture.BeginCycle(RouteHourCaptureFixture.City, fixture.Catalog)!;
        var feed = RouteHourCaptureFixture.Feed(
            RouteHourCaptureFixture.Vehicle("vehicle-1", "static-1", -84.4f),
            RouteHourCaptureFixture.Vehicle("vehicle-1", "B1", -84.4f));

        var result = await fixture.Worker.ProcessSpatialReconciliationAsync(fixture.City, feed, fixture.Index,
            fixture.Categories, CancellationToken.None, routeHourCycle: cycle);
        fixture.Complete(cycle, result, Utc(12, 5));
        fixture.Capture.FlushPending();

        Assert.Equal(2, result.VehiclesProcessed);
        Assert.True(result.HealthOk);
        var row = Assert.Single(fixture.Sink.Batches.SelectMany(batch => batch.Rows));
        Assert.Equal("B1", row.RouteJoinKey);
        Assert.Equal("static-1", row.StaticRouteId);
        Assert.Equal(1, row.ActiveVehicleCountSum);
        Assert.Equal(2, row.VehicleObservationsProcessedCount);
        Assert.Equal(1, row.StaleObservationsCount);
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 0)]
    public async Task CrossingRecordsAreDetectedButPublishedOnlyAfterPublisherSuccess(bool succeeds, long expectedPublished)
    {
        using var fixture = await WorkerFixture.CreateAsync();
        var first = await fixture.Observe(RouteHourCaptureFixture.Feed(
            RouteHourCaptureFixture.Vehicle("vehicle-1", "static-1", -84.4f, 2_000)), Utc(12, 5));
        fixture.Publisher.Succeeds = succeeds;
        var second = await fixture.Observe(RouteHourCaptureFixture.Feed(
            RouteHourCaptureFixture.Vehicle("vehicle-1", "static-1", -84.39f, 2_001)), Utc(12, 15));
        fixture.Capture.FlushPending();

        Assert.True(first.Result.HealthOk);
        Assert.Equal(succeeds, second.Result.PublishSucceeded);
        Assert.Equal(fixture.Publisher.LastCrossingCount, second.Result.TonesEmitted);
        var row = Assert.Single(fixture.Sink.Batches.SelectMany(batch => batch.Rows));
        Assert.Equal(fixture.Publisher.LastCrossingCount, row.CrossingsDetectedCount);
        Assert.Equal(expectedPublished, row.CrossingsPublishedCount);
        Assert.Equal(1, row.DistanceIntervalCount);
        Assert.InRange(row.DistanceMetersSum, 900m, 1_000m);
        Assert.Equal(1, row.CrossingsSuppressedFirstSeen);
    }

    [Fact]
    public async Task ThrowingRouteHourSinkAfterSuccessfulPublicationCannotChangeLiveWorkerOutcome()
    {
        using var fixture = await WorkerFixture.CreateAsync();
        await fixture.Observe(RouteHourCaptureFixture.Feed(
            RouteHourCaptureFixture.Vehicle("vehicle-1", "static-1", -84.4f, 2_000)), Utc(12, 5));
        var second = await fixture.Observe(RouteHourCaptureFixture.Feed(
            RouteHourCaptureFixture.Vehicle("vehicle-1", "static-1", -84.39f, 2_001)), Utc(12, 15));
        fixture.Sink.Throw = true;

        fixture.Capture.FlushPending();

        Assert.True(second.Result.HealthOk);
        Assert.True(second.Result.PublishSucceeded);
        Assert.Equal(fixture.Publisher.LastCrossingCount, second.Result.TonesEmitted);
    }

    static DateTime Utc(int hour, int minute) => new(2026, 10, 1, hour, minute, 0, DateTimeKind.Utc);

    sealed class WorkerFixture : IDisposable
    {
        public RouteHourCaptureFixture Base { get; } = new();
        public RouteHourCaptureFixture.RecordingPublisher Publisher { get; } = new();
        public RouteHourCaptureFixture.UnusedFetchCity City { get; } = new();
        public RouteHourStatisticsCapture Capture { get; }
        public Worker Worker { get; }
        public RouteHourCatalog Catalog { get; }
        public IReadOnlyDictionary<string, RoutePoint[]> Index { get; }
        public IReadOnlyDictionary<string, string> Categories { get; }
        public RouteHourCaptureFixture.RecordingRouteHourSink Sink => Base.Sink;

        WorkerFixture()
        {
            Capture = new RouteHourStatisticsCapture(Base.Runtime(), Base.Sink, Base.Clock,
                NullLogger<RouteHourStatisticsCapture>.Instance);
            var source = new InMemoryRouteShapeSource();
            source.Publish([RouteHourCaptureFixture.Shape()]);
            Worker = new Worker(NullLogger<Worker>.Instance, Publisher, [City],
                new TriggerPointGenerator(NullLogger<TriggerPointGenerator>.Instance), routeShapeSource: source,
                routeHourStatisticsCapture: Capture);
            Worker.InitializeRouteIndexAsync(CancellationToken.None).GetAwaiter().GetResult();
            Index = GetDictionary<IReadOnlyDictionary<string, RoutePoint[]>>("_routeIndex");
            Categories = GetDictionary<IReadOnlyDictionary<string, string>>("_routeMode");
            Catalog = GetDictionary<RouteHourCatalog?>("_routeHourCatalogs")!;
        }

        public static async Task<WorkerFixture> CreateAsync() => await Task.FromResult(new WorkerFixture());

        public async Task<(Worker.CityTickResult Result, RouteHourStatisticsCycle Cycle)> Observe(FeedMessage feed, DateTime atUtc)
        {
            var cycle = Capture.BeginCycle(RouteHourCaptureFixture.City, Catalog)!;
            var result = await Worker.ProcessSpatialReconciliationAsync(City, feed, Index, Categories,
                CancellationToken.None, routeHourCycle: cycle);
            Complete(cycle, result, atUtc);
            return (result, cycle);
        }

        public void Complete(RouteHourStatisticsCycle cycle, Worker.CityTickResult result, DateTime atUtc)
        {
            Base.Clock.SetUtcNow(atUtc);
            var activityEligible = result.HealthOk && result.ProcessingExceptionType is null;
            var publicationKnown = !result.PublishAttempted || result.PublishSucceeded == true;
            Capture.CompleteCycle(cycle, activityEligible, publicationKnown,
                !activityEligible || !publicationKnown, atUtc, Catalog);
        }

        T GetDictionary<T>(string fieldName)
        {
            var field = typeof(Worker).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!;
            var dictionary = (System.Collections.IDictionary)field.GetValue(Worker)!;
            return (T)dictionary[RouteHourCaptureFixture.City]!;
        }

        public void Dispose() => Worker.Dispose();
    }
}
