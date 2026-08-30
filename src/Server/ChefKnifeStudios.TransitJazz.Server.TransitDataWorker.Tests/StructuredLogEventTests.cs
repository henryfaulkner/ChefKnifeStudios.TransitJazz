using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests;

public sealed class StructuredLogEventTests
{
    [Fact]
    public void V1Taxonomy_ContainsExactlyTheContractEventNames()
    {
        var expected = new[]
        {
            "WorkerStarted", "WorkerStopped", "CityInputFailed", "CityInputPartial", "CityInputEmpty",
            "RouteIndexUnavailable", "RouteIndexLoadFailed", "RouteIndexLoaded", "CityCycleAnomaly", "PublishFailed", "PublishRecovered",
            "WorkerCycleFailed", "WorkerCycleRecovered",
        };

        Assert.Equal(expected, Enum.GetNames<StructuredLogEventName>());
    }

    [Fact]
    public void CityAnomaly_ValidatesRequiredFieldsAndProducesAllowListedState()
    {
        var logEvent = StructuredLogEvent.Create(
            StructuredLogEventName.CityCycleAnomaly,
            StructuredLogOutcome.Partial,
            StructuredLogEvent.NewEventId(),
            StructuredLogEvent.NewCycleId(),
            "atlanta",
            StructuredLogReasonCode.NO_CROSSINGS,
            new StructuredLogDiagnosticContext
            {
                TonesEmitted = 0,
                VehiclesProcessed = 37,
                CrossingsEmitted = 0,
                PublishAttempted = true,
                PublishSucceeded = true,
            });

        var state = StructuredLogRedactor.Validate(logEvent).ToLogState();

        Assert.Equal("CityCycleAnomaly", state.Single(x => x.Key == "EventName").Value);
        Assert.Equal(1, state.Single(x => x.Key == "EventVersion").Value);
        Assert.Equal("NO_CROSSINGS", state.Single(x => x.Key == "ReasonCode").Value);
        Assert.DoesNotContain(state, x => x.Key.Contains("Message", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(state, x => x.Key.Contains("Payload", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InvalidValues_AreRejectedAtSource()
    {
        var invalidCity = StructuredLogEvent.Create(
            StructuredLogEventName.CityInputFailed,
            StructuredLogOutcome.Failed,
            "event-1", "cycle-1", "Atlanta City", StructuredLogReasonCode.INPUT_FAILED);

        Assert.Throws<ArgumentException>(() => invalidCity.Validate());
        Assert.ThrowsAny<ArgumentException>(() => StructuredLogEvent.Create(
            StructuredLogEventName.CityCycleAnomaly,
            StructuredLogOutcome.Failed,
            "event-1", "cycle-1", "atlanta", StructuredLogReasonCode.NO_CROSSINGS,
            new StructuredLogDiagnosticContext { DurationMs = -1 }).Validate());
    }

    [Fact]
    public void WorkerLifecycle_DoesNotRequireCityOrCycle()
    {
        var logEvent = StructuredLogEvent.Create(
            StructuredLogEventName.WorkerStarted,
            StructuredLogOutcome.Succeeded,
            StructuredLogEvent.NewEventId());

        logEvent.Validate();
        Assert.Null(logEvent.City);
        Assert.Null(logEvent.CycleId);
    }

    [Fact]
    public void RouteIndexLoadEvents_AllowOnlyBoundedLoadDiagnostics()
    {
        var logEvent = StructuredLogEvent.Create(
            StructuredLogEventName.RouteIndexLoaded,
            StructuredLogOutcome.Succeeded,
            StructuredLogEvent.NewEventId(),
            context: new StructuredLogDiagnosticContext
            {
                DurationMs = 123,
                CityCount = 7,
                RouteCount = 412,
            });

        var state = logEvent.Validate().ToLogState();

        Assert.Equal(7, state.Single(entry => entry.Key == "CityCount").Value);
        Assert.Equal(412, state.Single(entry => entry.Key == "RouteCount").Value);
        Assert.DoesNotContain(state, entry => entry.Key.Contains("Payload", StringComparison.OrdinalIgnoreCase));
    }
}
