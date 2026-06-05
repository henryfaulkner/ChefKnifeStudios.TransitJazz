using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Threading.Channels;
using Xunit;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests;

public class ChannelLoadSheddingTests
{
    [Fact]
    public async Task Channel_DropWrite_NeverBlocksProducer_KeepsCapacityBounded()
    {
        const int capacity = 3;
        var channel = Channel.CreateBounded<IEventArgs>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

        long droppedCount = 0;
        const int totalWrites = capacity + 5;

        // All writes must complete immediately (DropWrite never blocks)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < totalWrites; i++)
        {
            bool wasFull = channel.Reader.CanCount && channel.Reader.Count >= capacity;
            channel.Writer.TryWrite(new SnapEventArgs
            {
                CycleId = $"cycle-{i}",
                ObservationUtc = DateTime.UtcNow,
                VehicleId = $"v{i}",
                RouteId = "R1",
                SnapOutcome = "Moved",
                RawLat = 1, RawLon = 1, SnappedLat = 1, SnappedLon = 1,
                SnapDistanceKm = 0, SnapIndex = 0, RoutePointCount = 10,
                IsStale = false
            });
            if (wasFull) Interlocked.Increment(ref droppedCount);
        }
        sw.Stop();

        // Writes completed without blocking (much faster than any I/O timeout)
        Assert.True(sw.ElapsedMilliseconds < 1000, $"Writes took too long: {sw.ElapsedMilliseconds}ms");

        // Exactly 5 should have been detected as dropped (wasFull check)
        Assert.Equal(5, Interlocked.Read(ref droppedCount));

        // Channel holds exactly capacity items
        Assert.Equal(capacity, channel.Reader.Count);

        channel.Writer.Complete();
        var read = new List<IEventArgs>();
        await foreach (var item in channel.Reader.ReadAllAsync())
            read.Add(item);

        Assert.Equal(capacity, read.Count);
    }

    [Fact]
    public async Task LogEventWorker_DroppedRecords_IncrementOnOverflow()
    {
        var options = Options.Create(new LoggingOptions
        {
            Enabled = true,
            ChannelCapacity = 2,
            FlushIntervalSeconds = 3600
        });

        var notifications = new EventNotificationService();
        var sink = new NoOpLoggingService();
        var logger = NullLogger<LogEventWorker>.Instance;

        var worker = new LogEventWorker(notifications, sink, options, logger);
        await worker.StartAsync(CancellationToken.None);

        // Post 10 events into a channel of capacity 2 — most should be dropped
        for (int i = 0; i < 10; i++)
        {
            notifications.PostEvent(this, new SnapEventArgs
            {
                CycleId = $"c{i}", ObservationUtc = DateTime.UtcNow,
                VehicleId = "v1", RouteId = "R1", SnapOutcome = "Moved",
                RawLat = 1, RawLon = 1, SnappedLat = 1, SnappedLon = 1,
                SnapDistanceKm = 0, SnapIndex = 0, RoutePointCount = 5,
                IsStale = false
            });
        }

        // At least some were dropped
        Assert.True(worker.DroppedRecords > 0,
            $"Expected dropped > 0 but got {worker.DroppedRecords}");

        await worker.StopAsync(CancellationToken.None);
    }

    sealed class NoOpLoggingService : ILoggingService
    {
        public long DroppedRecords => 0;
        public long PersistFailures => 0;
        public void Accumulate(IEventArgs e) { }
        public Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
