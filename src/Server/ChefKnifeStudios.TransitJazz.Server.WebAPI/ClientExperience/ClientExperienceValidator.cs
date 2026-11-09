#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ChefKnifeStudios.TransitJazz.Shared.ClientExperience;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.ClientExperience;

public sealed record ValidatedClientExperienceEvent(ClientExperienceEvent Event, bool ClockSkewed);
public sealed record ClientExperienceValidationResult(bool IsValid, string Reason, bool ClockSkewed = false);

public static partial class ClientExperienceValidator
{
    const long MaxDurationMs = 86_400_000;
    static readonly string[] PayloadProperties =
    [
        nameof(ClientExperienceEvent.AttemptId), nameof(ClientExperienceEvent.ParentActionId), nameof(ClientExperienceEvent.Journey),
        nameof(ClientExperienceEvent.Action), nameof(ClientExperienceEvent.Eligible), nameof(ClientExperienceEvent.DeadlineForegroundMs),
        nameof(ClientExperienceEvent.Outcome), nameof(ClientExperienceEvent.Reason), nameof(ClientExperienceEvent.ForegroundElapsedMs),
        nameof(ClientExperienceEvent.WallElapsedMs), nameof(ClientExperienceEvent.Capability), nameof(ClientExperienceEvent.PreviousState),
        nameof(ClientExperienceEvent.CurrentState), nameof(ClientExperienceEvent.EpisodeId), nameof(ClientExperienceEvent.OwnerGeneration),
        nameof(ClientExperienceEvent.InitialSnapshot), nameof(ClientExperienceEvent.WindowSequence), nameof(ClientExperienceEvent.WindowStartMs),
        nameof(ClientExperienceEvent.WindowEndMs), nameof(ClientExperienceEvent.ObservedMs), nameof(ClientExperienceEvent.GapMs),
        nameof(ClientExperienceEvent.ExcludedMs), nameof(ClientExperienceEvent.StateDurations), nameof(ClientExperienceEvent.PresentationId),
        nameof(ClientExperienceEvent.Severity), nameof(ClientExperienceEvent.SummaryKind), nameof(ClientExperienceEvent.CapabilitiesPresented),
        nameof(ClientExperienceEvent.IssueCodes), nameof(ClientExperienceEvent.ExplainedIssueCodes), nameof(ClientExperienceEvent.ResourceKeys),
        nameof(ClientExperienceEvent.LinkedEpisodeIds), nameof(ClientExperienceEvent.IsVisible), nameof(ClientExperienceEvent.DeliveryRequested),
        nameof(ClientExperienceEvent.AudioRequested), nameof(ClientExperienceEvent.ExclusionReason),
        nameof(ClientExperienceEvent.BootstrapStartOffsetMs), nameof(ClientExperienceEvent.RuntimeCategory)
    ];

    public static ClientExperienceValidationResult Validate(ClientExperienceEvent value, DateTimeOffset now, IReadOnlySet<string>? allowedCities = null)
    {
        if (value.SchemaVersion != ClientExperienceCatalog.SchemaVersion || value.MeasurementVersion != ClientExperienceCatalog.MeasurementVersion)
            return Invalid("unsupported_version");
        if (!ClientExperienceCatalog.EventKinds.Contains(value.EventKind)) return Invalid("unknown_event_kind");
        if (value.EventId == Guid.Empty || value.PageSessionId == Guid.Empty || value.CityActivationId == Guid.Empty ||
            value.AttemptId == Guid.Empty || value.ParentActionId == Guid.Empty || value.EpisodeId == Guid.Empty ||
            value.PresentationId == Guid.Empty || (value.LinkedEpisodeIds?.Any(x => x == Guid.Empty) ?? false))
            return Invalid("invalid_uuid");
        if (value.Sequence < 0 || value.PageElapsedMs < 0 || value.PageElapsedMs > MaxDurationMs)
            return Invalid("invalid_counter");
        if (value.CitySlug is null || !CitySlugRegex().IsMatch(value.CitySlug)
            || allowedCities is not null && value.CitySlug != "unknown" && !allowedCities.Contains(value.CitySlug)
            || value.ClientRelease is null || value.ClientRelease.Length is < 1 or > 64 || !ReleaseRegex().IsMatch(value.ClientRelease))
            return Invalid("invalid_context");
        if (value.OccurredAtUtc < DateTimeOffset.UnixEpoch || value.OccurredAtUtc > now.AddDays(30)) return Invalid("invalid_time");
        if (!PayloadMatchesKind(value)) return Invalid("invalid_payload");

        var skewed = (now - value.OccurredAtUtc).Duration() > TimeSpan.FromMinutes(5);
        return ValidateKind(value) ?? new(true, "", skewed);
    }

    public static bool ValidateBatch(ClientExperienceBatch batch, int maximumEvents, out string reason)
    {
        if (batch.SchemaVersion != ClientExperienceCatalog.SchemaVersion) { reason = "unsupported_version"; return false; }
        if (batch.Events is null || batch.Events.Count is < 1 || batch.Events.Count > maximumEvents) { reason = "invalid_batch_size"; return false; }
        if (batch.LostCounts is { Count: > 20 }) { reason = "invalid_loss_summary"; return false; }
        if (batch.LostCounts?.Any(x => !ClientExperienceCatalog.EventKinds.Contains(x.EventKind) || x.Count is < 1 or > 100_000 || !ClientExperienceCatalog.LossReasons.Contains(x.Reason)) == true)
        { reason = "invalid_loss_summary"; return false; }
        reason = "";
        return true;
    }

    static ClientExperienceValidationResult? ValidateKind(ClientExperienceEvent e)
    {
        switch (e.EventKind)
        {
            case "session_started":
                if (e.BootstrapStartOffsetMs is null or < 0 or > 86_400_000) return Invalid("invalid_session_start");
                break;
            case "context_changed":
                if (e.IsVisible is null && e.DeliveryRequested is null && e.AudioRequested is null && e.ExclusionReason is null)
                    return Invalid("invalid_context_change");
                if (e.ExclusionReason is not null && !ClientExperienceCatalog.ExclusionReasons.Contains(e.ExclusionReason)) return Invalid("invalid_reason");
                break;
            case "attempt_started":
                if (e.AttemptId is null || (e.Journey is null && e.Action is null) || e.Eligible is null || e.DeadlineForegroundMs is null or < 1 or > MaxDurationMs)
                    return Invalid("invalid_attempt_start");
                if (e.Journey is not null && !ClientExperienceCatalog.Journeys.Contains(e.Journey)) return Invalid("invalid_journey");
                if (e.Action is not null && !ClientExperienceCatalog.Actions.Contains(e.Action)) return Invalid("invalid_action");
                break;
            case "attempt_finished":
                if (e.AttemptId is null || (e.Journey is null && e.Action is null) || e.Outcome is null || !ClientExperienceCatalog.Outcomes.Contains(e.Outcome) || e.Reason is null || !ClientExperienceCatalog.Reasons.Contains(e.Reason) || !Duration(e.ForegroundElapsedMs) || !Duration(e.WallElapsedMs))
                    return Invalid("invalid_attempt_result");
                break;
            case "capability_changed":
                if (e.Capability is null || !ClientExperienceCatalog.Capabilities.Contains(e.Capability) || e.CurrentState is null || !ClientExperienceCatalog.States.Contains(e.CurrentState) || e.PreviousState is not null && !ClientExperienceCatalog.States.Contains(e.PreviousState) || e.EpisodeId is null || e.OwnerGeneration is null or < 0 || e.Reason is null || !ClientExperienceCatalog.Reasons.Contains(e.Reason))
                    return Invalid("invalid_capability_change");
                break;
            case "observation_window":
                if (e.WindowSequence is null or < 0 || e.WindowStartMs is null or < 0 || e.WindowEndMs is null || e.WindowEndMs <= e.WindowStartMs || !Duration(e.ObservedMs) || !Duration(e.GapMs) || !Duration(e.ExcludedMs) || e.StateDurations is null or { Count: < 1 or > 24 })
                    return Invalid("invalid_window");
                if (e.WindowEndMs - e.WindowStartMs != e.ObservedMs + e.GapMs + e.ExcludedMs) return Invalid("invalid_window_partition");
                if (e.StateDurations.Any(x => !ClientExperienceCatalog.Capabilities.Contains(x.Capability) || !ClientExperienceCatalog.States.Contains(x.State) || x.DurationMs < 0 || x.DurationMs > MaxDurationMs)) return Invalid("invalid_window_state");
                var perCapability = e.StateDurations.GroupBy(x => x.Capability);
                if (!ClientExperienceCatalog.Capabilities.All(capability => perCapability.Any(group => group.Key == capability))) return Invalid("incomplete_window_capabilities");
                if (perCapability.Any(group => group.Sum(x => x.DurationMs) != e.ObservedMs)) return Invalid("invalid_window_partition");
                if (e.StateDurations.GroupBy(x => (x.Capability, x.State)).Any(g => g.Count() > 1)) return Invalid("duplicate_window_state");
                break;
            case "toast_presented":
                if (e.PresentationId is null || e.Severity is null || !ClientExperienceCatalog.Severities.Contains(e.Severity) || e.SummaryKind is null || !ClientExperienceCatalog.SummaryKinds.Contains(e.SummaryKind) || !BoundedCodes(e.CapabilitiesPresented, ClientExperienceCatalog.Capabilities, 6) || !BoundedCodes(e.IssueCodes, ClientExperienceCatalog.NotificationIssues, 26) || !BoundedCodes(e.ExplainedIssueCodes, ClientExperienceCatalog.NotificationIssues, 26) || !BoundedStrings(e.ResourceKeys, 16, 64))
                    return Invalid("invalid_presentation");
                break;
            case "runtime_failed":
                if (e.RuntimeCategory is null || !ClientExperienceCatalog.RuntimeFailureCategories.Contains(e.RuntimeCategory)) return Invalid("invalid_runtime_failure");
                break;
        }
        return null;
    }

    static bool PayloadMatchesKind(ClientExperienceEvent e)
    {
        var allowed = e.EventKind switch
        {
            "session_started" => new[] { nameof(e.BootstrapStartOffsetMs) },
            "context_changed" => new[] { nameof(e.IsVisible), nameof(e.DeliveryRequested), nameof(e.AudioRequested), nameof(e.ExclusionReason) },
            "attempt_started" => new[] { nameof(e.AttemptId), nameof(e.ParentActionId), nameof(e.Journey), nameof(e.Action), nameof(e.Eligible), nameof(e.DeadlineForegroundMs) },
            "attempt_finished" => new[] { nameof(e.AttemptId), nameof(e.ParentActionId), nameof(e.Journey), nameof(e.Action), nameof(e.Outcome), nameof(e.Reason), nameof(e.ForegroundElapsedMs), nameof(e.WallElapsedMs) },
            "capability_changed" => new[] { nameof(e.Capability), nameof(e.PreviousState), nameof(e.CurrentState), nameof(e.EpisodeId), nameof(e.OwnerGeneration), nameof(e.InitialSnapshot), nameof(e.Reason) },
            "observation_window" => new[] { nameof(e.WindowSequence), nameof(e.WindowStartMs), nameof(e.WindowEndMs), nameof(e.ObservedMs), nameof(e.GapMs), nameof(e.ExcludedMs), nameof(e.StateDurations) },
            "toast_presented" => new[] { nameof(e.PresentationId), nameof(e.Severity), nameof(e.SummaryKind), nameof(e.CapabilitiesPresented), nameof(e.IssueCodes), nameof(e.ExplainedIssueCodes), nameof(e.ResourceKeys), nameof(e.LinkedEpisodeIds) },
            "runtime_failed" => new[] { nameof(e.RuntimeCategory) },
            _ => []
        };
        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        return PayloadProperties.Where(name => ReadPayloadValue(e, name) is not null).All(allowedSet.Contains);
    }

    static object? ReadPayloadValue(ClientExperienceEvent e, string name)
        => typeof(ClientExperienceEvent).GetProperty(name)?.GetValue(e);
    static bool Duration(long? value) => value is >= 0 and <= MaxDurationMs;
    static bool BoundedCodes(IReadOnlyList<string>? values, IReadOnlySet<string> catalog, int max)
        => values is not null && values.Count <= max && values.All(x => x.Length <= 64 && catalog.Contains(x));
    static bool BoundedStrings(IReadOnlyList<string>? values, int maxCount, int maxLength)
        => values is not null && values.Count <= maxCount && values.All(x => !string.IsNullOrWhiteSpace(x) && x.Length <= maxLength && ResourceKeyRegex().IsMatch(x));
    static ClientExperienceValidationResult Invalid(string reason) => new(false, reason);

    [GeneratedRegex("^(unknown|[a-z0-9][a-z0-9-]{0,62})$")]
    private static partial Regex CitySlugRegex();
    [GeneratedRegex("^[a-zA-Z0-9._-]{1,64}$")]
    private static partial Regex ReleaseRegex();
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*(?:[._-][A-Za-z0-9]+)*$")]
    private static partial Regex ResourceKeyRegex();
}
