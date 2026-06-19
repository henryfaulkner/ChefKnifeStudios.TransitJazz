using Microsoft.AspNetCore.Components;
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
            Console.WriteLine(ex.ToString());
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
            Console.WriteLine(ex.ToString());
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
                Console.WriteLine(ex.ToString());
            }
        }
    }

    public async Task AddRouteShapeFeatureAsync(string routeId, double[][] coordinates, string? color)
    {
        try
        {
            await JsRuntime.InvokeVoidAsync("ChefMap.addRouteShapeFeature", ElementId, routeId, coordinates, color);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Map] AddRouteShapeFeature failed for routeId={routeId}: {ex}");
        }
    }

    public async Task LoadRouteGeometryForAnimationAsync(string routeId, double[][] coordinates)
    {
        try
        {
            Console.WriteLine($"[Map] LoadRouteGeometryForAnimation: routeId={routeId} coords={coordinates.Length}");
            await JsRuntime.InvokeVoidAsync("ChefMapAnimator.loadRouteGeometry", routeId, coordinates);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Map] LoadRouteGeometryForAnimation failed for routeId={routeId}: {ex}");
        }
    }

    public async Task AddTriggerPointMarkersAsync(string routeId, object[] triggerPoints, double[][] coords)
    {
        try
        {
            await JsRuntime.InvokeVoidAsync("ChefMap.addTriggerPointMarkers", ElementId, routeId, triggerPoints, coords);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Map] AddTriggerPointMarkers failed for routeId={routeId}: {ex}");
        }
    }

    public async Task FocusRouteAsync(string routeId)
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.focusRoute", ElementId, routeId); }
        catch (Exception ex) { Console.WriteLine($"[Map] FocusRoute failed for routeId={routeId}: {ex}"); }
    }

    public async Task FocusRoutesAsync(IEnumerable<string> routeIds)
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.focusRoutes", ElementId, routeIds); }
        catch (Exception ex) { Console.WriteLine($"[Map] FocusRoutes failed: {ex}"); }
    }

    public async Task SetCheckpointVisibilityAsync(bool visible)
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.setCheckpointVisibility", ElementId, visible); }
        catch (Exception ex) { Console.WriteLine($"[Map] SetCheckpointVisibility failed: {ex}"); }
    }

    public async Task PulseCheckpointAsync(string routeId, int triggerIndex)
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.pulseCheckpoint", ElementId, routeId, triggerIndex); }
        catch (Exception ex) { Console.WriteLine($"[Map] PulseCheckpoint failed for routeId={routeId} triggerIndex={triggerIndex}: {ex}"); }
    }

    public async Task SetVehiclesVisibleAsync(bool visible)
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.setVehiclesVisible", ElementId, visible); }
        catch (Exception ex) { Console.WriteLine($"[Map] SetVehiclesVisible failed: {ex}"); }
    }

    public async Task ClearRouteFocusAsync()
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.clearRouteFocus", ElementId); }
        catch (Exception ex) { Console.WriteLine($"[Map] ClearRouteFocus failed: {ex}"); }
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
            Console.WriteLine($"[Map] SetBasemapStyle failed: {ex}");
            return null;
        }
    }

    public async Task ProcessNearestPointBatchAsync(object[] records)
    {
        try
        {
            Console.WriteLine($"[Map] ProcessNearestPointBatch: {records.Length} records → JS");
            await JsRuntime.InvokeVoidAsync("ChefMapAnimator.processNearestPointBatch", ElementId, records);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Map] ProcessNearestPointBatch failed: {ex}");
        }
    }
}
