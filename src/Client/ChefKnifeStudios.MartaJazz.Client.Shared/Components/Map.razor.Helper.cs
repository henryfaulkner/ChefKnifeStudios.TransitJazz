using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Components;

public partial class Map : ComponentBase
{
    async Task CreateMapAsync()
    {
        try
        {
            await JsRuntime.InvokeVoidAsync("ChefMap.createMap",
                ElementId, DotNetObjectReference.Create(this));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Map] CreateMap failed");
        }
    }

    public async Task CenterVehiclePinAsync(int vehicleId)
    {
        try
        {
            await JsRuntime.InvokeVoidAsync("ChefMap.centerVehiclePin", ElementId, vehicleId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Map] CenterVehiclePin failed for vehicleId={VehicleId}", vehicleId);
        }
    }

    public async Task PlotVehiclesAsync(object? mapFeatureCollection, bool centerMap = true)
    {
        if (mapFeatureCollection != null)
        {
            try
            {
                await JsRuntime.InvokeVoidAsync("ChefMap.plotFeatures",
                    ElementId, "vehicles", mapFeatureCollection, centerMap);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[Map] PlotVehicles failed");
            }
        }
    }

    public async Task AddAllRoutesAsync(object payload)
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.addAllRoutes", ElementId, payload); }
        catch (Exception ex) { Logger.LogError(ex, "[Map] AddAllRoutes failed"); }
    }

    public async Task AddTriggerPointMarkersAsync(string routeJoinKey, object[] triggerPoints, double[][] coords)
    {
        try
        {
            await JsRuntime.InvokeVoidAsync("ChefMap.addTriggerPointMarkers", ElementId, routeJoinKey, triggerPoints, coords);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Map] AddTriggerPointMarkers failed for routeJoinKey={RouteJoinKey}", routeJoinKey);
        }
    }

    public async Task FocusRouteAsync(string routeJoinKey)
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.focusRoute", ElementId, routeJoinKey); }
        catch (Exception ex) { Logger.LogError(ex, "[Map] FocusRoute failed for routeJoinKey={RouteJoinKey}", routeJoinKey); }
    }

    public async Task FocusRoutesAsync(IEnumerable<string> routeJoinKeys)
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.focusRoutes", ElementId, routeJoinKeys); }
        catch (Exception ex) { Logger.LogError(ex, "[Map] FocusRoutes failed"); }
    }

    public async Task SetCheckpointVisibilityAsync(bool visible)
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.setCheckpointVisibility", ElementId, visible); }
        catch (Exception ex) { Logger.LogError(ex, "[Map] SetCheckpointVisibility failed"); }
    }

    public async Task PulseCheckpointAsync(string routeJoinKey, int triggerIndex)
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.pulseCheckpoint", ElementId, routeJoinKey, triggerIndex); }
        catch (Exception ex) { Logger.LogError(ex, "[Map] PulseCheckpoint failed for routeJoinKey={RouteJoinKey} triggerIndex={TriggerIndex}", routeJoinKey, triggerIndex); }
    }

    public async Task StartCrossingTrailAsync(string routeJoinKey, string vehicleId, int triggerIndex, double durationSeconds)
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.startCrossingTrail", ElementId, routeJoinKey, vehicleId, triggerIndex, durationSeconds); }
        catch (Exception ex) { Logger.LogError(ex, "[Map] StartCrossingTrail failed for routeJoinKey={RouteJoinKey} triggerIndex={TriggerIndex}", routeJoinKey, triggerIndex); }
    }

    public async Task SetCrossingTrailVisibilityAsync(bool visible)
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.setCrossingTrailVisibility", ElementId, visible); }
        catch (Exception ex) { Logger.LogError(ex, "[Map] SetCrossingTrailVisibility failed"); }
    }

    public async Task SetAllCheckpointsVisibilityAsync(bool visible)
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.setAllCheckpointsVisibility", ElementId, visible); }
        catch (Exception ex) { Logger.LogError(ex, "[Map] SetAllCheckpointsVisibility failed"); }
    }

    public async Task SetVehiclesVisibleAsync(bool visible)
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.setVehiclesVisible", ElementId, visible); }
        catch (Exception ex) { Logger.LogError(ex, "[Map] SetVehiclesVisible failed"); }
    }

    public async Task ClearRouteFocusAsync()
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.clearRouteFocus", ElementId); }
        catch (Exception ex) { Logger.LogError(ex, "[Map] ClearRouteFocus failed"); }
    }

    public async Task<string?> SetBasemapStyleAsync(string styleUrl)
    {
        try
        {
            var result = await JsRuntime.InvokeAsync<System.Text.Json.JsonElement>("ChefMap.setMapStyle", ElementId, styleUrl);
            return result.TryGetProperty("checkpointVisible", out var v) ? v.GetString() : null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Map] SetBasemapStyle failed");
            return null;
        }
    }

    public async Task FlushTriggerPointsAsync()
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.flushTriggerPoints", ElementId); }
        catch (Exception ex) { Logger.LogError(ex, "[Map] FlushTriggerPoints failed"); }
    }

    public async Task ProcessNearestPointBatchAsync(IReadOnlyList<object> records)
    {
        try
        {
            Logger.LogDebug("[Map] ProcessNearestPointBatch: {Count} records → JS", records.Count);
            await JsRuntime.InvokeVoidAsync("ChefMapAnimator.processNearestPointBatch", ElementId, records);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Map] ProcessNearestPointBatch failed");
        }
    }
}
