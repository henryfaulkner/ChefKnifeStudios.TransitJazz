using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
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

    // NOTE: the former PulseCheckpointAsync / StartCrossingTrailAsync C# wrappers were removed —
    // they were the old per-crossing interop path (deleted with FireCrossingDelayedAsync). The
    // crossing-dispatcher JS module now calls ChefMap.pulseCheckpoint / startCrossingTrail
    // directly, resolving the checkpoint by alongDistanceM (see crossing-dispatcher.js).

    // Lazily-imported crossing-dispatcher ES module, reused across batches.
    IJSObjectReference? _crossingDispatcherModule;

    // Hand an entire crossing batch to the JS dispatcher in ONE interop call. The dispatcher owns
    // the per-crossing timers (each delay = time for the animated dot to reach the checkpoint's
    // AlongDistanceM) and fires each crossing's pulse + trail + note together,
    // so effects can't desync from each other and the interop cost stays O(1) per batch regardless
    // of fleet size — replacing the old per-crossing Task.Delay + 4-interop fan-out.
    public async Task DispatchCrossingsAsync(object crossings, object flags)
    {
        try
        {
            _crossingDispatcherModule ??= await JsRuntime.InvokeAsync<IJSObjectReference>(
                "import", "./_content/ChefKnifeStudios.MartaJazz.Client.Shared/js/crossing-dispatcher.js");
            await _crossingDispatcherModule.InvokeVoidAsync("dispatchCrossings", ElementId, crossings, flags);
        }
        catch (Exception ex) { Logger.LogError(ex, "[Map] DispatchCrossings failed"); }
    }

    // Push the current effective route filter (selection ∪ hover) to the dispatcher so its
    // already-scheduled timers re-check it at fire time. Pass null/empty to clear the filter
    // (all routes pass). This is what makes a filter change take effect immediately instead of
    // after the ~10s scheduling horizon drains — crossings queued before the change are silenced
    // the instant their timer fires if their route is no longer selected.
    public async Task SetCrossingFilterAsync(IEnumerable<string>? routeJoinKeys)
    {
        try
        {
            _crossingDispatcherModule ??= await JsRuntime.InvokeAsync<IJSObjectReference>(
                "import", "./_content/ChefKnifeStudios.MartaJazz.Client.Shared/js/crossing-dispatcher.js");
            // Must pass the key array as a SINGLE interop argument. InvokeVoidAsync(string, object?[])
            // takes a params object?[] — a bare string[] is covariantly assignable to object[], so
            // handing it directly gets spread as the params array itself (one positional JS arg PER
            // route key) instead of one array argument. JS's setActiveFilter(keys) then only ever
            // received the FIRST key as a bare string, which new Set(str) exploded into characters.
            // Wrapping in `new object[] { ... }` forces it to be treated as ONE argument.
            object?[] jsArgs = [routeJoinKeys?.ToArray()];
            await _crossingDispatcherModule.InvokeVoidAsync("setActiveFilter", jsArgs);
        }
        catch (Exception ex) { Logger.LogError(ex, "[Map] SetCrossingFilter failed"); }
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
