using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests;

public sealed class StructuredEventPolicyTests
{
    static readonly DateTimeOffset Start = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FirstCondition_EmitsInitial_ThenSuppressesIdenticalEvents()
    {
        var clock = new FakeTimeProvider(Start);
        var policy = new StructuredEventPolicy(clock, TimeSpan.FromMinutes(15));

        var first = policy.Decide("atlanta", "CityCycleAnomaly", "NO_VEHICLES", true);
        var repeat = policy.Decide("atlanta", "CityCycleAnomaly", "NO_VEHICLES", true);

        Assert.True(first.ShouldEmit);
        Assert.Equal(StructuredEventEmissionKind.Initial, first.Kind);
        Assert.False(repeat.ShouldEmit);
        Assert.Equal(StructuredEventEmissionKind.Suppressed, repeat.Kind);
    }

    [Fact]
    public void MaterialReasonChange_EmitsTransition_AndReplacesOldActiveCondition()
    {
        var policy = new StructuredEventPolicy(new FakeTimeProvider(Start), TimeSpan.FromMinutes(15));

        var initial = policy.Decide("atlanta", "CityCycleAnomaly", "NO_VEHICLES", true);
        var transition = policy.Decide("atlanta", "CityCycleAnomaly", "STALE_FEED", true);
        var oldRecovery = policy.DecideRecovery("atlanta", "CityCycleAnomaly", "NO_VEHICLES");
        var currentRecovery = policy.DecideRecovery("atlanta", "CityCycleAnomaly", "STALE_FEED");

        Assert.Equal(StructuredEventEmissionKind.Initial, initial.Kind);
        Assert.Equal(StructuredEventEmissionKind.Transition, transition.Kind);
        Assert.False(oldRecovery.ShouldEmit);
        Assert.True(currentRecovery.ShouldEmit);
        Assert.Equal(0, policy.ActiveConditionCount);
    }

    [Fact]
    public void PersistentCondition_EmitsReminderOnlyAfterConfiguredInterval()
    {
        var clock = new FakeTimeProvider(Start);
        var policy = new StructuredEventPolicy(clock, TimeSpan.FromMinutes(15));
        policy.Decide("atlanta", "PublishFailed", "PUBLISH_FAILED", true);

        clock.Advance(TimeSpan.FromMinutes(14).Add(TimeSpan.FromSeconds(59)));
        Assert.False(policy.Decide("atlanta", "PublishFailed", "PUBLISH_FAILED", true).ShouldEmit);

        clock.Advance(TimeSpan.FromSeconds(1));
        var reminder = policy.Decide("atlanta", "PublishFailed", "PUBLISH_FAILED", true);
        Assert.True(reminder.ShouldEmit);
        Assert.Equal(StructuredEventEmissionKind.Reminder, reminder.Kind);
    }

    [Fact]
    public void ClearingCondition_EmitsOneRecovery()
    {
        var clock = new FakeTimeProvider(Start);
        var policy = new StructuredEventPolicy(clock, TimeSpan.FromMinutes(15));
        policy.Decide("atlanta", "CityInputFailed", "INPUT_FAILED", true);

        var recovered = policy.DecideRecovery("atlanta", "CityInputFailed", "INPUT_FAILED");
        var duplicateRecovery = policy.DecideRecovery("atlanta", "CityInputFailed", "INPUT_FAILED");

        Assert.True(recovered.ShouldEmit);
        Assert.Equal(StructuredEventEmissionKind.Recovery, recovered.Kind);
        Assert.False(duplicateRecovery.ShouldEmit);
    }
}

