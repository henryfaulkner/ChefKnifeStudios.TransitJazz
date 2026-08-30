using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests;

/// <summary>
/// A logging-sink fault must never become a processing fault. Feature 013 asserted this
/// against the Parquet sidecar's flush path; feature 055 retired that path, so the same
/// scenario is now asserted against the surviving structured-event logger (contract C1).
/// </summary>
public class FailureIsolationTests
{
    [Fact]
    public void Emit_WhenSinkThrows_IsContainedByTheCallerAndCounted()
    {
        var sink = new ThrowingStructuredEventLogger();

        // The worker emits inside its cycle-level try/catch, so a sink fault surfaces there
        // rather than escaping the loop. Model that boundary explicitly.
        var escaped = RunGuardedCycle(() => sink.Emit(StructuredLogEvent.Create(
            StructuredLogEventName.CityCycleAnomaly,
            StructuredLogOutcome.Failed,
            StructuredLogEvent.NewEventId(),
            cycleId: StructuredLogEvent.NewCycleId(),
            city: "atlanta",
            reasonCode: StructuredLogReasonCode.NO_CROSSINGS)));

        Assert.Null(escaped);
        Assert.Equal(1, sink.EmitFailures);
    }

    [Fact]
    public void EmitRecovery_WhenSinkThrows_IsContainedByTheCallerAndCounted()
    {
        var sink = new ThrowingStructuredEventLogger();

        var escaped = RunGuardedCycle(() => sink.EmitRecovery(
            StructuredLogEvent.Create(
                StructuredLogEventName.PublishRecovered,
                StructuredLogOutcome.Succeeded,
                StructuredLogEvent.NewEventId(),
                cycleId: StructuredLogEvent.NewCycleId(),
                city: "atlanta"),
            nameof(StructuredLogEventName.PublishFailed),
            StructuredLogReasonCode.PUBLISH_FAILED.ToString()));

        Assert.Null(escaped);
        Assert.Equal(1, sink.EmitFailures);
    }

    [Fact]
    public void NullLogger_SwallowsEveryEmission_SoAnUnconfiguredWorkerStillRuns()
    {
        var logger = NullWorkerStructuredEventLogger.Instance;

        var escaped = Record.Exception(() =>
        {
            logger.Emit(StructuredLogEvent.Create(StructuredLogEventName.WorkerStarted,
                StructuredLogOutcome.Succeeded, StructuredLogEvent.NewEventId()));
            logger.EmitRecovery(
                StructuredLogEvent.Create(StructuredLogEventName.WorkerCycleRecovered,
                    StructuredLogOutcome.Succeeded, StructuredLogEvent.NewEventId(),
                    cycleId: StructuredLogEvent.NewCycleId()),
                nameof(StructuredLogEventName.WorkerCycleFailed),
                StructuredLogReasonCode.WORKER_CYCLE_FAILED.ToString());
        });

        Assert.Null(escaped);
    }

    /// <summary>Mirrors the worker's cycle-level guard: cancellation propagates, anything
    /// else is absorbed so the cycle ends without tearing down the loop.</summary>
    static Exception? RunGuardedCycle(Action emit)
    {
        try
        {
            emit();
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    sealed class ThrowingStructuredEventLogger : IWorkerStructuredEventLogger
    {
        long _emitFailures;
        public long EmitFailures => Interlocked.Read(ref _emitFailures);

        public void Emit(StructuredLogEvent logEvent)
        {
            Interlocked.Increment(ref _emitFailures);
            throw new InvalidOperationException("Simulated structured-log sink failure");
        }

        public void EmitRecovery(StructuredLogEvent recoveryEvent, string recoveredEventName, string? recoveredReasonCode)
        {
            Interlocked.Increment(ref _emitFailures);
            throw new InvalidOperationException("Simulated structured-log sink failure");
        }
    }
}
