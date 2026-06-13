using Blazored.LocalStorage;
using ChefKnifeStudios.MartaJazz.Client.Core;
using ChefKnifeStudios.MartaJazz.Client.Shared.Data;
using ChefKnifeStudios.MartaJazz.Client.Core.Services;
using ChefKnifeStudios.MartaJazz.Client.Core.Services.EndpointsServices;
using ChefKnifeStudios.MartaJazz.Client.Shared.Components;
using ChefKnifeStudios.MartaJazz.Client.Shared.Services;
using ChefKnifeStudios.MartaJazz.Client.Shared.Services.JsInterop;
using ChefKnifeStudios.MartaJazz.Client.Shared.ViewModels;
using ChefKnifeStudios.MartaJazz.Client.WebApp;
using ChefKnifeStudios.MartaJazz.Shared;
using ChefKnifeStudios.MartaJazz.Shared.Enums;
using MatBlazor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

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
    return new HttpServiceFactory(name => factory.CreateClient(name));
});

var featureFlags = appSettings?.FeatureFlags ?? new Dictionary<FeatureFlags, bool>();
builder.Services.AddSingleton<IFeatureFlagService>(_ => new FeatureFlagService(featureFlags));

builder.Services.AddSingleton<IEventNotificationService, EventNotificationService>();
builder.Services.AddSingleton<ISignalRNotificationService, SignalRNotificationService>();

builder.Services.AddSingleton<IGtfsEndpointsService, GtfsEndpointsService>();

builder.Services.AddSingleton<IApplicationViewModel, ApplicationViewModel>();

builder.Services.AddSingleton<IAudioPlayerJsInterop, AudioPlayerJsInterop>();

builder.Services.AddScoped<ITriggerPointGenerator, TriggerPointGenerator>();
builder.Services.AddSingleton<ICheckpointTrackerJsInterop, CheckpointTrackerJsInterop>();
builder.Services.AddSingleton<ITransitSynthJsInterop, TransitSynthJsInterop>();

builder.Services.AddScoped<IToastService, ToastService>();
builder.Services.AddScoped<IRouteFilterViewModel, RouteFilterViewModel>();

builder.Services.AddSingleton<IRouteBlurbStore, RouteBlurbStore>();

builder.Services.AddMatBlazor();

builder.Services.AddMatToaster(new MatToastConfiguration
{
    Position = MatToastPosition.BottomRight,
    PreventDuplicates = true,
    NewestOnTop = true,
    ShowCloseButton = true,
    ShowProgressBar = true
});

builder.Services.AddBlazoredLocalStorage();

builder.Services.AddTransient<ISettingsService, SettingsService>();
builder.Services.AddSingleton<IOutsideClickJsInterop, OutsideClickJsInterop>();

builder.Services.AddLocalization();

builder.Logging.SetMinimumLevel(LogLevel.Debug);

await builder.Build().RunAsync();
