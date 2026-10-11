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
using Microsoft.Extensions.Logging;
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
    public async Task Changed_catalog_during_publication_withholds_mixed_generation_capture()
    {
        using var fixture = await ReconciliationFixture.CreateAsync();
        var first = fixture.Statistics.Begin(1, "bus", "rail");
        await fixture.Worker.ProcessSpatialReconciliationAsync(fixture.City, Feed(Vehicle("vehicle", "B1", 0)),
            fixture.Index, ReconciliationFixture.Modes, CancellationToken.None, first);
        fixture.Statistics.Complete(first, fixture.Statistics.Start);
        var second = fixture.Statistics.Begin(1, "bus", "rail");
        fixture.Shapes = [ReconciliationFixture.Shape("B1", "bus", offset: 0.001), ReconciliationFixture.Shape("R1", "rail")];
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

    [Fact]
    public async Task Unchanged_catalog_during_publication_preserves_the_in_flight_cycle_and_movement()
    {
        using var fixture = await ReconciliationFixture.CreateAsync();
        await fixture.ObserveAndCompleteAsync(Feed(Vehicle("vehicle", "B1", 0)), fixture.Statistics.Start);
        fixture.Publisher.BeforeReturn = fixture.RefreshCatalogAsync;

        var cycle = await fixture.ObserveAndCompleteAsync(Feed(Vehicle("vehicle", "B1", 10, timestamp: 2_001)),
            fixture.Statistics.Start.AddSeconds(10));
        fixture.Statistics.Capture.FlushPending();

        Assert.False(cycle.CaptureFailed);
        Assert.Equal(1, fixture.Worker.GetRouteCatalogSnapshot("atlanta").Generation);
        var minute = Assert.Single(fixture.Statistics.Sink.Batches.SelectMany(x => x.Minutes), x => x.Category == "bus");
        Assert.Equal(1, minute.DistanceIntervalCount);
        Assert.InRange(minute.DistanceMetersSum, 900m, 1_000m);
        Assert.Equal(fixture.Publisher.LastCrossingCount, minute.CrossingsPublishedCount);
        Assert.Equal(0, minute.FailedCycleCount);
    }

    [Fact]
    public async Task Healthy_hours_keep_complete_minutes_exact_populations_and_movement_across_six_hour_refreshes()
    {
        using var fixture = await ReconciliationFixture.CreateAsync();
        var start = fixture.Statistics.Start;
        for (var seconds = -10; seconds <= 13 * 3_600; seconds += 10)
        {
            if (seconds is 2_280 or 23_880 or 45_480) await fixture.RefreshCatalogAsync();
            var point = (seconds / 10) % 2 == 0 ? 0 : 1;
            await fixture.ObserveAndCompleteAsync(Feed(Vehicle("vehicle", "B1", point, timestamp: (ulong)(2_001 + seconds / 10))),
                start.AddSeconds(seconds));
        }

        var hours = fixture.Statistics.Sink.Batches.SelectMany(x => x.Hours)
            .Where(x => x.WindowStartUtc >= start.UtcDateTime).ToArray();
        Assert.Equal(26, hours.Length); // Thirteen healthy hours for both bus and rail.
        Assert.All(hours, row =>
        {
            Assert.Equal(CategoryCoverageStatus.Complete, row.Status);
            Assert.Equal(60, row.CoveredMinutes);
            Assert.Equal(row.Category == "bus" ? 1L : 0L, row.DistinctActiveVehicleCount);
            Assert.Equal(row.Category == "bus" ? 360L : 0L, row.DistanceIntervalCount);
        });
        Assert.All(fixture.Statistics.Sink.Batches.SelectMany(x => x.Minutes)
            .Where(x => x.WindowStartUtc >= start.UtcDateTime), row => Assert.Equal(CategoryCoverageStatus.Complete, row.Status));
        var refreshes = fixture.Logs.Entries.Where(x => x.Fields.GetValueOrDefault("ReasonCode")?.ToString() == "CATALOG_CONTENT_UNCHANGED").ToArray();
        Assert.Equal(3, refreshes.Length);
        Assert.All(refreshes, entry => Assert.Equal(false, entry.Fields["CategoryCaptureInvalidated"]));
    }

    [Fact]
    public async Task Changed_route_reseeds_its_baseline_preserves_other_routes_and_invalidates_all_city_categories()
    {
        using var fixture = await ReconciliationFixture.CreateAsync();
        await fixture.ObserveAndCompleteAsync(Feed(Vehicle("bus", "B1", 0), Vehicle("rail", "R1", 0)), fixture.Statistics.Start);
        fixture.Shapes = [ReconciliationFixture.Shape("B1", "bus", offset: 0.001), ReconciliationFixture.Shape("R1", "rail")];
        await fixture.RefreshCatalogAsync();
        await fixture.ObserveAndCompleteAsync(Feed(Vehicle("bus", "B1", 1, timestamp: 2_001), Vehicle("rail", "R1", 1, timestamp: 2_001)),
            fixture.Statistics.Start.AddSeconds(10));
        fixture.Statistics.Capture.FlushPending();

        var batch = fixture.Statistics.Sink.Batches.Last();
        Assert.All(batch.Hours, row =>
        {
            Assert.Equal(CategoryCoverageStatus.Partial, row.Status);
            Assert.Null(row.DistinctActiveVehicleCount);
        });
        Assert.Equal(0, Assert.Single(batch.Minutes, x => x.Category == "bus").DistanceIntervalCount);
        Assert.Equal(1, Assert.Single(batch.Minutes, x => x.Category == "rail").DistanceIntervalCount);
        var log = Assert.Single(fixture.Logs.Entries, x => x.Fields.GetValueOrDefault("ReasonCode")?.ToString() == "CATALOG_CONTENT_CHANGED");
        Assert.Equal(1, log.Fields["ChangedRouteCount"]);
        Assert.Equal(true, log.Fields["CategoryCaptureInvalidated"]);
    }

    [Theory]
    [InlineData("category")]
    [InlineData("static-id")]
    [InlineData("added")]
    [InlineData("removed")]
    [InlineData("point-order")]
    public async Task Relevant_catalog_changes_advance_the_generation(string change)
    {
        using var fixture = await ReconciliationFixture.CreateAsync();
        var bus = fixture.Shapes[0];
        var rail = fixture.Shapes[1];
        fixture.Shapes = change switch
        {
            "category" => [bus with { Properties = bus.Properties with { Category = "rail" } }, rail],
            "static-id" => [bus with { Properties = bus.Properties with { RouteId = "bus-static-id" } }, rail],
            "added" => [bus, rail, ReconciliationFixture.Shape("B2", "bus")],
            "removed" => [rail],
            "point-order" => [bus with { Geometry = bus.Geometry with { Coordinates = bus.Geometry.Coordinates.Reverse().ToArray() } }, rail],
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };

        await fixture.RefreshCatalogAsync();

        Assert.Equal(2, fixture.Worker.GetRouteCatalogSnapshot("atlanta").Generation);
    }

    [Fact]
    public async Task Reordered_routes_and_display_only_metadata_keep_the_generation()
    {
        using var fixture = await ReconciliationFixture.CreateAsync();
        fixture.Shapes = [fixture.Shapes[1], fixture.Shapes[0] with { Properties = fixture.Shapes[0].Properties with { Color = "#ff0000" } }];

        await fixture.RefreshCatalogAsync();

        Assert.Equal(1, fixture.Worker.GetRouteCatalogSnapshot("atlanta").Generation);
    }

    [Fact]
    public async Task Other_city_changes_leave_this_city_unchanged_and_city_removal_invalidates_capture()
    {
        using var fixture = await ReconciliationFixture.CreateAsync();
        await fixture.ObserveAndCompleteAsync(Feed(Vehicle("bus", "B1", 0)), fixture.Statistics.Start);
        var other = ReconciliationFixture.Shape("other", "bus");
        other = other with { Properties = other.Properties with { City = "new-york-city" } };
        fixture.Shapes = [.. fixture.Shapes, other];
        await fixture.RefreshCatalogAsync();
        Assert.Equal(1, fixture.Worker.GetRouteCatalogSnapshot("atlanta").Generation);

        fixture.Shapes = [other];
        await fixture.RefreshCatalogAsync();
        fixture.Statistics.Capture.FlushPending();

        Assert.Equal(2, fixture.Worker.GetRouteCatalogSnapshot("atlanta").Generation);
        Assert.Null(fixture.Worker.GetRouteCatalogSnapshot("atlanta").Routes);
        Assert.All(fixture.Statistics.Sink.Batches.SelectMany(x => x.Hours), row => Assert.Null(row.DistinctActiveVehicleCount));
    }

    [Theory]
    [InlineData("ambiguous-alias")]
    [InlineData("case-collision")]
    [InlineData("invalid-key")]
    [InlineData("inconsistent-alias-category")]
    public async Task Unavailable_catalog_identity_cannot_claim_unchanged_continuity(string identity)
    {
        using var fixture = await ReconciliationFixture.CreateAsync();
        var conflicting = ReconciliationFixture.Shape("B2", "bus");
        conflicting = conflicting with { Properties = conflicting.Properties with { RouteId = "B1" } };
        var bus = fixture.Shapes[0];
        fixture.Shapes = identity switch
        {
            "ambiguous-alias" => [.. fixture.Shapes, conflicting],
            "case-collision" => [.. fixture.Shapes, ReconciliationFixture.Shape("b1", "bus")],
            "invalid-key" => [.. fixture.Shapes, ReconciliationFixture.Shape(new string('x', 513), "bus")],
            "inconsistent-alias-category" => [bus with { Properties = bus.Properties with { RouteId = "first-static" } }, fixture.Shapes[1],
                bus with { Properties = bus.Properties with { RouteId = "last-static", Category = "rail" } }],
            _ => throw new ArgumentOutOfRangeException(nameof(identity)),
        };
        await fixture.RefreshCatalogAsync();
        await fixture.RefreshCatalogAsync();

        Assert.Equal(3, fixture.Worker.GetRouteCatalogSnapshot("atlanta").Generation);
        Assert.Contains(fixture.Logs.Entries, x => x.Fields.GetValueOrDefault("ReasonCode")?.ToString() == "CATALOG_IDENTITY_UNAVAILABLE");
    }

    [Fact]
    public async Task Fingerprint_tracks_the_final_live_category_of_a_grouped_route()
    {
        using var fixture = await ReconciliationFixture.CreateAsync();
        var lastShape = fixture.Shapes[0] with { Properties = fixture.Shapes[0].Properties with { RouteId = "bus-static-2", Category = "rail" } };
        fixture.Shapes = [.. fixture.Shapes, lastShape];
        await fixture.RefreshCatalogAsync();
        var before = fixture.Worker.GetRouteCatalogSnapshot("atlanta");
        Assert.Equal("rail", before.RouteHourCatalog!.Entries["B1"].Category);

        fixture.Shapes = [.. fixture.Shapes.Take(2), lastShape with { Properties = lastShape.Properties with { Category = "bus" } }];
        await fixture.RefreshCatalogAsync();

        var after = fixture.Worker.GetRouteCatalogSnapshot("atlanta");
        Assert.Equal(before.Generation + 1, after.Generation);
        Assert.Equal("bus", after.RouteHourCatalog!.Entries["B1"].Category);
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
        public InMemoryJsonLoggerProvider Logs { get; } = new();
        public PublisherSpy Publisher { get; } = new();
        public ITransitCity City { get; } = new UnusedFetchCity();
        public Worker Worker { get; private set; } = null!;
        public IReadOnlyDictionary<string, RoutePoint[]> Index => Worker.GetRouteCatalogSnapshot(City.Name).Routes!;
        public IReadOnlyList<RouteShapeFeature> Shapes { get; set; } = [Shape("B1", "bus"), Shape("R1", "rail")];
        readonly InMemoryRouteShapeSource _source = new();
        readonly ILoggerFactory _loggerFactory;
        public static readonly IReadOnlyDictionary<string, string> Modes = new Dictionary<string, string> { ["B1"] = "bus", ["R1"] = "rail" };

        public ReconciliationFixture() => _loggerFactory = new LoggerFactory([Logs]);

        public static async Task<ReconciliationFixture> CreateAsync()
        {
            var fixture = new ReconciliationFixture();
            var source = fixture._source;
            source.Publish(fixture.Shapes);
            fixture.Worker = new Worker(fixture._loggerFactory.CreateLogger<Worker>(), fixture.Publisher, [fixture.City],
                new TriggerPointGenerator(NullLogger<TriggerPointGenerator>.Instance),
                routeShapeSource: source, categoryStatisticsCapture: fixture.Statistics.Capture);
            await fixture.Worker.InitializeRouteIndexAsync(CancellationToken.None);
            return fixture;
        }

        public async Task RefreshCatalogAsync()
        {
            _source.Publish(Shapes);
            await Worker.InitializeRouteIndexAsync(CancellationToken.None);
        }

        public async Task<CityCategoryStatisticsCycle> ObserveAndCompleteAsync(FeedMessage feed, DateTimeOffset at)
        {
            Statistics.Clock.SetUtcNow(at);
            var catalog = Worker.GetRouteCatalogSnapshot(City.Name);
            var cycle = Statistics.Begin(catalog.Generation, catalog.Categories!.Values.Distinct().ToArray());
            var result = await Worker.ProcessSpatialReconciliationAsync(City, feed, catalog.Routes!, catalog.Categories,
                CancellationToken.None, cycle, routeCatalog: catalog);
            var activityEligible = result.HealthOk && result.ProcessingExceptionType is null;
            var publicationKnown = activityEligible && (!result.PublishAttempted || result.PublishSucceeded == true);
            Statistics.Complete(cycle, at, activityEligible, publicationKnown, !activityEligible || !publicationKnown);
            return cycle;
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

        public static RouteShapeFeature Shape(string route, string category, double offset = 0) => new("Feature",
            new RouteShapeGeometry("LineString", Enumerable.Range(0, 11).Select(i => new[] { -84.4 + offset + i * 0.001, 33.7 }).ToArray()),
            new RouteShapeProperties(route, route, "#000000", "#ffffff", Category: category, City: "atlanta"));

        public void Dispose()
        {
            Worker.Dispose();
            _loggerFactory.Dispose();
        }
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
