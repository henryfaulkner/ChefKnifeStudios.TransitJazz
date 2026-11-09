using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Linq;
using ChefKnifeStudios.TransitJazz.Server.Data.ClientExperience;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.ClientExperience;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.EndpointGroups;
using ChefKnifeStudios.TransitJazz.Shared.ClientExperience;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.ClientExperience;

public sealed class ClientExperienceEndpointTests
{
    [Fact]
    public async Task ValidAndInvalidMembersAreAnonymousNoStoreAndNeverEchoPayload()
    {
        await using var host = await StartHost(enabled: true);
        var valid = Event("city_map_start");
        var invalid = Event("city_map_start") with { Journey = "visitor-secret-route" };
        using var response = await host.Client.PostAsync("/api/client-experience/batch", Json(new ClientExperienceBatch
        {
            SchemaVersion = 1,
            Events = [valid, invalid]
        }));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(valid.EventId.ToString(), body);
        Assert.Contains("invalid_journey", body);
        Assert.DoesNotContain("visitor-secret-route", body, StringComparison.Ordinal);
        Assert.Equal(1, host.Store.LastAppend?.Count);
    }

    [Fact]
    public async Task UnknownLengthBodyIsCappedWhileReading()
    {
        await using var host = await StartHost(enabled: true, maximumBytes: 1024);
        using var content = new UnknownLengthContent(Encoding.UTF8.GetBytes(new string('x', 2048)));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/client-experience/batch") { Content = content };
        request.Headers.TransferEncodingChunked = true;

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.DoesNotContain("xxxxxxxx", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledEndpointReturnsSafeRetriableResponse()
    {
        await using var host = await StartHost(enabled: false);
        using var response = await host.Client.PostAsync("/api/client-experience/batch", Json(new ClientExperienceBatch
        {
            SchemaVersion = 1,
            Events = [Event("city_map_start")]
        }));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("intake_disabled", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, host.Store.Calls);
    }

    [Fact]
    public async Task IntakeCanBeEnabledBeforeClientCapture()
    {
        await using var host = await StartHost(enabled: true, captureEnabled: false);
        using var response = await host.Client.PostAsync("/api/client-experience/batch", Json(new ClientExperienceBatch
        {
            SchemaVersion = 1,
            Events = [Event("city_map_start")]
        }));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("capture_disabled", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, host.Store.Calls);
    }

    [Fact]
    public async Task SyntheticIntakeRejectsMissingCredentialWithoutPersisting()
    {
        await using var host = await StartHost(enabled: true, captureEnabled: false, syntheticEnabled: true);
        using var response = await host.Client.PostAsync("/api/client-experience/synthetic/batch", Json(new ClientExperienceBatch
        {
            SchemaVersion = 1,
            Events = [Event("city_map_start")]
        }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("synthetic_unauthorized", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, host.Store.Calls);
    }

    [Fact]
    public async Task SyntheticIntakeUsesRunnerCredentialAndServerAssignedEnvironment()
    {
        await using var host = await StartHost(enabled: true, captureEnabled: false, syntheticEnabled: true);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/client-experience/synthetic/batch")
        {
            Content = Json(new ClientExperienceBatch { SchemaVersion = 1, Events = [Event("city_map_start")] })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestSyntheticToken);

        using var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("synthetic", host.Store.LastEnvironment);
        Assert.Equal("synthetic", host.Store.LastAppend!.Single().Event.Environment);
        Assert.DoesNotContain(TestSyntheticToken, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NormalIntakeRejectsBrowserSuppliedSyntheticEnvironment()
    {
        await using var host = await StartHost(enabled: true);
        using var response = await host.Client.PostAsync("/api/client-experience/batch",
            Json("{\"schemaVersion\":1,\"events\":[],\"environment\":\"synthetic\"}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, host.Store.Calls);
    }

    [Fact]
    public async Task UnknownEnvelopeMembersAreRejectedWithoutEcho()
    {
        await using var host = await StartHost(enabled: true);
        using var response = await host.Client.PostAsync("/api/client-experience/batch",
            Json("{\"schemaVersion\":1,\"events\":[],\"visitorSecret\":\"do-not-echo\"}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("do-not-echo", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourceRateLimitReturns429AndRetryAfter()
    {
        await using var host = await StartHost(enabled: true, burst: 1);
        using var first = await host.Client.PostAsync("/api/client-experience/batch", Json(new ClientExperienceBatch
        {
            SchemaVersion = 1,
            Events = [Event("city_map_start")]
        }));
        using var second = await host.Client.PostAsync("/api/client-experience/batch", Json(new ClientExperienceBatch
        {
            SchemaVersion = 1,
            Events = [Event("attempt_started")]
        }));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.NotNull(second.Headers.RetryAfter);
        Assert.Equal("no-store", second.Headers.CacheControl?.ToString());
    }

    static ClientExperienceEvent Event(string journey) => new()
    {
        SchemaVersion = 1,
        MeasurementVersion = 1,
        EventKind = "attempt_started",
        EventId = Guid.NewGuid(),
        PageSessionId = Guid.NewGuid(),
        CityActivationId = Guid.NewGuid(),
        Sequence = 2,
        OccurredAtUtc = DateTimeOffset.UtcNow,
        PageElapsedMs = 500,
        CitySlug = "atlanta",
        ClientRelease = "test-release",
        AttemptId = Guid.NewGuid(),
        Journey = journey,
        Eligible = true,
        DeadlineForegroundMs = 60_000
    };

    static StringContent Json(ClientExperienceBatch batch)
        => Json(JsonSerializer.Serialize(batch, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

    static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");

    const string TestSyntheticToken = "local-test-runner-token-that-is-longer-than-32-characters";

    static async Task<TestHostState> StartHost(bool enabled, int maximumBytes = 65_536, int burst = 10,
        bool? captureEnabled = null, bool syntheticEnabled = false)
    {
        var options = new ClientExperienceOptions
        {
            Enabled = enabled,
            CaptureEnabled = captureEnabled ?? enabled,
            SyntheticIntakeEnabled = syntheticEnabled,
            SyntheticIntakeToken = syntheticEnabled ? TestSyntheticToken : string.Empty,
            MaximumRequestBytes = maximumBytes,
            MaximumEventsPerBatch = 20,
            PerSourceBurst = burst,
            PerSourcePerMinute = Math.Max(30, burst),
            GlobalPerSecond = 100,
            MaximumConcurrentWrites = 4,
            RequestTimeoutSeconds = 10,
            AllowedCitySlugs = ["atlanta"]
        };
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<FakeStore>();
        builder.Services.AddSingleton<IClientExperienceEventStore>(sp => sp.GetRequiredService<FakeStore>());
        builder.Services.AddStatsOverlayRateLimiting();
        builder.Services.AddClientExperienceRateLimiting(options);
        var app = builder.Build();
        app.UseRateLimiter();
        app.MapClientExperienceEndpoints();
        await app.StartAsync();
        return new(app, app.GetTestClient(), app.Services.GetRequiredService<FakeStore>());
    }

    sealed class FakeStore : IClientExperienceEventStore
    {
        public int Calls { get; private set; }
        public List<ClientExperienceStoredEvent>? LastAppend { get; private set; }
        public string? LastEnvironment { get; private set; }
        public Task<ClientExperienceAppendResult> AppendAsync(IReadOnlyList<ClientExperienceStoredEvent> events,
            string environment, DateTimeOffset receivedAtUtc, CancellationToken cancellationToken)
        {
            Calls++;
            LastAppend = events.ToList();
            LastEnvironment = environment;
            return Task.FromResult(new ClientExperienceAppendResult(events.Select(x => x.Event.EventId).Distinct().ToArray(), events.Count, 0));
        }
    }

    sealed class TestHostState(WebApplication app, HttpClient client, FakeStore store) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public FakeStore Store { get; } = store;
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            => stream.WriteAsync(bytes).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
