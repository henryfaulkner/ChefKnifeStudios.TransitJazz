using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class RouteHourCaptureFixture
{
    public const string City = "atlanta";
    public readonly RouteHourTestClock Clock = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
    public readonly RecordingRouteHourSink Sink = new();
    public readonly Guid CaptureRunId = Guid.Parse("ed6150e0-b01d-4c16-8cf1-5c06826b1b1a");

    public RouteHourCaptureRuntimeOptions Runtime(RouteHourCaptureMode mode = RouteHourCaptureMode.DryRun) => new(
        mode, ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, City), new RouteHourHistoryOptions());

    public static RouteHourCatalogFixture Catalog() => new();

    public static FeedMessage Feed(params FeedEntity[] entities) => new() { Entities = entities.ToList() };

    public static FeedEntity Vehicle(string vehicleId, string routeId, float longitude, ulong? timestamp = 2_000) => new()
    {
        Id = vehicleId,
        Vehicle = new VehiclePosition
        {
            Vehicle = new VehicleDescriptor { Id = vehicleId },
            Trip = new TripDescriptor { RouteId = routeId },
            Timestamp = timestamp,
            Position = new Position { Latitude = 33.7f, Longitude = longitude },
        },
    };

    public static RouteShapeFeature Shape(string routeId = "static-1", string? shortName = "B1", string category = "bus") => new(
        "Feature",
        new RouteShapeGeometry("LineString", Enumerable.Range(0, 11).Select(i => new[] { -84.4 + i * 0.001, 33.7 }).ToArray()),
        new RouteShapeProperties(routeId, shortName, "#000000", "#ffffff", Category: category, City: City));

    public static RouteHourStatisticRow Row(string key = "Route-A", DateTime? hour = null, Guid? run = null,
        RouteHourCoverageStatus status = RouteHourCoverageStatus.Partial, ImmutableArray<string>? reasons = null) =>
        new(City, key, hour ?? new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc), key, "shape-a", "bus",
            new string('a', 64), false, RouteHourStatisticRow.Definition, run ?? Guid.Parse("ed6150e0-b01d-4c16-8cf1-5c06826b1b1a"),
            status, false, reasons ?? ImmutableArray.Create("boundary_unproven"), 30,
            1, 1, 1, 0, hour ?? new DateTime(2026, 10, 1, 12, 5, 0, DateTimeKind.Utc),
            hour?.AddMinutes(5) ?? new DateTime(2026, 10, 1, 12, 5, 0, DateTimeKind.Utc), 10m,
            false, false, 0, 0, 0, 0, 0, 0m, 0, 0, 0, 0, 0, 0, 0, 0);

    public static void InvokeInstrumentationHook(Action hook, Action markCaptureLoss)
    {
        try { hook(); }
        catch { markCaptureLoss(); }
    }

    public sealed class RouteHourCatalogFixture
    {
        public RouteHourCatalog Catalog { get; } = RouteHourCatalog.Create([
            new RouteHourCatalogInput("Route-A", "bus", "Route-A", ["shape-a", "shape-b"],
                [new(33.749, -84.388), new(33.75, -84.387)], ["R17", "r17"]),
        ]);
    }

    public sealed class RecordingRouteHourSink : IRouteHourStatisticsSink
    {
        readonly ConcurrentQueue<FinalizedRouteHourStatisticsBatch> _batches = new();
        public bool Accept { get; set; } = true;
        public bool Throw { get; set; }
        public IReadOnlyList<FinalizedRouteHourStatisticsBatch> Batches => _batches.ToArray();
        public bool TryEnqueue(FinalizedRouteHourStatisticsBatch batch)
        {
            if (Throw) throw new InvalidOperationException("injected sink failure");
            if (!Accept) return false;
            _batches.Enqueue(batch);
            return true;
        }
    }

    public sealed class RecordingPublisher : ITransitHubPublisher
    {
        public bool Succeeds { get; set; } = true;
        public int PublishCount { get; private set; }
        public int LastCrossingCount { get; private set; }
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> PublishBatchAsync(string city, List<EventEnvelope> batch, CancellationToken ct = default)
        {
            PublishCount++;
            LastCrossingCount = batch.Select(envelope => envelope.Payload).OfType<RouteCrossingBatchEvent>()
                .Sum(crossings => crossings.BatchRecords.Count());
            return Task.FromResult(Succeeds);
        }
    }

    public sealed class UnusedFetchCity : ITransitCity
    {
        public string Name => City;
        public Task<CityFetchResult> FetchVehiclesAsync(CancellationToken ct) =>
            throw new InvalidOperationException("Fetch is not part of the direct reconciliation fixture.");
    }

    public sealed class RouteHourTestClock(DateTimeOffset now) : TimeProvider
    {
        DateTimeOffset _now = now.ToUniversalTime();
        public override DateTimeOffset GetUtcNow() => _now;
        public void SetUtcNow(DateTimeOffset value) => _now = value.ToUniversalTime();
    }
}
