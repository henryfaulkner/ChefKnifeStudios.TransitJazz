using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Geospatial;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests.Cities;

public sealed class SeattleCityTests
{
    static readonly DateTimeOffset Now = SeattleTestFixtures.ReceiptUtc;
    const string KcmUrl = "https://s3.amazonaws.com/kcm-alerts-realtime-prod/vehiclepositions.pb";
    const string StUrl = "https://api.pugetsound.onebusaway.org/api/gtfs_realtime/vehicle-positions-for-agency/40.pb";

    [Fact]
    public async Task SoundTransitCredentialIsSentOnlyToItsEndpointAndNeverAppearsInLogsOrFeed()
    {
        var logger = new SeattleTestFixtures.CollectingLogger<SeattleCity>();
        var handler = RespondWith(request => SeattleTestFixtures.ProtobufResponse(
            SeattleTestFixtures.Feed(SeattleTestFixtures.Vehicle(
                request.RequestUri!.Host == "s3.amazonaws.com" ? "kcm-test-vehicle" : "st-test-vehicle", "100451"))));
        var city = CreateCity(handler, logger: logger);

        var result = await city.FetchVehiclesAsync(CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        var kcm = Assert.Single(handler.Requests, request => request.Uri!.Host == "s3.amazonaws.com");
        var st = Assert.Single(handler.Requests, request => request.Uri!.Host == "api.pugetsound.onebusaway.org");
        Assert.Equal(string.Empty, kcm.Uri!.Query);
        Assert.Equal("?key=" + SeattleTestFixtures.SoundTransitKeySentinel, st.Uri!.Query);
        Assert.DoesNotContain(SeattleTestFixtures.SoundTransitKeySentinel, result.Feed.ToString());
        Assert.DoesNotContain(SeattleTestFixtures.SoundTransitKeySentinel,
            string.Join("\n", logger.Messages), StringComparison.Ordinal);
        Assert.DoesNotContain(StUrl + "?key=", string.Join("\n", logger.Messages), StringComparison.Ordinal);
        Assert.Equal(new[] { "kcm:kcm-test-vehicle", "st:st-test-vehicle" },
            result.Feed.Entities.Select(entity => entity.Vehicle!.Vehicle!.Id).OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public async Task MissingSoundTransitKeyIsATruthfulPartialFailureAndMakesNoProtectedRequest()
    {
        var handler = RespondWith(_ => SeattleTestFixtures.ProtobufResponse(
            SeattleTestFixtures.Feed(SeattleTestFixtures.Vehicle("kcm-only", "100451"))));
        var city = CreateCity(handler, key: null);

        var result = await city.FetchVehiclesAsync(CancellationToken.None);

        Assert.Equal(CityFetchOutcome.PartialFailure, result.Outcome);
        Assert.Equal(1, result.SuccessfulSourceCount);
        Assert.Equal(1, result.FailedSourceCount);
        Assert.Single(result.Feed.Entities);
        Assert.Single(handler.Requests);
        Assert.Equal("s3.amazonaws.com", handler.Requests[0].Uri!.Host);
    }

    [Fact]
    public async Task CrossHostRedirectIsReturnedAsFailureWithoutForwardingTheCredential()
    {
        var handler = RespondWith(request => request.RequestUri!.Host == "s3.amazonaws.com"
            ? SeattleTestFixtures.ProtobufResponse(SeattleTestFixtures.Feed())
            : new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://redirect.example/collect?key=" + SeattleTestFixtures.SoundTransitKeySentinel) }
            });
        var city = CreateCity(handler);

        var result = await city.FetchVehiclesAsync(CancellationToken.None);

        Assert.Equal(CityFetchOutcome.PartialFailure, result.Outcome);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, request => request.Uri!.Host == "redirect.example");
        Assert.Equal(string.Empty, Assert.Single(handler.Requests, request => request.Uri!.Host == "s3.amazonaws.com").Uri!.Query);
        Assert.Contains("key=" + SeattleTestFixtures.SoundTransitKeySentinel,
            Assert.Single(handler.Requests, request => request.Uri!.Host == "api.pugetsound.onebusaway.org").Uri!.Query,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyMalformedAndFailedSourcesKeepIndependentReceiptAndCoverage()
    {
        var allEmptyHandler = RespondWith(_ => SeattleTestFixtures.ProtobufResponse(SeattleTestFixtures.Feed()));
        var allEmpty = await CreateCity(allEmptyHandler).FetchVehiclesAsync(CancellationToken.None);
        Assert.Equal(CityFetchOutcome.Empty, allEmpty.Outcome);
        Assert.Equal(2, allEmpty.SuccessfulSourceCount);

        var kcmEmptyHandler = RespondWith(request => request.RequestUri!.Host == "s3.amazonaws.com"
            ? SeattleTestFixtures.ProtobufResponse(SeattleTestFixtures.Feed())
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var kcmEmpty = await CreateCity(kcmEmptyHandler, captureReportedVehicles: true)
            .FetchVehiclesAsync(CancellationToken.None);
        Assert.Equal(CityFetchOutcome.PartialFailure, kcmEmpty.Outcome);
        var emptyObservation = Assert.Single(kcmEmpty.ReportedVehicleObservations, item => item.SourceKey == "seattle-kcm-test");
        var failedObservation = Assert.Single(kcmEmpty.ReportedVehicleObservations, item => item.SourceKey == "seattle-st-test");
        Assert.True(emptyObservation.IsSuccessful);
        Assert.Equal(0, emptyObservation.RawVehicleRecordCount);
        Assert.Equal(Now.UtcDateTime, emptyObservation.ReceiptUtc);
        Assert.False(failedObservation.IsSuccessful);
        Assert.Equal("source_failure", failedObservation.FailureCode);
        Assert.Equal(Now.UtcDateTime, failedObservation.ReceiptUtc);

        var malformedHandler = RespondWith(request => request.RequestUri!.Host == "s3.amazonaws.com"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0x0A]) }
            : SeattleTestFixtures.ProtobufResponse(SeattleTestFixtures.Feed(
                SeattleTestFixtures.Vehicle("healthy-st", "102767"))));
        var partial = await CreateCity(malformedHandler, captureReportedVehicles: true)
            .FetchVehiclesAsync(CancellationToken.None);
        Assert.Equal(CityFetchOutcome.PartialFailure, partial.Outcome);
        Assert.Single(partial.Feed.Entities);
        Assert.Equal("st:healthy-st", partial.Feed.Entities[0].Vehicle!.Vehicle!.Id);
        var malformed = Assert.Single(partial.ReportedVehicleObservations, item => item.SourceKey == "seattle-kcm-test");
        Assert.Equal("parse_failure", malformed.FailureCode);
        Assert.True(Assert.Single(partial.ReportedVehicleObservations, item => item.SourceKey == "seattle-st-test").IsSuccessful);

        var timeoutHandler = new SeattleTestFixtures.CapturingHandler((request, _) =>
            request.RequestUri!.Host == "s3.amazonaws.com"
                ? Task.FromException<HttpResponseMessage>(new TimeoutException("synthetic request timeout"))
                : Task.FromResult(SeattleTestFixtures.ProtobufResponse(SeattleTestFixtures.Feed(
                    SeattleTestFixtures.Vehicle("healthy-after-timeout", "102767", timestamp: Now)))));
        var timedOut = await CreateCity(timeoutHandler, captureReportedVehicles: true)
            .FetchVehiclesAsync(CancellationToken.None);
        Assert.Equal(CityFetchOutcome.PartialFailure, timedOut.Outcome);
        Assert.Equal("st:healthy-after-timeout", Assert.Single(timedOut.Feed.Entities).Vehicle!.Vehicle!.Id);
        Assert.Equal("source_failure", Assert.Single(timedOut.ReportedVehicleObservations,
            item => item.SourceKey == "seattle-kcm-test").FailureCode);
    }

    [Fact]
    public async Task CallerCancellationIsPropagatedWithoutConvertingItToSourceFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new SeattleTestFixtures.CapturingHandler((_, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(token);
        });
        var city = CreateCity(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => city.FetchVehiclesAsync(cancellation.Token));
    }

    [Fact]
    public async Task RawObservationsPrecedeRoute512AndOverlapDisplaySelection()
    {
        var handler = RespondWith(request => request.RequestUri!.Host == "s3.amazonaws.com"
            ? SeattleTestFixtures.ProtobufResponse(SeattleTestFixtures.Feed(
                SeattleTestFixtures.Vehicle("raw-512", "512", timestamp: Now),
                SeattleTestFixtures.Vehicle("route-less-position", "100451", timestamp: Now, omitPosition: true),
                SeattleTestFixtures.Vehicle("shared-vehicle", "100451", timestamp: Now),
                new FeedEntity { Id = "deleted", IsDeleted = true, Vehicle = new VehiclePosition { Vehicle = new VehicleDescriptor { Id = "deleted-id" } } },
                new FeedEntity { Id = "trip-update", TripUpdate = new TripUpdate() },
                SeattleTestFixtures.Vehicle(null, "100451", timestamp: Now)))
            : SeattleTestFixtures.ProtobufResponse(SeattleTestFixtures.Feed(
                SeattleTestFixtures.Vehicle("shared-vehicle", "556", timestamp: Now))));

        var result = await CreateCity(handler, captureReportedVehicles: true).FetchVehiclesAsync(CancellationToken.None);
        var kcm = Assert.Single(result.ReportedVehicleObservations, item => item.SourceKey == "seattle-kcm-test");
        var st = Assert.Single(result.ReportedVehicleObservations, item => item.SourceKey == "seattle-st-test");

        Assert.Equal(4, kcm.RawVehicleRecordCount);
        Assert.Equal(1, kcm.UnidentifiableRecordCount);
        Assert.Equal(new[] { "raw-512", "route-less-position", "shared-vehicle" }, kcm.Identities.Select(item => item.Value));
        Assert.Equal("shared-vehicle", Assert.Single(st.Identities).Value);
        Assert.DoesNotContain(result.Feed.Entities, item => item.Vehicle?.Trip?.RouteId == "512");
        Assert.Single(result.Feed.Entities, item => item.Vehicle?.Vehicle?.Id == "kcm:shared-vehicle");
    }

    [Fact]
    public async Task OverlapUsesKcmAtInclusiveTimeAndDistanceBoundsAndWithholdsContradictoryPairs()
    {
        var exactDistanceLatitude = AtOrBelowFiveKilometers(47.60f, -122.33f);
        var cases = new (string Name, DateTimeOffset KcmTime, DateTimeOffset StTime, float StLatitude, int Expected)[]
        {
            ("inclusive-boundaries", Now, Now.AddSeconds(-120), exactDistanceLatitude, 1),
            ("time-conflict", Now, Now.AddSeconds(-121), 47.60f, 0),
            ("distance-conflict", Now, Now, MathF.BitIncrement(exactDistanceLatitude), 0),
        };

        foreach (var item in cases)
        {
            var handler = RespondWith(request => request.RequestUri!.Host == "s3.amazonaws.com"
                ? SeattleTestFixtures.ProtobufResponse(SeattleTestFixtures.Feed(
                    SeattleTestFixtures.Vehicle("paired", "100451", timestamp: item.KcmTime)))
                : SeattleTestFixtures.ProtobufResponse(SeattleTestFixtures.Feed(
                    SeattleTestFixtures.Vehicle("paired", "556", latitude: item.StLatitude, timestamp: item.StTime))));
            var result = await CreateCity(handler).FetchVehiclesAsync(CancellationToken.None);
            Assert.Equal(item.Expected, result.Feed.Entities.Count);
            if (item.Expected == 1)
            {
                Assert.Equal("kcm:paired", Assert.Single(result.Feed.Entities).Vehicle!.Vehicle!.Id);
                Assert.Equal("556", result.Feed.Entities[0].Vehicle!.Trip!.RouteId);
            }
        }
    }

    [Fact]
    public async Task MissingOrStaleComparisonEvidenceWithholdsPairButDistinctAndStFallbackRemainSourceQualified()
    {
        var scenarios = new (FeedEntity[] Kcm, FeedEntity[] St, string[] ExpectedIds)[]
        {
            ([SeattleTestFixtures.Vehicle("pair", "100451", timestamp: Now, omitPosition: true)],
                [SeattleTestFixtures.Vehicle("pair", "556", timestamp: Now)], []),
            ([SeattleTestFixtures.Vehicle("pair", "100451", timestamp: Now.AddSeconds(-121))],
                [SeattleTestFixtures.Vehicle("pair", "556", timestamp: Now)], []),
            ([SeattleTestFixtures.Vehicle("other-route", "local-route", timestamp: Now)],
                [SeattleTestFixtures.Vehicle("other-route", "102767", timestamp: Now)], ["st:other-route", "kcm:other-route"]),
            ([], [SeattleTestFixtures.Vehicle("st-only", "102767", timestamp: Now)], ["st:st-only"]),
        };

        foreach (var scenario in scenarios)
        {
            var handler = RespondWith(request => SeattleTestFixtures.ProtobufResponse(
                SeattleTestFixtures.Feed(request.RequestUri!.Host == "s3.amazonaws.com" ? scenario.Kcm : scenario.St)));
            var result = await CreateCity(handler).FetchVehiclesAsync(CancellationToken.None);
            Assert.Equal(scenario.ExpectedIds.Order(StringComparer.Ordinal),
                result.Feed.Entities.Select(entity => entity.Vehicle!.Vehicle!.Id).Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task DirectAgencyRouteIdsMapToTheirSupportedCanonicalRouteNames()
    {
        var handler = RespondWith(request => SeattleTestFixtures.ProtobufResponse(
            SeattleTestFixtures.Feed(request.RequestUri!.Host == "s3.amazonaws.com"
                ? SeattleTestFixtures.Vehicle("kcm-556", "100451")
                : SeattleTestFixtures.Vehicle("st-547", "102767"),
                request.RequestUri!.Host == "s3.amazonaws.com"
                    ? SeattleTestFixtures.Vehicle("st-1-line", "100479")
                    : SeattleTestFixtures.Vehicle("st-2-line", "2LINE"))));

        var result = await CreateCity(handler).FetchVehiclesAsync(CancellationToken.None);

        Assert.Equal(new[] { "1 Line", "2 Line", "547", "556" }, result.Feed.Entities
            .Select(entity => entity.Vehicle!.Trip!.RouteId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ReportedObservationsKeepSourceFailureDistinctFromSuccessfulEmpty()
    {
        var handler = RespondWith(request => request.RequestUri!.Host == "s3.amazonaws.com"
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : SeattleTestFixtures.ProtobufResponse(SeattleTestFixtures.Feed()));

        var result = await CreateCity(handler, captureReportedVehicles: true).FetchVehiclesAsync(CancellationToken.None);
        var kcm = Assert.Single(result.ReportedVehicleObservations, observation => observation.SourceKey == "seattle-kcm-test");
        var st = Assert.Single(result.ReportedVehicleObservations, observation => observation.SourceKey == "seattle-st-test");

        Assert.Equal(CityFetchOutcome.PartialFailure, result.Outcome);
        Assert.Equal(ReportedVehicleSourceOutcome.Failure, kcm.Outcome);
        Assert.Equal("source_failure", kcm.FailureCode);
        Assert.Equal(Now.UtcDateTime, kcm.ReceiptUtc);
        Assert.True(st.IsSuccessful);
        Assert.Equal(0, st.RawVehicleRecordCount);
        Assert.Equal(Now.UtcDateTime, st.ReceiptUtc);
    }

    [Fact]
    public async Task MissingDescriptorFallbackIdentityRemainsSourceQualified()
    {
        var handler = RespondWith(request => SeattleTestFixtures.ProtobufResponse(
            SeattleTestFixtures.Feed(SeattleTestFixtures.Vehicle(null,
                request.RequestUri!.Host == "s3.amazonaws.com" ? "local-kcm" : "local-st", timestamp: Now))));

        var result = await CreateCity(handler).FetchVehiclesAsync(CancellationToken.None);

        Assert.Equal(new[] { "kcm:missing-descriptor", "st:missing-descriptor" },
            result.Feed.Entities.Select(entity => entity.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ConfirmedPairKeepsAStableKcmStateIdentityIfTheSourcePrecedenceChangesNextCycle()
    {
        var kcmAvailable = true;
        var handler = RespondWith(request =>
        {
            if (request.RequestUri!.Host == "s3.amazonaws.com")
                return kcmAvailable
                    ? SeattleTestFixtures.ProtobufResponse(SeattleTestFixtures.Feed(
                        SeattleTestFixtures.Vehicle("stable-pair", "100451", timestamp: Now)))
                    : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            return SeattleTestFixtures.ProtobufResponse(SeattleTestFixtures.Feed(
                SeattleTestFixtures.Vehicle("stable-pair", "556", timestamp: Now)));
        });
        var city = CreateCity(handler);
        var firstCycle = await city.FetchVehiclesAsync(CancellationToken.None);
        kcmAvailable = false;
        var secondCycle = await city.FetchVehiclesAsync(CancellationToken.None);

        var firstEntity = Assert.Single(firstCycle.Feed.Entities);
        Assert.Equal("kcm:stable-pair", firstEntity.Vehicle!.Vehicle!.Id);
        Assert.Equal(CityFetchOutcome.PartialFailure, secondCycle.Outcome);
        Assert.Equal("kcm:stable-pair", secondCycle.Feed.Entities.Single().Vehicle!.Vehicle!.Id);
    }

    static SeattleCity CreateCity(SeattleTestFixtures.CapturingHandler handler,
        SeattleTestFixtures.CollectingLogger<SeattleCity>? logger = null, string? key = "present",
        bool captureReportedVehicles = false)
    {
        var configValues = key is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { [SeattleCity.SoundTransitApiKeySetting] = key == "present"
                ? SeattleTestFixtures.SoundTransitKeySentinel : key };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        return new SeattleCity(Config(captureReportedVehicles),
            new SeattleTestFixtures.CapturingHttpClientFactory(_ => handler), configuration,
            logger is null ? NullLogger<SeattleCity>.Instance : logger,
            new SeattleTestFixtures.FixedTimeProvider(Now), captureReportedVehicles);
    }

    static CityConfig Config(bool includePolicies = false) => new()
    {
        Name = CityNames.Seattle,
        GtfsRtUrls = [KcmUrl, StUrl],
        SeattleSoundTransitApiKeyEnvVar = SeattleCity.SoundTransitApiKeySetting,
        ReportedVehicleSources = includePolicies ?
        [
            new ReportedVehicleSourceConfig { SourceKey = "seattle-kcm-test", IdentityNamespace = "fixture-kcm" },
            new ReportedVehicleSourceConfig { SourceKey = "seattle-st-test", IdentityNamespace = "fixture-st" },
        ] : [],
    };

    static SeattleTestFixtures.CapturingHandler RespondWith(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new((request, _) => Task.FromResult(respond(request)));

    static float AtOrBelowFiveKilometers(float baseLatitude, float longitude)
    {
        var candidate = (float)(baseLatitude + 0.045);
        while (HaversineCalculator.DistanceMeters(baseLatitude, longitude, candidate, longitude) > 5_000)
            candidate = MathF.BitDecrement(candidate);
        while (HaversineCalculator.DistanceMeters(baseLatitude, longitude, MathF.BitIncrement(candidate), longitude) <= 5_000)
            candidate = MathF.BitIncrement(candidate);
        return candidate;
    }
}
