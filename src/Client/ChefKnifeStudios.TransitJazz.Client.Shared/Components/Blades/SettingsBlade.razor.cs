using ChefKnifeStudios.TransitJazz.Client.Core.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.EventArgs;
using ChefKnifeStudios.TransitJazz.Client.Shared.Models;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.JsInterop;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components.Blades;

public partial class SettingsBlade : IDisposable
{
    [Inject] IEventNotificationService EventNotificationService { get; set; } = null!;
    [Inject] ISettingsService SettingsService { get; set; } = null!;
    [Inject] SettingsChangeController SettingsChangeController { get; set; } = null!;
    [Inject] ITransitSynthJsInterop TransitSynth { get; set; } = null!;
    [Inject] ILogger<SettingsBlade> Logger { get; set; } = null!;
    [Inject] NotificationCoordinator NotificationCoordinator { get; set; } = null!;

    BladeContainer? _bladeContainer;
    Settings _settings = new();
    readonly Dictionary<string, SettingsChangeOutcome> _changes = new(StringComparer.Ordinal);
    readonly string _audioEnableGestureId = $"settings-audio-enable-{Guid.NewGuid():N}";
    bool _isOpen;

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

    protected override async System.Threading.Tasks.Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_settings.IsAudioEnabled)
            await TransitSynth.AttachUnlockGestureAsync(_audioEnableGestureId);
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
        if (e is SettingsCommittedEventArgs)
        {
            var committed = (SettingsCommittedEventArgs)e;
            _settings = SettingsService.GetSettings();
            if (committed.ChangeOutcome is { } outcome)
                _changes[committed.PropertyName] = outcome;
            else if (committed.IsPersisted)
                _changes.Remove(committed.PropertyName);
            InvokeAsync(StateHasChanged);
            return;
        }

        if (e is AudioSettingChangedEventArgs audio)
        {
            _settings = SettingsService.GetSettings();
            InvokeAsync(StateHasChanged);
            return;
        }

        if (e is not BladeEventArgs blade) return;

        if (blade.Type == BladeEventArgs.Types.Settings)
        {
            _settings = SettingsService.GetSettings();
            _ = InvokeAsync(async () =>
            {
                _isOpen = true;
                NotificationCoordinator.SetBlockingOverlay(NotificationBlockingOverlay.SettingsBlade, true);
                await (_bladeContainer?.Open() ?? System.Threading.Tasks.Task.CompletedTask);
            });
        }
        else
        {
            _ = InvokeAsync(async () =>
            {
                await (_bladeContainer?.Close() ?? System.Threading.Tasks.Task.CompletedTask);
                _isOpen = false;
                NotificationCoordinator.SetBlockingOverlay(NotificationBlockingOverlay.SettingsBlade, false);
            });
        }
    }

    async System.Threading.Tasks.Task HandleSettingPressed(string propertyName, bool value)
    {
        var outcome = await SettingsChangeController.ChangeAsync(
            propertyName,
            value,
            (requested, cancellationToken) => ApplySettingEffectAsync(propertyName, requested, cancellationToken),
            (previous, cancellationToken) => ApplySettingEffectAsync(propertyName, previous, cancellationToken));
        _settings = SettingsService.GetSettings();
        _changes[propertyName] = outcome;
        if (outcome.Status is SettingsChangeStatus.Applied or SettingsChangeStatus.AppliedSessionOnly)
            EventNotificationService.PostEvent(this, new SettingsCommittedEventArgs
            {
                PropertyName = propertyName,
                IsPersisted = outcome.IsPersisted,
                ChangeOutcome = outcome
            });
        await InvokeAsync(StateHasChanged);
    }

    async System.Threading.Tasks.Task<SettingsEffectResult> ApplySettingEffectAsync(
        string propertyName,
        bool value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (propertyName)
        {
            case nameof(Settings.IsAudioEnabled):
                return await PostSettingEffectAsync(new AudioSettingChangedEventArgs { IsAudioEnabled = value });
            case nameof(Settings.AreCheckpointsVisible):
                return await PostSettingEffectAsync(new CheckpointVisibilityChangedEventArgs { AreCheckpointsVisible = value });
            case nameof(Settings.IsCrossingTrailVisible):
                return await PostSettingEffectAsync(new CrossingTrailVisibilityChangedEventArgs { IsCrossingTrailVisible = value });
            case nameof(Settings.IsStreetMapEnabled):
                var streetMapResult = await PostStreetMapEffectAsync(value, SettingsService.GetSettings().IsDarkModeEnabled);
                return streetMapResult.Succeeded ? streetMapResult : SettingsEffectResult.PartialFailure;
            case nameof(Settings.IsBusesVisible):
                return await PostSettingEffectAsync(new BusVisibilitySettingChangedEventArgs { IsBusesVisible = value });
            case nameof(Settings.AreAllCheckpointsVisible):
                return await PostSettingEffectAsync(new AllCheckpointsVisibilityChangedEventArgs { AreAllCheckpointsVisible = value });
            case nameof(Settings.IsDarkModeEnabled):
                EventNotificationService.PostEvent(this, new ThemeChangedEventArgs { IsDarkMode = value });
                var streetMapEnabled = SettingsService.GetSettings().IsStreetMapEnabled;
                var styleResult = await PostStreetMapEffectAsync(streetMapEnabled, value);
                return styleResult.Succeeded ? styleResult : SettingsEffectResult.PartialFailure;
            default:
                return SettingsEffectResult.Success;
        }
    }

    async System.Threading.Tasks.Task<SettingsEffectResult> PostSettingEffectAsync(IEventArgs effect) =>
        await EventNotificationService.PostEventAsync(this, effect)
            ? SettingsEffectResult.Success
            : SettingsEffectResult.Failure;

    async System.Threading.Tasks.Task<SettingsEffectResult> PostStreetMapEffectAsync(bool streetMapEnabled, bool darkModeEnabled) =>
        await EventNotificationService.PostEventAsync(this,
            new GisSettingChangedEventArgs { IsStreetMapEnabled = streetMapEnabled, IsDarkModeEnabled = darkModeEnabled })
            ? SettingsEffectResult.Success
            : SettingsEffectResult.Failure;

    string GetChangeStatusText(SettingsChangeOutcome outcome) => outcome.Status switch
    {
        SettingsChangeStatus.AppliedSessionOnly => Localizer["ToastSettingsNotSaved"],
        SettingsChangeStatus.MixedState => Localizer["SettingsMixedEffectFailure"],
        _ => Localizer["ToastSettingsApplyFailure"]
    };

    public void Dispose()
    {
        EventNotificationService.EventReceived -= HandleEventReceived;
        if (_isOpen)
        {
            _isOpen = false;
            NotificationCoordinator.SetBlockingOverlay(NotificationBlockingOverlay.SettingsBlade, false);
        }
    }
}
