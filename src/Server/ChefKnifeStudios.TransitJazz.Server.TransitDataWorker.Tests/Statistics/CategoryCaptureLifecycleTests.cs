using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class CategoryCaptureLifecycleTests
{
    [Fact]
    public void Shutdown_flush_emits_open_minute_and_hour_as_partial()
    {
        var fixture = new CategoryStatisticsCaptureFixture(accept: true);
        var cycle = fixture.Begin(categories: ["bus"]);
        fixture.Complete(cycle, fixture.Start);

        fixture.Capture.FlushPending();

        var batch = Assert.Single(fixture.Sink.Batches);
        Assert.Equal(CategoryCoverageStatus.Partial, Assert.Single(batch.Minutes).Status);
        Assert.Equal(CategoryCoverageStatus.Partial, Assert.Single(batch.Hours).Status);
    }

    [Fact]
    public void Deadline_sweep_closes_pending_minute_when_no_successor_cycle_arrives()
    {
        var fixture = new CategoryStatisticsCaptureFixture(accept: true);
        var cycle = fixture.Begin(categories: ["bus"]);
        fixture.Complete(cycle, fixture.Start);

        fixture.Clock.SetUtcNow(fixture.Start.AddSeconds(91));
        fixture.Capture.SweepPending();

        var minute = Assert.Single(fixture.Sink.Batches.SelectMany(x => x.Minutes));
        Assert.Equal(CategoryCoverageStatus.Partial, minute.Status);
        Assert.Empty(fixture.Sink.Batches.SelectMany(x => x.Hours));
    }

    [Fact]
    public async Task Hosted_sweeper_closes_deadline_without_another_worker_cycle()
    {
        var fixture = new CategoryStatisticsCaptureFixture(accept: true);
        var cycle = fixture.Begin(categories: ["bus"]);
        fixture.Complete(cycle, fixture.Start);
        fixture.Clock.SetUtcNow(fixture.Start.AddSeconds(91));
        var service = new CityCategoryStatisticsCaptureLifecycleService(
            fixture.Capture,
            new CityCategoryInsightsOptions { Enabled = true },
            fixture.Clock,
            TimeSpan.FromMilliseconds(5));

        await service.StartAsync(CancellationToken.None);
        try
        {
            var swept = await fixture.Sink.Enqueued.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var minute = Assert.Single(swept.Minutes);
            Assert.Equal(CategoryCoverageStatus.Partial, minute.Status);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }
}
