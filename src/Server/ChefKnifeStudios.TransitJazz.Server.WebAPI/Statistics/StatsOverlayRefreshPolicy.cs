using System;
using System.Collections.Generic;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;

public sealed record StatsOverlayRefreshSchedule(
    DateTime RefreshWindowStartUtc,
    DateTime ScheduledNextRefreshUtc,
    DateTime NextRefreshAtUtc);

public static class StatsOverlayRefreshPolicy
{
    static readonly DateTime OriginUtc = DateTime.UnixEpoch;

    public static StatsOverlayRefreshSchedule Resolve(
        DateTimeOffset now,
        int refreshIntervalMinutes,
        int ingestionGraceMinutes,
        IEnumerable<DateTime>? rootLocalDayEndsUtc = null)
    {
        Validate(refreshIntervalMinutes, ingestionGraceMinutes);
        var nowUtc = now.UtcDateTime;
        var intervalTicks = TimeSpan.FromMinutes(refreshIntervalMinutes).Ticks;
        var graceTicks = TimeSpan.FromMinutes(ingestionGraceMinutes).Ticks;
        var elapsedTicks = nowUtc.Ticks - OriginUtc.Ticks - graceTicks;
        var windowIndex = Math.DivRem(elapsedTicks, intervalTicks, out var remainder);
        if (remainder < 0) windowIndex--;

        var windowStart = new DateTime(
            checked(OriginUtc.Ticks + windowIndex * intervalTicks + graceTicks), DateTimeKind.Utc);
        var scheduledNext = windowStart.AddTicks(intervalTicks);
        DateTime? localDayEnd = null;
        if (rootLocalDayEndsUtc is not null)
        {
            foreach (var candidate in rootLocalDayEndsUtc)
            {
                var end = NormalizeUtc(candidate);
                if (end > nowUtc && (localDayEnd is null || end < localDayEnd.Value))
                    localDayEnd = end;
            }
        }
        var effectiveNext = localDayEnd is { } dayEnd && dayEnd < scheduledNext
            ? dayEnd
            : scheduledNext;

        return new StatsOverlayRefreshSchedule(windowStart, scheduledNext, effectiveNext);
    }

    static void Validate(int intervalMinutes, int graceMinutes)
    {
        if (intervalMinutes is < 1 or > 60 || 60 % intervalMinutes != 0)
            throw new ArgumentOutOfRangeException(nameof(intervalMinutes));
        if (graceMinutes is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(graceMinutes));
    }

    static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
