using ChefKnifeStudios.TransitJazz.Client.Shared.Models;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using ChefKnifeStudios.TransitJazz.Client.Shared.ViewModels;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components;

public partial class Map : ComponentBase, IAsyncDisposable
{
    const bool ConditionalMapNotificationsEnabled = false;
    public string ElementId { get; } = $"cks-map-{Guid.NewGuid()}".ToLower();

    [Inject] public IJSRuntime JsRuntime { get; set; } = null!;
    [Inject] public IConfiguration Configuration { get; set; } = null!;
    [Inject] public ISettingsService SettingsService { get; set; } = null!;
    [Inject] public ILogger<Map> Logger { get; set; } = null!;
    [Inject] public NavigationManager NavigationManager { get; set; } = null!;
    [Inject] public CoreCapabilityNotificationOwner CapabilityNotificationOwner { get; set; } = null!;
    [Inject] public IApplicationViewModel ApplicationViewModel { get; set; } = null!;

    readonly MapReadinessState _readiness = new(TimeProvider.System);
    readonly MapCapabilityOutcomeState _capabilityOutcome = new(TimeProvider.System);
    DotNetObjectReference<Map>? _dotNetReference;
    ITimer? _readinessTimer;
    long _readinessGeneration;
    long _renderOperationGeneration;
    bool _disposed;

    public MapReadinessOutcome Readiness => _readiness.Current;
    public bool ShowDelayedLoading => _readiness.IsDelayedLoading;

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

    [JSInvokable("notifyMapInitializationAsync")]
    public async Task NotifyMapInitializationAsync(long generation, bool requiredLayersAvailable)
    {
        var signal = requiredLayersAvailable
            ? MapReadinessSignal.RequiredContentReady
            : MapReadinessSignal.RequiredLayerUnavailable;
        if (!_readiness.Observe(generation, signal)) return;

        if (requiredLayersAvailable)
            _capabilityOutcome.MarkReady(generation);

        StopReadinessTimer();
        ReportReadinessOutcome();
        await InvokeAsync(StateHasChanged);
        if (requiredLayersAvailable)
            await OnMapReady.InvokeAsync(this);
    }

    [JSInvokable("notifyMapUnsupportedBrowserAsync")]
    public async Task NotifyMapUnsupportedBrowserAsync(long generation)
    {
        if (!_readiness.Observe(generation, MapReadinessSignal.UnsupportedBrowser)) return;

        StopReadinessTimer();
        ReportReadinessOutcome();
        await InvokeAsync(StateHasChanged);
    }

    void ReportReadinessOutcome()
    {
        var outcome = Readiness;
        if (outcome.Status != MapReadinessStatus.Unavailable) return;

        var scope = ApplicationViewModel.NotificationScope;
        var rootExplainsFailure = ApplicationViewModel.RouteLoadOutcome.FailureKind != RouteLoadFailureKind.None
            || ApplicationViewModel.LiveIssueCode is not null;
        CapabilityNotificationOwner.ObserveMapReadiness(outcome, scope, rootExplainsFailure);
    }

    [JSInvokable("notifyMapVisibilityAsync")]
    public Task NotifyMapVisibilityAsync(long generation, bool visible)
    {
        if (!_readiness.SetVisible(generation, visible)) return Task.CompletedTask;
        _capabilityOutcome.SetVisible(generation, visible);
        ScheduleReadinessTimer();
        return Task.CompletedTask;
    }

    public void ReloadMap() => NavigationManager.NavigateTo(NavigationManager.Uri, forceLoad: true);

    Task ObserveRenderResultAsync(MapRenderOperationOutcome outcome)
    {
        if (outcome.Generation != _readinessGeneration
            || outcome.OperationGeneration != Volatile.Read(ref _renderOperationGeneration))
            return Task.CompletedTask;

        var transition = outcome.Rendered
            ? _capabilityOutcome.ObserveSuccessfulRender(_readinessGeneration, primaryLayerUsable: true, secondaryLayerUsable: false)
            : _capabilityOutcome.ObserveRenderFailure(
                _readinessGeneration,
                MapRenderLayerKind.PrimaryTransit,
                validBatch: true,
                terminal: outcome.TerminalFailure);
        return ObserveMapTransitionAsync(transition);
    }

    async Task ObserveMapTransitionAsync(MapCapabilityTransition? transition)
    {
        if (transition is null) return;
        var confirmedCause = ApplicationViewModel.LiveIssueCode ?? ApplicationViewModel.RouteLoadOutcome.FailureKind switch
        {
            RouteLoadFailureKind.RequestFailed => NotificationIssueCode.RouteLoadFailed,
            RouteLoadFailureKind.NoUsableCurrentCityData => NotificationIssueCode.RoutesUnavailable,
            _ => (NotificationIssueCode?)null
        };
        await CapabilityNotificationOwner.ObserveConditionalMapOutcomeAsync(
            transition,
            ApplicationViewModel.NotificationScope,
            ConditionalMapNotificationsEnabled,
            confirmedCause);
    }

    void ScheduleReadinessTimer()
    {
        StopReadinessTimer();
        if (_disposed || Readiness.Status != MapReadinessStatus.Pending || !_readiness.IsVisible) return;
        _readinessTimer = TimeProvider.System.CreateTimer(
            _ => _ = InvokeAsync(OnReadinessTimerAsync),
            null,
            _readiness.RemainingVisibleDelay,
            Timeout.InfiniteTimeSpan);
    }

    Task OnReadinessTimerAsync()
    {
        if (_disposed || Readiness.Status != MapReadinessStatus.Pending) return Task.CompletedTask;
        StateHasChanged();
        if (!ShowDelayedLoading) ScheduleReadinessTimer();
        else StopReadinessTimer();
        return Task.CompletedTask;
    }

    void StopReadinessTimer()
    {
        _readinessTimer?.Dispose();
        _readinessTimer = null;
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        Interlocked.Increment(ref _renderOperationGeneration);
        _readiness.Invalidate(_readinessGeneration);
        _capabilityOutcome.Invalidate(_readinessGeneration);
        StopReadinessTimer();

        if (_dotNetReference is not null)
        {
            try { await JsRuntime.InvokeVoidAsync("ChefMap.removeMap", ElementId); }
            catch (Exception ex) { Logger.LogDebug(ex, "Map.RemoveMap failed during disposal"); }
            _dotNetReference.Dispose();
            _dotNetReference = null;
        }

        if (_crossingDispatcherModule is not null)
        {
            try { await _crossingDispatcherModule.DisposeAsync(); }
            catch (Exception ex) { Logger.LogDebug(ex, "Map crossing dispatcher disposal failed"); }
            _crossingDispatcherModule = null;
        }
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
