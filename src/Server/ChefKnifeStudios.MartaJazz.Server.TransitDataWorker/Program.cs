using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker;
using ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.MartaJazz.Shared;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpClient();
builder.Services.AddHttpClient("RouteShapeApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["services:apiservice:https:0"]
        ?? builder.Configuration["WebApi:BaseUrl"]!);
});
builder.Services.AddSingleton<ITransitHubPublisher, SignalRHubPublisher>();

// Logging sidecar pipeline
builder.Services.Configure<LoggingOptions>(builder.Configuration.GetSection("Logging:Telemetry"));
builder.Services.AddSingleton<IEventNotificationService, EventNotificationService>();
builder.Services.AddSingleton<ILoggingService, ParquetLoggingService>();
// Register LogEventWorker as singleton so Worker can inject it for health queries,
// then also register it as IHostedService so the host lifecycle wires StartAsync/StopAsync.
builder.Services.AddSingleton<LogEventWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LogEventWorker>());

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
