using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using ChefKnifeStudios.TransitJazz.Shared.Geospatial;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using ChefKnifeStudios.TransitJazz.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests;

public class TelemetryCityNameParityTests
{
    [Fact]
    public void transit_city_has_no_telemetry_only_identifier()
    {
        Assert.Null(typeof(ITransitCity).GetProperty("TelemetryName"));
    }

    [Fact]
    public async Task per_city_cycle_uses_the_canonical_city_name()
    {
        ITransitCity city = new FakeCity(CityNames.Marta);
        var worker = new Worker(
            new NullHttpClientFactory(),
            NullLogger<Worker>.Instance,
            new NoOpHubPublisher(),
            new EventNotificationService(),
            new LogEventWorker(
                new EventNotificationService(),
                new NoOpLoggingService(),
                Options.Create(new LoggingOptions { Enabled = false }),
                NullLogger<LogEventWorker>.Instance),
            new NoOpLoggingService(),
            [city],
            new NoOpTriggerPointGenerator());

        var result = await worker.ProcessSpatialReconciliationAsync(
            city,
            new FeedMessage(),
            new Dictionary<string, RoutePoint[]>(),
            modeMap: null,
            CancellationToken.None);

        Assert.Equal(CityNames.Marta, result.CityName);
    }

    sealed class FakeCity(string name) : ITransitCity
    {
        public string Name => name;
        public bool EmitsTelemetry => true;
        public Task<FeedMessage> FetchVehiclesAsync(CancellationToken ct) => Task.FromResult(new FeedMessage());
    }

    sealed class NullHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    sealed class NoOpHubPublisher : ITransitHubPublisher
    {
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> PublishBatchAsync(string city, List<EventEnvelope> batch, CancellationToken ct = default) => Task.FromResult(true);
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
}
