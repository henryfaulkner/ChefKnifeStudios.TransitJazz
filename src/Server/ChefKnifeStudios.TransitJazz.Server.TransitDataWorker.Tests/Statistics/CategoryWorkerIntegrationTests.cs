using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using ChefKnifeStudios.TransitJazz.Shared.Geospatial;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using ChefKnifeStudios.TransitJazz.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class CategoryWorkerIntegrationTests
{
    [Fact]
    public async Task Activity_uses_the_first_valid_joined_identity_before_snapping()
    {
        using var fixture = await ReconciliationFixture.CreateAsync();
        var index = fixture.Index.ToDictionary(x => x.Key, x => x.Value);
        index["joined-no-geometry"] = [];
        var modes = new Dictionary<string, string> { ["B1"] = "bus", ["R1"] = "rail", ["joined-no-geometry"] = "bus" };
        var cycle = fixture.Statistics.Begin(1, "bus", "rail");
        var feed = Feed(
            Vehicle("duplicate", "B1", 0),
            Vehicle("duplicate", "R1", 0),
            Vehicle("before-snap", "joined-no-geometry", 0),
            Vehicle("invalid-lat", "B1", 0, latitude: float.NaN),
            Vehicle("invalid-lon", "B1", 0, longitude: 181f),
            Vehicle("no-join", "absent-route", 0),
            Vehicle("no-route", null, 0));

        await fixture.Worker.ProcessSpatialReconciliationAsync(fixture.City, feed, index, modes, CancellationToken.None, cycle);
        var snapshot = cycle.ToSnapshot(fixture.Statistics.Start.UtcDateTime, true, true, false);

        Assert.Equal(new[] { "before-snap", "duplicate" }, snapshot.ActivityVehiclesByCategory["bus"].Order(StringComparer.Ordinal));
        Assert.False(snapshot.ActivityVehiclesByCategory.ContainsKey("rail"));
        Assert.False(snapshot.ActivityVehiclesByCategory.ContainsKey("unknown"));
    }

    [Fact]
    public async Task Movement_uses_fresh_source_timestamps_and_absolute_reverse_distance()
    {
        using var fixture = await ReconciliationFixture.CreateAsync();
        var first = await fixture.ObserveAsync(Feed(Vehicle("vehicle", "B1", 0, timestamp: 2_000)));
        var forward = await fixture.ObserveAsync(Feed(Vehicle("vehicle", "B1", 10, timestamp: 2_001)));
        var repeated = await fixture.ObserveAsync(Feed(Vehicle("vehicle", "B1", 0, timestamp: 2_001)));
        var reverse = await fixture.ObserveAsync(Feed(Vehicle("vehicle", "B1", 0, timestamp: 2_002)));

        Assert.Equal(0, first.MovementByCategory["bus"].AcceptedIntervalCount);
        Assert.Equal(1, forward.MovementByCategory["bus"].AcceptedIntervalCount);
        Assert.InRange(forward.MovementByCategory["bus"].DistanceMeters, 900m, 1_000m);
        Assert.Equal(0, repeated.MovementByCategory["bus"].AcceptedIntervalCount);
        Assert.Equal(1, repeated.MovementByCategory["bus"].RejectedCount);
        Assert.Equal(1, reverse.MovementByCategory["bus"].AcceptedIntervalCount);
        Assert.Equal(forward.MovementByCategory["bus"].DistanceMeters, reverse.MovementByCategory["bus"].DistanceMeters);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Crossing_credit_requires_the_actual_city_batch_to_publish_successfully(bool succeeds, bool throws)
    {
        using var fixture = await ReconciliationFixture.CreateAsync();
        await fixture.ObserveAsync(Feed(Vehicle("vehicle", "B1", 0, timestamp: 2_000)));
        fixture.Publisher.Success = succeeds;
        fixture.Publisher.Throws = throws;

        var snapshot = await fixture.ObserveAsync(Feed(Vehicle("vehicle", "B1", 10, timestamp: 2_001)));

        Assert.Equal(2, fixture.Publisher.LastCrossingCount);
        Assert.Equal(succeeds ? fixture.Publisher.LastCrossingCount : 0, snapshot.CrossingsByCategory.GetValueOrDefault("bus"));
        Assert.Equal(1, snapshot.ActivityVehiclesByCategory["bus"].Count);
        Assert.Equal(succeeds, snapshot.PublicationKnown);
    }

    [Fact]
    public async Task Catalog_refresh_during_publication_withholds_mixed_generation_capture()
    {
        using var fixture = await ReconciliationFixture.CreateAsync();
        var first = fixture.Statistics.Begin(1, "bus", "rail");
        await fixture.Worker.ProcessSpatialReconciliationAsync(fixture.City, Feed(Vehicle("vehicle", "B1", 0)),
            fixture.Index, ReconciliationFixture.Modes, CancellationToken.None, first);
        fixture.Statistics.Complete(first, fixture.Statistics.Start);
        var second = fixture.Statistics.Begin(1, "bus", "rail");
        fixture.Publisher.BeforeReturn = fixture.RefreshCatalogAsync;

        var result = await fixture.Worker.ProcessSpatialReconciliationAsync(fixture.City,
            Feed(Vehicle("vehicle", "B1", 10, timestamp: 2_001)), fixture.Index,
            ReconciliationFixture.Modes, CancellationToken.None, second);
        fixture.Statistics.Complete(second, fixture.Statistics.Start.AddSeconds(10));
        fixture.Statistics.Capture.FlushPending();

        Assert.True(result.PublishSucceeded);
        Assert.True(second.CaptureFailed);
        var minute = Assert.Single(fixture.Statistics.Sink.Batches.SelectMany(x => x.Minutes), x => x.Category == "bus");
        Assert.Equal(CategoryCoverageStatus.Partial, minute.Status);
        Assert.Equal(0, minute.CrossingsPublishedCount);
        Assert.Equal(0m, minute.DistanceMetersSum);
    }

    static FeedMessage Feed(params FeedEntity[] vehicles) => new() { Entities = vehicles.ToList() };

    static FeedEntity Vehicle(string id, string? route, int point, ulong timestamp = 2_000, float? latitude = null, float? longitude = null) => new()
    {
        Id = id,
        Vehicle = new VehiclePosition
        {
            Vehicle = new VehicleDescriptor { Id = id },
            Trip = new TripDescriptor { RouteId = route },
            Timestamp = timestamp,
            Position = new Position { Latitude = latitude ?? 33.7f, Longitude = longitude ?? (float)(-84.4 + point * 0.001) },
        },
    };

    sealed class ReconciliationFixture : IDisposable
    {
        public CategoryStatisticsCaptureFixture Statistics { get; } = new();
        public PublisherSpy Publisher { get; } = new();
        public ITransitCity City { get; } = new UnusedFetchCity();
        public Worker Worker { get; private set; } = null!;
        public IReadOnlyDictionary<string, RoutePoint[]> Index { get; private set; } = null!;
        readonly InMemoryRouteShapeSource _source = new();
        public static readonly IReadOnlyDictionary<string, string> Modes = new Dictionary<string, string> { ["B1"] = "bus", ["R1"] = "rail" };

        public static async Task<ReconciliationFixture> CreateAsync()
        {
            var fixture = new ReconciliationFixture();
            var source = fixture._source;
            source.Publish([Shape("B1", "bus"), Shape("R1", "rail")]);
            fixture.Worker = new Worker(NullLogger<Worker>.Instance, fixture.Publisher, [fixture.City],
                new TriggerPointGenerator(NullLogger<TriggerPointGenerator>.Instance),
                routeShapeSource: source, categoryStatisticsCapture: fixture.Statistics.Capture);
            await fixture.Worker.InitializeRouteIndexAsync(CancellationToken.None);
            // Reuse the actual initialized route points, cumulative distances and checkpoints.
            fixture.Index = ((Dictionary<string, IReadOnlyDictionary<string, RoutePoint[]>>)typeof(Worker)
                .GetField("_routeIndex", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Worker)!)["atlanta"];
            return fixture;
        }

        public async Task RefreshCatalogAsync()
        {
            _source.Publish([Shape("B1", "bus"), Shape("R1", "rail")]);
            await Worker.InitializeRouteIndexAsync(CancellationToken.None);
        }

        public async Task<CategoryCycleSnapshot> ObserveAsync(FeedMessage feed)
        {
            var cycle = Statistics.Begin(1, "bus", "rail");
            var result = await Worker.ProcessSpatialReconciliationAsync(City, feed, Index, Modes, CancellationToken.None, cycle);
            var activityEligible = result.HealthOk && result.ProcessingExceptionType is null;
            var publicationKnown = activityEligible && (!result.PublishAttempted || result.PublishSucceeded == true);
            return cycle.ToSnapshot(Statistics.Start.UtcDateTime, activityEligible, publicationKnown,
                !activityEligible || !publicationKnown);
        }

        static RouteShapeFeature Shape(string route, string category) => new("Feature",
            new RouteShapeGeometry("LineString", Enumerable.Range(0, 11).Select(i => new[] { -84.4 + i * 0.001, 33.7 }).ToArray()),
            new RouteShapeProperties(route, route, "#000000", "#ffffff", Category: category, City: "atlanta"));

        public void Dispose() => Worker.Dispose();
    }

    sealed class PublisherSpy : ITransitHubPublisher
    {
        public bool Success { get; set; } = true;
        public bool Throws { get; set; }
        public Func<Task>? BeforeReturn { get; set; }
        public long LastCrossingCount { get; private set; }
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public async Task<bool> PublishBatchAsync(string city, List<EventEnvelope> batch, CancellationToken ct = default)
        {
            LastCrossingCount = batch.Select(x => x.Payload).OfType<RouteCrossingBatchEvent>().Sum(x => (long)x.BatchRecords.Count());
            if (BeforeReturn is not null) await BeforeReturn();
            if (Throws) throw new InvalidOperationException("Controlled publisher failure.");
            return Success;
        }
    }

    sealed class UnusedFetchCity : ITransitCity
    {
        public string Name => "atlanta";
        public Task<CityFetchResult> FetchVehiclesAsync(CancellationToken ct) => throw new InvalidOperationException("Only reconciliation is under test.");
    }
}
