using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class RouteHourCoverageTests
{
    [Fact]
    public void HealthyEmptyHourNeedsHealthyPredecessorAndSuccessorAndRetainsKnownZeros()
    {
        var fixture = new RouteHourCaptureFixture();
        var catalog = RouteHourCaptureFixture.Catalog().Catalog;
        var capture = CreateCapture(fixture);

        for (var at = Utc(2026, 10, 1, 11, 59, 45); at <= Utc(2026, 10, 1, 12, 59, 45); at = at.AddSeconds(20))
            CompleteHealthyEmpty(capture, fixture.Clock, catalog, at);
        CompleteHealthyEmpty(capture, fixture.Clock, catalog, Utc(2026, 10, 1, 13, 0, 5));

        var batch = Assert.Single(fixture.Sink.Batches, item => item.HourStartUtc == Utc(2026, 10, 1, 12, 0, 0));
        var row = Assert.Single(batch.Rows);
        Assert.Equal(RouteHourCoverageStatus.Complete, row.CollectionStatus);
        Assert.True(row.StartBoundaryOk);
        Assert.True(row.EndBoundaryOk);
        Assert.Equal(180, row.ValidActiveSampleCount);
        Assert.Equal(180, row.ValidPublishCycleCount);
        Assert.Equal(0, row.ActiveVehicleCountSum);
        Assert.Equal(0, row.PeakActiveVehicleCount);
        Assert.Equal(0, row.DistinctActiveVehicleCount);
        Assert.Equal(0, row.CrossingsPublishedCount);
    }

    [Fact]
    public void SweepSealsExpiredHourOnceAndDoesNotReplayMissedWindows()
    {
        var fixture = new RouteHourCaptureFixture();
        var catalog = RouteHourCaptureFixture.Catalog().Catalog;
        var capture = CreateCapture(fixture);
        CompleteHealthyEmpty(capture, fixture.Clock, catalog, Utc(2026, 10, 1, 10, 15, 0));

        fixture.Clock.SetUtcNow(Utc(2026, 10, 1, 12, 0, 0));
        capture.SweepPending();
        capture.SweepPending();

        var batch = Assert.Single(fixture.Sink.Batches);
        var row = Assert.Single(batch.Rows);
        Assert.Equal(Utc(2026, 10, 1, 10, 0, 0), batch.HourStartUtc);
        Assert.Equal(RouteHourCoverageStatus.Partial, row.CollectionStatus);
        Assert.Contains("boundary_unproven", row.IncompleteReasons);
        Assert.Contains("startup_fragment", row.IncompleteReasons);
    }

    [Fact]
    public void ClockRegressionKeepsTheHourSealedProgressMonotoneAndCannotProduceCompleteRows()
    {
        var fixture = new RouteHourCaptureFixture();
        var catalog = RouteHourCaptureFixture.Catalog().Catalog;
        var capture = CreateCapture(fixture);
        CompleteHealthyEmpty(capture, fixture.Clock, catalog, Utc(2026, 10, 1, 12, 10, 0));
        CompleteHealthyEmpty(capture, fixture.Clock, catalog, Utc(2026, 10, 1, 12, 20, 0));
        CompleteHealthyEmpty(capture, fixture.Clock, catalog, Utc(2026, 10, 1, 12, 15, 0));
        CompleteHealthyEmpty(capture, fixture.Clock, catalog, Utc(2026, 10, 1, 12, 25, 0));
        capture.FlushPending();

        var batch = Assert.Single(fixture.Sink.Batches);
        var row = Assert.Single(batch.Rows);
        Assert.Equal(RouteHourCoverageStatus.Partial, row.CollectionStatus);
        Assert.Contains("clock_regression", row.IncompleteReasons);
        Assert.Equal(Utc(2026, 10, 1, 12, 25, 0), row.LastCycleUtc);
    }

    [Fact]
    public void CrossHourRegressionDoesNotOpenAnOlderWindowOrLoseTheRegressionReason()
    {
        var fixture = new RouteHourCaptureFixture();
        var catalog = RouteHourCaptureFixture.Catalog().Catalog;
        var capture = CreateCapture(fixture);
        CompleteHealthyEmpty(capture, fixture.Clock, catalog, Utc(2026, 10, 1, 12, 55, 0));
        CompleteHealthyEmpty(capture, fixture.Clock, catalog, Utc(2026, 10, 1, 13, 5, 0));
        CompleteHealthyEmpty(capture, fixture.Clock, catalog, Utc(2026, 10, 1, 12, 59, 0));
        CompleteHealthyEmpty(capture, fixture.Clock, catalog, Utc(2026, 10, 1, 13, 15, 0));
        capture.FlushPending();

        Assert.Equal(2, fixture.Sink.Batches.Count);
        Assert.Equal(new[] { Utc(2026, 10, 1, 12, 0, 0), Utc(2026, 10, 1, 13, 0, 0) },
            fixture.Sink.Batches.Select(batch => batch.HourStartUtc).Order());
        var rows = fixture.Sink.Batches.SelectMany(batch => batch.Rows).ToArray();
        Assert.All(rows, row => Assert.Equal(RouteHourCoverageStatus.Partial, row.CollectionStatus));
        Assert.Contains("clock_regression", Assert.Single(rows, row => row.HourStartUtc == Utc(2026, 10, 1, 13, 0, 0)).IncompleteReasons);
    }

    [Fact]
    public void SweepDuringAnInFlightCycleSealsItsHourAndLateCompletionCannotReopenIt()
    {
        var fixture = new RouteHourCaptureFixture();
        var catalog = RouteHourCaptureFixture.Catalog().Catalog;
        var capture = CreateCapture(fixture);
        var inFlightAt = Utc(2026, 10, 1, 12, 59, 50);
        fixture.Clock.SetUtcNow(inFlightAt);
        var inFlight = capture.BeginCycle(RouteHourCaptureFixture.City, catalog)!;

        fixture.Clock.SetUtcNow(Utc(2026, 10, 1, 13, 1, 0));
        capture.SweepPending();
        capture.CompleteCycle(inFlight, true, true, false, inFlightAt, catalog);
        CompleteHealthyEmpty(capture, fixture.Clock, catalog, Utc(2026, 10, 1, 13, 5, 0));
        capture.FlushPending();

        var onlyBatch = Assert.Single(fixture.Sink.Batches);
        Assert.Equal(Utc(2026, 10, 1, 13, 0, 0), onlyBatch.HourStartUtc);
        var row = Assert.Single(onlyBatch.Rows);
        Assert.Equal(RouteHourCoverageStatus.Partial, row.CollectionStatus);
        Assert.Contains("startup_fragment", row.IncompleteReasons);
    }

    [Fact]
    public void MidHourCatalogChangeRetainsOldAndNewRoutesWithIncompleteCoverage()
    {
        var fixture = new RouteHourCaptureFixture();
        var first = RouteHourCaptureFixture.Catalog().Catalog;
        var changed = RouteHourCatalog.Create([
            new RouteHourCatalogInput("Route-A", "bus", "A", ["shape-a", "shape-b"],
                [new(33.749, -84.388), new(33.75, -84.387)], ["R17", "r17"]),
            new RouteHourCatalogInput("Route-B", "bus", "B", ["shape-b2"],
                [new(33.75, -84.386), new(33.751, -84.385)], ["R18"]),
        ]);
        var capture = CreateCapture(fixture);
        CompleteHealthyEmpty(capture, fixture.Clock, first, Utc(2026, 10, 1, 12, 10, 0));
        CompleteHealthyEmpty(capture, fixture.Clock, changed, Utc(2026, 10, 1, 12, 20, 0));
        capture.FlushPending();

        var rows = fixture.Sink.Batches.SelectMany(batch => batch.Rows).OrderBy(row => row.RouteJoinKey, StringComparer.Ordinal).ToArray();
        Assert.Equal(["Route-A", "Route-B"], rows.Select(row => row.RouteJoinKey));
        Assert.All(rows, row =>
        {
            Assert.True(row.CatalogChanged);
            Assert.Contains("catalog_changed", row.IncompleteReasons);
            Assert.Equal(RouteHourCoverageStatus.Partial, row.CollectionStatus);
        });
        Assert.Equal("B", rows[1].RouteShortName);
        Assert.Equal(1, rows[1].ObservedCycleCount);
    }

    static RouteHourStatisticsCapture CreateCapture(RouteHourCaptureFixture fixture) => new(
        fixture.Runtime(), fixture.Sink, fixture.Clock, NullLogger<RouteHourStatisticsCapture>.Instance);

    static void CompleteHealthyEmpty(RouteHourStatisticsCapture capture, RouteHourCaptureFixture.RouteHourTestClock clock,
        RouteHourCatalog catalog, DateTime atUtc)
    {
        var cycle = capture.BeginCycle(RouteHourCaptureFixture.City, catalog)!;
        clock.SetUtcNow(atUtc);
        capture.CompleteCycle(cycle, true, true, false, atUtc, catalog);
    }

    static DateTime Utc(int year, int month, int day, int hour, int minute, int second) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);
}
