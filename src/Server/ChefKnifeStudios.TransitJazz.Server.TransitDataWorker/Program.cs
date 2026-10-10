using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.RailRealtime;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Subway;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Services;
using Microsoft.Extensions.Options;
using System.Collections.Immutable;

var builder = Host.CreateApplicationBuilder(args);

var workerOptions = builder.Configuration.GetSection(WorkerOptions.SectionName).Get<WorkerOptions>() ?? new WorkerOptions();
workerOptions.Validate();
var categoryInsightsOptions = CityCategoryInsightsOptions.FromConfiguration(builder.Configuration);
categoryInsightsOptions.Validate(workerOptions.CycleIntervalSeconds);
if (categoryInsightsOptions.Enabled)
    throw new InvalidOperationException("Standalone TransitDataWorker has no category statistics database sink; keep CityCategoryInsights.Enabled false.");
var routeHoursEnabled = builder.Configuration.GetValue<bool>("HistoricalStatistics:Enabled");
var routeHoursDryRun = builder.Configuration.GetValue("HistoricalStatistics:DryRun", true);
if (routeHoursEnabled && !routeHoursDryRun)
    throw new InvalidOperationException("Standalone TransitDataWorker has no route-hour database sink; persistent route-hour capture must run in the WebAPI host.");
builder.Services.AddSingleton(workerOptions);

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
var routeHourOptions = builder.Configuration.GetSection("HistoricalStatistics:RouteHours").Get<RouteHourHistoryOptions>()
    ?? new RouteHourHistoryOptions();
var selectedRouteCities = builder.Configuration.GetSection("HistoricalStatistics:Cities").Get<List<string>>() ?? [];
if (selectedRouteCities.Count == 0)
    selectedRouteCities = cityConfigs.Count == 0 ? [CityNames.Marta] : cityConfigs.Select(city => city.Name).ToList();
routeHourOptions.Validate(workerOptions.CycleIntervalSeconds, selectedRouteCities);
if (routeHoursEnabled)
{
    var runtime = new RouteHourCaptureRuntimeOptions(RouteHourCaptureMode.DryRun,
        selectedRouteCities.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase), routeHourOptions);
    builder.Services.AddSingleton(runtime);
    builder.Services.AddSingleton(routeHourOptions);
    builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
    builder.Services.AddSingleton<IRouteHourStatisticsSink>(sp =>
        new DryRunRouteHourStatisticsSink(sp.GetRequiredService<ILogger<DryRunRouteHourStatisticsSink>>(), routeHourOptions.MaxRoutesPerCityHour));
    builder.Services.AddSingleton<RouteHourStatisticsCapture>();
    builder.Services.AddHostedService<RouteHourCaptureLifecycleService>();
}

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
