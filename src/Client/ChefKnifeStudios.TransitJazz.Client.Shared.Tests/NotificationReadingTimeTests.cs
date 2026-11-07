using ChefKnifeStudios.TransitJazz.Client.Shared.Services.Notifications;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class NotificationReadingTimeTests
{
    [Theory]
    [InlineData(NotificationSeverity.Warning, 8)]
    [InlineData(NotificationSeverity.Error, 12)]
    public void ReadingBudgetExpiresAtExactVisibleBoundary(NotificationSeverity severity, int seconds)
    {
        var clock = new NotificationTestTimeProvider();
        using var reading = new NotificationReadingTime(clock);
        var expired = new List<long>();
        reading.Expired += expired.Add;

        reading.Start(41, severity);

        Assert.Equal(TimeSpan.FromSeconds(seconds), reading.Remaining);
        clock.Advance(TimeSpan.FromSeconds(seconds) - TimeSpan.FromTicks(1));
        clock.DrainCallbacks();
        Assert.Empty(expired);

        clock.Advance(TimeSpan.FromTicks(1));
        clock.DrainCallbacks();

        Assert.Equal([41L], expired);
        Assert.False(reading.IsActive);
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public void HiddenHoverAndFocusPauseReasonsOverlapWithoutResettingRemainingTime()
    {
        var clock = new NotificationTestTimeProvider();
        using var reading = new NotificationReadingTime(clock);
        var expired = new List<long>();
        reading.Expired += expired.Add;
        reading.Start(7, NotificationSeverity.Warning);
        clock.Advance(TimeSpan.FromSeconds(3));
        reading.SetPaused(7, NotificationReadingPauseReason.Hover, true);
        Assert.Equal(TimeSpan.FromSeconds(5), reading.Remaining);

        clock.Advance(TimeSpan.FromSeconds(4));
        reading.SetPaused(7, NotificationReadingPauseReason.Focus, true);
        reading.SetPaused(7, NotificationReadingPauseReason.Hover, false);
        reading.SetPaused(7, NotificationReadingPauseReason.Hidden, true);
        reading.SetPaused(7, NotificationReadingPauseReason.Focus, false);
        clock.Advance(TimeSpan.FromMinutes(1));
        clock.DrainCallbacks();
        Assert.Empty(expired);

        reading.SetPaused(7, NotificationReadingPauseReason.Hidden, false);
        clock.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromTicks(1));
        clock.DrainCallbacks();
        Assert.Empty(expired);
        clock.Advance(TimeSpan.FromTicks(1));
        clock.DrainCallbacks();

        Assert.Equal([7L], expired);
    }

    [Fact]
    public void StaleTimeoutAndManualCloseCannotRemoveANewerPresentation()
    {
        var clock = new NotificationTestTimeProvider();
        using var reading = new NotificationReadingTime(clock);
        var expired = new List<long>();
        reading.Expired += expired.Add;
        reading.Start(1, NotificationSeverity.Warning);
        clock.Advance(TimeSpan.FromSeconds(8));
        reading.Start(2, NotificationSeverity.Error);

        Assert.True(clock.RunNextCallback());
        reading.Cancel(1);
        Assert.Equal(2, reading.ActivePresentationId);
        Assert.Equal(TimeSpan.FromSeconds(12), reading.Remaining);
        Assert.Empty(expired);

        reading.Cancel(2);
        Assert.False(reading.IsActive);
        Assert.Equal(0, clock.ActiveTimerCount);
        clock.Advance(TimeSpan.FromSeconds(12));
        clock.DrainCallbacks();
        Assert.Empty(expired);
    }

    [Fact]
    public void DisposalCancelsOwnedDeadlineAndIgnoresAlreadyQueuedCallback()
    {
        var clock = new NotificationTestTimeProvider();
        var reading = new NotificationReadingTime(clock);
        var expired = new List<long>();
        reading.Expired += expired.Add;
        reading.Start(3, NotificationSeverity.Warning);
        clock.Advance(TimeSpan.FromSeconds(8));
        Assert.Equal(1, clock.PendingCallbackCount);

        reading.Dispose();
        Assert.Equal(0, clock.ActiveTimerCount);
        Assert.True(clock.RunNextCallback());

        Assert.Empty(expired);
        Assert.False(reading.IsActive);
    }
}
