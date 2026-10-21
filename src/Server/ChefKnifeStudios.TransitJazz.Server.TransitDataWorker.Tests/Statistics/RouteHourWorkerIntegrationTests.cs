using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Checkpoints;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Cities;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using ChefKnifeStudios.TransitJazz.Shared.Geospatial;
using ChefKnifeStudios.TransitJazz.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
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

    [Fact]
    public async Task SeattleOverlapContributesOnceAndKeepsWorkerIdentityWhenSourcePrecedenceChanges()
    {
        using var fixture = await WorkerFixture.CreateAsync(CityNames.Seattle);
        var firstAt = Utc(12, 5);
        var secondAt = Utc(12, 15);
        var kcmOverlap = SeattleTestFixtures.Feed(
            SeattleTestFixtures.Vehicle("paired", "100451", latitude: 47.6, longitude: -122.33, timestamp: new DateTimeOffset(firstAt)),
            SeattleTestFixtures.Vehicle("same-text", "local-kcm", latitude: 47.6, longitude: -122.33, timestamp: new DateTimeOffset(firstAt)));
        var stOverlap = SeattleTestFixtures.Feed(
            SeattleTestFixtures.Vehicle("paired", "556", latitude: 47.6, longitude: -122.33, timestamp: new DateTimeOffset(firstAt)),
            SeattleTestFixtures.Vehicle("same-text", "local-st", latitude: 47.6, longitude: -122.33, timestamp: new DateTimeOffset(firstAt)));
        var stOnlyAfterSwitch = SeattleTestFixtures.Feed(
            SeattleTestFixtures.Vehicle("paired", "556", latitude: 47.6, longitude: -122.32, timestamp: new DateTimeOffset(secondAt)),
            SeattleTestFixtures.Vehicle("same-text", "local-st", latitude: 47.6, longitude: -122.32, timestamp: new DateTimeOffset(secondAt)));
        var phase = 0;
        var clock = new MutableClock(new DateTimeOffset(firstAt));
        var city = SeattleCityForTest(kcmOverlap, stOverlap, stOnlyAfterSwitch, () => phase, clock);

        var firstFeed = await city.FetchVehiclesAsync(CancellationToken.None);
        Assert.Equal(3, firstFeed.Feed.Entities.Count);
        var firstEntity = Assert.Single(firstFeed.Feed.Entities, entity => entity.Vehicle!.Vehicle!.Id == "kcm:paired");
        Assert.Equal("kcm:paired", firstEntity.Vehicle!.Vehicle!.Id);
        Assert.Contains(firstFeed.Feed.Entities, entity => entity.Vehicle!.Vehicle!.Id == "kcm:same-text");
        Assert.Contains(firstFeed.Feed.Entities, entity => entity.Vehicle!.Vehicle!.Id == "st:same-text");
        var first = await fixture.Observe(firstFeed.Feed, firstAt);

        phase = 1; // KCM fails; remembered corroboration preserves identity when ST is the only source.
        clock.SetUtcNow(new DateTimeOffset(secondAt));
        var secondFeed = await city.FetchVehiclesAsync(CancellationToken.None);
        Assert.Equal(2, secondFeed.Feed.Entities.Count);
        var secondEntity = Assert.Single(secondFeed.Feed.Entities, entity => entity.Vehicle!.Vehicle!.Id == "kcm:paired");
        Assert.Equal("kcm:paired", secondEntity.Vehicle!.Vehicle!.Id);
        var second = await fixture.Observe(secondFeed.Feed, secondAt);
        fixture.Capture.FlushPending();

        Assert.Equal(3, first.Result.VehiclesProcessed);
        Assert.Equal(2, second.Result.VehiclesProcessed);
        Assert.Equal(3, first.Result.VehicleStateCacheSize);
        Assert.Equal(3, second.Result.VehicleStateCacheSize);
        Assert.True(second.Result.PublishSucceeded);
        Assert.True(second.Result.TonesEmitted > 0);
        var row = Assert.Single(fixture.Sink.Batches.SelectMany(batch => batch.Rows), row => row.RouteJoinKey == "556");
        Assert.Equal(2, row.VehicleObservationsProcessedCount);
        Assert.Equal(2, row.ActiveVehicleCountSum);
        Assert.Equal(1, row.DistanceIntervalCount);
        Assert.True(row.CrossingsPublishedCount > 0);
    }

    static DateTime Utc(int hour, int minute) => new(2026, 10, 1, hour, minute, 0, DateTimeKind.Utc);

    static SeattleCity SeattleCityForTest(FeedMessage kcm, FeedMessage firstSt, FeedMessage laterSt,
        Func<int> phase, MutableClock clock)
    {
        var config = new CityConfig
        {
            Name = CityNames.Seattle,
            GtfsRtUrls =
            [
                "https://s3.amazonaws.com/kcm-alerts-realtime-prod/vehiclepositions.pb",
                "https://api.pugetsound.onebusaway.org/api/gtfs_realtime/vehicle-positions-for-agency/40.pb",
            ],
            SeattleSoundTransitApiKeyEnvVar = SeattleCity.SoundTransitApiKeySetting,
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [SeattleCity.SoundTransitApiKeySetting] = SeattleTestFixtures.SoundTransitKeySentinel,
        }).Build();
        var factory = new SeattleTestFixtures.CapturingHttpClientFactory(_ => new SeattleTestFixtures.CapturingHandler((request, _) =>
        {
            if (request.RequestUri!.Host == "s3.amazonaws.com")
                return Task.FromResult(phase() == 0 ? SeattleTestFixtures.ProtobufResponse(kcm)
                    : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            return Task.FromResult(SeattleTestFixtures.ProtobufResponse(phase() == 0 ? firstSt : laterSt));
        }));
        return new SeattleCity(config, factory, configuration, NullLogger<SeattleCity>.Instance, clock);
    }

    sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void SetUtcNow(DateTimeOffset value) => _now = value;
    }

    sealed class WorkerFixture : IDisposable
    {
        public RouteHourCaptureFixture Base { get; } = new();
        public RouteHourCaptureFixture.RecordingPublisher Publisher { get; } = new();
        public RouteHourCaptureFixture.UnusedFetchCity City { get; }
        readonly string _citySlug;
        public RouteHourStatisticsCapture Capture { get; }
        public Worker Worker { get; }
        public RouteHourCatalog Catalog { get; }
        public IReadOnlyDictionary<string, RoutePoint[]> Index { get; }
        public IReadOnlyDictionary<string, string> Categories { get; }
        public RouteHourCaptureFixture.RecordingRouteHourSink Sink => Base.Sink;

        WorkerFixture(string citySlug)
        {
            _citySlug = citySlug;
            City = new RouteHourCaptureFixture.UnusedFetchCity(citySlug);
            Capture = new RouteHourStatisticsCapture(Base.Runtime(citySlug: citySlug), Base.Sink, Base.Clock,
                NullLogger<RouteHourStatisticsCapture>.Instance);
            var source = new InMemoryRouteShapeSource();
            var shapes = citySlug == CityNames.Seattle
                ? new[]
                {
                    RouteHourCaptureFixture.Shape("kcm-556", "556", citySlug: citySlug),
                    RouteHourCaptureFixture.Shape("local-kcm", "local-kcm", citySlug: citySlug),
                    RouteHourCaptureFixture.Shape("local-st", "local-st", citySlug: citySlug),
                }
                : [RouteHourCaptureFixture.Shape(citySlug: citySlug)];
            source.Publish(shapes);
            Worker = new Worker(NullLogger<Worker>.Instance, Publisher, [City],
                new TriggerPointGenerator(NullLogger<TriggerPointGenerator>.Instance), routeShapeSource: source,
                routeHourStatisticsCapture: Capture);
            Worker.InitializeRouteIndexAsync(CancellationToken.None).GetAwaiter().GetResult();
            Index = GetDictionary<IReadOnlyDictionary<string, RoutePoint[]>>("_routeIndex");
            Categories = GetDictionary<IReadOnlyDictionary<string, string>>("_routeMode");
            Catalog = GetDictionary<RouteHourCatalog?>("_routeHourCatalogs")!;
        }

        public static async Task<WorkerFixture> CreateAsync(string citySlug = RouteHourCaptureFixture.City) =>
            await Task.FromResult(new WorkerFixture(citySlug));

        public async Task<(Worker.CityTickResult Result, RouteHourStatisticsCycle Cycle)> Observe(FeedMessage feed, DateTime atUtc)
        {
            var cycle = Capture.BeginCycle(_citySlug, Catalog)!;
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
            return (T)dictionary[_citySlug]!;
        }

        public void Dispose() => Worker.Dispose();
    }
}
