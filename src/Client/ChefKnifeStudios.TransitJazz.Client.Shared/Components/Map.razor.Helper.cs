using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components;

public partial class Map : ComponentBase
{
    async Task CreateMapAsync()
    {
        _readinessGeneration = _readiness.BeginInstance();
        _capabilityOutcome.BeginInstance();
        ScheduleReadinessTimer();

        try
        {
            _dotNetReference = DotNetObjectReference.Create(this);
            await JsRuntime.InvokeVoidAsync("ChefMap.createMap",
                ElementId, _dotNetReference, _readinessGeneration);
        }
        catch (Exception ex)
        {
            _readiness.Observe(_readinessGeneration, MapReadinessSignal.InitializationFailed);
            StopReadinessTimer();
            Logger.LogError(ex, "[Map] CreateMap failed");
            ReportReadinessOutcome();
            await InvokeAsync(StateHasChanged);
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

    public async Task AddAllRoutesAsync(object payload, bool isDarkMode = false)
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.addAllRoutes", ElementId, payload, isDarkMode); }
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

    public async Task<bool> SetCheckpointVisibilityAsync(bool visible)
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.setCheckpointVisibility", ElementId, visible); return true; }
        catch (Exception ex) { Logger.LogError(ex, "[Map] SetCheckpointVisibility failed"); return false; }
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
                "import", "./_content/ChefKnifeStudios.TransitJazz.Client.Shared/js/crossing-dispatcher.js");
            await _crossingDispatcherModule.InvokeVoidAsync("dispatchCrossings", ElementId, crossings, flags);
        }
        catch (Exception ex) { Logger.LogError(ex, "[Map] DispatchCrossings failed"); }
    }

    // Push the current effective route filter (selection ∪ hover) to the dispatcher so its
    // already-scheduled timers re-check it at fire time. Pass null/empty to clear the filter
    // (all routes pass). This is what makes a filter change take effect immediately instead of
    // after the ~10s scheduling horizon drains — crossings queued before the change are silenced
    // the instant their timer fires if their route is no longer selected.
    public async Task<bool> SetCrossingFilterAsync(IEnumerable<string>? routeJoinKeys)
    {
        try
        {
            _crossingDispatcherModule ??= await JsRuntime.InvokeAsync<IJSObjectReference>(
                "import", "./_content/ChefKnifeStudios.TransitJazz.Client.Shared/js/crossing-dispatcher.js");
            // Must pass the key array as a SINGLE interop argument. InvokeVoidAsync(string, object?[])
            // takes a params object?[] — a bare string[] is covariantly assignable to object[], so
            // handing it directly gets spread as the params array itself (one positional JS arg PER
            // route key) instead of one array argument. JS's setActiveFilter(keys) then only ever
            // received the FIRST key as a bare string, which new Set(str) exploded into characters.
            // Wrapping in `new object[] { ... }` forces it to be treated as ONE argument.
            object?[] jsArgs = [routeJoinKeys?.ToArray()];
            return await _crossingDispatcherModule.InvokeAsync<bool>("setActiveFilter", jsArgs);
        }
        catch (Exception ex) { Logger.LogError(ex, "[Map] SetCrossingFilter failed"); return false; }
    }

    // Push the current mute/unmute setting to the dispatcher so its already-scheduled timers
    // re-check it at fire time (same live-flag pattern as SetCrossingFilterAsync). Without this,
    // crossings queued during a muted window stayed silent after unmute until the ~10s scheduling
    // horizon drained — the "extended mute" bug. See _audioEnabled in crossing-dispatcher.js.
    public async Task<bool> SetCrossingAudioEnabledAsync(bool enabled)
    {
        try
        {
            _crossingDispatcherModule ??= await JsRuntime.InvokeAsync<IJSObjectReference>(
                "import", "./_content/ChefKnifeStudios.TransitJazz.Client.Shared/js/crossing-dispatcher.js");
            return await _crossingDispatcherModule.InvokeAsync<bool>("setAudioEnabled", enabled);
        }
        catch (Exception ex) { Logger.LogError(ex, "[Map] SetCrossingAudioEnabled failed"); return false; }
    }

    public async Task<bool> SetCrossingTrailVisibilityAsync(bool visible)
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.setCrossingTrailVisibility", ElementId, visible); return true; }
        catch (Exception ex) { Logger.LogError(ex, "[Map] SetCrossingTrailVisibility failed"); return false; }
    }

    public async Task<bool> SetAllCheckpointsVisibilityAsync(bool visible)
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.setAllCheckpointsVisibility", ElementId, visible); return true; }
        catch (Exception ex) { Logger.LogError(ex, "[Map] SetAllCheckpointsVisibility failed"); return false; }
    }

    public async Task<bool> SetVehiclesVisibleAsync(bool visible)
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.setVehiclesVisible", ElementId, visible); return true; }
        catch (Exception ex) { Logger.LogError(ex, "[Map] SetVehiclesVisible failed"); return false; }
    }

    public async Task ClearRouteFocusAsync()
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.clearRouteFocus", ElementId); }
        catch (Exception ex) { Logger.LogError(ex, "[Map] ClearRouteFocus failed"); }
    }

    public async Task<MapStyleChangeResult?> SetBasemapStyleAsync(string styleUrl)
    {
        var operationGeneration = _capabilityOutcome.BeginStyleChange(_readinessGeneration);
        if (operationGeneration == 0) return null;

        try
        {
            var result = await JsRuntime.InvokeAsync<MapStyleChangeResult>(
                "ChefMap.setMapStyle", ElementId, styleUrl, _readinessGeneration, operationGeneration);
            if (result.Generation != _readinessGeneration
                || result.OperationGeneration != operationGeneration)
                return null;

            var transition = _capabilityOutcome.CompleteStyleChange(
                _readinessGeneration,
                operationGeneration,
                result.RequestedStyleLoaded,
                result.PreviousRequiredSourcesAndLayersRestored,
                result.MapUsable,
                result.TerminalFailure);
            await ObserveMapTransitionAsync(transition);

            if (result.TerminalFailure)
            {
                _readiness.MarkUnavailable(_readinessGeneration, MapFailureReason.RequiredLayerUnavailable);
                StopReadinessTimer();
                ReportReadinessOutcome();
                await InvokeAsync(StateHasChanged);
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Map] SetBasemapStyle failed");
            await ObserveMapTransitionAsync(_capabilityOutcome.CompleteStyleChange(
                _readinessGeneration,
                operationGeneration,
                requestedStyleLoaded: false,
                previousRequiredSourcesAndLayersRestored: false,
                mapUsable: false,
                terminalFailure: false));
            return null;
        }
    }

    public async Task FlushTriggerPointsAsync()
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.flushTriggerPoints", ElementId); }
        catch (Exception ex) { Logger.LogError(ex, "[Map] FlushTriggerPoints failed"); }
    }

    public async Task<bool> ProcessNearestPointBatchAsync(IReadOnlyList<object> records)
    {
        if (records.Count == 0) return false;
        var operationGeneration = Interlocked.Increment(ref _renderOperationGeneration);
        try
        {
            Logger.LogDebug("[Map] ProcessNearestPointBatch: {Count} records → JS", records.Count);
            var outcome = await JsRuntime.InvokeAsync<MapRenderOperationOutcome>(
                "ChefMap.processNearestPointBatch", ElementId, records, _readinessGeneration, operationGeneration);
            await ObserveRenderResultAsync(outcome);
            return outcome.Rendered;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Map] ProcessNearestPointBatch failed");
            await ObserveRenderResultAsync(new(
                Rendered: false,
                TerminalFailure: false,
                Generation: _readinessGeneration,
                OperationGeneration: operationGeneration));
            return false;
        }
    }
}
