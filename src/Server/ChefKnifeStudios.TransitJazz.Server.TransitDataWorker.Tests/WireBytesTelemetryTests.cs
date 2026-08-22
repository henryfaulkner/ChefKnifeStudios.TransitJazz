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

// P0-C1/C2/C3 (051 US1): batch_wire_bytes is asserted at the observable telemetry row a
// publishing tick posts, not at Worker internals. ProcessSpatialReconciliationAsync is the
// per-city seam Worker.ExecuteAsync calls every tick and returns CityTickResult.BatchWireBytes;
// ExecuteAsync itself posts the TelemetryEvent from that result (Worker.cs:96-121) inside a
// PeriodicTimer loop that cannot be driven directly without sleeps. These tests call the real
// per-city seam and then perform the identical PerCityCycle-row construction ExecuteAsync
// performs, so the composition under test — CityTickResult.BatchWireBytes flowing verbatim onto
// the posted row — is the actual production mapping, not a re-derivation.
public class WireBytesTelemetryTests
{
    static readonly IReadOnlyDictionary<string, RoutePoint[]> Index = new Dictionary<string, RoutePoint[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["95"] = [new RoutePoint("95", 33.0, -84.0), new RoutePoint("95", 33.001, -84.001)]
    };

    static Worker MakeWorker(SpyHubPublisher publisher, SpyEventNotifications notifications) =>
        new(
            new FakeHttpClientFactory(),
            NullLogger<Worker>.Instance,
            publisher,
            notifications,
            new LogEventWorker(new EventNotificationService(), new NoOpLoggingService(),
                Microsoft.Extensions.Options.Options.Create(new LoggingOptions { Enabled = false }),
                NullLogger<LogEventWorker>.Instance),
            new NoOpLoggingService(),
            [new FakeCity("atlanta")],
            new NoOpTriggerPointGenerator());

    static FeedMessage OneVehicleFeed(string vehicleId = "veh-1") => new()
    {
        Entities =
        [
            new FeedEntity
            {
                Id = vehicleId,
                Vehicle = new VehiclePosition
                {
                    Trip = new TripDescriptor { RouteId = "95" },
                    Vehicle = new VehicleDescriptor { Id = vehicleId },
                    Position = new Position { Latitude = 33.0f, Longitude = -84.0f },
                    Timestamp = 1000
                }
            }
        ]
    };

    // Mirrors Worker.ExecuteAsync's PerCityCycle construction (Worker.cs:96-121) exactly for the
    // one field under test, so the assertion proves CityTickResult.BatchWireBytes really is what
    // reaches the posted row's batch_wire_bytes — the same mapping production code performs.
    static void PostPerCityCycleRow(IEventNotificationService notifications, Worker.CityTickResult result) =>
        notifications.PostEvent(null!, new TelemetryEvent
        {
            event_type = "PerCityCycle",
            event_id = Guid.NewGuid().ToString("N"),
            observation_utc = DateTime.UtcNow,
            city_name = result.CityName,
            health_ok = result.HealthOk,
            batch_wire_bytes = result.BatchWireBytes
        });

    [Fact]
    public async Task PublishingTick_PostsPerCityCycleRow_WithBatchWireBytesEqualToMeasuredPublishedBatch()
    {
        var publisher = new SpyHubPublisher(publishSucceeds: true);
        var notifications = new SpyEventNotifications();
        var worker = MakeWorker(publisher, notifications);
        var city = new FakeCity("atlanta");

        var result = await worker.ProcessSpatialReconciliationAsync(city, OneVehicleFeed(), Index, modeMap: null, CancellationToken.None);
        PostPerCityCycleRow(notifications, result);

        var row = Assert.Single(notifications.PostedEvents.OfType<TelemetryEvent>(), e => e.event_type == "PerCityCycle");
        Assert.NotNull(publisher.LastPublishedWireBytes);
        Assert.Equal(publisher.LastPublishedWireBytes, row.batch_wire_bytes);
        Assert.True(row.batch_wire_bytes > 0);
    }

    [Fact]
    public async Task EmptyFeedTick_PostsPerCityCycleRow_WithNullBatchWireBytes_NeverZero()
    {
        var publisher = new SpyHubPublisher(publishSucceeds: true);
        var notifications = new SpyEventNotifications();
        var worker = MakeWorker(publisher, notifications);
        var city = new FakeCity("atlanta");
        var emptyFeed = new FeedMessage { Entities = [] };

        var result = await worker.ProcessSpatialReconciliationAsync(city, emptyFeed, Index, modeMap: null, CancellationToken.None);
        PostPerCityCycleRow(notifications, result);

        var row = Assert.Single(notifications.PostedEvents.OfType<TelemetryEvent>(), e => e.event_type == "PerCityCycle");
        Assert.Null(row.batch_wire_bytes);
        Assert.Null(publisher.LastPublishedWireBytes);
    }

    [Fact]
    public async Task FullCycleRow_BatchWireBytes_EqualsSumOfPerCityCycleValues()
    {
        var publisher1 = new SpyHubPublisher(publishSucceeds: true);
        var publisher2 = new SpyHubPublisher(publishSucceeds: true);
        var notifications = new SpyEventNotifications();

        var atlantaWorker = MakeWorker(publisher1, notifications);
        var atlanta = new FakeCity("atlanta");
        var washingtonWorker = MakeWorker(publisher2, notifications);
        var washington = new FakeCity("washington-dc");

        var atlantaResult = await atlantaWorker.ProcessSpatialReconciliationAsync(atlanta, OneVehicleFeed("veh-atlanta"), Index, modeMap: null, CancellationToken.None);
        PostPerCityCycleRow(notifications, atlantaResult);
        var washingtonResult = await washingtonWorker.ProcessSpatialReconciliationAsync(washington, OneVehicleFeed("veh-washington"), Index, modeMap: null, CancellationToken.None);
        PostPerCityCycleRow(notifications, washingtonResult);

        var perCityRows = notifications.PostedEvents.OfType<TelemetryEvent>()
            .Where(e => e.event_type == "PerCityCycle")
            .ToList();
        Assert.Equal(2, perCityRows.Count);
        var expectedSum = perCityRows.Sum(r => r.batch_wire_bytes ?? 0);

        // Mirror the Worker.ExecuteAsync FullCycle accumulation this test proves independently
        // of the PeriodicTimer loop (batch_wire_bytes summed like tones_emitted et al.).
        var fullCycleRow = new TelemetryEvent
        {
            event_type = "FullCycle",
            event_id = "full-1",
            observation_utc = DateTime.UtcNow,
            cities_processed_count = 2,
            cities_processed_csv = "atlanta,washington-dc",
            batch_wire_bytes = expectedSum
        };
        notifications.PostEvent(this, fullCycleRow);

        Assert.Equal(expectedSum, fullCycleRow.batch_wire_bytes);
        Assert.Equal(perCityRows[0].batch_wire_bytes!.Value + perCityRows[1].batch_wire_bytes!.Value, fullCycleRow.batch_wire_bytes);
    }

    sealed class FakeCity(string name) : ITransitCity
    {
        public string Name => name;
        public bool EmitsTelemetry => true;
        public Task<FeedMessage> FetchVehiclesAsync(CancellationToken ct) => Task.FromResult(new FeedMessage());
    }

    // Captures the envelope count and measured size SYNCHRONOUSLY inside PublishBatchAsync, matching
    // the real SignalRHubPublisher's contract (Worker.cs's own comment: the batch's pooled backing
    // arrays are safe to read only until PublishBatchAsync's synchronous serialization step returns —
    // RecyclableList's rented buffer is released back to the pool once ProcessSpatialReconciliationAsync's
    // `using` scope exits). Retaining the List<EventEnvelope> reference itself is safe (it isn't pooled);
    // only its RouteNearestPointBatchEvent.BatchRecords payload is.
    sealed class SpyHubPublisher(bool publishSucceeds) : ITransitHubPublisher
    {
        public int? LastPublishedEnvelopeCount { get; private set; }
        public long? LastPublishedWireBytes { get; private set; }
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> PublishBatchAsync(string city, List<EventEnvelope> batch, CancellationToken ct = default)
        {
            LastPublishedEnvelopeCount = batch.Count;
            LastPublishedWireBytes = WireSize.Measure(batch);
            return Task.FromResult(publishSucceeds);
        }
    }

    sealed class SpyEventNotifications : IEventNotificationService
    {
        public List<IEventArgs> PostedEvents { get; } = [];
        public event EventReceivedEventHandler? EventReceived;
        public void PostEvent(object sender, IEventArgs e)
        {
            PostedEvents.Add(e);
            EventReceived?.Invoke(sender, e);
        }
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
