using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Subway;
using ChefKnifeStudios.MartaJazz.Shared.GtfsData;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProtoBuf;
using System.Net;
using System.Text.Json;
using Xunit;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests;

// US3 (SC-006): among several configured line-group feeds, one throws/fails — the merged
// feed still contains the other feeds' synthesized entities. Mirrors
// CityLoopTests.FaultIsolation_ThrowingCity_DoesNotBlockOtherCity, but at the per-feed
// fan-out level inside NymtaCity itself (INV-N2).
public class NymtaCityFaultIsolationTests
{
    static readonly SubwayStopOffsetSet OffsetSet = new(
        "7", "N",
        [[-73.99, 40.75], [-73.97, 40.75]],
        [0, 1857.0],
        [
            new SubwayStop("701N", 40.75, -73.99, 0),
            new SubwayStop("702N", 40.75, -73.97, 1857.0),
        ]);

    [Fact]
    public async Task FetchVehiclesAsync_OneFeedThrows_OtherFeedStillSynthesizes()
    {
        var goodFeed = new FeedMessage
        {
            Entities =
            [
                new FeedEntity
                {
                    Id = "train-1",
                    Vehicle = new VehiclePosition
                    {
                        Trip = new TripDescriptor { RouteId = "7" },
                        Vehicle = new VehicleDescriptor { Id = "train-1" },
                        StopId = "702N",
                        CurrentStatus = ChefKnifeStudios.MartaJazz.Shared.EventData.VehicleStopStatus.StoppedAt,
                    }
                }
            ]
        };

        var handler = new FakeHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("stop-offsets"))
                return JsonResponse(JsonSerializer.Serialize(new[] { OffsetSet }, Shared.JsonOptions.Get()));
            if (url.Contains("dead-feed"))
                throw new HttpRequestException("Simulated dead line group");
            return ProtobufResponse(goodFeed);
        });

        var factory = new FakeHttpClientFactory(handler);
        var options = Options.Create(new SubwaySynthesisOptions
        {
            GtfsRtUrls = ["https://fake/dead-feed", "https://fake/good-feed"],
            NominalRunSeconds = 90,
        });
        var busConfig = new CityConfig { Name = "nymta", GtfsRtUrls = [] };

        var city = new NymtaCity(factory, options, busConfig, NullLogger<NymtaCity>.Instance, NullLogger<GtfsRtCity>.Instance);
        var result = await city.FetchVehiclesAsync(CancellationToken.None);

        Assert.Single(result.Entities);
        Assert.Equal("train-1", result.Entities[0].Id);
    }

    [Fact]
    public async Task FetchVehiclesAsync_TableFetchFails_ReturnsEmptyFeed_DoesNotThrow()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var factory = new FakeHttpClientFactory(handler);
        var options = Options.Create(new SubwaySynthesisOptions { GtfsRtUrls = ["https://fake/feed"] });
        var busConfig = new CityConfig { Name = "nymta", GtfsRtUrls = [] };

        var city = new NymtaCity(factory, options, busConfig, NullLogger<NymtaCity>.Instance, NullLogger<GtfsRtCity>.Instance);
        var exception = await Record.ExceptionAsync(() => city.FetchVehiclesAsync(CancellationToken.None));

        Assert.Null(exception);
    }

    static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };

    static HttpResponseMessage ProtobufResponse(FeedMessage feed)
    {
        var ms = new MemoryStream();
        Serializer.Serialize(ms, feed);
        ms.Position = 0;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(ms) };
    }

    sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
    }

    sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = name == "RouteShapeApi" ? new Uri("https://fake/") : null
        };
    }
}
