using Blazored.LocalStorage;
using ChefKnifeStudios.TransitJazz.Client.Core;
using ChefKnifeStudios.TransitJazz.Client.Shared.Data;
using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Client.Core.Services.EndpointsServices;
using ChefKnifeStudios.TransitJazz.Client.Shared.Components;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Audio;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.JsInterop;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using ChefKnifeStudios.TransitJazz.Shared.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.ViewModels;
using ChefKnifeStudios.TransitJazz.Client.WebApp;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.Enums;
using MatBlazor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using pax.BlazorChartJs;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;

var culture = CultureInfo.GetCultureInfo("en");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;
CultureInfo.CurrentCulture = culture;
CultureInfo.CurrentUICulture = culture;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var appSettings = builder.Configuration.GetSection("AppSettings").Get<AppSettings>();
if (appSettings?.ExternalApis != null)
{
    foreach (var api in appSettings.ExternalApis.Where(a => a.AddHttpClient))
    {
        builder.Services.AddHttpClient(api.Name, client =>
        {
            client.BaseAddress = new Uri(api.BaseUri);
        });
    }
}

builder.Services.AddSingleton<IHttpServiceFactory>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    return new HttpServiceFactory(name => factory.CreateClient(name), loggerFactory);
});

var featureFlags = appSettings?.FeatureFlags ?? new Dictionary<FeatureFlags, bool>();
builder.Services.AddSingleton<IFeatureFlagService>(_ => new FeatureFlagService(featureFlags));

builder.Services.AddSingleton<IEventNotificationService, EventNotificationService>();
builder.Services.AddSingleton<ISignalRNotificationService, SignalRNotificationService>();

builder.Services.AddSingleton<IGtfsEndpointsService, GtfsEndpointsService>();
builder.Services.AddSingleton<ITransitEndpointsService, TransitEndpointsService>();
builder.Services.AddSingleton<IStatsEndpointsService, StatsEndpointsService>();

builder.Services.AddSingleton<IApplicationViewModel, ApplicationViewModel>();

builder.Services.AddSingleton<IAudioPlayerJsInterop, AudioPlayerJsInterop>();

builder.Services.AddScoped<ITriggerPointGenerator, TriggerPointGenerator>();
builder.Services.AddSingleton<ITransitSynthJsInterop, TransitSynthJsInterop>();

builder.Services.AddNotificationPresentation();
builder.Services.AddScoped<IRouteFilterViewModel, RouteFilterViewModel>();

builder.Services.AddSingleton<IRouteBlurbStore, RouteBlurbStore>();

builder.Services.AddMatBlazor();
builder.Services.AddChartJs(options =>
{
    options.ChartJsLocation = $"{builder.HostEnvironment.BaseAddress}vendor/chartjs/4.5.1/chart.umd.min.js";
    options.ChartJsCallbacksModuleLocation = $"{builder.HostEnvironment.BaseAddress}_content/ChefKnifeStudios.TransitJazz.Client.Shared/js/chart-formatters.js";
});

builder.Services.AddMatToaster(new MatToastConfiguration
{
    Position = MatToastPosition.BottomLeft,
    PreventDuplicates = true,
    MaxDisplayedToasts = 1,
    NewestOnTop = true,
    ShowTransitionDuration = 0,
    HideTransitionDuration = 0,
    ShowCloseButton = false,
    ShowProgressBar = false
});

builder.Services.AddBlazoredLocalStorage();

builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<SettingsChangeController>();
builder.Services.AddScoped<CityNavigationController>();
builder.Services.AddSingleton<AudioSettingOutcomeController>();
builder.Services.AddSingleton<IOutsideClickJsInterop, OutsideClickJsInterop>();
builder.Services.AddSingleton<IViewportSizeJsInterop, ViewportSizeJsInterop>();
builder.Services.AddSingleton<IPageVisibilityJsInterop, PageVisibilityJsInterop>();

builder.Services.AddLocalization();
builder.Services.AddScoped<StatsChartText>();

builder.Logging.SetMinimumLevel(LogLevel.Debug);

await builder.Build().RunAsync();
