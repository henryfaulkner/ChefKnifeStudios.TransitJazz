using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;

/// <summary>Emits validated sparse events directly through ILogger; no second transport is used.</summary>
public sealed class StructuredEventEmitter : IWorkerStructuredEventLogger
{
    static readonly HashSet<string> CoalescedNames =
    [
        nameof(StructuredLogEventName.CityInputFailed),
        nameof(StructuredLogEventName.CityInputPartial),
        nameof(StructuredLogEventName.CityInputEmpty),
        nameof(StructuredLogEventName.RouteIndexUnavailable),
        nameof(StructuredLogEventName.RouteIndexLoadFailed),
        nameof(StructuredLogEventName.CityCycleAnomaly),
        nameof(StructuredLogEventName.PublishFailed),
        nameof(StructuredLogEventName.WorkerCycleFailed),
    ];

    readonly ILogger _logger;
    readonly StructuredEventPolicy _policy;
    readonly StructuredLoggingOptions _options;

    public StructuredEventEmitter(
        ILogger<StructuredEventEmitter> logger,
        IOptions<StructuredLoggingOptions> options,
        StructuredEventPolicy policy)
    {
        _logger = logger;
        _options = options.Value;
        _policy = policy;
    }

    public void Emit(StructuredLogEvent logEvent)
    {
        if (!_options.Enabled) return;
        var safe = StructuredLogRedactor.Validate(Enrich(logEvent));
        var decision = CoalescedNames.Contains(safe.EventName)
            && _options.CoalesceRepeatedConditions
            ? _policy.Decide(safe)
            : new StructuredEventPolicyDecision(true, StructuredEventEmissionKind.Initial, "", DateTimeOffset.UtcNow);
        if (!decision.ShouldEmit) return;
        Write(safe);
    }

    public void EmitRecovery(StructuredLogEvent recoveryEvent, string recoveredEventName, string? recoveredReasonCode)
    {
        if (!_options.Enabled) return;
        var safe = StructuredLogRedactor.Validate(Enrich(recoveryEvent));
        var decision = _options.CoalesceRepeatedConditions
            ? _policy.DecideRecovery(safe.City, recoveredEventName, recoveredReasonCode)
            : new StructuredEventPolicyDecision(true, StructuredEventEmissionKind.Recovery, "", DateTimeOffset.UtcNow);
        if (decision.ShouldEmit) Write(safe);
    }

    void Write(StructuredLogEvent logEvent)
    {
        var level = logEvent.Outcome == nameof(StructuredLogOutcome.Failed)
            && !(logEvent.EventName == nameof(StructuredLogEventName.CityCycleAnomaly)
                && logEvent.ReasonCode == nameof(StructuredLogReasonCode.DUPLICATE_FEED))
            ? LogLevel.Warning
            : LogLevel.Information;
        _logger.Log(level, new EventId(GetEventId(logEvent.EventName), logEvent.EventName),
            logEvent.ToLogState(), null, static (state, _) => state.First(kvp => kvp.Key == "EventName").Value?.ToString() ?? "StructuredLogEvent");
    }

    StructuredLogEvent Enrich(StructuredLogEvent logEvent) =>
        logEvent.DeploymentRevision is null && !string.IsNullOrWhiteSpace(_options.DeploymentRevision)
            ? logEvent with { DeploymentRevision = _options.DeploymentRevision }
            : logEvent;

    static int GetEventId(string eventName) => eventName.GetHashCode(StringComparison.Ordinal);
}
