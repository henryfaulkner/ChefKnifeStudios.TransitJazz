using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Metrics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.RailRealtime;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Subway;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.EndpointGroups;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Health;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Interfaces;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Repositories;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.SignalR;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Services;
using ChefKnifeStudios.TransitJazz.Server.Data;
using ChefKnifeStudios.TransitJazz.Server.Data.Statistics;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Scalar.AspNetCore;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = false;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    options.UseUtcTimestamp = true;
});

var workerOptions = builder.Configuration.GetSection(WorkerOptions.SectionName).Get<WorkerOptions>() ?? new WorkerOptions();
workerOptions.Validate();
var metricsOptions = builder.Configuration.GetSection(MetricsOptions.SectionName).Get<MetricsOptions>() ?? new MetricsOptions();
metricsOptions.Validate(workerOptions.CycleIntervalSeconds, builder.Environment.IsProduction());
if (metricsOptions.LocalPrometheusEnabled && !builder.Environment.IsDevelopment())
    throw new OptionsValidationException(MetricsOptions.SectionName, typeof(MetricsOptions), ["LocalPrometheusEnabled is development-only."]);
var statsOverlayOptions = builder.Configuration.GetSection(StatsOverlayOptions.SectionName).Get<StatsOverlayOptions>() ?? new StatsOverlayOptions();

builder.Services.AddSingleton(workerOptions);
builder.Services.AddSingleton(metricsOptions);
builder.Services.AddSingleton(statsOverlayOptions);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddMetrics();

var instanceId = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}";
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(metricsOptions.ServiceName)
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment.name"] = metricsOptions.Environment,
            ["service.instance.id"] = instanceId,
        }))
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(WorkerMetricsReporter.MeterName);
        metrics.AddMeter(StatsOverlayTelemetry.MeterName);
        if (metricsOptions.Enabled && metricsOptions.LocalPrometheusEnabled)
        {
            metrics.AddPrometheusHttpListener(options =>
            {
                // Prometheus runs in Docker and reaches this development-only listener
                // through host.docker.internal rather than localhost.
                options.Host = "host.docker.internal";
                options.Port = 9464;
                options.ScrapeEndpointPath = "/metrics";
                // service.instance.id stays resource-only; local metric series do not carry it.
                options.ResourceConstantLabels = static _ => false;
            });
        }
        else if (metricsOptions.Enabled)
        {
            metrics.AddOtlpExporter((options, readerOptions) =>
            {
                options.Endpoint = new Uri(metricsOptions.OtlpMetricsEndpoint!, UriKind.Absolute);
                options.Protocol = OtlpExportProtocol.HttpProtobuf;
                options.Headers = $"Authorization={metricsOptions.OtlpAuthorization}";
                options.ExportProcessorType = ExportProcessorType.Batch;
                readerOptions.PeriodicExportingMetricReaderOptions = new()
                {
                    ExportIntervalMilliseconds = metricsOptions.ExportIntervalMilliseconds,
                };
            });
        }
    });

builder.Services.AddSingleton<IWorkerMetricsReporter>(sp => metricsOptions.Enabled
    ? new WorkerMetricsReporter(sp.GetRequiredService<IMeterFactory>(), sp.GetService<MeterProvider>())
    : new NullWorkerMetricsReporter());

builder.Services.AddProblemDetails();

builder.Services.AddOpenApi();

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<StatsOverlayService>(sp => new StatsOverlayService(
    sp.GetService<IStatsOverlayQuery>(),
    sp.GetRequiredService<StatsOverlayOptions>(),
    sp.GetRequiredService<TimeProvider>(),
    sp.GetService<StatsOverlayReportedVehiclePolicyCatalog>()?.Catalog));

builder.Services.AddStatsOverlayRateLimiting();

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
CityConfig.ValidateUniqueCityNames(cityConfigs);
var reportedVehicleConfiguration = ReportedVehicleCaptureConfiguration.FromConfiguration(
    builder.Configuration, cityConfigs, ReportedVehicleCaptureHost.WebApi);

var historicalStatisticsOptions = builder.Configuration.GetSection(HistoricalStatisticsOptions.SectionName).Get<HistoricalStatisticsOptions>()
    ?? new HistoricalStatisticsOptions();
historicalStatisticsOptions.ResolveCitySelection(cityConfigs.Select(city => city.Name), CityNames.Marta);
historicalStatisticsOptions.Validate();
var routeHourHistoryOptions = historicalStatisticsOptions.RouteHours ?? new RouteHourHistoryOptions();
historicalStatisticsOptions.RouteHours = routeHourHistoryOptions;
routeHourHistoryOptions.Validate(workerOptions.CycleIntervalSeconds, historicalStatisticsOptions.Cities);
statsOverlayOptions.PopulateConfiguredCityPolicy(cityConfigs, routeHourHistoryOptions,
    requireLocalizedDisplayNames: statsOverlayOptions.Enabled);
statsOverlayOptions.Validate(requireCities: statsOverlayOptions.Enabled);
if (statsOverlayOptions.ReportedVehiclesEnabled)
{
    var reportingCatalog = ReportedVehicleCatalogBuilder.Build(cityConfigs.Where(city =>
        statsOverlayOptions.Cities.Any(policy => string.Equals(policy.CitySlug, city.Name, StringComparison.OrdinalIgnoreCase))));
    builder.Services.AddSingleton(new StatsOverlayReportedVehiclePolicyCatalog(reportingCatalog));
}
IEnumerable<string> configuredCityNamesSource = cityConfigs.Count == 0
    ? new[] { CityNames.Marta }
    : cityConfigs.Select(city => city.Name);
var configuredCityNames = configuredCityNamesSource.ToHashSet(StringComparer.OrdinalIgnoreCase);
if (historicalStatisticsOptions.Enabled)
{
    var unavailableCities = historicalStatisticsOptions.Cities.Where(city => !configuredCityNames.Contains(city)).ToArray();
    if (unavailableCities.Length > 0)
        throw new InvalidOperationException($"Historical statistics cities must be configured in Cities: {string.Join(", ", unavailableCities)}.");
    foreach (var city in cityConfigs.Where(city => historicalStatisticsOptions.Cities.Contains(city.Name, StringComparer.OrdinalIgnoreCase)))
    {
        if (string.IsNullOrWhiteSpace(city.TimeZoneId))
            throw new InvalidOperationException($"Enabled route-hour history requires an IANA time zone for {city.Name}.");
        try { CityCategoryInsightsOptions.ValidateIanaTimeZoneId(city.TimeZoneId); }
        catch (Exception exception) when (exception is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException($"Enabled route-hour history requires a valid IANA time zone for {city.Name}.", exception);
        }
    }
}
var routeHourRuntime = historicalStatisticsOptions.CreateRouteHourRuntimeOptions();
builder.Services.AddSingleton(historicalStatisticsOptions);
builder.Services.AddSingleton<IOptions<HistoricalStatisticsOptions>>(Options.Create(historicalStatisticsOptions));

var transitJazzConnectionString = builder.Configuration.GetConnectionString("TransitJazzDB");
if (!string.IsNullOrWhiteSpace(transitJazzConnectionString))
{
    builder.Services.RegisterDataServices(transitJazzConnectionString);
    builder.Services.AddHttpClient<IHistoricalStatisticsSource, GrafanaPrometheusStatisticsSource>();
    builder.Services.AddSingleton<ICityMinuteStatisticsStore, CityMinuteStatisticsStore>();
    builder.Services.AddHostedService<HistoricalStatisticsCollector>();
}
else if (historicalStatisticsOptions.Enabled)
{
    throw new InvalidOperationException("Historical statistics collection requires ConnectionStrings:TransitJazzDB.");
}
ReportedVehicleCaptureServiceCollectionExtensions.ValidatePersistencePrerequisites(
    reportedVehicleConfiguration.Runtime, transitJazzConnectionString);
builder.Services.AddReportedVehicleCaptureRuntime(reportedVehicleConfiguration);

var cityCategoryInsightsOptions = CityCategoryInsightsOptions.FromConfiguration(builder.Configuration);
cityCategoryInsightsOptions.Validate(workerOptions.CycleIntervalSeconds);
if (cityCategoryInsightsOptions.Enabled)
{
    if (string.IsNullOrWhiteSpace(transitJazzConnectionString))
        throw new InvalidOperationException("City category statistics capture requires ConnectionStrings:TransitJazzDB.");
    var configuredCities = cityConfigs.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var missingCities = cityCategoryInsightsOptions.DisabledCities.Concat(cityCategoryInsightsOptions.CityMaxObservationGapSeconds.Keys)
        .Where(x => !configuredCities.Contains(x)).ToArray();
    if (missingCities.Length > 0)
        throw new InvalidOperationException("City category statistics DisabledCities and cadence overrides must name configured cities.");
    foreach (var city in cityConfigs.Where(x => cityCategoryInsightsOptions.IsEnabledFor(x.Name)))
    {
        if (string.IsNullOrWhiteSpace(city.TimeZoneId))
            throw new InvalidOperationException("Enabled city category statistics require a configured IANA time zone.");
        CityCategoryInsightsOptions.ValidateIanaTimeZoneId(city.TimeZoneId);
    }

    builder.Services.AddSingleton<CityCategoryStatisticsStore>();
    builder.Services.AddSingleton<CategoryStatisticsWriter>();
    builder.Services.AddSingleton<ICategoryStatisticsSink>(sp => sp.GetRequiredService<CategoryStatisticsWriter>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<CategoryStatisticsWriter>());
}
else
{
    builder.Services.AddSingleton<ICategoryStatisticsSink>(NullCategoryStatisticsSink.Instance);
}
builder.Services.AddCategoryStatisticsCaptureRuntime(cityCategoryInsightsOptions);
RouteHourCaptureServiceCollectionExtensions.ValidatePersistencePrerequisites(routeHourRuntime, transitJazzConnectionString);
builder.Services.AddRouteHourStatisticsCaptureRuntime(routeHourRuntime);

var nymtaConfig = cityConfigs.FirstOrDefault(c => string.Equals(c.Name, CityNames.Nymta, StringComparison.OrdinalIgnoreCase));
var martaConfig = cityConfigs.FirstOrDefault(c => string.Equals(c.Name, CityNames.Marta, StringComparison.OrdinalIgnoreCase));
builder.Services.PostConfigure<RailRealtimeOptions>(options =>
{
    options.ReportedVehicleCaptureEnabled = reportedVehicleConfiguration.Runtime.IsEnabledFor(CityNames.Marta);
    options.BusReportedVehicleSource = martaConfig?.ReportedVehicleSources.SingleOrDefault();
    options.RailReportedVehicleSource = martaConfig?.RailRealtime?.ReportedVehicleSource;
});
builder.Services.Configure<SubwaySynthesisOptions>(o =>
{
    o.GtfsRtUrls = nymtaConfig?.GtfsRtUrls ?? [];
    o.ReportedVehicleCaptureEnabled = reportedVehicleConfiguration.Runtime.IsEnabledFor(CityNames.Nymta);
    o.ReportedVehicleSources = nymtaConfig?.ReportedVehicleSources ?? [];
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
        ReportedVehicleSources = nymtaConfig?.BusReportedVehicleSources ?? [],
    };
    return new NymtaCity(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<IOptions<SubwaySynthesisOptions>>(),
        busConfig,
        sp.GetRequiredService<ILogger<NymtaCity>>(),
        sp.GetRequiredService<ILogger<GtfsRtCity>>(),
        sp.GetRequiredService<TimeProvider>(),
        sp.GetRequiredService<ReportedVehicleBufferBudget>(),
        reportedVehicleConfiguration.Options);
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
        else if (string.Equals(cfg.Name, CityNames.Seattle, StringComparison.OrdinalIgnoreCase))
        {
            cities.Add(new SeattleCity(cfg, httpFactory, builder.Configuration,
                logFactory.CreateLogger<SeattleCity>(), sp.GetRequiredService<TimeProvider>(),
                reportedVehicleConfiguration.Runtime.IsEnabledFor(cfg.Name),
                sp.GetRequiredService<ReportedVehicleBufferBudget>(), reportedVehicleConfiguration.Options));
        }
        else
        {
            cities.Add(new GtfsRtCity(cfg, httpFactory, logFactory.CreateLogger<GtfsRtCity>(),
                sp.GetRequiredService<TimeProvider>(), reportedVehicleConfiguration.Runtime.IsEnabledFor(cfg.Name),
                sp.GetRequiredService<ReportedVehicleBufferBudget>(), reportedVehicleConfiguration.Options));
        }
    }
    if (cities.Count == 0)
        cities.Add(sp.GetRequiredService<MartaCity>());
    return cities;
});

builder.Services.AddSingleton(typeof(IKeyValueRepository<>), typeof(InMemoryKeyValueRepository<>));
builder.Services.AddSingleton<IRouteShapeResponseCache, RouteShapeResponseCache>();
builder.Services.AddSingleton<InMemoryRouteShapeSource>();
builder.Services.AddSingleton<IRouteShapeSource>(sp => sp.GetRequiredService<InMemoryRouteShapeSource>());
builder.Services.AddHttpClient();
builder.Services.AddHttpClient(SeattleCity.SoundTransitHttpClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
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
builder.Services.AddHttpClient("GtfsStaticApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["services:apiservice:https:0"]
        ?? builder.Configuration["WebApi:BaseUrl"]!);
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
builder.Services.AddSingleton<Worker>();
builder.Services.AddSingleton<IRouteIndexReadiness>(sp => sp.GetRequiredService<Worker>());
builder.Services.AddHealthChecks()
    .AddCheck<RouteIndexHealthCheck>("route-index", tags: ["ready"]);

// Registered before Worker so StopAsync force-flushes the final worker-cycle measurement.
builder.Services.AddHostedService<WorkerMetricsLifecycleService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Worker>());

var app = builder.Build();

app.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics.Startup")
    .LogInformation("Historical statistics startup: enabled={Enabled}, dryRun={DryRun}, databaseConfigured={DatabaseConfigured}, sourceEndpointConfigured={SourceEndpointConfigured}, readerAuthorizationConfigured={ReaderAuthorizationConfigured}",
        historicalStatisticsOptions.Enabled, historicalStatisticsOptions.DryRun,
        !string.IsNullOrWhiteSpace(transitJazzConnectionString),
        !string.IsNullOrWhiteSpace(historicalStatisticsOptions.SourceEndpoint),
        !string.IsNullOrWhiteSpace(historicalStatisticsOptions.ReaderAuthorization));

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
app.UseRateLimiter();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
}).AllowAnonymous();

app.MapHub<TransitHub>("/hubs/transit").AllowAnonymous();
app.MapHub<WorkerTransitHub>("/hubs/worker-transit")
    .AllowAnonymous();

app.MapTestEndpoints()
    .MapGtfsEndpoints()
    .MapTransitEndpoints()
    .MapStatsEndpoints();

app.Run();
