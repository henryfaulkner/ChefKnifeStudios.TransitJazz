using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.RailRealtime;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Subway;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProtoBuf;
using System.Net;
using System.Text.Json;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Statistics;

public sealed class ReportedVehicleSourceExtractionTests
{
    static readonly DateTimeOffset Receipt = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GtfsExtractionKeepsUnmappableVehiclesAndExcludesDeletedAndNonVehicleEntities()
    {
        var feed = new FeedMessage
        {
            Entities =
            [
                Vehicle("orphan", position: null, route: null),
                new FeedEntity
                {
                    Id = "deleted",
                    IsDeleted = true,
                    Vehicle = new VehiclePosition { Vehicle = new VehicleDescriptor { Id = "deleted" } },
                },
                new FeedEntity { Id = "trip", TripUpdate = new TripUpdate() },
                Vehicle(null, position: null, route: null),
            ]
        };
        var config = new CityConfig
        {
            Name = "test-city",
            GtfsRtUrls = ["https://feed/one"],
            ReportedVehicleSources = [Source("gtfs-1", "city-bus")],
        };
        var clock = new FixedClock(Receipt);
        var city = new GtfsRtCity(config, new FakeHttpClientFactory(_ => Protobuf(feed)),
            NullLogger<GtfsRtCity>.Instance, clock, captureReportedVehicles: true);

        var result = await city.FetchVehiclesAsync(CancellationToken.None);
        var observation = Assert.Single(result.ReportedVehicleObservations);

        Assert.Equal(CityFetchOutcome.Success, result.Outcome);
        Assert.Equal(4, result.ValidRecordCount);
        Assert.Equal(Receipt.UtcDateTime, observation.ReceiptUtc);
        Assert.Equal(2, observation.RawVehicleRecordCount);
        Assert.Equal(1, observation.UnidentifiableRecordCount);
        Assert.Equal("orphan", Assert.Single(observation.Identities).Value);
    }

    [Fact]
    public async Task MartaRailExtractionIncludesOriginalTrainIdsBeforeRealtimeAndPositionFilters()
    {
        var options = Options.Create(new RailRealtimeOptions
        {
            Enabled = true,
            ReportedVehicleCaptureEnabled = true,
            BusReportedVehicleSource = Source("marta-bus", "marta-bus"),
            RailReportedVehicleSource = new ReportedVehicleSourceConfig
            {
                SourceKey = "marta-rail", IdentityNamespace = "marta-rail", IdentityKind = "agency-train-id",
            },
        });
        var railJson = """
            [
              {"TRAIN_ID":"T1","IS_REALTIME":"false","LINE":"1"},
              {"TRAIN_ID":"T2","IS_REALTIME":"true","LINE":"2","LATITUDE":"bad","LONGITUDE":"bad"},
              {"IS_REALTIME":"true","LINE":"2","LATITUDE":"33","LONGITUDE":"-84"}
            ]
            """;
        var factory = new FakeHttpClientFactory(request =>
            request.RequestUri!.Host == "gtfs-rt.itsmarta.com" ? Protobuf(new FeedMessage()) : Json(railJson));
        var city = new MartaCity(factory, options, NullLogger<MartaCity>.Instance, new FixedClock(Receipt));

        var result = await city.FetchVehiclesAsync(CancellationToken.None);
        var rail = Assert.Single(result.ReportedVehicleObservations.Where(x => x.SourceKey == "marta-rail"));

        Assert.Equal(3, rail.RawVehicleRecordCount);
        Assert.Equal(new[] { "T1", "T2" }, rail.Identities.Select(x => x.Value));
        Assert.Empty(result.Feed.Entities);
    }

    [Fact]
    public async Task NymtaFetchesRawSubwayVehiclesBeforeUnavailableStopOffsets()
    {
        var feed = new FeedMessage { Entities = [Vehicle("train-7", position: null, route: "7")] };
        var options = Options.Create(new SubwaySynthesisOptions
        {
            GtfsRtUrls = ["https://feed/subway"],
            ReportedVehicleCaptureEnabled = true,
            ReportedVehicleSources = [Source("nyc-subway-group", "nyc-rail")],
        });
        var factory = new FakeHttpClientFactory(request => request.RequestUri!.AbsolutePath.Contains("stop-offsets", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Protobuf(feed));
        var bus = new CityConfig { Name = "new-york-city", GtfsRtUrls = [] };
        var city = new NymtaCity(factory, options, bus, NullLogger<NymtaCity>.Instance,
            NullLogger<GtfsRtCity>.Instance, new FixedClock(Receipt));

        var result = await city.FetchVehiclesAsync(CancellationToken.None);
        var observation = Assert.Single(result.ReportedVehicleObservations);

        Assert.Equal(CityFetchOutcome.Empty, result.Outcome);
        Assert.Empty(result.Feed.Entities);
        Assert.Equal("train-7", Assert.Single(observation.Identities).Value);
        Assert.Equal(Receipt.UtcDateTime, observation.ReceiptUtc);
    }

    [Fact]
    public void CombineRetainsPerSourceObservationsEvenWhenAggregateFeedIsEmpty()
    {
        var definition = Source("source-a", "domain-a").ToDefinition();
        var observation = ReportedVehicleSourceObservation.Successful(definition, Receipt, ["x"]);
        var combined = CityFetchResult.Combine(
            CityFetchResult.FromSources(new FeedMessage(), 1, 0, reportedVehicleObservations: [observation]),
            CityFetchResult.FromSources(new FeedMessage(), 1, 0));

        Assert.Single(combined.ReportedVehicleObservations);
        Assert.Equal("x", Assert.Single(combined.ReportedVehicleObservations[0].Identities).Value);
        Assert.Equal(CityFetchOutcome.Empty, combined.Outcome);
    }

    static ReportedVehicleSourceConfig Source(string key, string identityNamespace) => new()
    {
        SourceKey = key, IdentityNamespace = identityNamespace, IdentityKind = "vehicle-descriptor-id",
    };

    static FeedEntity Vehicle(string? id, Position? position, string? route) => new()
    {
        Id = id ?? "missing-descriptor",
        Vehicle = new VehiclePosition
        {
            Vehicle = new VehicleDescriptor { Id = id },
            Trip = route is null ? null! : new TripDescriptor { RouteId = route },
            Position = position,
        }
    };

    static HttpResponseMessage Protobuf(FeedMessage feed)
    {
        var stream = new MemoryStream();
        Serializer.Serialize(stream, feed);
        stream.Position = 0;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
    }

    static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, System.Text.Encoding.UTF8, "application/json")
    };

    sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    sealed class FakeHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> respond) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FakeHandler(respond), disposeHandler: true)
        {
            BaseAddress = name == "GtfsStaticApi" ? new Uri("https://fake/")
                : name == "RailRealtimeApi" ? new Uri("https://fake/rail") : null,
        };
    }

    sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
    }
}
