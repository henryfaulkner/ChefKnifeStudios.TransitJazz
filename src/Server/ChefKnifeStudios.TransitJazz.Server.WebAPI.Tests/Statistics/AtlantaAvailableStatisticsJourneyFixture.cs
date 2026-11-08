using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using ChefKnifeStudios.TransitJazz.Server.Data.Models;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.RailRealtime;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using ChefKnifeStudios.TransitJazz.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

/// <summary>Composes the real MARTA fetch and Worker city-cycle path with disposable persistence.
/// It owns no process environment, external HTTP, or database outside the guarded child fixture.</summary>
public sealed class AtlantaAvailableStatisticsJourneyFixture : IAsyncDisposable
{
    public static readonly DateTimeOffset HourStartUtc = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
    public static readonly Guid CaptureRunId = Guid.Parse("4cc8a0a9-bd1e-4b12-9fd0-b053fb124683");

    readonly RouteHourStatisticsDatabaseFixture _database;
    readonly SignalingStore _store;
    readonly StubHttpClientFactory _httpClients;
    bool _disposed;

    AtlantaAvailableStatisticsJourneyFixture(
        RouteHourStatisticsDatabaseFixture database,
        SignalingStore store,
        StubHttpClientFactory httpClients,
        RouteHourStatisticsWriter writer,
        RouteHourStatisticsCapture capture,
        RouteHourTestClock clock,
        MartaCity city,
        Worker worker)
    {
        _database = database;
        _store = store;
        _httpClients = httpClients;
        Writer = writer;
        Capture = capture;
        Clock = clock;
        City = city;
        Worker = worker;
    }

    public RouteHourStatisticsWriter Writer { get; }
    public RouteHourStatisticsCapture Capture { get; }
    public RouteHourTestClock Clock { get; }
    public MartaCity City { get; }
    public Worker Worker { get; }
    public RouteHourStatisticsDatabaseFixture Database => _database;

    public static async Task<AtlantaAvailableStatisticsJourneyFixture> CreateAsync()
    {
        var database = await RouteHourStatisticsDatabaseFixture.CreateAsync();
        StubHttpClientFactory? httpClients = null;
        RouteHourStatisticsWriter? writer = null;
        Worker? worker = null;
        try
        {
            var limits = new RouteHourHistoryOptions();
            var store = new SignalingStore(database.Store);
            writer = new RouteHourStatisticsWriter(store, limits,
                NullLogger<RouteHourStatisticsWriter>.Instance);
            await writer.StartAsync(CancellationToken.None);

            var clock = new RouteHourTestClock(HourStartUtc);
            var runtime = new RouteHourCaptureRuntimeOptions(RouteHourCaptureMode.Persistence,
                ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, CityNames.Marta), limits);
            var capture = new RouteHourStatisticsCapture(runtime, writer, clock,
                NullLogger<RouteHourStatisticsCapture>.Instance, CaptureRunId);

            httpClients = new StubHttpClientFactory();
            var city = new MartaCity(httpClients, Options.Create(new RailRealtimeOptions
            {
                Enabled = true,
                BaseUrl = "https://marta.invalid/rail",
            }), NullLogger<MartaCity>.Instance, clock);

            var shapes = AtlantaShapes();
            var routeSource = new InMemoryRouteShapeSource();
            routeSource.Publish(shapes);
            worker = new Worker(NullLogger<Worker>.Instance, new RecordingPublisher(), [city],
                new TriggerPointGenerator(NullLogger<TriggerPointGenerator>.Instance),
                routeShapeSource: routeSource, routeHourStatisticsCapture: capture);
            await worker.InitializeRouteIndexAsync(CancellationToken.None);

            return new AtlantaAvailableStatisticsJourneyFixture(database, store, httpClients,
                writer, capture, clock, city, worker);
        }
        catch
        {
            worker?.Dispose();
            if (writer is not null)
            {
                try { await writer.StopAsync(CancellationToken.None); }
                finally { writer.Dispose(); }
            }
            httpClients?.Dispose();
            await database.DisposeAsync();
            throw;
        }
    }

    /// <summary>Runs the same actual fetch-to-reconciliation path as the hosted Worker loop.</summary>
    internal async Task<Worker.CityCycleExecution> RunCityCycleAsync(DateTimeOffset completedUtc,
        CancellationToken cancellationToken = default)
    {
        completedUtc = completedUtc.ToUniversalTime();
        Clock.SetUtcNow(completedUtc);
        return await Worker.ExecuteCityCycleAsync(City, cancellationToken,
            completedUtc.UtcDateTime, completedUtc);
    }

    public void SetBusFeed(FeedMessage feed) => _httpClients.SetBusFeed(feed);

    public void SetRailArrivals(IEnumerable<RailArrivalDto> arrivals) => _httpClients.SetRailArrivals(arrivals);

    public Task<RouteHourStatisticsWriteReport> WaitForPersistedEnvelopeAsync(CancellationToken cancellationToken = default) =>
        _store.WaitForWriteAsync(cancellationToken);

    public static FeedEntity BusVehicle(string id, string routeId, float latitude, float longitude, ulong timestamp) => new()
    {
        Id = id,
        Vehicle = new VehiclePosition
        {
            Vehicle = new VehicleDescriptor { Id = id },
            Trip = new TripDescriptor { RouteId = routeId },
            Position = new Position { Latitude = latitude, Longitude = longitude },
            Timestamp = timestamp,
        },
    };

    static IReadOnlyList<RouteShapeFeature> AtlantaShapes() =>
    [
        Shape("bus-static", "B1", "bus", 33.700, -84.410),
        Shape("rail-red", "Red", "rail", 33.710, -84.410),
        Shape("rail-gold", "Gold", "rail", 33.720, -84.410),
        Shape("rail-blue", "Blue", "rail", 33.730, -84.410),
        Shape("rail-green", "Green", "rail", 33.740, -84.410),
    ];

    static RouteShapeFeature Shape(string routeId, string shortName, string category, double latitude, double longitude) => new(
        "Feature",
        new RouteShapeGeometry("LineString", Enumerable.Range(0, 11)
            .Select(index => new[] { longitude + index * 0.002, latitude }).ToArray()),
        new RouteShapeProperties(routeId, shortName, "#000000", "#ffffff", Category: category, City: CityNames.Marta));

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        Worker.Dispose();
        try { await Writer.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30)); }
        finally
        {
            Writer.Dispose();
            _httpClients.Dispose();
            await _database.DisposeAsync();
        }
    }

    sealed class SignalingStore(ICityRouteHourStatisticsStore inner) : ICityRouteHourStatisticsStore
    {
        TaskCompletionSource<RouteHourStatisticsWriteReport> _writeCompleted = NewSignal();

        public async Task<RouteHourStatisticsWriteReport> WriteAsync(IReadOnlyCollection<CityRouteHourStatistic> rows,
            int maxRowsPerCommand = 128, int commandTimeoutSeconds = 5, int maxRoutesPerCityHour = 4096,
            CancellationToken cancellationToken = default)
        {
            var report = await inner.WriteAsync(rows, maxRowsPerCommand, commandTimeoutSeconds,
                maxRoutesPerCityHour, cancellationToken);
            Volatile.Read(ref _writeCompleted).TrySetResult(report);
            return report;
        }

        public Task<RouteHourStatisticsWriteReport> WaitForWriteAsync(CancellationToken cancellationToken) =>
            Volatile.Read(ref _writeCompleted).Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

        static TaskCompletionSource<RouteHourStatisticsWriteReport> NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    sealed class StubHttpClientFactory : IHttpClientFactory, IDisposable
    {
        readonly object _gate = new();
        byte[] _busBytes = SerializeBusFeed(new FeedMessage());
        string _railJson = "[]";
        readonly HttpClient _busClient;
        readonly HttpClient _railClient;

        public StubHttpClientFactory()
        {
            _busClient = new HttpClient(new ResponseHandler(this, isRail: false));
            _railClient = new HttpClient(new ResponseHandler(this, isRail: true))
            {
                BaseAddress = new Uri("https://marta.invalid/rail"),
            };
        }

        public HttpClient CreateClient(string name) => name == "RailRealtimeApi" ? _railClient : _busClient;

        public void SetBusFeed(FeedMessage feed)
        {
            var bytes = SerializeBusFeed(feed);
            lock (_gate) _busBytes = bytes;
        }

        public void SetRailArrivals(IEnumerable<RailArrivalDto> arrivals)
        {
            var json = JsonSerializer.Serialize(arrivals);
            lock (_gate) _railJson = json;
        }

        HttpResponseMessage CreateResponse(bool isRail)
        {
            lock (_gate)
            {
                return isRail
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_railJson, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_busBytes.ToArray()) };
            }
        }

        static byte[] SerializeBusFeed(FeedMessage feed)
        {
            using var stream = new MemoryStream();
            ProtoBuf.Serializer.Serialize(stream, feed);
            return stream.ToArray();
        }

        public void Dispose()
        {
            _busClient.Dispose();
            _railClient.Dispose();
        }

        sealed class ResponseHandler(StubHttpClientFactory owner, bool isRail) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(owner.CreateResponse(isRail));
        }
    }

    sealed class RecordingPublisher : ITransitHubPublisher
    {
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> PublishBatchAsync(string city, List<EventEnvelope> batch, CancellationToken ct = default) =>
            Task.FromResult(true);
    }

    public sealed class RouteHourTestClock(DateTimeOffset now) : TimeProvider
    {
        DateTimeOffset _now = now.ToUniversalTime();
        public override DateTimeOffset GetUtcNow() => _now;
        public void SetUtcNow(DateTimeOffset value) => _now = value.ToUniversalTime();
    }
}
