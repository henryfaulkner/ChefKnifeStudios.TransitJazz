using System.Net;
using System.Net.Http.Headers;
using System.Net.Http;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.EndpointGroups;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class StatsOverlayEndpointTests
{
    [Fact]
    public async Task DisabledEndpointIsAnonymousNoStoreAndReturnsOnlySafeProblemCode()
    {
        await using var host = await StartHost(new StatsOverlayOptions { Enabled = false }, new StaticQuery(new StatsOverlayQueryResult()));
        using var response = await host.Client.PostAsync("/stats/overlay/query", Json("{}"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("\"code\":\"stats_disabled\"", body);
        Assert.DoesNotContain("connection", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LegacyAnonymousPostReturnsAdditiveScheduleMetadataCompressedAndNoStore()
    {
        await using var host = await StartHost(EnabledOptions(), new StaticQuery(EmptyResult()));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/stats/overlay/query")
        {
            Content = Json("{\"scope\":\"all\",\"routeJoinKeys\":[]}")
        };
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

        using var response = await host.Client.SendAsync(request);
        var compressedBytes = await response.Content.ReadAsByteArrayAsync();
        using var compressedStream = new MemoryStream(compressedBytes);
        using var gzip = new GZipStream(compressedStream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var body = await reader.ReadToEndAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("gzip", Assert.Single(response.Content.Headers.ContentEncoding));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(60, root.GetProperty("refreshIntervalMinutes").GetInt32());
        var refreshWindow = root.GetProperty("refreshWindowStartUtc").GetDateTime();
        var nextRefresh = root.GetProperty("nextRefreshAtUtc").GetDateTime();
        Assert.True(nextRefresh > refreshWindow);
    }

    [Fact]
    public async Task RequestBodyAndUnknownFieldsAreRejectedBeforeQuery()
    {
        var query = new StaticQuery(new StatsOverlayQueryResult());
        await using var host = await StartHost(EnabledOptions(), query);
        using var tooLarge = await host.Client.PostAsync("/stats/overlay/query", Json(new string('x', 2048)));
        using var unknown = await host.Client.PostAsync("/stats/overlay/query", Json("{\"scope\":\"all\",\"routeJoinKeys\":[],\"unexpected\":1}"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.Contains("\"code\":\"request_too_large\"", await tooLarge.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("\"code\":\"invalid_request\"", await unknown.Content.ReadAsStringAsync());
        Assert.Equal(0, query.Calls);
    }

    [Fact]
    public async Task UnknownLengthChunkedBodyStopsAtConfiguredByteCap()
    {
        var query = new StaticQuery(new StatsOverlayQueryResult());
        await using var host = await StartHost(EnabledOptions(), query);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/stats/overlay/query")
        {
            Content = new UnknownLengthContent(Encoding.UTF8.GetBytes(new string('x', 2048)))
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.TransferEncodingChunked = true;
        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("\"code\":\"request_too_large\"", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, query.Calls);
    }

    [Fact]
    public async Task Utf8KeyByteCapAnd4096SelectedKeyLimitAreEnforcedBeforeQuery()
    {
        var options = EnabledOptions();
        options.MaximumRequestBytes = 4 * 1024 * 1024;
        var query = new StaticQuery(new StatsOverlayQueryResult());
        await using var host = await StartHost(options, query);

        var exactKey = new string('é', 256); // 512 UTF-8 bytes
        using var exact = await host.Client.PostAsync("/stats/overlay/query", Json(CityRequest([exactKey])));
        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
        Assert.Equal(exactKey, Assert.Single(query.LastInput!.SelectedRouteKeys));

        var tooLongKey = new string('é', 257); // 514 UTF-8 bytes
        using var tooLong = await host.Client.PostAsync("/stats/overlay/query", Json(CityRequest([tooLongKey])));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("\"code\":\"invalid_route_key\"", await tooLong.Content.ReadAsStringAsync());
        Assert.Equal(1, query.Calls);

        var selected4096 = Enumerable.Range(0, 4096).Select(index => $"route-{index:D4}").ToArray();
        using var maximum = await host.Client.PostAsync("/stats/overlay/query", Json(CityRequest(selected4096)));
        Assert.Equal(HttpStatusCode.OK, maximum.StatusCode);
        Assert.Equal(4096, query.LastInput!.SelectedRouteKeys.Length);

        var selected4097 = Enumerable.Range(0, 4097).Select(index => $"route-{index:D4}").ToArray();
        using var overMaximum = await host.Client.PostAsync("/stats/overlay/query", Json(CityRequest(selected4097)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, overMaximum.StatusCode);
        Assert.Contains("\"code\":\"request_too_large\"", await overMaximum.Content.ReadAsStringAsync());
        Assert.Equal(2, query.Calls);
    }

    [Fact]
    public async Task MissingHistoryQueryIsUnavailableAndOversizedCompleteResponseIsRejected()
    {
        await using (var unavailableHost = await StartHost(EnabledOptions(), query: null))
        using (var unavailable = await unavailableHost.Client.PostAsync("/stats/overlay/query", Json("{\"scope\":\"all\",\"routeJoinKeys\":[]}")))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.Contains("\"code\":\"dependency_unavailable\"", await unavailable.Content.ReadAsStringAsync());
        }

        var responseOptions = EnabledOptions();
        const string longLabel = "A permitted route label that makes this complete response exceed the uncompressed response cap without using invalid oversized route keys.";
        const int labelLength = 2200;
        var tooLargeResult = new StatsOverlayQueryResult
        {
            RouteOptions = Enumerable.Range(0, 4096).Select(index => new StatsOverlayRouteOption
            {
                CitySlug = "atlanta",
                RouteJoinKey = $"{new string('k', 496)}{index:D4}",
                Label = longLabel + new string('L', labelLength),
                CategoryKey = "bus",
                MetadataTimestampUtc = new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc)
            }).ToList()
        };
        await using var responseHost = await StartHost(responseOptions, new FixedResultQuery(tooLargeResult));
        using var tooLarge = await responseHost.Client.PostAsync("/stats/overlay/query", Json("{\"scope\":\"city\",\"citySlug\":\"atlanta\",\"routeJoinKeys\":[]}"));

        Assert.Equal((HttpStatusCode)422, tooLarge.StatusCode);
        Assert.Contains("\"code\":\"response_too_large\"", await tooLarge.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OversizedScopeAndSafeResponseNullsUseStableWireCodes()
    {
        var query = new StaticQuery(new StatsOverlayQueryResult { ScopeTooLarge = true });
        await using var host = await StartHost(EnabledOptions(), query);
        using var tooLarge = await host.Client.PostAsync("/stats/overlay/query", Json("{\"scope\":\"all\",\"routeJoinKeys\":[]}"));
        Assert.Equal((HttpStatusCode)422, tooLarge.StatusCode);
        Assert.Contains("\"code\":\"scope_too_large\"", await tooLarge.Content.ReadAsStringAsync());

        query.Result = EmptyResult();
        using var success = await host.Client.PostAsync("/stats/overlay/query", Json("{\"scope\":\"all\",\"routeJoinKeys\":[]}"));
        var payload = await success.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.Equal("no-store", success.Headers.CacheControl?.ToString());
        Assert.Contains("\"citySlug\":null", payload);
        Assert.Contains("\"kilometers\":null", payload);
        Assert.Contains("\"state\":\"unavailable\"", payload);
        Assert.Equal(2, query.Calls);
    }

    [Fact]
    public async Task UnsafeAggregateIntegerIsRejectedBeforeWritingSuccess()
    {
        var unsafeResult = new StatsOverlayQueryResult
        {
            CityHours =
            [
                new StatsOverlayCityHour
                {
                    CitySlug = "atlanta",
                    Window = "today",
                    HourStartUtc = new DateTime(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc),
                    DistanceKilometers = 1m,
                    RouteVehicleHours = 9_007_199_254_740_992L,
                    ActiveClockHours = 1,
                    ActivitySampleNumerator = 1,
                    SampleDenominator = 1
                }
            ]
        };
        await using var host = await StartHost(EnabledOptions(), new FixedResultQuery(unsafeResult));
        using var response = await host.Client.PostAsync("/stats/overlay/query", Json("{\"scope\":\"all\",\"routeJoinKeys\":[]}"));

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Contains("\"code\":\"scope_too_large\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SpoofedForwardedHeadersCannotSelectRateLimitBucket()
    {
        await using var host = await StartHost(EnabledOptions(), new StaticQuery(EmptyResult()), tokenLimit: 1);
        using var first = new HttpRequestMessage(HttpMethod.Post, "/stats/overlay/query")
        {
            Content = Json("{\"scope\":\"all\",\"routeJoinKeys\":[]}")
        };
        first.Headers.TryAddWithoutValidation("X-Forwarded-For", "198.51.100.10");
        using var firstResponse = await host.Client.SendAsync(first);

        using var second = new HttpRequestMessage(HttpMethod.Post, "/stats/overlay/query")
        {
            Content = Json("{\"scope\":\"all\",\"routeJoinKeys\":[]}")
        };
        second.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.20");
        using var secondResponse = await host.Client.SendAsync(second);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondResponse.StatusCode);
        Assert.Equal("no-store", secondResponse.Headers.CacheControl?.ToString());
        Assert.NotNull(secondResponse.Headers.RetryAfter);
        Assert.Equal("application/problem+json", secondResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"code\":\"rate_limited\"", await secondResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CanceledEndpointWaiterLeavesSafeSharedFillAvailableToAnotherRequest()
    {
        var query = new BlockingEndpointQuery();
        await using var host = await StartHost(EnabledOptions(), query);
        using var cancellation = new CancellationTokenSource();
        using var canceledRequest = new HttpRequestMessage(HttpMethod.Post, "/stats/overlay/query")
        {
            Content = Json("{\"scope\":\"all\",\"routeJoinKeys\":[]}")
        };
        var canceledResponse = host.Client.SendAsync(canceledRequest, cancellation.Token);
        await query.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var liveResponse = host.Client.PostAsync("/stats/overlay/query", Json("{\"scope\":\"all\",\"routeJoinKeys\":[]}"));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledResponse);
        Assert.False(query.Canceled.Task.IsCompleted);

        query.Release.TrySetResult();
        using var response = await liveResponse;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(1, query.Calls);
        Assert.False(query.Canceled.Task.IsCompleted);
    }

    [Fact]
    public async Task HostStoppingCancelsTheEndpointSharedRead()
    {
        var query = new BlockingEndpointQuery();
        await using var host = await StartHost(EnabledOptions(), query);
        var request = host.Client.PostAsync("/stats/overlay/query", Json("{\"scope\":\"all\",\"routeJoinKeys\":[]}"));
        await query.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await host.StopAsync();
        query.Release.TrySetResult();
        Assert.True(query.Token.IsCancellationRequested);
        try { _ = await request; }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException) { }
    }

    static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    static StatsOverlayOptions EnabledOptions() => new()
    {
        Enabled = true,
        MaximumRequestBytes = 1024,
        Cities =
        [
            new StatsOverlayCityOptions
            {
                CitySlug = "atlanta", DisplayNameKey = "city.atlanta", TimeZoneId = "UTC", HealthyCadenceLimitSeconds = 30
            }
        ]
    };

    static StatsOverlayQueryResult EmptyResult() => new()
    {
        CohortHours = [],
        RouteOptions = [],
        RouteMeasures = [],
        CategoryHours = [],
        CategoryTotals = [],
        CityHours = [],
        Categories = []
    };

    static async Task<TestHostState> StartHost(StatsOverlayOptions options, IStatsOverlayQuery? query, int tokenLimit = 10)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddResponseCompression(compression =>
        {
            compression.EnableForHttps = true;
            compression.Providers.Add<GzipCompressionProvider>();
        });
        builder.Services.Configure<GzipCompressionProviderOptions>(compression =>
            compression.Level = CompressionLevel.Fastest);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<TimeProvider>(new TestTimeProvider());
        builder.Services.AddSingleton<StatsOverlayService>(services =>
            new StatsOverlayService(query, options, services.GetRequiredService<TimeProvider>(),
                hostStoppingToken: services.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>().ApplicationStopping));
        builder.Services.AddStatsOverlayRateLimiting(tokenLimit, TimeSpan.FromDays(1));
        var app = builder.Build();
        app.UseResponseCompression();
        app.UseRateLimiter();
        app.MapStatsEndpoints();
        await app.StartAsync();
        return new TestHostState(app, app.GetTestClient());
    }

    sealed class TestHostState(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public Task StopAsync() => app.StopAsync();
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    sealed class StaticQuery(StatsOverlayQueryResult result) : IStatsOverlayQuery
    {
        public StatsOverlayQueryResult Result { get; set; } = result;
        public int Calls { get; private set; }
        public StatsOverlayQueryInput? LastInput { get; private set; }
        public Task<StatsOverlayQueryResult> ReadAsync(StatsOverlayQueryInput input, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls++;
            LastInput = input;
            if (Result.ScopeTooLarge) return Task.FromResult(Result);
            return Task.FromResult(EmptyResultFor(input));
        }

        static StatsOverlayQueryResult EmptyResultFor(StatsOverlayQueryInput input)
        {
            var result = EmptyResult();
            result.CohortHours = input.Hours.Select(hour => new StatsOverlayCohortHour
            {
                CitySlug = hour.CitySlug, HourStartUtc = hour.HourStartUtc,
                IsToday = hour.IsToday, IsLast24 = hour.IsLast24,
                IsEligible = false, Reason = "missing_cohort"
            }).ToList();
            return result;
        }
    }

    sealed class FixedResultQuery(StatsOverlayQueryResult result) : IStatsOverlayQuery
    {
        public Task<StatsOverlayQueryResult> ReadAsync(StatsOverlayQueryInput input, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(result);
    }

    sealed class BlockingEndpointQuery : IStatsOverlayQuery
    {
        int calls;
        public int Calls => Volatile.Read(ref calls);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }

        public async Task<StatsOverlayQueryResult> ReadAsync(StatsOverlayQueryInput input, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            Token = cancellationToken;
            using var registration = cancellationToken.Register(() => Canceled.TrySetResult());
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            var result = EmptyResult();
            result.CohortHours = input.Hours.Select(hour => new StatsOverlayCohortHour
            {
                CitySlug = hour.CitySlug,
                HourStartUtc = hour.HourStartUtc,
                IsToday = hour.IsToday,
                IsLast24 = hour.IsLast24,
                IsEligible = false,
                Reason = "missing_cohort"
            }).ToList();
            return result;
        }
    }

    sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    static string CityRequest(string[] keys) => JsonSerializer.Serialize(new
    {
        scope = "city",
        citySlug = "atlanta",
        routeJoinKeys = keys
    });

    sealed class TestTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
    }
}
