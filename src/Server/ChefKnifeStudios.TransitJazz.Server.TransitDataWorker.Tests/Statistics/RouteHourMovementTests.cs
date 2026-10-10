using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class RouteHourMovementTests
{
    [Fact]
    public void FreshnessWatermarkRejectsEqualOlderAndMissingTimesClearOnlyTheMovementBaseline()
    {
        var fixture = new RouteHourCaptureFixture();
        var catalog = RouteHourCaptureFixture.Catalog().Catalog;
        var capture = CreateCapture(fixture);

        Observe(capture, fixture, catalog, 5, 1_000, 1_000);
        Observe(capture, fixture, catalog, 10, 1_001, 1_010);
        Observe(capture, fixture, catalog, 15, 1_001, 1_200); // Equal source timestamp.
        Observe(capture, fixture, catalog, 20, 1_000, 1_100); // Older source timestamp.
        Observe(capture, fixture, catalog, 25, 1_002, 1_020);
        Observe(capture, fixture, catalog, 30, null, 1_030); // Missing time clears the position baseline.
        Observe(capture, fixture, catalog, 35, 1_003, 2_000);
        Observe(capture, fixture, catalog, 40, 1_004, 2_010);
        capture.FlushPending();

        var row = Assert.Single(fixture.Sink.Batches.SelectMany(batch => batch.Rows));
        Assert.Equal(3, row.DistanceIntervalCount);
        Assert.Equal(30m, row.DistanceMetersSum);
        Assert.Equal(5, row.DistanceRejectedCount);
    }

    [Fact]
    public void StationaryReverseAndExactTwoKilometerIntervalsAreAcceptedButLongerJumpsAreRejected()
    {
        var fixture = new RouteHourCaptureFixture();
        var catalog = RouteHourCaptureFixture.Catalog().Catalog;
        var capture = CreateCapture(fixture);

        Observe(capture, fixture, catalog, 5, 1_000, 100);
        Observe(capture, fixture, catalog, 10, 1_001, 100); // Stationary.
        Observe(capture, fixture, catalog, 15, 1_002, 2_100); // Exactly 2,000 meters.
        Observe(capture, fixture, catalog, 20, 1_003, 100); // Reverse direction, absolute distance.
        Observe(capture, fixture, catalog, 25, 1_004, 2_100.001); // More than 2,000 meters.
        capture.FlushPending();

        var row = Assert.Single(fixture.Sink.Batches.SelectMany(batch => batch.Rows));
        Assert.Equal(3, row.DistanceIntervalCount);
        Assert.Equal(4_000m, row.DistanceMetersSum);
        Assert.Equal(2, row.DistanceRejectedCount);
    }

    [Fact]
    public void RouteTransferAndChangedGeometryReseedBaselinesBeforeCountingMovement()
    {
        var fixture = new RouteHourCaptureFixture();
        var firstCatalog = RouteHourCatalog.Create([
            new RouteHourCatalogInput("Route-A", "bus", "A", ["shape-a"], [new(33.7, -84.4), new(33.7, -84.39)], ["A1"]),
            new RouteHourCatalogInput("Route-B", "bus", "B", ["shape-b"], [new(33.7, -84.38), new(33.7, -84.37)], ["B1"]),
        ]);
        var capture = CreateCapture(fixture);

        Observe(capture, fixture, firstCatalog, 5, 1_000, 100, "A1", "vehicle-1");
        Observe(capture, fixture, firstCatalog, 10, 1_001, 200, "B1", "vehicle-1"); // Transfer reseeds.
        Observe(capture, fixture, firstCatalog, 15, 1_002, 210, "B1", "vehicle-1");

        var changedGeometry = RouteHourCatalog.Create([
            new RouteHourCatalogInput("Route-A", "bus", "A", ["shape-a"], [new(33.7, -84.4), new(33.7, -84.39)], ["A1"]),
            new RouteHourCatalogInput("Route-B", "bus", "B", ["shape-b"], [new(33.7, -84.375), new(33.7, -84.365)], ["B1"]),
        ]);
        Observe(capture, fixture, changedGeometry, 20, 1_003, 500, "B1", "vehicle-1"); // Content change reseeds.
        Observe(capture, fixture, changedGeometry, 25, 1_004, 515, "B1", "vehicle-1");
        capture.FlushPending();

        var rowB = Assert.Single(fixture.Sink.Batches.SelectMany(batch => batch.Rows), row => row.RouteJoinKey == "Route-B");
        Assert.Equal(2, rowB.DistanceIntervalCount);
        Assert.Equal(25m, rowB.DistanceMetersSum);
        Assert.Contains("catalog_changed", rowB.IncompleteReasons);
    }

    static RouteHourStatisticsCapture CreateCapture(RouteHourCaptureFixture fixture) => new(
        fixture.Runtime(), fixture.Sink, fixture.Clock, NullLogger<RouteHourStatisticsCapture>.Instance);

    static void Observe(RouteHourStatisticsCapture capture, RouteHourCaptureFixture fixture, RouteHourCatalog catalog,
        int minute, ulong? sourceTime, double meters, string alias = "shape-a", string vehicle = "vehicle-1")
    {
        var at = new DateTime(2026, 10, 1, 12, minute, 0, DateTimeKind.Utc);
        fixture.Clock.SetUtcNow(at);
        var cycle = capture.BeginCycle(RouteHourCaptureFixture.City, catalog)!;
        Assert.True(capture.RecordEligibleVehicle(cycle, vehicle, alias));
        capture.ObserveMovement(cycle, vehicle, cycle.Catalog.TryResolve(alias, out var route) ? route.RouteJoinKey : alias,
            sourceTime, meters);
        capture.CompleteCycle(cycle, true, true, false, at, catalog);
    }
}
