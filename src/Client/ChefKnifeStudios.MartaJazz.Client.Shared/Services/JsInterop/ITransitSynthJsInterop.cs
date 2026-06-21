using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Services.JsInterop;

public interface ITransitSynthJsInterop : IAsyncDisposable
{
    Task PreloadAsync(IEnumerable<string> routeIds);
    Task UnlockAsync();
    Task<bool> IsUnlockedAsync();
    Task TriggerNoteAsync(string routeId, string vehicleId, int triggerIndex = 0, int totalTriggers = 1);
}
