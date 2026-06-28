using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker;
using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.RailRealtime;
using ChefKnifeStudios.MartaJazz.Shared;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpClient();
builder.Services.AddHttpClient("RouteShapeApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["services:apiservice:https:0"]
        ?? builder.Configuration["WebApi:BaseUrl"]!);
});
builder.Services.AddHttpClient("RailRealtimeApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Cities:0:RailRealtime:BaseUrl"]!);
});
builder.Services.Configure<RailRealtimeOptions>(builder.Configuration.GetSection("Cities:0:RailRealtime"));
builder.Services.AddSingleton<MartaCity>();
builder.Services.AddSingleton<ITransitHubPublisher, SignalRHubPublisher>();

// Build city registry from Cities: config array
var cityConfigs = builder.Configuration.GetSection("Cities").Get<List<CityConfig>>() ?? [];
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
        else
        {
            cities.Add(new GtfsRtCity(cfg, httpFactory, logFactory.CreateLogger<GtfsRtCity>()));
        }
    }

    // Fallback: if no Cities: config exists, run MARTA only (backwards compat)
    if (cities.Count == 0)
        cities.Add(sp.GetRequiredService<MartaCity>());

    return cities;
});

// Logging sidecar pipeline
builder.Services.Configure<LoggingOptions>(builder.Configuration.GetSection("Logging:Telemetry"));
builder.Services.AddSingleton<IEventNotificationService, EventNotificationService>();
builder.Services.AddSingleton<ILoggingService, ParquetLoggingService>();
builder.Services.AddSingleton<LogEventWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LogEventWorker>());

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
