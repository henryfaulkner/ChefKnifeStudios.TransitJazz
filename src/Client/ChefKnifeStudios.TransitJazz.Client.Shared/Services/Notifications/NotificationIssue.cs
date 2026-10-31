using System;
using System.Collections.Generic;
using System.Linq;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;

public enum NotificationCapability
{
    Live,
    Routes,
    Map,
    Audio,
    Settings,
    Navigation
}

public enum NotificationSeverity
{
    Warning,
    Error
}

/// <summary>Fixed user-impact codes. Dynamic IDs and diagnostic text never form an incident code.</summary>
public enum NotificationIssueCode
{
    LiveConnectionFailed,
    LiveJoinFailed,
    LiveInterrupted,
    LiveStopped,
    LiveResumeFailed,
    LiveVersionRejected,
    LiveProcessingFailed,
    RouteLoadFailed,
    RoutesUnavailable,
    SelectedRouteUnavailable,
    MapUnavailable,
    MapUnsupportedBrowser,
    MapStyleChangeFailed,
    MapTransitDisplayFailed,
    MapLayerUnavailable,
    DisplaySettingFailed,
    CityNavigationFailed,
    AudioResumeFailed,
    AudioUnavailable,
    RequiredVoicesPartiallyUnavailable,
    RequiredVoicesUnavailable,
    AudioTextureChangeFailed,
    AudioFilterFailed,
    AudioStopFailed,
    SettingsNotSaved,
    SettingsApplyFailed
}

public enum NotificationSummaryKind
{
    SingleCapability,
    MultipleCapabilities,
    General
}

/// <summary>An opaque page/city generation. It is never used in visitor-facing copy.</summary>
public readonly record struct NotificationScope(long PageGeneration, string CitySlug);

/// <summary>
/// A bounded owner report. Reasons are a closed enum; detailed exceptions remain with their owner.
/// </summary>
public readonly record struct NotificationIssue(
    NotificationIssueCode Code,
    NotificationScope Scope = default,
    long OperationGeneration = 0,
    bool IsLocallyOwned = false,
    bool IsObservedNetworkFailure = false,
    NotificationIssueCode? ConfirmedCause = null,
    bool IsVerifiedAudioStillPlaying = false);

/// <summary>A fresh summary candidate built from current fixed-code incidents and resource keys.</summary>
public sealed record NotificationSummary(
    NotificationSeverity Severity,
    NotificationSummaryKind Kind,
    string SeverityKey,
    string ImpactKey,
    string ActionKey,
    string CloseKey,
    IReadOnlyList<NotificationCapability> Capabilities,
    IReadOnlyList<NotificationIssueCode> IssueCodes,
    string? OfflineQualifierKey)
{
    public string MessageKey => ImpactKey;
    public bool IsOfflineQualified => OfflineQualifierKey is not null;
    public IReadOnlyList<NotificationIssueCode> ExplainedIssueCodes { get; init; } = Array.Empty<NotificationIssueCode>();
    public IReadOnlyList<string> CapabilityKeys => Capabilities.Select(NotificationPolicy.GetCapabilityResourceKey).ToArray();
}
