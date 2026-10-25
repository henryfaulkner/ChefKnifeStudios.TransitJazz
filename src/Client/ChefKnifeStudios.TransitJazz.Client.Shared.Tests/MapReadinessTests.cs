using ChefKnifeStudios.TransitJazz.Client.Shared.Components;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class MapReadinessTests
{
    [Fact]
    public void MapRemainsPendingBeforeWatchdogAndShowsDelayedLoadingAt20VisibleSeconds()
    {
        var clock = new NotificationTestTimeProvider();
        var state = new MapReadinessState(clock);
        state.BeginInstance();

        clock.Advance(TimeSpan.FromSeconds(20) - TimeSpan.FromTicks(1));
        Assert.False(state.IsDelayedLoading);
        Assert.Equal(MapReadinessStatus.Pending, state.Current.Status);

        clock.Advance(TimeSpan.FromTicks(1));
        Assert.True(state.IsDelayedLoading);
        Assert.Equal(MapReadinessStatus.Pending, state.Current.Status);
    }

    [Fact]
    public void HiddenTimeDoesNotCountTowardPendingMapDelay()
    {
        var clock = new NotificationTestTimeProvider();
        var state = new MapReadinessState(clock);
        var generation = state.BeginInstance();
        clock.Advance(TimeSpan.FromSeconds(10));
        state.SetVisible(generation, false);

        clock.Advance(TimeSpan.FromMinutes(2));
        state.SetVisible(generation, true);
        Assert.False(state.IsDelayedLoading);

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.True(state.IsDelayedLoading);
    }

    [Fact]
    public void MissingRequiredLayerIsNotConfirmedReady()
    {
        var state = new MapReadinessState(new NotificationTestTimeProvider());
        var generation = state.BeginInstance();

        Assert.True(state.Observe(generation, MapReadinessSignal.RequiredLayerUnavailable));

        Assert.Equal(MapReadinessStatus.Unavailable, state.Current.Status);
        Assert.Equal(MapFailureReason.RequiredLayerUnavailable, state.Current.FailureReason);
    }

    [Fact]
    public void ConfirmedRequiredMapContentMarksMapReady()
    {
        var state = new MapReadinessState(new NotificationTestTimeProvider());
        var generation = state.BeginInstance();

        Assert.True(state.Observe(generation, MapReadinessSignal.RequiredContentReady));

        Assert.Equal(MapReadinessStatus.Ready, state.Current.Status);
        Assert.True(state.Current.IsReady);
    }

    [Fact]
    public void FatalInitializationFailureRetainsUnavailableOutcome()
    {
        var state = new MapReadinessState(new NotificationTestTimeProvider());
        var generation = state.BeginInstance();

        Assert.True(state.Observe(generation, MapReadinessSignal.InitializationFailed));

        Assert.Equal(MapReadinessStatus.Unavailable, state.Current.Status);
        Assert.Equal(MapFailureReason.InitializationFailed, state.Current.FailureReason);
    }

    [Fact]
    public void IndividualResourceFailureDoesNotPoisonMapReadiness()
    {
        var state = new MapReadinessState(new NotificationTestTimeProvider());
        var generation = state.BeginInstance();

        Assert.True(state.Observe(generation, MapReadinessSignal.IndividualResourceFailed));

        Assert.Equal(MapReadinessStatus.Pending, state.Current.Status);
        Assert.Null(state.Current.FailureReason);
    }

    [Fact]
    public void StaleInstanceCallbackCannotChangeCurrentReadiness()
    {
        var state = new MapReadinessState(new NotificationTestTimeProvider());
        var oldGeneration = state.BeginInstance();
        var currentGeneration = state.BeginInstance();

        Assert.False(state.Observe(oldGeneration, MapReadinessSignal.RequiredContentReady));
        Assert.Equal(currentGeneration, state.Current.InstanceGeneration);
        Assert.Equal(MapReadinessStatus.Pending, state.Current.Status);
    }
}
