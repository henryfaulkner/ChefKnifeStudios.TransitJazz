using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker;
using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.RailRealtime;
using ChefKnifeStudios.MartaJazz.Server.WebAPI.EndpointGroups;
using ChefKnifeStudios.MartaJazz.Server.WebAPI.GtfsStatic;
using ChefKnifeStudios.MartaJazz.Server.WebAPI.Interfaces;
using ChefKnifeStudios.MartaJazz.Server.WebAPI.Repositories;
using ChefKnifeStudios.MartaJazz.Server.WebAPI.SignalR;
using ChefKnifeStudios.MartaJazz.Shared;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Identity.Web;
using Scalar.AspNetCore;
using System;
using System.Collections.Generic;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddProblemDetails();

builder.Services.AddOpenApi();

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
        options.MaximumReceiveMessageSize = 1024 * 1024; // 1MB
    })
    .AddJsonProtocol(options => JsonSettings.ApplyTo(options.PayloadSerializerOptions));

builder.Services.ConfigureHttpJsonOptions(static options =>
{
    JsonSettings.ApplyTo(options.SerializerOptions);
});

builder.Services.AddHttpClient("RailRealtimeApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Marta:RailRealtime:BaseUrl"]!);
});
builder.Services.Configure<RailRealtimeOptions>(builder.Configuration.GetSection("Marta:RailRealtime"));
builder.Services.AddSingleton<IRailRealtimeAdapter, RailRealtimeAdapter>();

builder.Services.AddSingleton(typeof(IKeyValueRepository<>), typeof(InMemoryKeyValueRepository<>));
builder.Services.AddHttpClient();
builder.Services.AddHostedService<GtfsStaticLoader>();
builder.Services.AddSingleton<IFeatureFlagService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var flagsSection = config.GetSection("AppSettings:FeatureFlags");
    var flags = new Dictionary<ChefKnifeStudios.MartaJazz.Shared.Enums.FeatureFlags, bool>();
    if (flagsSection.Exists())
    {
        foreach (var child in flagsSection.GetChildren())
        {
            if (Enum.TryParse<ChefKnifeStudios.MartaJazz.Shared.Enums.FeatureFlags>(child.Key, out var flag))
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

//app.UseAuthentication();
//app.UseAuthorization();

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
    //.RequireAuthorization("TransitDataPublisher");

app.MapTestEndpoints()
    .MapGtfsEndpoints()
    .MapTransitEndpoints();

app.MapDefaultEndpoints();

app.Run();