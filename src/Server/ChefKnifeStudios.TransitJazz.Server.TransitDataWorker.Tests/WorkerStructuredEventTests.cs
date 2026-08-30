using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests;

public sealed class WorkerStructuredEventTests
{
    [Fact]
    public void LifecycleEvents_AreTypedAndBounded()
    {
        using var provider = new InMemoryJsonLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var emitter = CreateEmitter(loggerFactory, TimeProvider.System);

        emitter.Emit(StructuredLogEvent.Create(StructuredLogEventName.WorkerStarted, StructuredLogOutcome.Succeeded,
            StructuredLogEvent.NewEventId()));
        emitter.Emit(StructuredLogEvent.Create(StructuredLogEventName.WorkerStopped, StructuredLogOutcome.Succeeded,
            StructuredLogEvent.NewEventId()));

        Assert.Equal(2, provider.Entries.Count);
        Assert.All(provider.Entries, entry =>
        {
            Assert.Contains("EventName", entry.Fields.Keys);
            Assert.Contains("EventVersion", entry.Fields.Keys);
            Assert.Contains("EventId", entry.Fields.Keys);
            Assert.Contains("Outcome", entry.Fields.Keys);
            Assert.DoesNotContain("Exception", entry.Json, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void InputRouteAnomalyPublishAndWorkerCycleEvents_CarryCycleAndCityCorrelation()
    {
        using var provider = new InMemoryJsonLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var emitter = CreateEmitter(loggerFactory, TimeProvider.System, TimeSpan.Zero);
        var cycleId = "cycle-054";

        foreach (var nameAndReason in new[]
        {
            (StructuredLogEventName.CityInputFailed, StructuredLogReasonCode.INPUT_FAILED),
            (StructuredLogEventName.RouteIndexUnavailable, StructuredLogReasonCode.ROUTE_INDEX_UNAVAILABLE),
            (StructuredLogEventName.CityCycleAnomaly, StructuredLogReasonCode.NO_CROSSINGS),
            (StructuredLogEventName.PublishFailed, StructuredLogReasonCode.PUBLISH_FAILED),
            (StructuredLogEventName.WorkerCycleFailed, StructuredLogReasonCode.WORKER_CYCLE_FAILED),
        })
        {
            var city = nameAndReason.Item1 is StructuredLogEventName.WorkerCycleFailed ? null : "atlanta";
            emitter.Emit(StructuredLogEvent.Create(nameAndReason.Item1, StructuredLogOutcome.Failed,
                StructuredLogEvent.NewEventId(), cycleId, city, nameAndReason.Item2));
        }

        Assert.Equal(5, provider.Entries.Count);
        Assert.All(provider.Entries, entry => Assert.Equal(cycleId, entry.Fields["CycleId"]));
        Assert.Equal(4, provider.Entries.Count(entry => entry.Fields.ContainsKey("City")));
    }

    [Fact]
    public void RecoveryEvents_AreOnlyEmittedForAnActiveCondition()
    {
        using var provider = new InMemoryJsonLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var emitter = CreateEmitter(loggerFactory, TimeProvider.System);
        var cycleId = "cycle-recovery";
        var failure = StructuredLogEvent.Create(StructuredLogEventName.PublishFailed, StructuredLogOutcome.Failed,
            StructuredLogEvent.NewEventId(), cycleId, "atlanta", StructuredLogReasonCode.PUBLISH_FAILED);
        var recovery = StructuredLogEvent.Create(StructuredLogEventName.PublishRecovered, StructuredLogOutcome.Succeeded,
            StructuredLogEvent.NewEventId(), cycleId, "atlanta", StructuredLogReasonCode.PUBLISH_FAILED);

        emitter.EmitRecovery(recovery, nameof(StructuredLogEventName.PublishFailed), StructuredLogReasonCode.PUBLISH_FAILED.ToString());
        emitter.Emit(failure);
        emitter.EmitRecovery(recovery, nameof(StructuredLogEventName.PublishFailed), StructuredLogReasonCode.PUBLISH_FAILED.ToString());

        Assert.Equal(2, provider.Entries.Count);
        Assert.Equal(nameof(StructuredLogEventName.PublishFailed), provider.Entries[0].Fields["EventName"]);
        Assert.Equal(nameof(StructuredLogEventName.PublishRecovered), provider.Entries[1].Fields["EventName"]);
    }

    [Fact]
    public void RouteIndexLoadEvents_IncludeConfiguredDeploymentRevision()
    {
        using var provider = new InMemoryJsonLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var emitter = new StructuredEventEmitter(
            loggerFactory.CreateLogger<StructuredEventEmitter>(),
            Options.Create(new StructuredLoggingOptions { DeploymentRevision = "marta-jazz-dev--0000165" }),
            new StructuredEventPolicy());

        emitter.Emit(StructuredLogEvent.Create(
            StructuredLogEventName.RouteIndexLoaded,
            StructuredLogOutcome.Succeeded,
            StructuredLogEvent.NewEventId(),
            context: new StructuredLogDiagnosticContext { DurationMs = 45, CityCount = 7, RouteCount = 412 }));

        var entry = Assert.Single(provider.Entries);
        Assert.Equal("marta-jazz-dev--0000165", entry.Fields["DeploymentRevision"]);
        Assert.Equal(412, entry.Fields["RouteCount"]);
    }

    static StructuredEventEmitter CreateEmitter(ILoggerFactory loggerFactory, TimeProvider clock, TimeSpan? reminderInterval = null) =>
        new(
            loggerFactory.CreateLogger<StructuredEventEmitter>(),
            Options.Create(new StructuredLoggingOptions { ReminderInterval = reminderInterval ?? TimeSpan.FromMinutes(15) }),
            new StructuredEventPolicy(clock, reminderInterval ?? TimeSpan.FromMinutes(15)));
}
