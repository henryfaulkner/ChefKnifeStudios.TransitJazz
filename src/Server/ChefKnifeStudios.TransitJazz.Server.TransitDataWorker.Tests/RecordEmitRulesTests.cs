using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using ChefKnifeStudios.TransitJazz.Shared.Geospatial;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using ChefKnifeStudios.TransitJazz.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests;

// P3-U3 (051 US4): the v2 emit rules, asserted on the records the Worker actually publishes.
//
// These drive the real ProcessSpatialReconciliationAsync seam (same harness style as
// WireBytesTelemetryTests) and read the batch a spy ITransitHubPublisher captures, so they
// pin the production emit decisions rather than a re-derivation of them. The rules under
// test are what make v2 smaller AND what make it decodable: a prior pair wrongly omitted on
// a first observation leaves the client with no origin to animate from, and a category
// wrongly omitted erases the "unknown" data-quality signal.
public class RecordEmitRulesTests
{
    static readonly IReadOnlyDictionary<string, RoutePoint[]> Index = new Dictionary<string, RoutePoint[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["95"] = [new RoutePoint("95", 33.0, -84.0), new RoutePoint("95", 33.001, -84.001), new RoutePoint("95", 33.002, -84.002)],
        ["12"] = [new RoutePoint("12", 34.0, -85.0), new RoutePoint("12", 34.001, -85.001)]
    };

    static Worker MakeWorker(SpyHubPublisher publisher) =>
        new(
            new FakeHttpClientFactory(),
            NullLogger<Worker>.Instance,
            publisher,
            new SpyEventNotifications(),
            new LogEventWorker(new EventNotificationService(), new NoOpLoggingService(),
                Microsoft.Extensions.Options.Options.Create(new LoggingOptions { Enabled = false }),
                NullLogger<LogEventWorker>.Instance),
            new NoOpLoggingService(),
            [new FakeCity("atlanta")],
            new NoOpTriggerPointGenerator());

    static FeedMessage Feed(string vehicleId, string routeId, double lat, double lon, ulong timestamp) => new()
    {
        Entities =
        [
            new FeedEntity
            {
                Id = vehicleId,
                Vehicle = new VehiclePosition
                {
                    Trip = new TripDescriptor { RouteId = routeId },
                    Vehicle = new VehicleDescriptor { Id = vehicleId },
                    Position = new Position { Latitude = (float)lat, Longitude = (float)lon, Speed = 8.5f },
                    Timestamp = timestamp
                }
            }
        ]
    };

    [Fact]
    public async Task FirstObservation_CarriesPriorPair_SoTheClientHasAnOriginToSnapTo()
    {
        var publisher = new SpyHubPublisher();
        var worker = MakeWorker(publisher);

        await worker.ProcessSpatialReconciliationAsync(
            new FakeCity("atlanta"), Feed("v1", "95", 33.0, -84.0, 1000), Index, modeMap: null, CancellationToken.None);

        var rec = Assert.Single(publisher.CapturedRecords);
        Assert.NotNull(rec.PriorNearestLatE5);
        Assert.NotNull(rec.PriorNearestLonE5);
        // First-observation contract: prior == current with a zero tween → client snaps into place.
        Assert.Equal(rec.CurrentNearestLatE5, rec.PriorNearestLatE5);
        Assert.Equal(rec.CurrentNearestLonE5, rec.PriorNearestLonE5);
        Assert.Equal(0, rec.DurationMs);
    }

    [Fact]
    public async Task SteadyStateObservation_OmitsPriorPair_TheSavingThatMakesV2Smaller()
    {
        var publisher = new SpyHubPublisher();
        var worker = MakeWorker(publisher);
        var city = new FakeCity("atlanta");

        // Tick 1 establishes prior state; tick 2 is the steady-state case under test.
        await worker.ProcessSpatialReconciliationAsync(city, Feed("v1", "95", 33.0, -84.0, 1000), Index, null, CancellationToken.None);
        publisher.CapturedRecords.Clear();
        await worker.ProcessSpatialReconciliationAsync(city, Feed("v1", "95", 33.002, -84.002, 2000), Index, null, CancellationToken.None);

        var rec = Assert.Single(publisher.CapturedRecords);
        Assert.Null(rec.PriorNearestLatE5);
        Assert.Null(rec.PriorNearestLonE5);
        Assert.True(rec.CurrentNearestLatE5 != 0);
    }

    [Fact]
    public async Task RouteChange_RestatesPriorPair_BecauseTheRetainedPositionIsOnADifferentShape()
    {
        var publisher = new SpyHubPublisher();
        var worker = MakeWorker(publisher);
        var city = new FakeCity("atlanta");

        await worker.ProcessSpatialReconciliationAsync(city, Feed("v1", "95", 33.0, -84.0, 1000), Index, null, CancellationToken.None);
        publisher.CapturedRecords.Clear();
        // Same vehicle, different route: the client's retained position belongs to route 95's
        // geometry and would animate a false sweep across the map if the prior were omitted.
        await worker.ProcessSpatialReconciliationAsync(city, Feed("v1", "12", 34.0, -85.0, 2000), Index, null, CancellationToken.None);

        var rec = Assert.Single(publisher.CapturedRecords);
        Assert.Equal("12", rec.RouteJoinKey);
        Assert.NotNull(rec.PriorNearestLatE5);
        Assert.NotNull(rec.PriorNearestLonE5);
    }

    [Fact]
    public async Task PriorPairIsAtomic_BothPresentOrBothAbsent_NeverOne()
    {
        var publisher = new SpyHubPublisher();
        var worker = MakeWorker(publisher);
        var city = new FakeCity("atlanta");

        await worker.ProcessSpatialReconciliationAsync(city, Feed("v1", "95", 33.0, -84.0, 1000), Index, null, CancellationToken.None);
        await worker.ProcessSpatialReconciliationAsync(city, Feed("v1", "95", 33.002, -84.002, 2000), Index, null, CancellationToken.None);
        await worker.ProcessSpatialReconciliationAsync(city, Feed("v1", "12", 34.0, -85.0, 3000), Index, null, CancellationToken.None);

        Assert.NotEmpty(publisher.CapturedRecords);
        Assert.All(publisher.CapturedRecords, r =>
            Assert.Equal(r.PriorNearestLatE5.HasValue, r.PriorNearestLonE5.HasValue));
    }

    [Fact]
    public async Task KnownCategory_IsOmitted_AndUnknownCategoryStillRides()
    {
        var publisher = new SpyHubPublisher();
        var worker = MakeWorker(publisher);
        var city = new FakeCity("atlanta");
        var modeMap = new Dictionary<string, string> { ["95"] = "rail" };

        // Route 95 IS in the map → client resolves "rail" from its catalog → omit.
        await worker.ProcessSpatialReconciliationAsync(city, Feed("v1", "95", 33.0, -84.0, 1000), Index, modeMap, CancellationToken.None);
        var known = Assert.Single(publisher.CapturedRecords);
        Assert.Null(known.Category);

        // Route 12 is NOT in the map → route join failed → the signal must travel.
        publisher.CapturedRecords.Clear();
        await worker.ProcessSpatialReconciliationAsync(city, Feed("v2", "12", 34.0, -85.0, 1000), Index, modeMap, CancellationToken.None);
        var unknown = Assert.Single(publisher.CapturedRecords);
        Assert.Equal("unknown", unknown.Category);
    }

    [Fact]
    public async Task StaleSample_StillRidesEveryRecord_WithIsStaleSet()
    {
        // Regression guard (data-model §1, Key 9): IsStale must ride every record. Losing it
        // is what made vehicles read as "synthetically all moving" in a past regression.
        var publisher = new SpyHubPublisher();
        var worker = MakeWorker(publisher);
        var city = new FakeCity("atlanta");

        await worker.ProcessSpatialReconciliationAsync(city, Feed("v1", "95", 33.0, -84.0, 1000), Index, null, CancellationToken.None);
        publisher.CapturedRecords.Clear();
        // Same per-vehicle timestamp as the prior observation = the feed repeated itself.
        await worker.ProcessSpatialReconciliationAsync(city, Feed("v1", "95", 33.002, -84.002, 1000), Index, null, CancellationToken.None);

        var rec = Assert.Single(publisher.CapturedRecords);
        Assert.True(rec.IsStale);
    }

    [Fact]
    public async Task PublishedCoordinates_AreDegreesScaledByE5()
    {
        var publisher = new SpyHubPublisher();
        var worker = MakeWorker(publisher);

        await worker.ProcessSpatialReconciliationAsync(
            new FakeCity("atlanta"), Feed("v1", "95", 33.0, -84.0, 1000), Index, null, CancellationToken.None);

        var rec = Assert.Single(publisher.CapturedRecords);
        // Snapped to the route point at (33.0, -84.0) → 3_300_000 / -8_400_000.
        Assert.Equal(Worker.ScaleE5(33.0), rec.CurrentNearestLatE5);
        Assert.Equal(Worker.ScaleE5(-84.0), rec.CurrentNearestLonE5);
    }

    sealed class FakeCity(string name) : ITransitCity
    {
        public string Name => name;
        public bool EmitsTelemetry => true;
        public Task<FeedMessage> FetchVehiclesAsync(CancellationToken ct) => Task.FromResult(new FeedMessage());
    }

    // Copies records out SYNCHRONOUSLY inside PublishBatchAsync. The batch's backing arrays are
    // pooled (RecyclableList) and returned once the caller's `using` scope exits, so capturing
    // the list reference alone would read recycled memory by assertion time.
    sealed class SpyHubPublisher : ITransitHubPublisher
    {
        public List<RouteNearestPointBatchEvent.RouteNearestPointRecord> CapturedRecords { get; } = [];
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> PublishBatchAsync(string city, List<EventEnvelope> batch, CancellationToken ct = default)
        {
            foreach (var envelope in batch)
                if (envelope.Payload is RouteNearestPointBatchEvent e)
                    CapturedRecords.AddRange(e.BatchRecords);
            return Task.FromResult(true);
        }
    }

    sealed class SpyEventNotifications : IEventNotificationService
    {
        public event EventReceivedEventHandler? EventReceived;
        public void PostEvent(object sender, IEventArgs e) => EventReceived?.Invoke(sender, e);
    }

    sealed class NoOpLoggingService : ILoggingService
    {
        public long DroppedRecords => 0;
        public long PersistFailures => 0;
        public void Accumulate(IEventArgs e) { }
        public Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
    }

    sealed class NoOpTriggerPointGenerator : Shared.Services.ITriggerPointGenerator
    {
        public IReadOnlyList<TriggerPoint> Generate(double[][] coords, double[] cumDist) => [];
    }

    sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { BaseAddress = new Uri("https://fake/") };
    }
}
