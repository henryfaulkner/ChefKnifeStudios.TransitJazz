using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Services.JsInterop;

public interface ITransitSynthJsInterop : IAsyncDisposable
{
    Task PreloadAsync(IEnumerable<string> routeIds);
    Task AttachUnlockGestureAsync(string elementId);
    Task UnlockAsync();
    Task<bool> IsUnlockedAsync();
    Task TriggerNoteAsync(string routeId, string vehicleId, int triggerIndex = 0, int totalTriggers = 1);
    Task<double> DurationSecondsForAsync(string vehicleId, string? routeId = null);

    /// <summary>
    /// Frees Samplers cached for routes not in <paramref name="activeRouteIds"/> so decoded
    /// audio doesn't accumulate across a long session. Pass the routes with live vehicles.
    /// </summary>
    Task DisposeInactiveRoutesAsync(IEnumerable<string> activeRouteIds);
}
