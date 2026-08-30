using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.RailRealtime;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Subway;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Services;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = false;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    options.UseUtcTimestamp = true;
});

builder.Services.AddHttpClient();
builder.Services.AddHttpClient("GtfsStaticApi", client =>
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

    // Fallback: if no Cities: config exists, run MARTA only (backwards compat)
    if (cities.Count == 0)
        cities.Add(sp.GetRequiredService<MartaCity>());

    return cities;
});

builder.Services.AddSingleton<ITriggerPointGenerator, TriggerPointGenerator>();

// Structured logging pipeline
builder.Services.Configure<StructuredLoggingOptions>(builder.Configuration.GetSection(StructuredLoggingOptions.SectionName));
builder.Services.PostConfigure<StructuredLoggingOptions>(options =>
    options.DeploymentRevision ??= Environment.GetEnvironmentVariable("CONTAINER_APP_REVISION"));
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<StructuredLoggingOptions>>().Value;
    return new StructuredEventPolicy(TimeProvider.System, options.ReminderInterval);
});
builder.Services.AddSingleton<IWorkerStructuredEventLogger, StructuredEventEmitter>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
