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

    /// <summary>
    /// DEV-ONLY. Returns the palette's instrument names (transit-synth.js PALETTE, in slot
    /// order) so a dev UI can render one checkbox per instrument without hardcoding names.
    /// </summary>
    Task<IReadOnlyList<string>> GetInstrumentNamesAsync();

    /// <summary>
    /// DEV-ONLY. Restricts route→instrument selection to <paramref name="instrumentNames"/>
    /// (palette instrument names). Empty/omitted means no filter (all instruments enabled).
    /// In-memory only, not persisted — call again after reload to reapply.
    /// </summary>
    Task SetEnabledInstrumentsAsync(IEnumerable<string> instrumentNames);
}
