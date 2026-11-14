using System.Net;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.EndpointGroups;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Hosting;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.SignalR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Hosting;

public sealed class ServerHostingTests
{
    [Theory]
    [InlineData("https://transitjazz.com", true)]
    [InlineData("https://martajazz.com", true)]
    [InlineData("https://unapproved.example", false)]
    public async Task PreflightGrantsCredentialsOnlyToConfiguredOrigins(string origin, bool allowed)
    {
        await using var app = await StartHost("Production", "127.0.0.1");
        using var request = new HttpRequestMessage(HttpMethod.Options, "/probe");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type,x-signalr-user-agent");
        using var response = await app.GetTestClient().SendAsync(request);
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        if (allowed)
        {
            Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
        }
    }

    [Theory]
    [InlineData("127.0.0.1", "198.51.100.10", "https")]
    [InlineData("::1", "198.51.100.10", "https")]
    [InlineData("::ffff:127.0.0.1", "198.51.100.10", "https")]
    [InlineData("127.0.0.2", "127.0.0.2", "http")]
    [InlineData("203.0.113.20", "203.0.113.20", "http")]
    public async Task OnlyExactLoopbackProxyCanForwardOneHop(string peer, string expectedIp, string expectedScheme)
    {
        await using var app = await StartHost("Production", peer);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/probe");
        request.Headers.Add("X-Forwarded-For", "192.0.2.99, 198.51.100.10");
        request.Headers.Add("X-Forwarded-Proto", "http, https");
        using var response = await app.GetTestClient().SendAsync(request);
        Assert.Equal($"{expectedIp}|{expectedScheme}", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("Production", HttpStatusCode.NotFound)]
    [InlineData("Development", HttpStatusCode.OK)]
    public async Task BroadcastTestRouteIsAvailableOnlyInDevelopment(string environment, HttpStatusCode expected)
    {
        await using var app = await StartHost(environment, "127.0.0.1");
        using var response = await app.GetTestClient().GetAsync("/test/signalr");
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task ProductionRetainsLoopbackWorkerHubAndShutdownBudget()
    {
        await using var app = await StartHost("Production", "127.0.0.1");
        using var response = await app.GetTestClient().PostAsync("/hubs/worker-transit/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("connectionToken", await response.Content.ReadAsStringAsync());
        Assert.Equal(TimeSpan.FromSeconds(90), app.Services.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout);
    }

    private static async Task<WebApplication> StartHost(string environment, string peer)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "https://transitjazz.com",
            ["Cors:AllowedOrigins:1"] = "https://martajazz.com",
        });
        builder.Services.AddServerHosting(builder.Configuration);
        builder.Services.AddSignalR().AddMessagePackProtocol();
        var app = builder.Build();
        app.Use((context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            return next(context);
        });
        app.UseForwardedHeaders();
        app.UseRouting();
        app.UseCors("Default");
        app.MapGet("/probe", (HttpContext context) => $"{context.Connection.RemoteIpAddress}|{context.Request.Scheme}");
        app.MapPost("/probe", () => Results.Ok());
        app.MapTestEndpoints();
        app.MapHub<WorkerTransitHub>("/hubs/worker-transit");
        await app.StartAsync();
        return app;
    }
}
