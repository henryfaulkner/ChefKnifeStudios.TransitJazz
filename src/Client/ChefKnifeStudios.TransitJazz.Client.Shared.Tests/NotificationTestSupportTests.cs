using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class NotificationTestSupportTests
{
    [Fact]
    public void ChangingTimerReplacesItsDeadlineAndDisposalCancelsFutureTicks()
    {
        var time = new NotificationTestTimeProvider();
        var calls = 0;
        using var timer = time.CreateTimer(_ => calls++, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        time.Advance(TimeSpan.FromSeconds(4));
        Assert.True(timer.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan));
        time.Advance(TimeSpan.FromSeconds(1));
        time.DrainCallbacks();
        Assert.Equal(0, calls);

        time.Advance(TimeSpan.FromSeconds(1));
        time.DrainCallbacks();
        Assert.Equal(1, calls);

        timer.Dispose();
        time.Advance(TimeSpan.FromHours(1));
        time.DrainCallbacks();
        Assert.Equal(1, calls);
    }

    [Fact]
    public void DisposingTimerCannotWithdrawCallbackAlreadyQueuedForDelivery()
    {
        var time = new NotificationTestTimeProvider();
        var calls = 0;
        var timer = time.CreateTimer(_ => calls++, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, time.PendingCallbackCount);

        timer.Dispose();
        time.DrainCallbacks();

        Assert.Equal(1, calls);
        Assert.Equal(0, time.ActiveTimerCount);
    }

    [Fact]
    public void WallClockJumpsDoNotAdvanceMonotonicDeadlines()
    {
        var initialUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var time = new NotificationTestTimeProvider(initialUtc);
        var calls = 0;
        using var timer = time.CreateTimer(_ => calls++, null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);

        time.SetUtcNow(initialUtc.AddDays(30));
        Assert.Equal(0, time.GetTimestamp());
        Assert.Equal(1, time.ActiveTimerCount);

        time.Advance(TimeSpan.FromSeconds(10));
        time.DrainCallbacks();

        Assert.Equal(1, calls);
        Assert.Equal(initialUtc.AddDays(30).AddSeconds(10), time.GetUtcNow());
    }

    [Fact]
    public async Task HeldOperationSignalsEntryAndCompletionOnlyAfterExplicitRelease()
    {
        var held = new HeldOperation();
        var operation = held.WaitAsync();

        await held.Entered.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(operation.IsCompleted);

        held.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(1));
        await held.Completed.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(operation.IsCompletedSuccessfully);
    }
}
