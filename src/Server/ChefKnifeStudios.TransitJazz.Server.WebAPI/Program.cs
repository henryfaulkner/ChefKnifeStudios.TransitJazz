using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.RailRealtime;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Subway;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.EndpointGroups;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Interfaces;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Repositories;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.SignalR;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();

builder.Services.AddOpenApi();

builder.Services.AddMemoryCache();

// Response compression (research D3): first-ever registration in src/Server. The
// GetAllRouteShapes/GetAllRoutes catalog responses are megabytes of coordinate-dense JSON on
// every client startup; Brotli/Gzip over HTTPS is the highest-ROI zero-risk change in the
// package. EnableForHttps defaults to false and production is HTTPS-only. WebSockets (the
// SignalR/MessagePack path) bypass this middleware entirely — unaffected.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
    {
        policy.WithOrigins("https://localhost:7150", "https://localhost:5186", "https://localhost:7333", "https://localhost:52832", "http://localhost:52833")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddSignalR(options =>
    {
        options.EnableDetailedErrors = true;
        // 5 MB: NYMTA's full-fleet batch (~5k+ vehicles) rides a raised ceiling even after
        // field-thinning (feature 040). MessagePack (below) shrinks the wire further, but the
        // ceiling stays generous for peak fleets + the next large city.
        options.MaximumReceiveMessageSize = 5 * 1024 * 1024; // 5MB
    })
    // MessagePack for both hubs (worker→WorkerTransitHub and TransitHub→browser). Polymorphic
    // payloads are handled by [Union]/[Key] on the Shared contracts. Every SignalR peer must
    // speak MessagePack — there is no JSON fallback registered, so a JSON client is rejected.
    .AddMessagePackProtocol();

builder.Services.ConfigureHttpJsonOptions(static options =>
{
    JsonSettings.ApplyTo(options.SerializerOptions);
});

builder.Services.AddHttpClient("RailRealtimeApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Cities:0:RailRealtime:BaseUrl"]!);
});
builder.Services.Configure<RailRealtimeOptions>(builder.Configuration.GetSection("Cities:0:RailRealtime"));
builder.Services.AddSingleton<MartaCity>();

var cityConfigs = builder.Configuration.GetSection("Cities").Get<List<CityConfig>>() ?? [];

var nymtaConfig = cityConfigs.FirstOrDefault(c => string.Equals(c.Name, CityNames.Nymta, StringComparison.OrdinalIgnoreCase));
builder.Services.Configure<SubwaySynthesisOptions>(o =>
{
    o.GtfsRtUrls = nymtaConfig?.GtfsRtUrls ?? [];
});
builder.Services.AddSingleton(sp =>
{
    // NYC rail + bus are one city (nymta): GtfsRtUrls feed the subway synthesizer above,
    // BusGtfsRtUrls feed NymtaCity's internal GtfsRtCity for real-GPS bus positions.
    var busConfig = new CityConfig
    {
        Name = CityNames.Nymta,
        GtfsRtUrls = nymtaConfig?.BusGtfsRtUrls ?? [],
        ApiKeyEnvVar = nymtaConfig?.ApiKeyEnvVar,
        ApiKeyQueryParam = nymtaConfig?.ApiKeyQueryParam ?? "api_key",
        RouteIdNormalization = nymtaConfig?.RouteIdNormalization ?? [],
    };
    return new NymtaCity(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<IOptions<SubwaySynthesisOptions>>(),
        busConfig,
        sp.GetRequiredService<ILogger<NymtaCity>>(),
        sp.GetRequiredService<ILogger<GtfsRtCity>>());
});

builder.Services.AddSingleton<IEnumerable<ITransitCity>>(sp =>
{
    var cities = new List<ITransitCity>();
    var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
    var logFactory = sp.GetRequiredService<ILoggerFactory>();
    foreach (var cfg in cityConfigs)
    {
        if (string.Equals(cfg.Name, CityNames.Marta, StringComparison.OrdinalIgnoreCase))
        {
            cities.Add(sp.GetRequiredService<MartaCity>());
        }
        else if (string.Equals(cfg.Name, CityNames.Nymta, StringComparison.OrdinalIgnoreCase))
        {
            cities.Add(sp.GetRequiredService<NymtaCity>());
        }
        else
        {
            cities.Add(new GtfsRtCity(cfg, httpFactory, logFactory.CreateLogger<GtfsRtCity>()));
        }
    }
    if (cities.Count == 0)
        cities.Add(sp.GetRequiredService<MartaCity>());
    return cities;
});

builder.Services.AddSingleton(typeof(IKeyValueRepository<>), typeof(InMemoryKeyValueRepository<>));
builder.Services.AddSingleton<IRouteShapeResponseCache, RouteShapeResponseCache>();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<GtfsStaticLoader>();
builder.Services.AddSingleton<IFeatureFlagService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var flagsSection = config.GetSection("AppSettings:FeatureFlags");
    var flags = new Dictionary<ChefKnifeStudios.TransitJazz.Shared.Enums.FeatureFlags, bool>();
    if (flagsSection.Exists())
    {
        foreach (var child in flagsSection.GetChildren())
        {
            if (Enum.TryParse<ChefKnifeStudios.TransitJazz.Shared.Enums.FeatureFlags>(child.Key, out var flag))
            {
                flags[flag] = bool.Parse(child.Value ?? "false");
            }
        }
    }
    return new FeatureFlagService(flags);
});

builder.Services.AddSingleton<ILastBatchCache, LastBatchCache>();
builder.Services.AddSingleton<ITransitHubPublisher, SignalRHubPublisher>();
builder.Services.AddHttpClient("RouteShapeApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["services:apiservice:https:0"]
        ?? builder.Configuration["WebApi:BaseUrl"]!);
});

builder.Services.AddSingleton<ITriggerPointGenerator, TriggerPointGenerator>();

// Logging sidecar pipeline
builder.Services.Configure<LoggingOptions>(builder.Configuration.GetSection("Logging:Telemetry"));
builder.Services.AddSingleton<IEventNotificationService, EventNotificationService>();
builder.Services.AddSingleton<ILoggingService, ParquetLoggingService>();
// Register LogEventWorker as singleton so Worker can inject it for health queries,
// then also register it as IHostedService so the host lifecycle wires StartAsync/StopAsync.
builder.Services.AddSingleton<LogEventWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LogEventWorker>());

builder.Services.AddHostedService<Worker>();

var app = builder.Build();

app.UseExceptionHandler();

app.UseResponseCompression();

app.MapOpenApi().AllowAnonymous();

app.MapScalarApiReference(options =>
{
    options.Title = "TransitJazz API";
    options.Theme = ScalarTheme.Solarized;
    options.Layout = ScalarLayout.Classic;
    options.DarkMode = true;
    options.HiddenClients = true;
    options.DefaultHttpClient = new(ScalarTarget.JavaScript, ScalarClient.Axios);
});

app.UseCors("Default");

app.MapHub<TransitHub>("/hubs/transit").AllowAnonymous();
app.MapHub<WorkerTransitHub>("/hubs/worker-transit")
    .AllowAnonymous();

app.MapTestEndpoints()
    .MapGtfsEndpoints()
    .MapTransitEndpoints()
    .MapTelemetryEndpoints();

app.Run();