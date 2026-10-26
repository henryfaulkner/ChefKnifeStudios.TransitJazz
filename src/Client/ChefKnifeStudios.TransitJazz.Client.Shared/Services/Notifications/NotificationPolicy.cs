using System;
using System.Collections.Generic;
using System.Linq;
using ChefKnifeStudios.TransitJazz.Client.Shared.Resources;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;

/// <summary>Finite issue ownership, burst selection, and safe resource-key summary policy.</summary>
public sealed class NotificationPolicy
{
    public static TimeSpan BurstWindow { get; } = TimeSpan.FromSeconds(2);
    static readonly IReadOnlyDictionary<NotificationIssueCode, IssueDefinition> Definitions = CreateDefinitions();

    readonly TimeProvider _timeProvider;
    readonly bool _conditionalAudioSettingsMappingsEnabled;
    readonly bool _conditionalDeliveryMappingsEnabled;
    readonly Dictionary<NotificationIssueCode, Incident> _incidents = [];
    NotificationScope? _scope;
    long? _burstStartedAt;

    public NotificationPolicy(
        TimeProvider timeProvider,
        bool conditionalAudioSettingsMappingsEnabled = false,
        bool conditionalDeliveryMappingsEnabled = false)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _conditionalAudioSettingsMappingsEnabled = conditionalAudioSettingsMappingsEnabled;
        _conditionalDeliveryMappingsEnabled = conditionalDeliveryMappingsEnabled;
    }

    public NotificationScope? CurrentScope => _scope;
    public int ActiveIncidentCount => _incidents.Count;
    public long? BurstStartedAt => _burstStartedAt;

    public void SetScope(NotificationScope scope)
    {
        if (scope.PageGeneration < 0 || string.IsNullOrWhiteSpace(scope.CitySlug))
        {
            throw new ArgumentException("A notification scope needs a valid page generation and configured city.", nameof(scope));
        }

        if (_scope == scope)
        {
            return;
        }

        _scope = scope;
        _incidents.Clear();
        _burstStartedAt = null;
    }

    public bool Report(NotificationIssue issue)
    {
        if (!Definitions.ContainsKey(issue.Code)
            || issue.OperationGeneration < 0
            || issue.IsLocallyOwned
            || (IsConditionalAudioSettingsCode(issue.Code) && !_conditionalAudioSettingsMappingsEnabled)
            || (IsConditionalDeliveryCode(issue.Code) && !_conditionalDeliveryMappingsEnabled)
            || (issue.Code == NotificationIssueCode.AudioStopFailed && !issue.IsVerifiedAudioStillPlaying)
            || (_scope is not null && issue.Scope != _scope))
        {
            return false;
        }

        if (_scope is null && !string.IsNullOrWhiteSpace(issue.Scope.CitySlug))
        {
            if (_incidents.Count != 0)
            {
                return false;
            }

            _scope = issue.Scope;
        }

        if (_incidents.TryGetValue(issue.Code, out var existing))
        {
            if (issue.Scope == existing.Issue.Scope && issue.OperationGeneration >= existing.Issue.OperationGeneration && issue != existing.Issue)
            {
                existing.Issue = issue;
                return true;
            }

            return false;
        }

        var now = _timeProvider.GetTimestamp();
        var alreadyExplained = issue.ConfirmedCause is NotificationIssueCode cause &&
            _incidents.TryGetValue(cause, out var parent) && parent.Communicated;
        _incidents.Add(issue.Code, new Incident(issue, now) { Communicated = alreadyExplained });
        if (!alreadyExplained && _burstStartedAt is null)
        {
            _burstStartedAt = now;
        }

        return true;
    }

    public bool Resolve(NotificationIssueCode code)
    {
        var removed = _incidents.Remove(code);
        if (_incidents.Values.All(incident => incident.Communicated))
        {
            _burstStartedAt = null;
        }

        return removed;
    }

    public bool TryGetActiveIssue(NotificationIssueCode code, out NotificationIssue issue, out bool communicated)
    {
        if (_incidents.TryGetValue(code, out var incident))
        {
            issue = incident.Issue;
            communicated = incident.Communicated;
            return true;
        }

        issue = default;
        communicated = false;
        return false;
    }

    public bool CanBypassPresentationSpacing(NotificationSummary summary) =>
        summary.IssueCodes.Contains(NotificationIssueCode.AudioStopFailed)
        && _incidents.TryGetValue(NotificationIssueCode.AudioStopFailed, out var incident)
        && incident.Issue.IsVerifiedAudioStillPlaying;

    public void SuspendPendingBurst() => _burstStartedAt = null;

    public void ResumePendingBurst()
    {
        if (_incidents.Values.Any(incident => !incident.Communicated))
        {
            _burstStartedAt = _timeProvider.GetTimestamp();
        }
    }

    public bool ResolveCapability(NotificationCapability capability)
    {
        var matchingCodes = _incidents.Keys
            .Where(code => Definitions[code].Capability == capability)
            .ToArray();
        foreach (var code in matchingCodes)
        {
            _incidents.Remove(code);
        }

        if (_incidents.Values.All(incident => incident.Communicated))
        {
            _burstStartedAt = null;
        }

        return matchingCodes.Length > 0;
    }

    public void MarkCommunicated(IEnumerable<NotificationIssueCode> issueCodes)
    {
        foreach (var code in issueCodes)
        {
            if (_incidents.TryGetValue(code, out var incident))
            {
                incident.Communicated = true;
            }
        }

        if (_incidents.Values.All(incident => incident.Communicated))
        {
            _burstStartedAt = null;
        }
    }

    public NotificationSummary? TryCreateSummary(bool browserOffline = false)
    {
        if (_burstStartedAt is not long burstStartedAt ||
            _timeProvider.GetElapsedTime(burstStartedAt) < BurstWindow)
        {
            return null;
        }

        var pending = _incidents.Values.Where(incident => !incident.Communicated).ToArray();
        foreach (var child in pending)
        {
            if (child.Issue.ConfirmedCause is NotificationIssueCode cause &&
                _incidents.TryGetValue(cause, out var parent) && parent.Communicated)
            {
                child.Communicated = true;
            }
        }

        pending = _incidents.Values.Where(incident => !incident.Communicated).ToArray();
        if (pending.Length == 0)
        {
            _burstStartedAt = null;
            return null;
        }

        var explained = pending
            .Where(incident => incident.Issue.ConfirmedCause is NotificationIssueCode cause &&
                _incidents.ContainsKey(cause))
            .ToArray();
        var roots = pending.Except(explained).ToArray();
        if (roots.Length == 0)
        {
            _burstStartedAt = null;
            return null;
        }

        var definitions = roots
            .Select(incident => Definitions[incident.Issue.Code])
            .ToArray();
        var capabilities = definitions
            .Select(definition => definition.Capability)
            .Distinct()
            .OrderBy(capability => capability)
            .ToArray();
        var kind = capabilities.Length switch
        {
            1 => NotificationSummaryKind.SingleCapability,
            2 => NotificationSummaryKind.MultipleCapabilities,
            _ => NotificationSummaryKind.General
        };
        var highest = roots
            .OrderByDescending(incident => Definitions[incident.Issue.Code].Severity)
            .ThenBy(incident => incident.Issue.Code)
            .First();
        var highestDefinition = Definitions[highest.Issue.Code];
        var severity = definitions.Any(definition => definition.Severity == NotificationSeverity.Error)
            ? NotificationSeverity.Error
            : NotificationSeverity.Warning;
        var impactKey = kind switch
        {
            NotificationSummaryKind.SingleCapability => highestDefinition.ImpactKey,
            NotificationSummaryKind.MultipleCapabilities => RouteFilterResources.ToastMultipleCapabilitiesFailure,
            _ => RouteFilterResources.ToastGeneralFailure
        };
        var actionKey = kind == NotificationSummaryKind.SingleCapability
            ? highestDefinition.ActionKey
            : RouteFilterResources.ToastActionReload;
        var offlineQualified = browserOffline && pending.Any(incident => incident.Issue.IsObservedNetworkFailure);
        var issueCodes = roots.Select(incident => incident.Issue.Code).OrderBy(code => code).ToArray();
        var explainedCodes = explained.Select(incident => incident.Issue.Code).OrderBy(code => code).ToArray();
        var capabilityNames = kind == NotificationSummaryKind.General
            ? Array.Empty<NotificationCapability>()
            : capabilities;

        return new NotificationSummary(
            severity,
            kind,
            severity == NotificationSeverity.Error ? RouteFilterResources.ToastSeverityError : RouteFilterResources.ToastSeverityWarning,
            impactKey,
            actionKey,
            RouteFilterResources.ToastClose,
            capabilityNames,
            issueCodes,
            offlineQualified ? RouteFilterResources.ToastOfflineQualifier : null)
        {
            ExplainedIssueCodes = explainedCodes
        };
    }

    public static NotificationCapability GetCapability(NotificationIssueCode code) => Definitions.TryGetValue(code, out var definition)
        ? definition.Capability
        : throw new ArgumentOutOfRangeException(nameof(code));

    public static NotificationSeverity GetSeverity(NotificationIssueCode code) => Definitions.TryGetValue(code, out var definition)
        ? definition.Severity
        : throw new ArgumentOutOfRangeException(nameof(code));

    public static string GetSeverityResourceKey(NotificationIssueCode code) => GetSeverity(code) == NotificationSeverity.Warning
        ? RouteFilterResources.ToastSeverityWarning
        : RouteFilterResources.ToastSeverityError;

    public static string GetImpactResourceKey(NotificationIssueCode code) => Definitions.TryGetValue(code, out var definition)
        ? definition.ImpactKey
        : throw new ArgumentOutOfRangeException(nameof(code));

    public static string GetActionResourceKey(NotificationIssueCode code) => Definitions.TryGetValue(code, out var definition)
        ? definition.ActionKey
        : throw new ArgumentOutOfRangeException(nameof(code));

    public static string GetCapabilityResourceKey(NotificationCapability capability) => capability switch
    {
        NotificationCapability.Live => RouteFilterResources.ToastCapabilityLive,
        NotificationCapability.Routes => RouteFilterResources.ToastCapabilityRoutes,
        NotificationCapability.Map => RouteFilterResources.ToastCapabilityMap,
        NotificationCapability.Audio => RouteFilterResources.ToastCapabilityAudio,
        NotificationCapability.Settings => RouteFilterResources.ToastCapabilitySettings,
        NotificationCapability.Navigation => RouteFilterResources.ToastCapabilityNavigation,
        _ => throw new ArgumentOutOfRangeException(nameof(capability))
    };

    public static bool IsKnownCode(NotificationIssueCode code) => Definitions.ContainsKey(code);

    public static bool IsConditionalAudioSettingsCode(NotificationIssueCode code) => code is
        NotificationIssueCode.AudioResumeFailed or
        NotificationIssueCode.RequiredVoicesPartiallyUnavailable or
        NotificationIssueCode.RequiredVoicesUnavailable or
        NotificationIssueCode.AudioTextureChangeFailed or
        NotificationIssueCode.AudioFilterFailed or
        NotificationIssueCode.AudioStopFailed or
        NotificationIssueCode.SettingsNotSaved or
        NotificationIssueCode.SettingsApplyFailed;

    public static bool IsConditionalDeliveryCode(NotificationIssueCode code) => code is
        NotificationIssueCode.LiveProcessingFailed or
        NotificationIssueCode.SelectedRouteUnavailable or
        NotificationIssueCode.MapStyleChangeFailed or
        NotificationIssueCode.MapTransitDisplayFailed or
        NotificationIssueCode.MapLayerUnavailable or
        NotificationIssueCode.DisplaySettingFailed or
        NotificationIssueCode.CityNavigationFailed;

    static IReadOnlyDictionary<NotificationIssueCode, IssueDefinition> CreateDefinitions() => new Dictionary<NotificationIssueCode, IssueDefinition>
    {
        [NotificationIssueCode.LiveConnectionFailed] = new(NotificationCapability.Live, NotificationSeverity.Error, RouteFilterResources.ToastLiveFailure, RouteFilterResources.ToastActionReload),
        [NotificationIssueCode.LiveJoinFailed] = new(NotificationCapability.Live, NotificationSeverity.Error, RouteFilterResources.ToastLiveFailure, RouteFilterResources.ToastActionReload),
        [NotificationIssueCode.LiveInterrupted] = new(NotificationCapability.Live, NotificationSeverity.Warning, RouteFilterResources.ToastLiveInterrupted, RouteFilterResources.ToastActionWaitForReconnect),
        [NotificationIssueCode.LiveStopped] = new(NotificationCapability.Live, NotificationSeverity.Error, RouteFilterResources.ToastLiveStopped, RouteFilterResources.ToastActionReload),
        [NotificationIssueCode.LiveResumeFailed] = new(NotificationCapability.Live, NotificationSeverity.Error, RouteFilterResources.ToastLiveResumeFailed, RouteFilterResources.ToastActionReload),
        [NotificationIssueCode.LiveVersionRejected] = new(NotificationCapability.Live, NotificationSeverity.Error, RouteFilterResources.ToastLiveVersionFailure, RouteFilterResources.ToastActionReload),
        [NotificationIssueCode.LiveProcessingFailed] = new(NotificationCapability.Live, NotificationSeverity.Error, RouteFilterResources.ToastLiveProcessingFailure, RouteFilterResources.ToastActionReload),
        [NotificationIssueCode.RouteLoadFailed] = new(NotificationCapability.Routes, NotificationSeverity.Error, RouteFilterResources.ToastRoutesFailure, RouteFilterResources.ToastActionReload),
        [NotificationIssueCode.RoutesUnavailable] = new(NotificationCapability.Routes, NotificationSeverity.Error, RouteFilterResources.ToastRoutesUnavailable, RouteFilterResources.ToastActionChooseCity),
        [NotificationIssueCode.SelectedRouteUnavailable] = new(NotificationCapability.Routes, NotificationSeverity.Warning, RouteFilterResources.ToastSelectedRouteUnavailable, RouteFilterResources.ToastActionChooseRoute),
        [NotificationIssueCode.MapUnavailable] = new(NotificationCapability.Map, NotificationSeverity.Error, RouteFilterResources.ToastMapFailure, RouteFilterResources.ToastActionReload),
        [NotificationIssueCode.MapUnsupportedBrowser] = new(NotificationCapability.Map, NotificationSeverity.Error, RouteFilterResources.ToastMapUnsupportedBrowser, RouteFilterResources.ToastActionTryAnotherBrowser),
        [NotificationIssueCode.MapStyleChangeFailed] = new(NotificationCapability.Map, NotificationSeverity.Warning, RouteFilterResources.ToastMapStyleFailure, RouteFilterResources.ToastActionTryAgain),
        [NotificationIssueCode.MapTransitDisplayFailed] = new(NotificationCapability.Map, NotificationSeverity.Error, RouteFilterResources.ToastMapTransitFailure, RouteFilterResources.ToastActionReload),
        [NotificationIssueCode.MapLayerUnavailable] = new(NotificationCapability.Map, NotificationSeverity.Warning, RouteFilterResources.ToastMapLayerFailure, RouteFilterResources.ToastActionTryAgain),
        [NotificationIssueCode.DisplaySettingFailed] = new(NotificationCapability.Settings, NotificationSeverity.Warning, RouteFilterResources.ToastDisplaySettingFailure, RouteFilterResources.ToastActionTryAgain),
        [NotificationIssueCode.CityNavigationFailed] = new(NotificationCapability.Navigation, NotificationSeverity.Error, RouteFilterResources.ToastNavigationFailure, RouteFilterResources.ToastActionTryAgain),
        [NotificationIssueCode.AudioResumeFailed] = new(NotificationCapability.Audio, NotificationSeverity.Warning, RouteFilterResources.ToastAudioResumeFailure, RouteFilterResources.ToastActionResumeAudio),
        [NotificationIssueCode.AudioUnavailable] = new(NotificationCapability.Audio, NotificationSeverity.Error, RouteFilterResources.ToastAudioFailure, RouteFilterResources.ToastActionReload),
        [NotificationIssueCode.RequiredVoicesPartiallyUnavailable] = new(NotificationCapability.Audio, NotificationSeverity.Warning, RouteFilterResources.ToastAudioPartialFailure, RouteFilterResources.ToastActionTryAgain),
        [NotificationIssueCode.RequiredVoicesUnavailable] = new(NotificationCapability.Audio, NotificationSeverity.Error, RouteFilterResources.ToastAudioFailure, RouteFilterResources.ToastActionReload),
        [NotificationIssueCode.AudioTextureChangeFailed] = new(NotificationCapability.Audio, NotificationSeverity.Warning, RouteFilterResources.ToastAudioTextureFailure, RouteFilterResources.ToastActionTryAgain),
        [NotificationIssueCode.AudioFilterFailed] = new(NotificationCapability.Audio, NotificationSeverity.Warning, RouteFilterResources.ToastAudioFilterFailure, RouteFilterResources.ToastActionTryAgain),
        [NotificationIssueCode.AudioStopFailed] = new(NotificationCapability.Audio, NotificationSeverity.Error, RouteFilterResources.ToastAudioStopFailure, RouteFilterResources.ToastActionCloseTab),
        [NotificationIssueCode.SettingsNotSaved] = new(NotificationCapability.Settings, NotificationSeverity.Warning, RouteFilterResources.ToastSettingsNotSaved, RouteFilterResources.ToastActionReviewSettings),
        [NotificationIssueCode.SettingsApplyFailed] = new(NotificationCapability.Settings, NotificationSeverity.Warning, RouteFilterResources.ToastSettingsApplyFailure, RouteFilterResources.ToastActionTryAgain)
    };

    sealed class Incident(NotificationIssue issue, long firstEvidenceTimestamp)
    {
        public NotificationIssue Issue { get; set; } = issue;
        public long FirstEvidenceTimestamp { get; } = firstEvidenceTimestamp;
        public bool Communicated { get; set; }
    }

    sealed record IssueDefinition(
        NotificationCapability Capability,
        NotificationSeverity Severity,
        string ImpactKey,
        string ActionKey);
}
