using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class RouteHourFailureIsolationTests
{
    [Fact]
    public void Source_failure_is_retained_as_no_data_instead_of_a_healthy_zero()
    {
        var fixture = new RouteHourCaptureFixture();
        var capture = CreateCapture(fixture);
        var catalog = RouteHourCaptureFixture.Catalog().Catalog;
        var at = new DateTime(2026, 10, 1, 12, 5, 0, DateTimeKind.Utc);
        var cycle = capture.BeginCycle(RouteHourCaptureFixture.City, catalog)!;

        capture.CompleteCycle(cycle, false, false, true, at, catalog, RouteHourIncompleteReason.SourceFailure);
        capture.FlushPending();

        var row = Assert.Single(fixture.Sink.Batches.SelectMany(batch => batch.Rows));
        Assert.Equal(RouteHourCoverageStatus.NoData, row.CollectionStatus);
        Assert.Equal(0, row.ActiveVehicleCountSum);
        Assert.Equal(1, row.FailedCycleCount);
        Assert.Contains("source_failure", row.IncompleteReasons);
    }

    [Fact]
    public void Publication_failure_remains_partial_and_never_counts_a_published_opportunity()
    {
        var fixture = new RouteHourCaptureFixture();
        var capture = CreateCapture(fixture);
        var catalog = RouteHourCaptureFixture.Catalog().Catalog;
        var at = new DateTime(2026, 10, 1, 12, 5, 0, DateTimeKind.Utc);
        var cycle = capture.BeginCycle(RouteHourCaptureFixture.City, catalog)!;
        capture.RecordEligibleVehicle(cycle, "vehicle-1", "shape-a");
        capture.RecordPublished(cycle, [("missing-route", 3)]);

        capture.CompleteCycle(cycle, true, false, true, at, catalog, RouteHourIncompleteReason.PublicationUnavailable);
        capture.FlushPending();

        var row = Assert.Single(fixture.Sink.Batches.SelectMany(batch => batch.Rows));
        Assert.Equal(RouteHourCoverageStatus.Partial, row.CollectionStatus);
        Assert.Equal(0, row.CrossingsPublishedCount);
        Assert.Contains("publication_unavailable", row.IncompleteReasons);
        Assert.Contains("route_index_unavailable", row.IncompleteReasons);
    }

    [Fact]
    public void Sink_exception_is_isolated_from_successful_capture_completion()
    {
        var fixture = new RouteHourCaptureFixture();
        fixture.Sink.Throw = true;
        var capture = CreateCapture(fixture);
        var catalog = RouteHourCaptureFixture.Catalog().Catalog;
        var at = new DateTime(2026, 10, 1, 12, 5, 0, DateTimeKind.Utc);
        var cycle = capture.BeginCycle(RouteHourCaptureFixture.City, catalog)!;

        capture.CompleteCycle(cycle, true, true, false, at, catalog);
        capture.FlushPending();

        Assert.Empty(fixture.Sink.Batches);
    }

    static RouteHourStatisticsCapture CreateCapture(RouteHourCaptureFixture fixture) => new(
        fixture.Runtime(), fixture.Sink, fixture.Clock, NullLogger<RouteHourStatisticsCapture>.Instance);
}
