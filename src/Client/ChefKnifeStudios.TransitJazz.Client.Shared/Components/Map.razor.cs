using ChefKnifeStudios.TransitJazz.Client.Shared.Models;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components;

public partial class Map : ComponentBase
{
    public string ElementId { get; } = $"cks-map-{Guid.NewGuid()}".ToLower();

    [Inject] public IJSRuntime JsRuntime { get; set; } = null!;
    [Inject] public IConfiguration Configuration { get; set; } = null!;
    [Inject] public ISettingsService SettingsService { get; set; } = null!;
    [Inject] public ILogger<Map> Logger { get; set; } = null!;

    [Parameter]
    public CameraOptions CameraOptions { get; set; }
        = new() { Center = new Position(0, 0), Zoom = 1 };

    [Parameter] public EventCallback<Map> OnMapReady { get; set; }
    [Parameter] public EventCallback<Map> OnMapBodyClicked { get; set; }
    [Parameter] public EventCallback<(Map Map, string VehicleId)> OnBusMarkerClicked { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await CreateMapAsync();
        }
    }

    [JSInvokable("notifyMapReadyAsync")]
    public async Task NotifyMapReadyAsync()
    {
        await OnMapReady.InvokeAsync(this);
    }

    [JSInvokable("mapBodyClickedAsync")]
    public async Task MapBodyClickedAsync()
    {
        await OnMapBodyClicked.InvokeAsync(this);
    }

    [JSInvokable("BusMarkerClickedAsync")]
    public async Task BusMarkerClickedAsync(string vehicleId)
    {
        await OnBusMarkerClicked.InvokeAsync((this, vehicleId));
    }

    [JSInvokable("getMapSettings")]
    public Task<object> GetMapSettings()
    {
        var longitude = CameraOptions.Center.Longitude;
        var latitude = CameraOptions.Center.Latitude;
        var language = CultureInfo.DefaultThreadCurrentCulture?.Name ?? "en-US";

        var apiKey = Configuration.GetValue<string>("MapTiler:ApiKey") ?? string.Empty;

        var settings = SettingsService.GetSettings();
        var shade    = settings.IsDarkModeEnabled ? "Dark" : "Light";
        var on       = settings.IsStreetMapEnabled ? "On" : "Off";
        var styleKey = $"MapTiler:StyleUrls:{shade}{on}";
        var styleUrl = Configuration.GetValue<string>(styleKey)
                       ?? Configuration.GetValue<string>("MapTiler:StyleUrl")
                       ?? string.Empty;

        return Task.FromResult<object>(new
        {
            maptilerKey = apiKey,
            styleUrl,
            center = new[] { longitude, latitude },
            zoom = CameraOptions.Zoom,
            language
        });
    }
}
