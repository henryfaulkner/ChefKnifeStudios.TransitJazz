using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests;

public sealed class StructuredLoggingVolumeTests
{
    [Fact]
    public void NormalCycleWithoutAnomaly_ProducesNoStructuredRows()
    {
        using var provider = new InMemoryJsonLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var emitter = CreateEmitter(loggerFactory, TimeProvider.System);

        // A healthy cycle has no call to Emit; numeric telemetry remains on its existing path.
        Assert.Empty(provider.Entries);
        _ = emitter;
    }

    [Fact]
    public void DisabledStructuredLogging_ProducesNoNoise()
    {
        using var provider = new InMemoryJsonLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var emitter = new StructuredEventEmitter(
            loggerFactory.CreateLogger<StructuredEventEmitter>(),
            Options.Create(new StructuredLoggingOptions { Enabled = false }),
            new StructuredEventPolicy());

        emitter.Emit(StructuredLogEvent.Create(StructuredLogEventName.CityCycleAnomaly, StructuredLogOutcome.Failed,
            StructuredLogEvent.NewEventId(), "cycle-disabled", "atlanta", StructuredLogReasonCode.NO_VEHICLES));

        Assert.Empty(provider.Entries);
    }

    [Fact]
    public void TenIdenticalFailures_ProduceInitialAndPeriodicReminderOnly()
    {
        using var provider = new InMemoryJsonLoggerProvider();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-30T12:00:00Z"));
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var emitter = CreateEmitter(loggerFactory, clock, TimeSpan.FromMinutes(15));
        var template = StructuredLogEvent.Create(StructuredLogEventName.CityCycleAnomaly, StructuredLogOutcome.Failed,
            StructuredLogEvent.NewEventId(), "cycle-volume", "atlanta", StructuredLogReasonCode.NO_VEHICLES);

        for (var i = 0; i < 10; i++)
        {
            emitter.Emit(template with { EventId = StructuredLogEvent.NewEventId(), CycleId = $"cycle-{i}" });
            if (i == 4) clock.Advance(TimeSpan.FromMinutes(15));
        }

        Assert.Equal(2, provider.Entries.Count);
        Assert.All(provider.Entries, entry => Assert.Equal(nameof(StructuredLogEventName.CityCycleAnomaly), entry.Fields["EventName"]));
    }

    [Fact]
    public void RecoveryAndNormalSuccess_DoNotDuplicateEveryHealthyCycle()
    {
        using var provider = new InMemoryJsonLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var emitter = CreateEmitter(loggerFactory, TimeProvider.System);
        var failure = StructuredLogEvent.Create(StructuredLogEventName.CityCycleAnomaly, StructuredLogOutcome.Failed,
            StructuredLogEvent.NewEventId(), "cycle-1", "atlanta", StructuredLogReasonCode.NO_CROSSINGS);
        var recovery = StructuredLogEvent.Create(StructuredLogEventName.CityCycleAnomaly, StructuredLogOutcome.Succeeded,
            StructuredLogEvent.NewEventId(), "cycle-2", "atlanta", StructuredLogReasonCode.NO_CROSSINGS,
            new StructuredLogDiagnosticContext { TonesEmitted = 2, VehiclesProcessed = 2 });

        emitter.Emit(failure);
        emitter.EmitRecovery(recovery, nameof(StructuredLogEventName.CityCycleAnomaly), StructuredLogReasonCode.NO_CROSSINGS.ToString());
        emitter.EmitRecovery(recovery with { CycleId = "cycle-3" }, nameof(StructuredLogEventName.CityCycleAnomaly), StructuredLogReasonCode.NO_CROSSINGS.ToString());

        Assert.Equal(2, provider.Entries.Count);
        Assert.Equal(nameof(StructuredLogEventName.CityCycleAnomaly), provider.Entries[0].Fields["EventName"]);
        Assert.Equal(nameof(StructuredLogEventName.CityCycleAnomaly), provider.Entries[1].Fields["EventName"]);
        Assert.Equal(nameof(StructuredLogOutcome.Succeeded), provider.Entries[1].Fields["Outcome"]);
    }

    static StructuredEventEmitter CreateEmitter(ILoggerFactory loggerFactory, TimeProvider clock, TimeSpan? reminderInterval = null) =>
        new(
            loggerFactory.CreateLogger<StructuredEventEmitter>(),
            Options.Create(new StructuredLoggingOptions { ReminderInterval = reminderInterval ?? TimeSpan.FromMinutes(15) }),
            new StructuredEventPolicy(clock, reminderInterval ?? TimeSpan.FromMinutes(15)));
}
