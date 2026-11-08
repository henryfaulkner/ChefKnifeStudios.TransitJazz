using System.Net;
using System.Text;
using System.Text.Json;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.RailRealtime;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class ReportedVehicleSourceExtractionTests
{
    static readonly DateTimeOffset ReceiptUtc = new(2026, 11, 7, 12, 5, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FetchProducesIndependentBusAndRailEvidenceWhetherReportedCaptureIsEnabled(bool reportedCaptureEnabled)
    {
        using var clients = new StubHttpClientFactory(BusFeed(), RailJson());
        var city = CreateCity(clients, reportedCaptureEnabled, railEnabled: true, new FixedClock(ReceiptUtc));

        var fetch = await city.FetchVehiclesAsync(CancellationToken.None);

        Assert.Equal(CityFetchOutcome.Success, fetch.Outcome);
        Assert.Equal(new[] { "marta-bus", "marta-rail" },
            fetch.SourceObservations.Select(source => source.SourceKey).Order(StringComparer.Ordinal));
        Assert.All(fetch.SourceObservations, source =>
        {
            Assert.Equal("success", source.Outcome);
            Assert.Equal(ReceiptUtc.UtcDateTime, source.ReceiptUtc);
            Assert.Null(source.FailureReason);
        });

        if (reportedCaptureEnabled)
        {
            var bus = Assert.Single(fetch.ReportedVehicleObservations, observation => observation.SourceKey == "marta-bus");
            var rail = Assert.Single(fetch.ReportedVehicleObservations, observation => observation.SourceKey == "marta-rail");
            Assert.Equal(new[] { "bus-no-position", "bus-positioned" }, bus.Identities.Select(identity => identity.Value));
            Assert.Equal(new[] { "train-non-realtime", "train-realtime" }, rail.Identities.Select(identity => identity.Value));
        }
        else
        {
            Assert.Empty(fetch.ReportedVehicleObservations);
        }

        foreach (var observation in fetch.ReportedVehicleObservations)
            observation.Dispose();
    }

    [Fact]
    public async Task FailedBusAndSuccessfulRailRemainSeparateAndDoNotEraseExpectedBusEvidence()
    {
        using var clients = new StubHttpClientFactory(BusFeed(), RailJson()) { BusStatusCode = HttpStatusCode.ServiceUnavailable };
        var city = CreateCity(clients, reportedCaptureEnabled: false, railEnabled: true, new FixedClock(ReceiptUtc));

        var fetch = await city.FetchVehiclesAsync(CancellationToken.None);

        Assert.Equal(CityFetchOutcome.PartialFailure, fetch.Outcome);
        var bus = Assert.Single(fetch.SourceObservations, source => source.SourceKey == "marta-bus");
        Assert.Equal("failed", bus.Outcome);
        Assert.Equal("source_failure", bus.FailureReason);
        Assert.False(bus.PositionedCoverageValid);
        var rail = Assert.Single(fetch.SourceObservations, source => source.SourceKey == "marta-rail");
        Assert.Equal("success", rail.Outcome);
        Assert.Contains(fetch.SourceObservations, source => source.SourceKey == "marta-bus");
        Assert.Empty(fetch.ReportedVehicleObservations);
    }

    [Fact]
    public async Task DisabledRailRemainsAnExplicitExpectedSourceWhenReportedCaptureIsOff()
    {
        using var clients = new StubHttpClientFactory(BusFeed(), RailJson());
        var city = CreateCity(clients, reportedCaptureEnabled: false, railEnabled: false, new FixedClock(ReceiptUtc));

        var fetch = await city.FetchVehiclesAsync(CancellationToken.None);

        var bus = Assert.Single(fetch.SourceObservations, source => source.SourceKey == "marta-bus");
        Assert.Equal("success", bus.Outcome);
        var rail = Assert.Single(fetch.SourceObservations, source => source.SourceKey == "marta-rail");
        Assert.Equal("disabled", rail.Outcome);
        Assert.Equal("source_disabled", rail.FailureReason);
        Assert.False(rail.PositionedCoverageValid);
    }

    static MartaCity CreateCity(StubHttpClientFactory clients, bool reportedCaptureEnabled,
        bool railEnabled, TimeProvider clock) => new(
        clients,
        Options.Create(new RailRealtimeOptions
        {
            Enabled = railEnabled,
            BaseUrl = "https://marta.invalid/rail",
            ReportedVehicleCaptureEnabled = reportedCaptureEnabled,
            BusReportedVehicleSource = Source("marta-bus"),
            RailReportedVehicleSource = Source("marta-rail"),
        }),
        NullLogger<MartaCity>.Instance,
        clock);

    static ReportedVehicleSourceConfig Source(string key) => new()
    {
        SourceKey = key,
        IdentityNamespace = key,
        IdentityKind = "vehicle-descriptor-id",
    };

    static FeedMessage BusFeed() => new()
    {
        Entities =
        [
            new FeedEntity
            {
                Id = "bus-positioned",
                Vehicle = new VehiclePosition
                {
                    Vehicle = new VehicleDescriptor { Id = "bus-positioned" },
                    Trip = new TripDescriptor { RouteId = "B1" },
                    Position = new Position { Latitude = 33.7f, Longitude = -84.4f },
                },
            },
            new FeedEntity
            {
                Id = "bus-no-position",
                Vehicle = new VehiclePosition { Vehicle = new VehicleDescriptor { Id = "bus-no-position" } },
            },
        ],
    };

    static string RailJson() => JsonSerializer.Serialize(new[]
    {
        new RailArrivalDto { TrainId = "train-realtime", Line = "RED", Latitude = "33.7", Longitude = "-84.4", IsRealtime = "true" },
        new RailArrivalDto { TrainId = "train-non-realtime", Line = "BLUE", Latitude = "", Longitude = "", IsRealtime = "false" },
    });

    sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    sealed class StubHttpClientFactory : IHttpClientFactory, IDisposable
    {
        readonly FeedMessage _busFeed;
        readonly string _railJson;
        readonly HttpClient _bus;
        readonly HttpClient _rail;

        public StubHttpClientFactory(FeedMessage busFeed, string railJson)
        {
            _busFeed = busFeed;
            _railJson = railJson;
            _bus = new HttpClient(new ResponseHandler(() =>
                new HttpResponseMessage(BusStatusCode) { Content = new ByteArrayContent(Serialize(_busFeed)) }));
            _rail = new HttpClient(new ResponseHandler(() =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_railJson, Encoding.UTF8, "application/json") }))
            {
                BaseAddress = new Uri("https://marta.invalid/rail"),
            };
        }

        public HttpStatusCode BusStatusCode { get; set; } = HttpStatusCode.OK;

        public HttpClient CreateClient(string name) => name == "RailRealtimeApi" ? _rail : _bus;

        static byte[] Serialize(FeedMessage feed)
        {
            using var stream = new MemoryStream();
            ProtoBuf.Serializer.Serialize(stream, feed);
            return stream.ToArray();
        }

        public void Dispose()
        {
            _bus.Dispose();
            _rail.Dispose();
        }
    }

    sealed class ResponseHandler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response());
    }
}
