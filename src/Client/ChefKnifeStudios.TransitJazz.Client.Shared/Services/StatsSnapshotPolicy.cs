using System;
using System.Collections.Generic;
using System.Linq;
using ChefKnifeStudios.TransitJazz.Shared.Stats;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services;

/// <summary>Validates complete evidence before replacing the accepted snapshot for its selection.</summary>
public static class StatsSnapshotPolicy
{
    static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    static readonly TimeSpan MaximumGenerationSkew = TimeSpan.FromSeconds(30);
    static readonly TimeSpan MaximumCutoffAge = TimeSpan.FromMinutes(70);

    public static StatsSelection NormalizeSelection(StatsSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.RouteJoinKeys is null
            || selection.RouteJoinKeys.Any(string.IsNullOrWhiteSpace)
            || selection.Scope is not ("city" or "all")
            || selection.Scope == "city" && string.IsNullOrWhiteSpace(selection.CitySlug)
            || selection.Scope == "all" && (selection.CitySlug is not null || selection.RouteJoinKeys.Count != 0))
            throw new ArgumentException("Stats selection is not a normalized city or All selection.", nameof(selection));

        var routes = selection.RouteJoinKeys.Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal).ToArray();
        return selection with { RouteJoinKeys = Array.AsReadOnly(routes) };
    }

    public static StatsSnapshotAcceptance Accept(
        StatsSnapshotState current,
        StatsOverlayResponse? response,
        DateTimeOffset receivedUtc,
        long receivedTimestamp)
    {
        ArgumentNullException.ThrowIfNull(current);
        receivedUtc = receivedUtc.ToUniversalTime();
        if (!IsValidSafely(response, current.Selection, receivedUtc))
            return Reject(current);

        var candidate = response!;
        if (current.Snapshot is { } existing)
        {
            if (candidate.GeneratedAtUtc < existing.GeneratedAtUtc
                || candidate.GeneratedAtUtc == existing.GeneratedAtUtc && candidate.CutoffUtc < existing.CutoffUtc)
                return Reject(current);

            if (SameEvidence(existing, candidate))
                return new StatsSnapshotAcceptance(StatsSnapshotAcceptanceKind.Identical, current with
                {
                    LastContactUtc = receivedUtc,
                    ProblemCode = null
                });
        }

        var schedule = GetDeadlines(candidate, receivedUtc);
        return new StatsSnapshotAcceptance(StatsSnapshotAcceptanceKind.Accepted, current with
        {
            Snapshot = Freeze(candidate, current.Selection),
            ReceivedUtc = receivedUtc,
            ReceivedTimestamp = receivedTimestamp,
            LastContactUtc = receivedUtc,
            ProblemCode = null,
            ScheduleMode = schedule.Mode,
            RefreshIntervalMinutes = schedule.IntervalMinutes,
            RefreshDueAfter = schedule.RefreshDueAfter,
            StaleAfter = schedule.StaleAfter,
            HardExpiryAfter = schedule.HardExpiryAfter,
            RootDayEndAfter = schedule.RootDayEndAfter
        });
    }

    public static StatsSnapshotFreshness Evaluate(StatsSnapshotState state, TimeSpan elapsedSinceReceipt, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Snapshot is null) return new StatsSnapshotFreshness(false, false, false, false);
        if (elapsedSinceReceipt < TimeSpan.Zero) elapsedSinceReceipt = TimeSpan.Zero;
        var historical = elapsedSinceReceipt >= state.RootDayEndAfter;
        return new StatsSnapshotFreshness(
            elapsedSinceReceipt >= state.RefreshDueAfter,
            state.ProblemCode is not null || elapsedSinceReceipt >= state.StaleAfter || historical,
            elapsedSinceReceipt >= state.HardExpiryAfter,
            historical);
    }

    static bool IsValidSafely(StatsOverlayResponse? response, StatsSelection expected, DateTimeOffset receivedUtc)
    {
        try { return IsValid(response, expected, receivedUtc); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            return false;
        }
    }

    static (StatsSnapshotScheduleMode Mode, int IntervalMinutes, TimeSpan RefreshDueAfter,
        TimeSpan StaleAfter, TimeSpan HardExpiryAfter, TimeSpan RootDayEndAfter) GetDeadlines(
        StatsOverlayResponse response, DateTimeOffset receivedUtc)
    {
        var rootDayEnd = response.Cities.Min(city => new DateTimeOffset(city.LocalDayEndUtc, TimeSpan.Zero));
        var rootDayEndAfter = NonNegative(rootDayEnd - receivedUtc);
        if (!response.RefreshIntervalMinutesPresent)
        {
            var legacyExpiry = NonNegative(new DateTimeOffset(response.GeneratedAtUtc, TimeSpan.Zero)
                .AddSeconds(60) - receivedUtc);
            return (StatsSnapshotScheduleMode.Legacy, 1, legacyExpiry, legacyExpiry, legacyExpiry, rootDayEndAfter);
        }

        var interval = response.RefreshIntervalMinutes!.Value;
        var nextRefresh = new DateTimeOffset(response.NextRefreshAtUtc!.Value, TimeSpan.Zero);
        var refreshDue = nextRefresh - receivedUtc;
        if (refreshDue < TimeSpan.Zero) refreshDue = TimeSpan.Zero;
        if (refreshDue > TimeSpan.FromMinutes(interval)) refreshDue = TimeSpan.FromMinutes(interval);
        var staleAfter = NonNegative(nextRefresh.AddSeconds(30) - receivedUtc);
        var hardExpiryAfter = NonNegative(nextRefresh.AddMinutes(interval * 2) - receivedUtc);
        return (StatsSnapshotScheduleMode.ScheduleAware, interval, refreshDue, staleAfter, hardExpiryAfter, rootDayEndAfter);
    }

    static TimeSpan NonNegative(TimeSpan duration) => duration > TimeSpan.Zero ? duration : TimeSpan.Zero;

    static StatsSnapshotAcceptance Reject(StatsSnapshotState current) =>
        new(StatsSnapshotAcceptanceKind.Rejected, current with { ProblemCode = "invalid_response" });

    static bool IsValid(StatsOverlayResponse? response, StatsSelection expected, DateTimeOffset receivedUtc)
    {
        if (response is null
            || response.ContractVersion != StatsOverlayVocabulary.ContractVersion
            || response.MetricDefinitionVersion != StatsOverlayVocabulary.MetricDefinitionVersion
            || !IsUtc(response.GeneratedAtUtc)
            || !IsUtc(response.CutoffUtc)
            || response.CutoffUtc > response.GeneratedAtUtc
            || response.CutoffUtc.Ticks % Hour.Ticks != 0
            || response.GeneratedAtUtc - response.CutoffUtc > MaximumCutoffAge
            || response.GeneratedAtUtc - response.CutoffUtc <= TimeSpan.Zero
            || new DateTimeOffset(response.GeneratedAtUtc, TimeSpan.Zero) > receivedUtc + MaximumGenerationSkew
            || response.Selection is null
            || !SelectionMatches(response.Selection, expected)
            || response.Cities is null || response.Cities.Count == 0
            || response.RouteOptions is null
            || response.CategoryShares is null
            || response.Bubbles is null
            || response.Warnings is null || response.Warnings.Any(value => value is null)
            || !ValidCoverageSummary(response.TodayDistance, "today")
            || !ValidCoverageSummary(response.Last24Distance, "last24CompletedHours")
            || !ValidReportedSummary(response.TodayReportedVehicles, "today")
            || !ValidReportedSummary(response.Last24ReportedVehicles, "last24")
            || !ValidTimeline(response.Timeline)
            || !ValidRoutes(response.RouteOptions)
            || response.CategoryShares.Any(value => value is null || string.IsNullOrWhiteSpace(value.CategoryKey))
            || response.Bubbles.Any(value => value is null || !ValidCoverage(value.Coverage)))
            return false;

        if (expected.Scope == "city"
            && (response.Cities.Count != 1 || response.Cities[0].CitySlug != expected.CitySlug))
            return false;
        if (response.Cities.Select(city => city.CitySlug).Distinct(StringComparer.Ordinal).Count() != response.Cities.Count)
            return false;
        foreach (var city in response.Cities)
            if (!ValidCity(city, response.GeneratedAtUtc, response.CutoffUtc, requireCurrentDay: true)) return false;

        if (!ValidSchedule(response, response.Cities)) return false;
        return response.PreviousDay is null || ValidPreviousDay(response.PreviousDay, response.Cities, response.GeneratedAtUtc, response.CutoffUtc);
    }

    static bool SelectionMatches(StatsSelection actual, StatsSelection expected)
    {
        if (actual.RouteJoinKeys is null
            || actual.Scope != expected.Scope
            || !string.Equals(actual.CitySlug, expected.CitySlug, StringComparison.Ordinal))
            return false;
        var routes = actual.RouteJoinKeys;
        return routes.All(key => !string.IsNullOrWhiteSpace(key))
            && routes.Distinct(StringComparer.Ordinal).Count() == routes.Count
            && routes.SequenceEqual(expected.RouteJoinKeys, StringComparer.Ordinal);
    }

    static bool ValidSchedule(StatsOverlayResponse response, IReadOnlyList<StatsCityWindow> cities)
    {
        var presentCount = (response.RefreshIntervalMinutesPresent ? 1 : 0)
            + (response.RefreshWindowStartUtcPresent ? 1 : 0)
            + (response.NextRefreshAtUtcPresent ? 1 : 0);
        if (presentCount == 0) return true;
        if (presentCount != 3
            || response.RefreshIntervalMinutes is not { } interval
            || interval is < 1 or > 60 || 60 % interval != 0
            || response.RefreshWindowStartUtc is not { } windowStart
            || response.NextRefreshAtUtc is not { } nextRefresh
            || !IsUtc(windowStart) || !IsUtc(nextRefresh)
            || windowStart > response.GeneratedAtUtc
            || response.GeneratedAtUtc >= nextRefresh
            || nextRefresh > windowStart.AddMinutes(interval))
            return false;

        return cities.All(city => nextRefresh <= city.LocalDayEndUtc);
    }

    static bool ValidCity(StatsCityWindow? city, DateTime generatedAt, DateTime cutoff, bool requireCurrentDay)
    {
        if (city is null
            || string.IsNullOrWhiteSpace(city.CitySlug)
            || string.IsNullOrWhiteSpace(city.TimeZone)
            || !IsUtc(city.LocalDayStartUtc) || !IsUtc(city.LocalDayEndUtc)
            || !IsUtc(city.RequestedTodayStartUtc) || !IsUtc(city.RequestedTodayEndUtc)
            || !IsUtc(city.Last24StartUtc) || !IsUtc(city.Last24EndUtc)
            || city.EligibleTodayStartUtc is { } eligibleStart && !IsUtc(eligibleStart)
            || city.EligibleTodayEndUtc is { } eligibleEnd && !IsUtc(eligibleEnd)
            || city.LocalDayStartUtc >= city.LocalDayEndUtc
            || city.LocalDayEndUtc - city.LocalDayStartUtc > TimeSpan.FromHours(26)
            || city.RequestedTodayStartUtc != city.LocalDayStartUtc
            || city.RequestedTodayEndUtc < city.LocalDayStartUtc
            || city.RequestedTodayEndUtc > city.LocalDayEndUtc
            || city.Last24EndUtc != cutoff
            || city.Last24EndUtc - city.Last24StartUtc != TimeSpan.FromHours(24)
            || !ValidCoverage(city.TodayCoverage) || !ValidCoverage(city.Last24Coverage))
            return false;

        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(city.TimeZone);
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(new DateTimeOffset(generatedAt, TimeSpan.Zero), zone).DateTime);
            var startDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(new DateTimeOffset(city.LocalDayStartUtc, TimeSpan.Zero), zone).DateTime);
            var endDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(new DateTimeOffset(city.LocalDayEndUtc.AddTicks(-1), TimeSpan.Zero), zone).DateTime);
            if (startDate != city.LocalDate || endDate != city.LocalDate) return false;
            if (requireCurrentDay && (localDate != city.LocalDate
                || generatedAt < city.LocalDayStartUtc || generatedAt >= city.LocalDayEndUtc)) return false;
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return false;
        }

        if ((city.EligibleTodayStartUtc is null) != (city.EligibleTodayEndUtc is null)) return false;
        if (city.EligibleTodayStartUtc is { } start && city.EligibleTodayEndUtc is { } end
            && (start >= end || start < city.LocalDayStartUtc || end > city.RequestedTodayEndUtc
                || start.Ticks % Hour.Ticks != 0 || end.Ticks % Hour.Ticks != 0))
            return false;
        return true;
    }

    static bool ValidPreviousDay(
        StatsPreviousDaySnapshot previous,
        IReadOnlyList<StatsCityWindow> currentCities,
        DateTime generatedAt,
        DateTime cutoff)
    {
        if (previous.City is null || currentCities.Count != 1
            || !ValidCity(previous.City, generatedAt, cutoff, requireCurrentDay: false)
            || previous.City.CitySlug != currentCities[0].CitySlug
            || previous.City.LocalDate.AddDays(1) != currentCities[0].LocalDate
            || previous.City.LocalDayEndUtc != currentCities[0].LocalDayStartUtc
            || !ValidCoverageSummary(previous.Distance, "today")
            || !ValidReportedSummary(previous.ReportedVehicles, "today")
            || previous.CategoryShares is null || previous.CategoryShares.Any(value => value is null)
            || previous.Bubbles is null || previous.Bubbles.Any(value => value is null || !ValidCoverage(value.Coverage))
            || !ValidTimeline(previous.Timeline))
            return false;
        return true;
    }

    static bool ValidCoverageSummary(StatsDistanceSummary? summary, string expectedWindow) =>
        summary is not null && summary.Window == expectedWindow && ValidCoverage(summary.Coverage);

    static bool ValidCoverage(StatsCoverage? coverage) => coverage is not null
        && StatsOverlayVocabulary.CoverageStates.Contains(coverage.State, StringComparer.Ordinal)
        && coverage.Exclusions is not null
        && coverage.Exclusions.All(value => value is not null && !string.IsNullOrWhiteSpace(value.Reason))
        && (coverage.SelectedRouteCoverage is null || coverage.SelectedRouteCoverage.All(value => value is not null
            && !string.IsNullOrWhiteSpace(value.CitySlug) && !string.IsNullOrWhiteSpace(value.RouteJoinKey)
            && value.Reasons is not null && value.Reasons.All(reason => reason is not null)));

    static bool ValidReportedSummary(StatsReportedVehicleSummary? summary, string expectedWindow) =>
        summary is not null && summary.Window == expectedWindow
        && summary.Cities is not null && summary.Limitations is not null
        && summary.Cities.All(city => city is not null && !string.IsNullOrWhiteSpace(city.CitySlug)
            && !string.IsNullOrWhiteSpace(city.WindowReference)
            && StatsOverlayVocabulary.CoverageStates.Contains(city.State, StringComparer.Ordinal)
            && city.Exclusions is not null && city.Exclusions.All(value => value is not null)
            && city.Limitations is not null);

    static bool ValidTimeline(StatsTimeline? timeline) => timeline is not null
        && timeline.Series is not null
        && timeline.Series.All(series => series is not null && !string.IsNullOrWhiteSpace(series.Key)
            && StatsOverlayVocabulary.WindowReferences.Contains(series.WindowReference, StringComparer.Ordinal)
            && series.Buckets is not null && series.Buckets.All(bucket => bucket is not null
                && IsUtc(bucket.StartUtc) && IsUtc(bucket.EndUtc) && bucket.StartUtc < bucket.EndUtc
                && bucket.EndUtc - bucket.StartUtc <= Hour
                && !string.IsNullOrWhiteSpace(bucket.LocalEndLabel)
                && !string.IsNullOrWhiteSpace(bucket.UtcOffset)
                && StatsOverlayVocabulary.CoverageStates.Contains(bucket.CoverageState, StringComparer.Ordinal)));

    static bool ValidRoutes(IReadOnlyList<StatsRouteOption> routes) => routes.All(route => route is not null
        && !string.IsNullOrWhiteSpace(route.CitySlug) && !string.IsNullOrWhiteSpace(route.RouteJoinKey)
        && !string.IsNullOrWhiteSpace(route.Label) && !string.IsNullOrWhiteSpace(route.CategoryKey)
        && IsUtc(route.MetadataTimestampUtc));

    static bool IsUtc(DateTime value) => value.Kind == DateTimeKind.Utc;

    static bool SameEvidence(StatsOverlayResponse left, StatsOverlayResponse right) =>
        left.ContractVersion == right.ContractVersion
        && left.MetricDefinitionVersion == right.MetricDefinitionVersion
        && left.GeneratedAtUtc == right.GeneratedAtUtc
        && left.CutoffUtc == right.CutoffUtc
        && left.RefreshIntervalMinutesPresent == right.RefreshIntervalMinutesPresent
        && left.RefreshIntervalMinutes == right.RefreshIntervalMinutes
        && left.RefreshWindowStartUtcPresent == right.RefreshWindowStartUtcPresent
        && left.RefreshWindowStartUtc == right.RefreshWindowStartUtc
        && left.NextRefreshAtUtcPresent == right.NextRefreshAtUtcPresent
        && left.NextRefreshAtUtc == right.NextRefreshAtUtc;

    static StatsOverlayResponse Freeze(StatsOverlayResponse response, StatsSelection selection) => response with
    {
        Selection = NormalizeSelection(selection),
        Cities = FreezeList(response.Cities.Select(city => city with
        {
            TodayCoverage = FreezeCoverage(city.TodayCoverage),
            Last24Coverage = FreezeCoverage(city.Last24Coverage)
        }).ToArray()),
        RouteOptions = FreezeList(response.RouteOptions.ToArray()),
        TodayDistance = FreezeDistance(response.TodayDistance),
        Last24Distance = FreezeDistance(response.Last24Distance),
        TodayReportedVehicles = FreezeReported(response.TodayReportedVehicles),
        Last24ReportedVehicles = FreezeReported(response.Last24ReportedVehicles),
        CategoryShares = FreezeList(response.CategoryShares.ToArray()),
        Bubbles = FreezeList(response.Bubbles.Select(bubble => bubble with { Coverage = FreezeCoverage(bubble.Coverage) }).ToArray()),
        Timeline = FreezeTimeline(response.Timeline),
        Warnings = FreezeList(response.Warnings.ToArray()),
        PreviousDay = response.PreviousDay is { } previous ? previous with
        {
            City = previous.City with
            {
                TodayCoverage = FreezeCoverage(previous.City.TodayCoverage),
                Last24Coverage = FreezeCoverage(previous.City.Last24Coverage)
            },
            Distance = FreezeDistance(previous.Distance),
            ReportedVehicles = FreezeReported(previous.ReportedVehicles),
            CategoryShares = FreezeList(previous.CategoryShares.ToArray()),
            Bubbles = FreezeList(previous.Bubbles.Select(bubble => bubble with { Coverage = FreezeCoverage(bubble.Coverage) }).ToArray()),
            Timeline = FreezeTimeline(previous.Timeline)
        } : null
    };

    static StatsDistanceSummary FreezeDistance(StatsDistanceSummary summary) => summary with { Coverage = FreezeCoverage(summary.Coverage) };

    static StatsCoverage FreezeCoverage(StatsCoverage coverage) => coverage with
    {
        Exclusions = FreezeList(coverage.Exclusions.ToArray()),
        SelectedRouteCoverage = coverage.SelectedRouteCoverage is null ? null : FreezeList(
            coverage.SelectedRouteCoverage.Select(route => route with { Reasons = FreezeList(route.Reasons.ToArray()) }).ToArray())
    };

    static StatsReportedVehicleSummary FreezeReported(StatsReportedVehicleSummary summary) => summary with
    {
        Cities = FreezeList(summary.Cities.Select(city => city with
        {
            Exclusions = FreezeList(city.Exclusions.ToArray()),
            Limitations = FreezeList(city.Limitations.ToArray())
        }).ToArray()),
        Limitations = FreezeList(summary.Limitations.ToArray())
    };

    static StatsTimeline FreezeTimeline(StatsTimeline timeline) => timeline with
    {
        Series = FreezeList(timeline.Series.Select(series => series with { Buckets = FreezeList(series.Buckets.ToArray()) }).ToArray())
    };

    static IReadOnlyList<T> FreezeList<T>(T[] values) => Array.AsReadOnly(values);
}
