using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Audio;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services.JsInterop;

public interface ITransitSynthJsInterop : IAsyncDisposable
{
    event Action<AudioCapabilityHealthTransition>? AudioCapabilityHealthChanged;

    Task PreloadAsync(IEnumerable<string> routeIds);
    Task AttachUnlockGestureAsync(string elementId);
    Task AttachAudioFabGestureAsync(string elementId);
    Task<AudioFabGestureOutcome?> ConsumeAudioFabGestureAsync();
    Task<AudioUnlockStatus> UnlockAsync();
    Task BeginAudioCapabilityHealthAsync(long generation, bool audioRequested, bool audioUnlocked);
    Task StopAudioCapabilityHealthAsync();
    Task<bool> IsUnlockedAsync();
    Task<bool> IsPlaybackSupportedAsync();
    Task TriggerNoteAsync(string routeId, string vehicleId, int triggerIndex = 0, int totalTriggers = 1);
    Task<double> DurationSecondsForAsync(string vehicleId, string? routeId = null);

    /// <summary>
    /// Mirrors the app's mute/unmute setting into the JS module so the continuous background
    /// texture (noise bed on the master bus) is silenced along with note triggers — the C#
    /// side already gates <see cref="TriggerNoteAsync"/> on this same setting, but that alone
    /// doesn't stop the always-on noise bed running underneath it.
    /// </summary>
    Task<bool> SetAudioEnabledAsync(bool enabled);

    /// <summary>Applies a confirmed gain mute to the synth's complete output bus.</summary>
    Task<bool> SetMasterMutedAsync(bool muted);

    /// <summary>Reports whether this synth currently has audible output or active sources.</summary>
    Task<AudioMuteVerification> VerifyAudioSilencedAsync();

    /// <summary>
    /// Selects which continuous background "backfill" texture plays under the note triggers
    /// ('noise' | 'percussion'). Mirrors the persisted BackfillTexture into the JS engine.
    /// Safe to call before the master bus exists — the mode is recorded and honored on build.
    /// </summary>
    Task<bool> SetBackfillTextureAsync(string mode);

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
