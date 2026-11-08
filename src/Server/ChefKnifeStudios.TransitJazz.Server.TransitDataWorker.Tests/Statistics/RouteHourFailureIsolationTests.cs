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

    [Fact]
    public void Population_overflow_invalidates_only_activity_when_source_and_route_evidence_are_known()
    {
        var fixture = new RouteHourCaptureFixture();
        var limits = new RouteHourHistoryOptions { MaxTrackedVehiclesPerCity = 1 };
        var capture = new RouteHourStatisticsCapture(fixture.Runtime() with { Limits = limits }, fixture.Sink,
            fixture.Clock, NullLogger<RouteHourStatisticsCapture>.Instance);
        var catalog = RouteHourCaptureFixture.Catalog().Catalog;
        var at = new DateTime(2026, 10, 1, 12, 5, 0, DateTimeKind.Utc);
        fixture.Clock.SetUtcNow(at);
        var cycle = capture.BeginCycle(RouteHourCaptureFixture.City, catalog)!;
        cycle.AttachSourceEvidence("Route-A", RouteHourCaptureFixture.SourceEvidence("Route-A", at));

        Assert.True(capture.RecordEligibleVehicle(cycle, "vehicle-1", "shape-a"));
        capture.ObserveMovement(cycle, "vehicle-1", "Route-A", 1_000, 100);
        capture.ObserveMovement(cycle, "vehicle-1", "Route-A", 1_010, 110);
        Assert.False(capture.RecordEligibleVehicle(cycle, "vehicle-2", "shape-a"));
        capture.CompleteCycle(cycle, true, true, false, at, catalog);
        capture.FlushPending();

        var row = Assert.Single(fixture.Sink.Batches.SelectMany(batch => batch.Rows));
        Assert.Equal(RouteHourStatisticRow.SourceEvidenceDefinition, row.DefinitionVersion);
        Assert.True(row.DistanceValid);
        Assert.False(row.ActivityValid);
        Assert.True(row.PublicationValid);
        Assert.Contains("identity_limit_exceeded", row.ActivityReasons!);
    }

    [Fact]
    public void Publication_only_failure_preserves_activity_and_distance_validity()
    {
        var fixture = new RouteHourCaptureFixture();
        var capture = CreateCapture(fixture);
        var catalog = RouteHourCaptureFixture.Catalog().Catalog;
        var at = new DateTime(2026, 10, 1, 12, 5, 0, DateTimeKind.Utc);
        fixture.Clock.SetUtcNow(at);
        var cycle = capture.BeginCycle(RouteHourCaptureFixture.City, catalog)!;
        cycle.AttachSourceEvidence("Route-A", RouteHourCaptureFixture.SourceEvidence("Route-A", at));
        capture.RecordEligibleVehicle(cycle, "vehicle-1", "shape-a");
        capture.ObserveMovement(cycle, "vehicle-1", "Route-A", 1_000, 100);
        capture.ObserveMovement(cycle, "vehicle-1", "Route-A", 1_010, 110);

        capture.CompleteCycle(cycle, true, false, true, at, catalog, RouteHourIncompleteReason.PublicationUnavailable);
        capture.FlushPending();

        var row = Assert.Single(fixture.Sink.Batches.SelectMany(batch => batch.Rows));
        Assert.Equal(RouteHourStatisticRow.SourceEvidenceDefinition, row.DefinitionVersion);
        Assert.True(row.DistanceValid);
        Assert.True(row.ActivityValid);
        Assert.False(row.PublicationValid);
        Assert.Contains("publication_unavailable", row.PublicationReasons!);
    }

    [Fact]
    public void Unlocalizable_capture_loss_invalidates_every_measure_even_with_valid_source_receipts()
    {
        var fixture = new RouteHourCaptureFixture();
        var capture = CreateCapture(fixture);
        var catalog = RouteHourCaptureFixture.Catalog().Catalog;
        var at = new DateTime(2026, 10, 1, 12, 5, 0, DateTimeKind.Utc);
        fixture.Clock.SetUtcNow(at);
        var cycle = capture.BeginCycle(RouteHourCaptureFixture.City, catalog)!;
        cycle.AttachSourceEvidence("Route-A", RouteHourCaptureFixture.SourceEvidence("Route-A", at));
        capture.RecordEligibleVehicle(cycle, "vehicle-1", "shape-a");
        cycle.MarkFailure("capture_failure");

        capture.CompleteCycle(cycle, true, true, false, at, catalog);
        capture.FlushPending();

        var row = Assert.Single(fixture.Sink.Batches.SelectMany(batch => batch.Rows));
        Assert.Equal(RouteHourStatisticRow.SourceEvidenceDefinition, row.DefinitionVersion);
        Assert.False(row.DistanceValid);
        Assert.False(row.ActivityValid);
        Assert.False(row.PublicationValid);
        Assert.Contains("capture_failure", row.DistanceReasons!);
        Assert.Contains("capture_failure", row.ActivityReasons!);
        Assert.Contains("capture_failure", row.PublicationReasons!);
    }

    [Fact]
    public void Lost_finalized_batch_marks_the_next_admitted_hour_as_capture_loss()
    {
        var fixture = new RouteHourCaptureFixture();
        fixture.Sink.Accept = false;
        var capture = CreateCapture(fixture);
        var catalog = RouteHourCaptureFixture.Catalog().Catalog;

        var lostAt = new DateTime(2026, 10, 1, 12, 55, 0, DateTimeKind.Utc);
        fixture.Clock.SetUtcNow(lostAt);
        var lost = capture.BeginCycle(RouteHourCaptureFixture.City, catalog)!;
        lost.AttachSourceEvidence("Route-A", RouteHourCaptureFixture.SourceEvidence("Route-A", lostAt));
        capture.RecordEligibleVehicle(lost, "vehicle-1", "shape-a");
        capture.CompleteCycle(lost, true, true, false, lostAt, catalog);

        var nextAt = new DateTime(2026, 10, 1, 13, 5, 0, DateTimeKind.Utc);
        fixture.Clock.SetUtcNow(nextAt);
        var next = capture.BeginCycle(RouteHourCaptureFixture.City, catalog)!;
        next.AttachSourceEvidence("Route-A", RouteHourCaptureFixture.SourceEvidence("Route-A", nextAt));
        capture.RecordEligibleVehicle(next, "vehicle-2", "shape-a");
        capture.CompleteCycle(next, true, true, false, nextAt, catalog);
        fixture.Sink.Accept = true;
        capture.FlushPending();

        var row = Assert.Single(fixture.Sink.Batches.SelectMany(batch => batch.Rows));
        Assert.Equal(new DateTime(2026, 10, 1, 13, 0, 0, DateTimeKind.Utc), row.HourStartUtc);
        Assert.Contains("capture_failure", row.IncompleteReasons);
        Assert.False(row.DistanceValid);
        Assert.False(row.ActivityValid);
        Assert.False(row.PublicationValid);
    }

    [Fact]
    public void Unlocalizable_processing_index_loss_invalidates_every_measure()
    {
        var fixture = new RouteHourCaptureFixture();
        var capture = CreateCapture(fixture);
        var catalog = RouteHourCaptureFixture.Catalog().Catalog;
        var at = new DateTime(2026, 10, 1, 12, 5, 0, DateTimeKind.Utc);
        fixture.Clock.SetUtcNow(at);
        var cycle = capture.BeginCycle(RouteHourCaptureFixture.City, catalog)!;
        cycle.AttachSourceEvidence("Route-A", RouteHourCaptureFixture.SourceEvidence("Route-A", at));
        capture.RecordEligibleVehicle(cycle, "vehicle-1", "shape-a");
        capture.RecordProcessedObservation(cycle, "missing-route", false);

        capture.CompleteCycle(cycle, true, true, false, at, catalog);
        capture.FlushPending();

        var row = Assert.Single(fixture.Sink.Batches.SelectMany(batch => batch.Rows));
        Assert.False(row.DistanceValid);
        Assert.False(row.ActivityValid);
        Assert.False(row.PublicationValid);
        Assert.Contains("processing_failure", row.IncompleteReasons);
    }

    [Fact]
    public void Checked_counter_overflow_keeps_the_last_exact_value_and_invalidates_only_publication()
    {
        var fixture = new RouteHourCaptureFixture();
        var capture = CreateCapture(fixture);
        var catalog = RouteHourCaptureFixture.Catalog().Catalog;
        var at = new DateTime(2026, 10, 1, 12, 5, 0, DateTimeKind.Utc);
        fixture.Clock.SetUtcNow(at);
        var cycle = capture.BeginCycle(RouteHourCaptureFixture.City, catalog)!;
        cycle.AttachSourceEvidence("Route-A", RouteHourCaptureFixture.SourceEvidence("Route-A", at));
        capture.RecordEligibleVehicle(cycle, "vehicle-1", "shape-a");
        capture.RecordDetected(cycle, "Route-A", long.MaxValue);
        capture.RecordDetected(cycle, "Route-A", 1);
        capture.CompleteCycle(cycle, true, true, false, at, catalog);
        capture.FlushPending();

        var row = Assert.Single(fixture.Sink.Batches.SelectMany(batch => batch.Rows));
        Assert.Equal(long.MaxValue, row.CrossingsDetectedCount);
        Assert.True(row.DistanceValid);
        Assert.True(row.ActivityValid);
        Assert.False(row.PublicationValid);
        Assert.Contains("capture_failure", row.PublicationReasons!);
    }

    static RouteHourStatisticsCapture CreateCapture(RouteHourCaptureFixture fixture) => new(
        fixture.Runtime(), fixture.Sink, fixture.Clock, NullLogger<RouteHourStatisticsCapture>.Instance);
}
