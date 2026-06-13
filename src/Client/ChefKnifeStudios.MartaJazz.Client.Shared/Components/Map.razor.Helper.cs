using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System;
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

    public async Task ChangeMapZoomAsync(bool zoomIn)
    {
        try
        {
            CameraOptions.ChangeZoom(zoomIn);
            await JsRuntime.InvokeVoidAsync("ChefMap.setMapZoom", ElementId, CameraOptions.Zoom);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    public async Task SetMapZoomAsync(int zoom)
    {
        try
        {
            CameraOptions.Zoom = zoom;
            await JsRuntime.InvokeVoidAsync("ChefMap.setMapZoom", ElementId, zoom);
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

    public async Task ShowRouteShapeAsync(string geoJson)
    {
        try
        {
            await JsRuntime.InvokeVoidAsync("ChefMap.showRouteShape", ElementId, geoJson);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    public async Task ClearRouteShapeAsync()
    {
        try
        {
            await JsRuntime.InvokeVoidAsync("ChefMap.clearRouteShape", ElementId);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
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

    public async Task ClearRouteFocusAsync()
    {
        try { await JsRuntime.InvokeVoidAsync("ChefMap.clearRouteFocus", ElementId); }
        catch (Exception ex) { Console.WriteLine($"[Map] ClearRouteFocus failed: {ex}"); }
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
