using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Audio;
using ChefKnifeStudios.TransitJazz.Client.Shared.Models;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services;

public sealed record SettingsEffectResult(bool Succeeded, bool PartiallyApplied = false)
{
    public static SettingsEffectResult Success { get; } = new(true);
    public static SettingsEffectResult Failure { get; } = new(false);
    public static SettingsEffectResult PartialFailure { get; } = new(false, true);
}

public enum SettingsChangeStatus
{
    Applied,
    AppliedSessionOnly,
    ApplyFailedRestored,
    MixedState,
    Superseded
}

public sealed record SettingsChangeOutcome(long Revision, SettingsChangeStatus Status, object? ConfirmedValue, bool IsPersisted)
{
    public NotificationIssueCode? IssueCode => Status switch
    {
        SettingsChangeStatus.AppliedSessionOnly => NotificationIssueCode.SettingsNotSaved,
        SettingsChangeStatus.ApplyFailedRestored or SettingsChangeStatus.MixedState => NotificationIssueCode.SettingsApplyFailed,
        _ => null
    };
}

public sealed class SettingsChangeController
{
    readonly ISettingsService _settingsService;
    readonly Func<Settings, CancellationToken, Task>? _persist;
    readonly object _gate = new();
    readonly Dictionary<string, long> _revisions = new(StringComparer.Ordinal);
    readonly HashSet<string> _pendingSaves = new(StringComparer.Ordinal);

    public SettingsChangeController(ISettingsService settings, Func<Settings, CancellationToken, Task>? persist = null)
    {
        _settingsService = settings ?? throw new ArgumentNullException(nameof(settings));
        _persist = persist;
    }

    public bool HasPendingSave(string propertyName)
    {
        lock (_gate) return _pendingSaves.Contains(propertyName);
    }

    public static NotificationIssueCode? GetEligibleTextureFailureCode(
        string propertyName,
        SettingsChangeOutcome outcome,
        bool explicitlyRequested,
        bool mainAudioWorking,
        AudioTextureChangeOutcome textureOutcome)
    {
        var controlConfirmed = string.Equals(propertyName, nameof(Settings.BackfillTexture), StringComparison.Ordinal)
            && outcome.Status == SettingsChangeStatus.ApplyFailedRestored
            && outcome.ConfirmedValue is BackfillTexture;
        return AudioTextureChangePolicy.IsEligibleFailure(
            explicitlyRequested,
            mainAudioWorking,
            controlConfirmed,
            textureOutcome)
            ? NotificationIssueCode.AudioTextureChangeFailed
            : null;
    }

    public static NotificationIssueCode? GetEligibleDisplaySettingFailureCode(
        string propertyName,
        SettingsChangeOutcome outcome,
        bool explicitlyRequested,
        bool hasLocalFeedback)
    {
        var isDisplaySetting = propertyName is
            nameof(Settings.AreCheckpointsVisible)
            or nameof(Settings.IsCrossingTrailVisible)
            or nameof(Settings.IsBusesVisible)
            or nameof(Settings.AreAllCheckpointsVisible);
        var controlWasRestored = outcome.Status == SettingsChangeStatus.ApplyFailedRestored
            && outcome.ConfirmedValue is bool;
        return explicitlyRequested && !hasLocalFeedback && isDisplaySetting && controlWasRestored
            ? NotificationIssueCode.DisplaySettingFailed
            : null;
    }

    public async Task<SettingsChangeOutcome> ChangeAsync<T>(
        string propertyName,
        T requestedValue,
        Func<T, CancellationToken, Task<SettingsEffectResult>> applyEffect,
        Func<T, CancellationToken, Task<SettingsEffectResult>>? compensate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(applyEffect);
        var settings = _settingsService.GetSettings();
        var property = typeof(Settings).GetProperty(propertyName);
        if (property?.CanRead != true || property.CanWrite != true)
            throw new ArgumentException($"Unknown settings property: {propertyName}", nameof(propertyName));

        var priorValue = ReadValue<T>(property, settings);
        long revision;
        lock (_gate)
        {
            _revisions.TryGetValue(propertyName, out var currentRevision);
            revision = currentRevision + 1;
            _revisions[propertyName] = revision;
        }

        SettingsEffectResult applied;
        try
        {
            applied = await applyEffect(requestedValue, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // An exception may follow a partial external effect; attempt verified compensation.
            applied = SettingsEffectResult.PartialFailure;
        }

        if (!IsCurrent(propertyName, revision))
        {
            if (applied.Succeeded || applied.PartiallyApplied)
            {
                var restored = await CompensateAsync(compensate, ReadValue<T>(property, _settingsService.GetSettings()), cancellationToken)
                    .ConfigureAwait(false);
                if (!restored)
                    return Outcome(propertyName, revision, SettingsChangeStatus.MixedState, property, _settingsService.GetSettings(), false);
            }

            return Outcome(propertyName, revision, SettingsChangeStatus.Superseded, property, _settingsService.GetSettings(), false);
        }

        if (!applied.Succeeded)
        {
            if (!applied.PartiallyApplied)
                return Outcome(propertyName, revision, SettingsChangeStatus.ApplyFailedRestored, property, settings, false);

            var restored = await CompensateAsync(compensate, priorValue, cancellationToken).ConfigureAwait(false);
            return Outcome(propertyName, revision,
                restored ? SettingsChangeStatus.ApplyFailedRestored : SettingsChangeStatus.MixedState,
                property, _settingsService.GetSettings(), false);
        }

        lock (_gate)
        {
            if (!IsCurrentLocked(propertyName, revision))
                return Outcome(propertyName, revision, SettingsChangeStatus.Superseded, property, settings, false);
            property.SetValue(settings, requestedValue);
        }

        try
        {
            if (_persist is null)
            {
                _settingsService.SaveSettings(settings);
            }
            else
            {
                await _persist(Copy(settings), cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            lock (_gate)
            {
                if (!IsCurrentLocked(propertyName, revision))
                    return Outcome(propertyName, revision, SettingsChangeStatus.Superseded, property, _settingsService.GetSettings(), false);
                _pendingSaves.Add(propertyName);
                return Outcome(propertyName, revision, SettingsChangeStatus.AppliedSessionOnly, property, settings, false);
            }
        }

        lock (_gate)
        {
            if (!IsCurrentLocked(propertyName, revision))
                return Outcome(propertyName, revision, SettingsChangeStatus.Superseded, property, _settingsService.GetSettings(), false);
            _pendingSaves.Remove(propertyName);
            return Outcome(propertyName, revision, SettingsChangeStatus.Applied, property, settings, true);
        }
    }

    async Task<bool> CompensateAsync<T>(
        Func<T, CancellationToken, Task<SettingsEffectResult>>? compensate,
        T confirmedValue,
        CancellationToken cancellationToken)
    {
        if (compensate is null) return false;
        try
        {
            return (await compensate(confirmedValue, cancellationToken).ConfigureAwait(false)).Succeeded;
        }
        catch
        {
            return false;
        }
    }

    bool IsCurrent(string propertyName, long revision)
    {
        lock (_gate) return IsCurrentLocked(propertyName, revision);
    }

    bool IsCurrentLocked(string propertyName, long revision) =>
        _revisions.TryGetValue(propertyName, out var currentRevision) && currentRevision == revision;

    static T ReadValue<T>(System.Reflection.PropertyInfo property, Settings settings) =>
        property.GetValue(settings) is T value ? value : default!;

    static SettingsChangeOutcome Outcome(
        string propertyName,
        long revision,
        SettingsChangeStatus status,
        System.Reflection.PropertyInfo property,
        Settings settings,
        bool persisted) =>
        new(revision, status, status == SettingsChangeStatus.MixedState ? null : property.GetValue(settings), persisted);

    static Settings Copy(Settings settings) => new()
    {
        Version = settings.Version,
        IsAudioEnabled = settings.IsAudioEnabled,
        AreCheckpointsVisible = settings.AreCheckpointsVisible,
        IsCrossingTrailVisible = settings.IsCrossingTrailVisible,
        IsStreetMapEnabled = settings.IsStreetMapEnabled,
        IsBusesVisible = settings.IsBusesVisible,
        AreAllCheckpointsVisible = settings.AreAllCheckpointsVisible,
        IsDarkModeEnabled = settings.IsDarkModeEnabled,
        BackfillTexture = settings.BackfillTexture
    };
}
