using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class RouteHourCatalogAndLimitTests
{
    [Fact]
    public void Tracked_identity_cap_loses_the_distinct_population_denominator()
    {
        var fixture = new RouteHourCaptureFixture();
        var limits = new RouteHourHistoryOptions { MaxTrackedVehiclesPerCity = 1 };
        var runtime = fixture.Runtime() with { Limits = limits };
        var capture = new RouteHourStatisticsCapture(runtime, fixture.Sink, fixture.Clock,
            NullLogger<RouteHourStatisticsCapture>.Instance);
        var at = Utc(10);
        fixture.Clock.SetUtcNow(at);
        var cycle = capture.BeginCycle(RouteHourCaptureFixture.City, RouteHourCaptureFixture.Catalog().Catalog)!;

        Assert.True(capture.RecordEligibleVehicle(cycle, "vehicle-1", "shape-a"));
        Assert.False(capture.RecordEligibleVehicle(cycle, "vehicle-2", "shape-a"));
        capture.CompleteCycle(cycle, true, true, false, at, cycle.Catalog);
        capture.FlushPending();

        var row = Assert.Single(fixture.Sink.Batches.SelectMany(batch => batch.Rows));
        Assert.Equal(RouteHourCoverageStatus.Partial, row.CollectionStatus);
        Assert.Null(row.DistinctActiveVehicleCount);
        Assert.Contains("identity_limit_exceeded", row.IncompleteReasons);
    }

    [Fact]
    public void Hourly_route_vehicle_membership_cap_loses_the_denominator()
    {
        var fixture = new RouteHourCaptureFixture();
        var limits = new RouteHourHistoryOptions { MaxVehicleRouteMembershipsPerCityHour = 1 };
        var capture = new RouteHourStatisticsCapture(fixture.Runtime() with { Limits = limits }, fixture.Sink,
            fixture.Clock, NullLogger<RouteHourStatisticsCapture>.Instance);
        var catalog = RouteHourCaptureFixture.Catalog().Catalog;

        foreach (var (minute, vehicle) in new[] { (5, "vehicle-1"), (10, "vehicle-2") })
        {
            var at = Utc(minute);
            fixture.Clock.SetUtcNow(at);
            var cycle = capture.BeginCycle(RouteHourCaptureFixture.City, catalog)!;
            capture.RecordEligibleVehicle(cycle, vehicle, "shape-a");
            capture.CompleteCycle(cycle, true, true, false, at, catalog);
        }
        capture.FlushPending();

        var row = Assert.Single(fixture.Sink.Batches.SelectMany(batch => batch.Rows));
        Assert.Null(row.DistinctActiveVehicleCount);
        Assert.Contains("identity_limit_exceeded", row.IncompleteReasons);
    }

    [Fact]
    public void Movement_baseline_identity_cap_is_enforced_without_activity_feed_dedup()
    {
        var fixture = new RouteHourCaptureFixture();
        var limits = new RouteHourHistoryOptions { MaxTrackedVehiclesPerCity = 1 };
        var capture = new RouteHourStatisticsCapture(fixture.Runtime() with { Limits = limits }, fixture.Sink,
            fixture.Clock, NullLogger<RouteHourStatisticsCapture>.Instance);
        var catalog = RouteHourCaptureFixture.Catalog().Catalog;

        foreach (var (minute, vehicle, timestamp) in new[] { (5, "vehicle-1", (ulong?)1_000), (10, "vehicle-2", (ulong?)1_001) })
        {
            var at = Utc(minute);
            fixture.Clock.SetUtcNow(at);
            var cycle = capture.BeginCycle(RouteHourCaptureFixture.City, catalog)!;
            capture.ObserveMovement(cycle, vehicle, "Route-A", timestamp, 100);
            capture.CompleteCycle(cycle, true, true, false, at, catalog);
        }
        capture.FlushPending();

        var row = Assert.Single(fixture.Sink.Batches.SelectMany(batch => batch.Rows));
        Assert.Null(row.DistinctActiveVehicleCount);
        Assert.Contains("identity_limit_exceeded", row.IncompleteReasons);
    }

    [Fact]
    public void AnOversizedMidHourCatalogMarksExistingEvidenceInsteadOfAllowingCompleteCoverage()
    {
        var fixture = new RouteHourCaptureFixture();
        var limits = new RouteHourHistoryOptions { MaxRoutesPerCityHour = 1 };
        var runtime = fixture.Runtime() with { Limits = limits };
        var capture = new RouteHourStatisticsCapture(runtime, fixture.Sink, fixture.Clock,
            NullLogger<RouteHourStatisticsCapture>.Instance);
        var first = RouteHourCaptureFixture.Catalog().Catalog;
        var tooLarge = RouteHourCatalog.Create([
            new RouteHourCatalogInput("Route-A", "bus", "A", ["shape-a"], [new(33.7, -84.4)], ["A"]),
            new RouteHourCatalogInput("Route-B", "bus", "B", ["shape-b"], [new(33.7, -84.3)], ["B"]),
        ]);
        var firstAt = Utc(10);
        fixture.Clock.SetUtcNow(firstAt);
        var cycle = capture.BeginCycle(RouteHourCaptureFixture.City, first)!;
        capture.CompleteCycle(cycle, true, true, false, firstAt, first);

        fixture.Clock.SetUtcNow(Utc(20));
        Assert.Null(capture.BeginCycle(RouteHourCaptureFixture.City, tooLarge));
        fixture.Clock.SetUtcNow(Utc(30));
        var resumed = capture.BeginCycle(RouteHourCaptureFixture.City, first)!;
        capture.CompleteCycle(resumed, true, true, false, Utc(30), first);
        capture.FlushPending();

        var row = Assert.Single(fixture.Sink.Batches.SelectMany(batch => batch.Rows));
        Assert.Equal(RouteHourCoverageStatus.Partial, row.CollectionStatus);
        Assert.Contains("identity_limit_exceeded", row.IncompleteReasons);
    }

    [Fact]
    public void InFlightCatalogRefreshRetainsTheOldAndNewRouteUnionInTheFinalEnvelope()
    {
        var fixture = new RouteHourCaptureFixture();
        var capture = new RouteHourStatisticsCapture(fixture.Runtime(), fixture.Sink, fixture.Clock,
            NullLogger<RouteHourStatisticsCapture>.Instance);
        var oldCatalog = RouteHourCaptureFixture.Catalog().Catalog;
        var refreshedCatalog = RouteHourCatalog.Create([
            new RouteHourCatalogInput("Route-A", "bus", "A", ["shape-a", "shape-b"],
                [new(33.749, -84.388), new(33.75, -84.387)], ["R17", "r17"]),
            new RouteHourCatalogInput("Route-B", "rail", "B", ["shape-c"],
                [new(33.75, -84.386), new(33.751, -84.385)], ["R18"]),
        ]);
        var at = Utc(10);
        fixture.Clock.SetUtcNow(at);
        var cycle = capture.BeginCycle(RouteHourCaptureFixture.City, oldCatalog)!;
        capture.RecordEligibleVehicle(cycle, "vehicle-1", "R17");

        capture.CompleteCycle(cycle, true, true, false, at, refreshedCatalog);
        capture.FlushPending();

        var rows = fixture.Sink.Batches.SelectMany(batch => batch.Rows).OrderBy(row => row.RouteJoinKey, StringComparer.Ordinal).ToArray();
        Assert.Equal(["Route-A", "Route-B"], rows.Select(row => row.RouteJoinKey));
        Assert.Equal(RouteHourCoverageStatus.Partial, rows[0].CollectionStatus);
        Assert.Equal(RouteHourCoverageStatus.NoData, rows[1].CollectionStatus);
        Assert.All(rows, row => Assert.Contains("catalog_changed", row.IncompleteReasons));
        Assert.Equal("B", rows[1].RouteShortName);
        Assert.Equal(1, rows[1].ObservedCycleCount);
    }

    static DateTime Utc(int minute) => new(2026, 10, 1, 12, minute, 0, DateTimeKind.Utc);
}
