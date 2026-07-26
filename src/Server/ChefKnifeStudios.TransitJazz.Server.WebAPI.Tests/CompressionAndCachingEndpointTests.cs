using Ardalis.Result;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.EndpointGroups;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests;

// P1-S1/P1-S2 (051 US2): in-process greybox tests via TestHost — the only place middleware
// order (compression) and the full header contract (ETag/Cache-Control/304/503) are provable
// end-to-end without a deployed environment. Wires only response compression + GtfsEndpoints
// against fakes, deliberately NOT the full Program (which starts real hosted services / hits
// real network endpoints) — test-plan's "the only escalation above component level."
public class CompressionAndCachingEndpointTests : IAsyncLifetime
{
    IHost _host = null!;
    HttpClient _client = null!;
    FakeKeyValueRepository _repo = null!;
    RouteShapeResponseCache _cache = null!;

    public async Task InitializeAsync()
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddResponseCompression(options =>
                    {
                        options.EnableForHttps = true;
                        options.Providers.Add<BrotliCompressionProvider>();
                        options.Providers.Add<GzipCompressionProvider>();
                    });
                    services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

                    _repo = new FakeKeyValueRepository();
                    _cache = new RouteShapeResponseCache();
                    services.AddSingleton<IKeyValueRepository<string>>(_repo);
                    services.AddSingleton<IRouteShapeResponseCache>(_cache);
                    services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(
                        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
                });
                webBuilder.Configure(app =>
                {
                    app.UseResponseCompression();
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapGtfsEndpoints());
                });
            });

        _host = await builder.StartAsync();
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);

    [Fact]
    public async Task Get_WithBrotliAcceptEncoding_ReturnsBrotliEncoded_BodyDecompressesIntact()
    {
        await _repo.SetAsync(GtfsStaticLoader.ReadyKey, "ready", CancellationToken.None);
        _cache.Populate(RouteShapeResponseCacheKey.AllShapes("*"), Bytes("[{\"routeId\":\"95\"}]"));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/gtfs/routes/shapes");
        request.Headers.Add("Accept-Encoding", "br");
        using var response = await _client.SendAsync(request);

        Assert.Equal("br", response.Content.Headers.ContentEncoding.ToString());
        var raw = await response.Content.ReadAsByteArrayAsync();
        using var decompressed = new MemoryStream();
        using (var brotli = new BrotliStream(new MemoryStream(raw), CompressionMode.Decompress))
            await brotli.CopyToAsync(decompressed);
        Assert.Equal("[{\"routeId\":\"95\"}]", Encoding.UTF8.GetString(decompressed.ToArray()));
    }

    [Fact]
    public async Task Get_WithGzipAcceptEncoding_ReturnsGzipEncoded()
    {
        await _repo.SetAsync(GtfsStaticLoader.ReadyKey, "ready", CancellationToken.None);
        _cache.Populate(RouteShapeResponseCacheKey.AllShapes("*"), Bytes("[{\"routeId\":\"95\"}]"));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/gtfs/routes/shapes");
        request.Headers.Add("Accept-Encoding", "gzip");
        using var response = await _client.SendAsync(request);

        Assert.Equal("gzip", response.Content.Headers.ContentEncoding.ToString());
    }

    [Fact]
    public async Task Get_WithNoAcceptEncoding_ReturnsIdentity_NoContentEncodingHeader()
    {
        await _repo.SetAsync(GtfsStaticLoader.ReadyKey, "ready", CancellationToken.None);
        _cache.Populate(RouteShapeResponseCacheKey.AllShapes("*"), Bytes("[{\"routeId\":\"95\"}]"));

        using var response = await _client.GetAsync("/gtfs/routes/shapes");

        Assert.Empty(response.Content.Headers.ContentEncoding);
        Assert.Equal("[{\"routeId\":\"95\"}]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_ColdRequest_Returns200_WithETagAndCacheControl()
    {
        await _repo.SetAsync(GtfsStaticLoader.ReadyKey, "ready", CancellationToken.None);
        _cache.Populate(RouteShapeResponseCacheKey.AllShapes("*"), Bytes("[{\"routeId\":\"95\"}]"));

        using var response = await _client.GetAsync("/gtfs/routes/shapes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        Assert.Equal("public, max-age=3600", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Get_ReplayedIfNoneMatch_Returns304_EmptyBody()
    {
        await _repo.SetAsync(GtfsStaticLoader.ReadyKey, "ready", CancellationToken.None);
        _cache.Populate(RouteShapeResponseCacheKey.AllShapes("*"), Bytes("[{\"routeId\":\"95\"}]"));

        using var first = await _client.GetAsync("/gtfs/routes/shapes");
        var etag = first.Headers.ETag!.ToString();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/gtfs/routes/shapes");
        request.Headers.Add("If-None-Match", etag);
        using var second = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Empty(await second.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Get_LoaderNotReady_Returns503()
    {
        using var response = await _client.GetAsync("/gtfs/routes/shapes");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    sealed class FakeKeyValueRepository : IKeyValueRepository<string>
    {
        readonly Dictionary<string, string> _storage = new();

        public Task<Result<string>> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_storage.TryGetValue(key, out var v) ? Result<string>.Success(v) : Result<string>.NotFound());

        public Task<Result<Dictionary<string, string>>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult(Result<Dictionary<string, string>>.Success(new Dictionary<string, string>(_storage)));

        public Task<Result> SetAsync(string key, string value, CancellationToken ct = default)
        {
            _storage[key] = value;
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteAsync(string key, CancellationToken ct = default)
        {
            _storage.Remove(key);
            return Task.FromResult(Result.Success());
        }
    }
}
