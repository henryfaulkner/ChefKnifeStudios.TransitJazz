using ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests.Statistics;

public sealed class StatsOverlayRefreshPolicyTests
{
    [Theory]
    [InlineData(60, "2026-10-30T10:02:00Z", "2026-10-30T11:02:00Z")]
    [InlineData(15, "2026-10-30T10:47:00Z", "2026-10-30T11:02:00Z")]
    public void ScheduleUsesFixedGraceAdjustedWindowAndNextBoundary(int intervalMinutes, string expectedStart, string expectedNext)
    {
        var schedule = StatsOverlayRefreshPolicy.Resolve(
            DateTimeOffset.Parse("2026-10-30T10:59:00Z"), intervalMinutes, 2);

        Assert.Equal(DateTimeOffset.Parse(expectedStart).UtcDateTime, schedule.RefreshWindowStartUtc);
        Assert.Equal(DateTimeOffset.Parse(expectedNext).UtcDateTime, schedule.ScheduledNextRefreshUtc);
        Assert.Equal(schedule.ScheduledNextRefreshUtc, schedule.NextRefreshAtUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(61)]
    public void ScheduleRejectsInvalidRefreshDivisors(int intervalMinutes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => StatsOverlayRefreshPolicy.Resolve(
            DateTimeOffset.Parse("2026-10-30T10:59:00Z"), intervalMinutes, 2));
}
