using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class RouteHourCaptureLifecycleTests
{
    [Fact]
    public async Task Shutdown_flush_is_idempotent_and_marks_pending_window_incomplete()
    {
        var fixture = new RouteHourCaptureFixture();
        var catalog = RouteHourCaptureFixture.Catalog().Catalog;
        var capture = new RouteHourStatisticsCapture(fixture.Runtime(), fixture.Sink, fixture.Clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RouteHourStatisticsCapture>.Instance);
        var at = new DateTime(2026, 10, 1, 12, 5, 0, DateTimeKind.Utc);
        var cycle = capture.BeginCycle(RouteHourCaptureFixture.City, catalog)!;
        capture.CompleteCycle(cycle, true, true, false, at, catalog);
        var lifecycle = new RouteHourCaptureLifecycleService(capture, new RouteHourHistoryOptions(), fixture.Clock);

        await lifecycle.StopAsync(CancellationToken.None);
        await lifecycle.StopAsync(CancellationToken.None);

        var batch = Assert.Single(fixture.Sink.Batches);
        var row = Assert.Single(batch.Rows);
        Assert.Equal(RouteHourCoverageStatus.Partial, row.CollectionStatus);
        Assert.Contains("shutdown_fragment", row.IncompleteReasons);
        Assert.Contains("boundary_unproven", row.IncompleteReasons);
    }
}
