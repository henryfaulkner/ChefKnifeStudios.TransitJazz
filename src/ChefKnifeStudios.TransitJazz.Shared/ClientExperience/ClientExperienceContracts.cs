using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ChefKnifeStudios.TransitJazz.Shared.ClientExperience;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClientExperienceEvent
{
    public required int SchemaVersion { get; init; }
    public required int MeasurementVersion { get; init; }
    public required string EventKind { get; init; }
    public required Guid EventId { get; init; }
    public required Guid PageSessionId { get; init; }
    public Guid? CityActivationId { get; init; }
    public required long Sequence { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required long PageElapsedMs { get; init; }
    public required string CitySlug { get; init; }
    public required string ClientRelease { get; init; }

    public Guid? AttemptId { get; init; }
    public Guid? ParentActionId { get; init; }
    public string? Journey { get; init; }
    public string? Action { get; init; }
    public bool? Eligible { get; init; }
    public long? DeadlineForegroundMs { get; init; }
    public string? Outcome { get; init; }
    public string? Reason { get; init; }
    public long? ForegroundElapsedMs { get; init; }
    public long? WallElapsedMs { get; init; }

    public string? Capability { get; init; }
    public string? PreviousState { get; init; }
    public string? CurrentState { get; init; }
    public Guid? EpisodeId { get; init; }
    public long? OwnerGeneration { get; init; }
    public bool? InitialSnapshot { get; init; }

    public long? WindowSequence { get; init; }
    public long? WindowStartMs { get; init; }
    public long? WindowEndMs { get; init; }
    public long? ObservedMs { get; init; }
    public long? GapMs { get; init; }
    public long? ExcludedMs { get; init; }
    public IReadOnlyList<ClientExperienceStateDuration>? StateDurations { get; init; }

    public Guid? PresentationId { get; init; }
    public string? Severity { get; init; }
    public string? SummaryKind { get; init; }
    public IReadOnlyList<string>? CapabilitiesPresented { get; init; }
    public IReadOnlyList<string>? IssueCodes { get; init; }
    public IReadOnlyList<string>? ExplainedIssueCodes { get; init; }
    public IReadOnlyList<string>? ResourceKeys { get; init; }
    public IReadOnlyList<Guid>? LinkedEpisodeIds { get; init; }

    public bool? IsVisible { get; init; }
    public bool? DeliveryRequested { get; init; }
    public bool? AudioRequested { get; init; }
    public string? ExclusionReason { get; init; }
    public long? BootstrapStartOffsetMs { get; init; }
    public string? RuntimeCategory { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClientExperienceStateDuration
{
    public required string Capability { get; init; }
    public required string State { get; init; }
    public required long DurationMs { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClientExperienceLostCount
{
    public required string EventKind { get; init; }
    public required int Count { get; init; }
    public required string Reason { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClientExperienceBatch
{
    public required int SchemaVersion { get; init; }
    public required IReadOnlyList<ClientExperienceEvent> Events { get; init; }
    public IReadOnlyList<ClientExperienceLostCount>? LostCounts { get; init; }
}

public sealed record ClientExperienceRejectedEvent(Guid EventId, string Reason);
public sealed record ClientExperienceBatchAcknowledgement(
    IReadOnlyList<Guid> AcknowledgedEventIds,
    IReadOnlyList<ClientExperienceRejectedEvent> RejectedEvents);
