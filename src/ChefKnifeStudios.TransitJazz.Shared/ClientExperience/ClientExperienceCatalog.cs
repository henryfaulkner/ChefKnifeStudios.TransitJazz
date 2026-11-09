using System;
using System.Collections.Generic;
using System.Linq;

namespace ChefKnifeStudios.TransitJazz.Shared.ClientExperience;

/// <summary>Versioned, closed vocabulary for anonymous client-experience observations.</summary>
public static class ClientExperienceCatalog
{
    public const int SchemaVersion = 1;
    public const int MeasurementVersion = 1;

    public static readonly IReadOnlySet<string> EventKinds = Set(
        "session_started", "context_changed", "attempt_started", "attempt_finished",
        "capability_changed", "observation_window", "toast_presented", "runtime_failed");

    public static readonly IReadOnlySet<string> Journeys = Set(
        "bootstrap_start", "city_map_start", "live_start", "live_resume", "audio_unlock", "audio_resume",
        "settings_effect", "settings_persistence", "audio_stop", "city_navigation");

    public static readonly IReadOnlySet<string> Actions = Set(
        "display_setting", "map_style", "audio_filter", "audio_texture", "voice_selection",
        "delivery_stop", "audio_stop", "audio_setting", "city_navigation");

    public static readonly IReadOnlySet<string> Capabilities = Set("routes", "map", "live_delivery", "audio");
    public static readonly IReadOnlySet<string> States = Set("pending", "healthy", "degraded", "unavailable", "unknown", "not_requested");
    public static readonly IReadOnlySet<string> Outcomes = Set("succeeded", "failed", "deadline_exceeded", "cancelled", "superseded");
    public static readonly IReadOnlySet<string> Severities = Set("warning", "error");
    public static readonly IReadOnlySet<string> SummaryKinds = Set("single_capability", "multiple_capabilities", "general");
    public static readonly IReadOnlySet<string> Reasons = Set(
        "owner_confirmed", "owner_failed", "deadline", "user_cancelled", "superseded", "not_requested",
        "hidden", "muted", "paused", "suspended", "stale_generation", "unknown", "still_playing",
        "session_only", "save_failed", "recovered", "scope_ended", "bootstrap_fatal", "other_fixed");
    public static readonly IReadOnlySet<string> ExclusionReasons = Set("hidden", "muted", "paused", "not_requested", "scope_ended");
    public static readonly IReadOnlySet<string> RuntimeFailureCategories = Set("bootstrap_fatal", "blazor_fatal_ui", "application_initialization_fatal");
    public static readonly IReadOnlySet<string> LossReasons = Set("queue_capacity", "queue_age", "storage_unavailable", "oversize_event", "invalid_event", "memory_limit");

    // Kept explicit so changes to the UI issue enum are reviewed as wire-contract changes.
    public static readonly IReadOnlySet<string> NotificationIssues = Set(
        "LiveConnectionFailed", "LiveJoinFailed", "LiveInterrupted", "LiveStopped", "LiveResumeFailed",
        "LiveVersionRejected", "LiveProcessingFailed", "RouteLoadFailed", "RoutesUnavailable",
        "SelectedRouteUnavailable", "MapUnavailable", "MapUnsupportedBrowser", "MapStyleChangeFailed",
        "MapTransitDisplayFailed", "MapLayerUnavailable", "DisplaySettingFailed", "CityNavigationFailed",
        "AudioResumeFailed", "AudioUnavailable", "RequiredVoicesPartiallyUnavailable", "RequiredVoicesUnavailable",
        "AudioTextureChangeFailed", "AudioFilterFailed", "AudioStopFailed", "SettingsNotSaved", "SettingsApplyFailed");

    static IReadOnlySet<string> Set(params string[] values) => new HashSet<string>(values, StringComparer.Ordinal);

    public static string NotificationIssueCode(Enum issue) => issue.ToString();

    public static bool ContainsNotificationIssues(IEnumerable<string> values)
        => values.All(NotificationIssues.Contains);
}
