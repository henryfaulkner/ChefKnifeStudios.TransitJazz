using ChefKnifeStudios.TransitJazz.Client.Shared.ClientExperience;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests.ClientExperience;

public sealed class ClientExperienceHealthTests
{
    [Fact]
    public void FullWindowPartitionsEachCapabilityByItsObservedState()
    {
        var clock = new ManualTimeProvider();
        var tracker = new ClientExperienceObservationTracker(clock);
        Assert.True(tracker.SetCapabilityState("map", "pending", out var initialWindows));
        Assert.Equal("pending", tracker.GetCapabilityState("map"));
        Assert.Empty(initialWindows);
        Assert.Empty(AdvanceWithScheduler(tracker, clock, 4));
        Assert.True(tracker.SetCapabilityState("map", "healthy", out var transitionWindows));
        Assert.Empty(transitionWindows);
        var window = Assert.Single(AdvanceWithScheduler(tracker, clock, 8));

        Assert.Equal(0, window.WindowStartMs);
        Assert.Equal(60_000, window.WindowEndMs);
        Assert.Equal(60_000, window.ObservedMs);
        Assert.Equal(0, window.GapMs + window.ExcludedMs);
        Assert.Equal(20_000, window.StateDurations.Single(x => x.Capability == "map" && x.State == "pending").DurationMs);
        Assert.Equal(40_000, window.StateDurations.Single(x => x.Capability == "map" && x.State == "healthy").DurationMs);
        Assert.Equal(60_000, window.StateDurations.Where(x => x.Capability == "routes").Sum(x => x.DurationMs));
    }

    static IReadOnlyList<ClientExperienceObservationWindow> AdvanceWithScheduler(ClientExperienceObservationTracker tracker, ManualTimeProvider clock, int ticks)
    {
        var emitted = new List<ClientExperienceObservationWindow>();
        for (var i = 0; i < ticks; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(5));
            emitted.AddRange(tracker.Advance());
        }
        return emitted;
    }

    [Fact]
    public void VisibilityAndLateCallbacksKeepExcludedAndGapTimeOutOfHealth()
    {
        var clock = new ManualTimeProvider();
        var tracker = new ClientExperienceObservationTracker(clock);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Empty(tracker.Advance());
        var hiddenBoundary = Assert.Single(tracker.SetVisible(false));
        Assert.Equal(5_000, hiddenBoundary.ObservedMs);

        clock.Advance(TimeSpan.FromSeconds(70));
        var hiddenWindow = Assert.Single(tracker.Advance());
        Assert.Equal(70_000, hiddenWindow.ExcludedMs);
        Assert.Equal(0, hiddenWindow.ObservedMs);
        Assert.All(hiddenWindow.StateDurations, duration => Assert.Equal(0, duration.DurationMs));

        Assert.Empty(tracker.SetVisible(true));
        clock.Advance(TimeSpan.FromSeconds(20));
        var lateWindow = Assert.Single(tracker.FlushScopeEnd());
        Assert.Equal(20_000, lateWindow.GapMs);
        Assert.Equal(0, lateWindow.ObservedMs);
        Assert.Equal(hiddenWindow.WindowEndMs, lateWindow.WindowStartMs);
        Assert.False(lateWindow.WindowStartMs < hiddenWindow.WindowEndMs);
    }

    sealed class ManualTimeProvider : TimeProvider
    {
        long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
