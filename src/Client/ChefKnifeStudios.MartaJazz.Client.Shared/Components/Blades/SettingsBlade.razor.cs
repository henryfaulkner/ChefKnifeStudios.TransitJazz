using ChefKnifeStudios.MartaJazz.Client.Core.Services;
using ChefKnifeStudios.MartaJazz.Client.Shared.EventArgs;
using ChefKnifeStudios.MartaJazz.Client.Shared.Models;
using ChefKnifeStudios.MartaJazz.Client.Shared.Services;
using ChefKnifeStudios.MartaJazz.Client.Shared.Services.JsInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Components.Blades;

public partial class SettingsBlade : IDisposable
{
    [Inject] IEventNotificationService EventNotificationService { get; set; } = null!;
    [Inject] ISettingsService SettingsService { get; set; } = null!;
    [Inject] ITransitSynthJsInterop TransitSynth { get; set; } = null!;
    [Inject] ILogger<SettingsBlade> Logger { get; set; } = null!;

    BladeContainer? _bladeContainer;
    Settings _settings = new();

    // DEV-ONLY instrument toggle (compile-time gated like SettingsFab._showInDebug — never
    // true in Release/prod builds). In-memory only: not part of Settings.cs/ISettingsService,
    // so it's never persisted to local storage and resets on reload. See
    // docs/SYNTH_REFACTOR_DESIGN_DOCUMENT.md.
#if DEBUG
    readonly bool _showDevInstrumentToggles = true;
#else
    readonly bool _showDevInstrumentToggles = false;
#endif
    IReadOnlyList<string> _instrumentNames = Array.Empty<string>();
    readonly HashSet<string> _enabledInstruments = new();

    protected override void OnInitialized()
    {
        _settings = SettingsService.GetSettings();
        EventNotificationService.EventReceived += HandleEventReceived;
    }

    protected override async System.Threading.Tasks.Task OnInitializedAsync()
    {
        if (!_showDevInstrumentToggles) return;

        _instrumentNames = await TransitSynth.GetInstrumentNamesAsync();
        foreach (var name in _instrumentNames) _enabledInstruments.Add(name);

        // transit-synth.js defaults to the PROD_INSTRUMENTS ship list (3 voices) so a plain
        // page load — with no dev interaction — never plays contrabass/viola/cello. Dev
        // builds explicitly widen the JS-side filter back to all 6 here so the checkbox UI's
        // "all checked" initial state actually matches what's audible.
        try { await TransitSynth.SetEnabledInstrumentsAsync(_instrumentNames); }
        catch (Exception ex) { Logger.LogError(ex, "SettingsBlade.OnInitializedAsync: failed to enable full dev instrument set"); }
    }

    async void HandleInstrumentTogglePressed(string instrumentName, bool enabled)
    {
        if (enabled) _enabledInstruments.Add(instrumentName);
        else _enabledInstruments.Remove(instrumentName);

        // All-enabled is equivalent to "no filter" — send an empty list either way so
        // transit-synth.js takes its cheap unfiltered path (see setEnabledInstruments).
        var toSend = _enabledInstruments.Count == _instrumentNames.Count
            ? Array.Empty<string>()
            : _enabledInstruments.ToArray();

        try { await TransitSynth.SetEnabledInstrumentsAsync(toSend); }
        catch (Exception ex) { Logger.LogError(ex, "SettingsBlade.HandleInstrumentTogglePressed: failed for instrument {InstrumentName}", instrumentName); }
    }

    void HandleEventReceived(object sender, IEventArgs e)
    {
        if (e is not BladeEventArgs blade) return;

        if (blade.Type == BladeEventArgs.Types.Settings)
        {
            _settings = SettingsService.GetSettings();
            InvokeAsync(async () => await (_bladeContainer?.Open() ?? System.Threading.Tasks.Task.CompletedTask));
        }
        else
        {
            InvokeAsync(async () => await (_bladeContainer?.Close() ?? System.Threading.Tasks.Task.CompletedTask));
        }
    }

    void HandleSettingPressed(string propertyName, bool value)
    {
        try
        {
            SettingsService.SetSettingValue(propertyName, value);
            _settings = SettingsService.GetSettings();

            IEventArgs? effectEvent = propertyName switch
            {
                nameof(Settings.IsAudioEnabled) => new AudioSettingChangedEventArgs { IsAudioEnabled = value },
                nameof(Settings.AreCheckpointsVisible) => new CheckpointVisibilityChangedEventArgs { AreCheckpointsVisible = value },
                nameof(Settings.IsStreetMapEnabled) => new GisSettingChangedEventArgs { IsStreetMapEnabled = value },
                nameof(Settings.IsBusesVisible) => new BusVisibilitySettingChangedEventArgs { IsBusesVisible = value },
                nameof(Settings.AreAllCheckpointsVisible) => new AllCheckpointsVisibilityChangedEventArgs { AreAllCheckpointsVisible = value },
                _ => null
            };

            if (effectEvent is not null)
                EventNotificationService.PostEvent(this, effectEvent);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "SettingsBlade.HandleSettingPressed: failed for property {PropertyName}", propertyName);
        }
    }

    public void Dispose()
    {
        EventNotificationService.EventReceived -= HandleEventReceived;
    }
}
