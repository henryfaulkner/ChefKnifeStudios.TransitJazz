using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests;

public class FailureIsolationTests
{
    [Fact]
    public async Task FlushAsync_WhenSinkThrows_IncrementsCounter_DoesNotRethrow()
    {
        var options = Options.Create(new LoggingOptions
        {
            Enabled = true,
            ChannelCapacity = 100,
            FlushIntervalSeconds = 3600,
            BlobServiceUri = "https://fake.blob.core.windows.net",
            Container = "telemetry"
        });

        var notifications = new EventNotificationService();
        var sink = new ThrowingLoggingService();
        var workerLogger = NullLogger<LogEventWorker>.Instance;
        var worker = new LogEventWorker(notifications, sink, options, workerLogger);

        await worker.StartAsync(CancellationToken.None);

        // Post one event so there's something to flush
        notifications.PostEvent(this, new CycleEventArgs
        {
            CycleId = "cycle-1",
            CycleStartUtc = DateTime.UtcNow,
            CycleEndUtc = DateTime.UtcNow,
            CycleExecutionSeconds = 0.1,
            BusesProcessed = 0, BusesMoved = 0, BusesUnchanged = 0,
            BusesStationary = 0, BusesStale = 0,
            BusesSkippedNoRouteId = 0, BusesSkippedUnknownRoute = 0,
            DuplicateFeed = false,
            LastUpdateCacheSize = 0, VehicleStateCacheSize = 0,
            SidecarBufferOccupancy = 0, SidecarDroppedRecords = 0,
            SidecarPersistFailures = 0
        });

        // Give the consumer a moment to accumulate
        await Task.Delay(50);

        // FlushAsync on the throwing sink must not rethrow
        var exception = await Record.ExceptionAsync(() => sink.FlushAsync(CancellationToken.None));
        Assert.Null(exception);
        Assert.Equal(1, sink.PersistFailures);

        // Worker StopAsync should also not throw even if final flush fails
        var stopException = await Record.ExceptionAsync(() => worker.StopAsync(CancellationToken.None));
        Assert.Null(stopException);
    }

    sealed class ThrowingLoggingService : ILoggingService
    {
        long _persistFailures;
        public long DroppedRecords => 0;
        public long PersistFailures => Interlocked.Read(ref _persistFailures);

        public void Accumulate(IEventArgs e) { }

        public Task FlushAsync(CancellationToken ct)
        {
            // Simulate a blob upload failure — swallow and count it
            try { throw new InvalidOperationException("Simulated blob upload failure"); }
            catch { Interlocked.Increment(ref _persistFailures); }
            return Task.CompletedTask;
        }
    }
}
