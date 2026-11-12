using ChefKnifeStudios.TransitJazz.Client.Shared.ClientExperience;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests.ClientExperience;

public sealed class ClientExperienceAttemptTests
{
    [Fact]
    public void OneAcceptedAttemptGetsOneResultAndAUserRetryGetsANewId()
    {
        var time = new ManualTimeProvider();
        var tracker = new ClientExperienceAttemptTracker(time);
        var firstId = Guid.NewGuid();
        var retryId = Guid.NewGuid();

        Assert.True(tracker.Start(firstId, "atlanta", "city_map_start", 60_000));
        Assert.False(tracker.Start(firstId, "atlanta", "city_map_start", 60_000));
        Assert.True(tracker.Start(retryId, "atlanta", "city_map_start", 60_000));

        time.Advance(TimeSpan.FromSeconds(4));
        Assert.Empty(tracker.Advance());
        var result = tracker.Finish(firstId, "succeeded", "owner_confirmed");

        Assert.NotNull(result);
        Assert.Equal(firstId, result.AttemptId);
        Assert.Equal("succeeded", result.Outcome);
        Assert.Equal(4_000, result.ForegroundElapsedMs);
        Assert.Null(tracker.Finish(firstId, "failed", "owner_failed"));
        Assert.Equal(1, tracker.ActiveCount);
    }

    [Fact]
    public void ActionAttemptsRetainFixedActionAndParentCorrelation()
    {
        var tracker = new ClientExperienceAttemptTracker(new ManualTimeProvider());
        var attemptId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        Assert.True(tracker.Start(attemptId, "atlanta", "settings_effect", 60_000,
            parentActionId: actionId, action: "display_setting"));

        var result = tracker.Finish(attemptId, "succeeded", "owner_confirmed");

        Assert.Equal(actionId, result?.ParentActionId);
        Assert.Equal("display_setting", result?.Action);
    }

    [Fact]
    public void ForegroundDeadlineExpiresAfterObservedForegroundTime()
    {
        var time = new ManualTimeProvider();
        var tracker = new ClientExperienceAttemptTracker(time);
        var id = Guid.NewGuid();
        tracker.Start(id, "atlanta", "live_start", 30_000);

        for (var i = 0; i < 6; i++)
        {
            time.Advance(TimeSpan.FromSeconds(5));
            var completed = tracker.Advance();
            if (i < 5) Assert.Empty(completed);
            else
            {
                var result = Assert.Single(completed);
                Assert.Equal("deadline_exceeded", result.Outcome);
                Assert.Equal(30_000, result.ForegroundElapsedMs);
            }
        }
    }

    [Fact]
    public void HiddenTimeAndLongCallbackGapsDoNotAdvanceForegroundDeadline()
    {
        var time = new ManualTimeProvider();
        var tracker = new ClientExperienceAttemptTracker(time);
        var id = Guid.NewGuid();
        tracker.Start(id, "atlanta", "audio_unlock", 15_000);
        Assert.Empty(tracker.SetVisible(false));

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Empty(tracker.Advance());
        Assert.Empty(tracker.SetVisible(true));
        time.Advance(TimeSpan.FromSeconds(20));
        Assert.Empty(tracker.Advance());

        for (var i = 0; i < 3; i++)
        {
            time.Advance(TimeSpan.FromSeconds(5));
            var completed = tracker.Advance();
            if (i < 2) Assert.Empty(completed);
            else Assert.Equal("deadline_exceeded", Assert.Single(completed).Outcome);
        }
    }

    [Fact]
    public void AttemptPastCorrelationHorizonIsSuperseded()
    {
        var time = new ManualTimeProvider();
        var tracker = new ClientExperienceAttemptTracker(time);
        var id = Guid.NewGuid();
        tracker.Start(id, "atlanta", "bootstrap_start", 60_000);
        tracker.SetVisible(false);
        time.Advance(TimeSpan.FromHours(24).Add(TimeSpan.FromSeconds(1)));

        var result = Assert.Single(tracker.Advance());

        Assert.Equal("superseded", result.Outcome);
        Assert.Equal("superseded", result.Reason);
        Assert.Equal(0, result.ForegroundElapsedMs);
    }

    sealed class ManualTimeProvider : TimeProvider
    {
        long _timestamp;
        DateTimeOffset _utcNow = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration)
        {
            _timestamp += duration.Ticks;
            _utcNow += duration;
        }
    }
}
