using System.Net;
using System.Text;
using System.Collections.Concurrent;
using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Client.Core.Services.EndpointsServices;
using ChefKnifeStudios.TransitJazz.Client.Shared.Components.Stats;
using ChefKnifeStudios.TransitJazz.Shared;
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

        var response = await service.PostWithMetadataAsync<object, StatsOverlayResponse>("/stats", new { scope = "all" },
            new Dictionary<string, string> { ["X-TransitJazz-Stats-Contract"] = StatsOverlayVocabulary.ContractVersionV2 });

        Assert.True(response.Result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.ProblemCode);
        Assert.Null(response.Result.Value.Selection.CitySlug);
        Assert.Null(response.Result.Value.TodayDistance.Kilometers);
        Assert.False(response.Result.Value.RefreshIntervalMinutesPresent);
        Assert.False(response.Result.Value.RefreshWindowStartUtcPresent);
        Assert.False(response.Result.Value.NextRefreshAtUtcPresent);
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task TypedStatsResponsePreservesAdditiveRefreshScheduleMetadata()
    {
        const string window = "2026-01-02T12:00:00Z";
        const string next = "2026-01-02T12:15:00Z";
        var json = MinimalStatsResponse.Replace("\"selection\":", $"\"refreshIntervalMinutes\":15,\"refreshWindowStartUtc\":\"{window}\",\"nextRefreshAtUtc\":\"{next}\",\"selection\":", StringComparison.Ordinal);
        var content = new TrackingContent(json);
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });

        var response = await service.PostWithMetadataAsync<object, StatsOverlayResponse>("/stats", new { scope = "all" });

        Assert.True(response.Result.IsSuccess);
        Assert.True(response.Result.Value.RefreshIntervalMinutesPresent);
        Assert.True(response.Result.Value.RefreshWindowStartUtcPresent);
        Assert.True(response.Result.Value.NextRefreshAtUtcPresent);
        Assert.Equal(15, response.Result.Value.RefreshIntervalMinutes);
        Assert.Equal(DateTime.Parse(window, null, System.Globalization.DateTimeStyles.AdjustToUniversal), response.Result.Value.RefreshWindowStartUtc);
        Assert.Equal(DateTime.Parse(next, null, System.Globalization.DateTimeStyles.AdjustToUniversal), response.Result.Value.NextRefreshAtUtc);
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task FractionalRefreshScheduleJsonBecomesSafeInvalidResponseAndIsDisposed()
    {
        var json = MinimalStatsResponse.Replace("\"selection\":", "\"refreshIntervalMinutes\":7.5,\"selection\":", StringComparison.Ordinal);
        var content = new TrackingContent(json);
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });

        var response = await service.PostWithMetadataAsync<object, StatsOverlayResponse>("/stats", new { scope = "all" });

        Assert.False(response.Result.IsSuccess);
        Assert.Equal("invalid_response", response.ProblemCode);
        Assert.DoesNotContain("7.5", string.Join(";", response.Result.Errors), StringComparison.Ordinal);
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

        var result = await service.PostWithMetadataAsync<object, StatsOverlayResponse>("/stats", new { scope = "all" },
            new Dictionary<string, string> { ["X-TransitJazz-Stats-Contract"] = StatsOverlayVocabulary.ContractVersionV2 });

        Assert.Equal(HttpStatusCode.TooManyRequests, result.StatusCode);
        Assert.Equal("rate_limited", result.ProblemCode);
        Assert.Equal(TimeSpan.FromSeconds(3), result.RetryAfter);
        Assert.False(result.Result.IsSuccess);
        Assert.DoesNotContain(result.Result.Errors, error => error.Contains("private database details", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExplicitStatsContractHeaderIsRequestLocalForConcurrentCalls()
    {
        var observed = new ConcurrentDictionary<string, (string? Contract, string? Shared)>();
        using var client = new HttpClient(new DelegateHandler((request, _) =>
        {
            observed[request.RequestUri!.AbsolutePath] = (
                request.Headers.TryGetValues("X-TransitJazz-Stats-Contract", out var contract) ? contract.Single() : null,
                request.Headers.TryGetValues("X-Shared-Context", out var shared) ? shared.Single() : null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(MinimalStatsResponse, Encoding.UTF8, "application/json")
            });
        })) { BaseAddress = new Uri("https://unit.test") };
        client.DefaultRequestHeaders.Add("X-Shared-Context", "unchanged");
        var service = new HttpService(client, NullLogger<HttpService>.Instance);

        await Task.WhenAll(
            service.PostWithMetadataAsync<object, StatsOverlayResponse>("/stats-v2", new { },
                new Dictionary<string, string> { ["X-TransitJazz-Stats-Contract"] = StatsOverlayVocabulary.ContractVersionV2 }),
            service.PostWithMetadataAsync<object, StatsOverlayResponse>("/unrelated", new { }));

        Assert.Equal(StatsOverlayVocabulary.ContractVersionV2, observed["/stats-v2"].Contract);
        Assert.Null(observed["/unrelated"].Contract);
        Assert.Equal("unchanged", observed["/stats-v2"].Shared);
        Assert.Equal("unchanged", observed["/unrelated"].Shared);
        Assert.DoesNotContain("X-TransitJazz-Stats-Contract", client.DefaultRequestHeaders.Select(header => header.Key));
    }

    [Fact]
    public async Task StatsEndpointContractOverloadAddsOnlyTheRequestedVersion()
    {
        string? requested = null;
        using var client = new HttpClient(new DelegateHandler((request, _) =>
        {
            requested = request.Headers.TryGetValues("X-TransitJazz-Stats-Contract", out var values) ? values.Single() : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(MinimalStatsResponse, Encoding.UTF8, "application/json")
            });
        })) { BaseAddress = new Uri("https://unit.test") };
        var endpoints = new StatsEndpointsService(new HttpServiceFactory(_ => client, NullLoggerFactory.Instance));

        var response = await endpoints.QueryAsync(new StatsOverlayRequest { Scope = "all" }, StatsOverlayVocabulary.ContractVersionV2);

        Assert.True(response.Result.IsSuccess);
        Assert.Equal(StatsOverlayVocabulary.ContractVersionV2, requested);
    }

    [Fact]
    public async Task UnsupportedContractProblemCodeIsSafeAndLocalizedAsInvalidRequest()
    {
        var service = CreateService(_ => Problem(HttpStatusCode.BadRequest, "unsupported_stats_contract", "private implementation detail"));

        var result = await service.PostWithMetadataAsync<object, StatsOverlayResponse>("/stats", new { },
            new Dictionary<string, string> { ["X-TransitJazz-Stats-Contract"] = StatsOverlayVocabulary.ContractVersionV2 });

        Assert.Equal("unsupported_stats_contract", result.ProblemCode);
        Assert.Equal("StatsInvalidRequestError", StatsOverlayErrorResources.ResourceKey(result.ProblemCode));
        Assert.DoesNotContain(result.Result.Errors, error => error.Contains("private implementation detail", StringComparison.OrdinalIgnoreCase));
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
            blocked.PostWithMetadataAsync<object, StatsOverlayResponse>("/stats", new { scope = "all" },
                new Dictionary<string, string> { ["X-TransitJazz-Stats-Contract"] = StatsOverlayVocabulary.ContractVersionV2 }, cancellation.Token));

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
