using System.Net;
using System.Text;
using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class StatsTransportTests
{
    [Fact]
    public async Task TypedStatsResponsePreservesExplicitNullEvidenceAndDisposesResponse()
    {
        var content = new TrackingContent(MinimalStatsResponse);
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });

        var response = await service.PostWithMetadataAsync<object, StatsOverlayResponse>("/stats", new { scope = "all" });

        Assert.True(response.Result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.ProblemCode);
        Assert.Null(response.Result.Value.Selection.CitySlug);
        Assert.Null(response.Result.Value.TodayDistance.Kilometers);
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task SafeProblemCodeAndNumericRetryAfterAreReturnedWithoutRawBody()
    {
        var service = CreateService(_ =>
        {
            var response = Problem(HttpStatusCode.TooManyRequests, "rate_limited", "private database details");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
            return response;
        });

        var result = await service.PostWithMetadataAsync<object, StatsOverlayResponse>("/stats", new { scope = "all" });

        Assert.Equal(HttpStatusCode.TooManyRequests, result.StatusCode);
        Assert.Equal("rate_limited", result.ProblemCode);
        Assert.Equal(TimeSpan.FromSeconds(3), result.RetryAfter);
        Assert.False(result.Result.IsSuccess);
        Assert.DoesNotContain(result.Result.Errors, error => error.Contains("private database details", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SuccessfulNullJsonBodyIsReportedAsSafeInvalidResponse()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        });

        var response = await service.PostWithMetadataAsync<object, StatsOverlayResponse>("/stats", new { scope = "all" });

        Assert.False(response.Result.IsSuccess);
        Assert.Equal("invalid_response", response.ProblemCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("invalid_response", response.Result.Errors);
    }

    [Fact]
    public async Task DateRetryAfterIsConvertedToDelayAndUntrustedCodesAreDiscarded()
    {
        var service = CreateService(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("{\"code\":\"private_sql_error\",\"detail\":\"secret\"}", Encoding.UTF8, "application/problem+json")
            };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(20));
            return response;
        });

        var result = await service.PostWithMetadataAsync<object, StatsOverlayResponse>("/stats", new { scope = "all" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.Null(result.ProblemCode);
        Assert.InRange(result.RetryAfter!.Value.TotalSeconds, 0, 20);
        Assert.DoesNotContain(result.Result.Errors, error => error.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("invalid_scope")]
    [InlineData("invalid_city")]
    [InlineData("invalid_route_key")]
    public async Task ContractRequestValidationCodesArePreserved(string code)
    {
        var service = CreateService(_ => Problem(HttpStatusCode.BadRequest, code, "safe test detail"));

        var result = await service.PostWithMetadataAsync<object, StatsOverlayResponse>("/stats", new { scope = "all" });

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(code, result.ProblemCode);
    }

    [Fact]
    public async Task MalformedProblemBodyIsSafeAndResponseIsDisposed()
    {
        var content = new TrackingContent("not-json");
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = content });

        var result = await service.PostWithMetadataAsync<object, StatsOverlayResponse>("/stats", new { scope = "all" });

        Assert.Equal(HttpStatusCode.BadGateway, result.StatusCode);
        Assert.Null(result.ProblemCode);
        Assert.True(content.Disposed);
        Assert.DoesNotContain(result.Result.Errors, error => error.Contains("not-json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CallerCancellationPropagatesAndExistingTypedPostStillWorks()
    {
        var blocked = CreateService(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            blocked.PostWithMetadataAsync<object, StatsOverlayResponse>("/stats", new { scope = "all" }, cancellation.Token));

        var ordinary = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"value\":7}", Encoding.UTF8, "application/json")
        });
        var existing = await ordinary.PostAsync<object, Probe>("/existing", new { value = 7 });
        Assert.True(existing.IsSuccess);
        Assert.Equal(7, existing.Value.Value);
    }

    static HttpService CreateService(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) =>
        CreateService((request, _) => Task.FromResult(responseFactory(request)));

    static HttpService CreateService(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) =>
        new(new HttpClient(new DelegateHandler(responseFactory)) { BaseAddress = new Uri("https://unit.test") },
            NullLogger<HttpService>.Instance);

    static HttpResponseMessage Problem(HttpStatusCode status, string code, string detail) => new(status)
    {
        Content = new StringContent($"{{\"code\":\"{code}\",\"detail\":\"{detail}\"}}", Encoding.UTF8, "application/problem+json")
    };

    const string MinimalStatsResponse = """
        {
          "selection":{"scope":"all","citySlug":null,"routeJoinKeys":[]},
          "cities":[],"routeOptions":[],
          "todayDistance":{"window":"today","kilometers":null,"coverage":{"state":"unavailable","exclusions":[]}},
          "last24Distance":{"window":"last24CompletedHours","kilometers":null,"coverage":{"state":"unavailable","exclusions":[]}},
          "categoryShares":[],"bubbles":[],
          "timeline":{"seriesKind":"unavailable","series":[]},"warnings":[]
        }
        """;

    sealed record Probe(int Value);

    sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    sealed class TrackingContent(string value) : StringContent(value, Encoding.UTF8, "application/json")
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
