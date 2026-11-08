using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
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
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or OverflowException or NullReferenceException)
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
        var v2 = response?.ContractVersion == StatsOverlayVocabulary.ContractVersionV2;
        if (response is null
            || !(response.ContractVersion == StatsOverlayVocabulary.ContractVersion
                    && response.MetricDefinitionVersion == StatsOverlayVocabulary.MetricDefinitionVersion
                || response.ContractVersion == StatsOverlayVocabulary.ContractVersionV2
                    && response.MetricDefinitionVersion == StatsOverlayVocabulary.MetricDefinitionVersionV2)
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
            || !ValidCoverageSummary(response.TodayDistance, "today", v2, response.CutoffUtc)
            || !ValidCoverageSummary(response.Last24Distance, "last24CompletedHours", v2, response.CutoffUtc)
            || !ValidReportedSummary(response.TodayReportedVehicles, "today")
            || !ValidReportedSummary(response.Last24ReportedVehicles, "last24")
            || !ValidTimeline(response.Timeline, v2)
            || !ValidRoutes(response.RouteOptions)
            || response.CategoryShares.Any(value => value is null || string.IsNullOrWhiteSpace(value.CategoryKey)
                || v2 && (value.Coverage is null || value.DenominatorKind != "availableDistance"
                    || !StatsOverlayVocabulary.WindowReferences.Contains(value.CoverageReference, StringComparer.Ordinal)
                    || !ValidCoverage(value.Coverage, true, value.CoverageReference, response.CutoffUtc)))
            || response.Bubbles.Any(value => value is null || !ValidCoverage(value.Coverage, v2, null, response.CutoffUtc)
                || v2 && !ValidBubbleMeasures(value.MeasureAvailability))
            || !v2 && HasV2Fields(response))
            return false;

        if (expected.Scope == "city"
            && (response.Cities.Count != 1 || response.Cities[0].CitySlug != expected.CitySlug))
            return false;
        if (response.Cities.Select(city => city.CitySlug).Distinct(StringComparer.Ordinal).Count() != response.Cities.Count)
            return false;
        foreach (var city in response.Cities)
            if (!ValidCity(city, response.GeneratedAtUtc, response.CutoffUtc, requireCurrentDay: true, v2)) return false;

        if (!ValidSchedule(response, response.Cities)) return false;
        return response.PreviousDay is null || ValidPreviousDay(response.PreviousDay, response.Cities,
            response.GeneratedAtUtc, response.CutoffUtc, v2);
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

    static bool ValidCity(StatsCityWindow? city, DateTime generatedAt, DateTime cutoff, bool requireCurrentDay, bool v2)
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
            || !ValidCoverage(city.TodayCoverage, v2, "today", cutoff)
            || !ValidCoverage(city.Last24Coverage, v2, "last24CompletedHours", cutoff))
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
        DateTime cutoff,
        bool v2)
    {
        if (previous.City is null || currentCities.Count != 1
            || !ValidCity(previous.City, generatedAt, cutoff, requireCurrentDay: false, v2)
            || previous.City.CitySlug != currentCities[0].CitySlug
            || previous.City.LocalDate.AddDays(1) != currentCities[0].LocalDate
            || previous.City.LocalDayEndUtc != currentCities[0].LocalDayStartUtc
            || !ValidCoverageSummary(previous.Distance, "today", v2, cutoff)
            || !ValidReportedSummary(previous.ReportedVehicles, "today")
            || previous.CategoryShares is null || previous.CategoryShares.Any(value => value is null
                || v2 && (value.Coverage is null || value.DenominatorKind != "availableDistance"
                    || !StatsOverlayVocabulary.WindowReferences.Contains(value.CoverageReference, StringComparer.Ordinal)
                    || !ValidCoverage(value.Coverage, true, value.CoverageReference, cutoff)))
            || previous.Bubbles is null || previous.Bubbles.Any(value => value is null
                || !ValidCoverage(value.Coverage, v2, null, cutoff)
                || v2 && !ValidBubbleMeasures(value.MeasureAvailability))
            || !ValidTimeline(previous.Timeline, v2))
            return false;
        return true;
    }

    static bool ValidCoverageSummary(StatsDistanceSummary? summary, string expectedWindow, bool v2, DateTime cutoff) =>
        summary is not null && summary.Window == expectedWindow
        && ValidCoverage(summary.Coverage, v2, expectedWindow, cutoff);

    static bool ValidCoverage(StatsCoverage? coverage, bool v2 = false, string? expectedWindow = null, DateTime? latestUtc = null) => coverage is not null
        && StatsOverlayVocabulary.CoverageStates.Contains(coverage.State, StringComparer.Ordinal)
        && coverage.Exclusions is not null
        && coverage.Exclusions.All(value => value is not null && !string.IsNullOrWhiteSpace(value.Reason))
        && (coverage.SelectedRouteCoverage is null || coverage.SelectedRouteCoverage.All(value => value is not null
            && !string.IsNullOrWhiteSpace(value.CitySlug) && !string.IsNullOrWhiteSpace(value.RouteJoinKey)
            && value.Reasons is not null && (!v2 || value.Reasons.All(IsSafeReason))))
        && (!v2 || ValidV2Coverage(coverage, expectedWindow, latestUtc));

    static bool ValidV2Coverage(StatsCoverage coverage, string? expectedWindow, DateTime? latestUtc)
    {
        if (coverage.ObservedRouteHours is not { } observed || observed < 0 || coverage.AvailableRouteHours < 0
            || coverage.AvailableRouteHours > observed || coverage.FullyCoveredHours < 0
            || coverage.FullyCoveredHours > coverage.ContributingHours || coverage.ContributingHours < 0
            || coverage.ExpectedRouteHours is < 0
            || coverage.UnavailableRouteHours is < 0
            || !IsUtc(coverage.PendingThroughUtc)
            || latestUtc is not { } latest || coverage.PendingThroughUtc > latest
            || coverage.AssessmentMeasure is not ("distance" or "activity" or "publication" or "derived")
            || coverage.SourceCoverage is null || coverage.SourceCoverage.Count > 32
            || coverage.MeasureAvailability is null || coverage.MeasureAvailability.Count > 32
            || coverage.DefinitionVersions is null || !ValidDefinitionVersions(coverage.DefinitionVersions)
            || coverage.DefinitionTransitionWarning is { } warning
                && !StatsOverlayVocabulary.Warnings.Contains(warning, StringComparer.Ordinal)
            || coverage.SourceCoverage.Any(source => !ValidSourceCoverage(source, expectedWindow, latestUtc))
            || coverage.MeasureAvailability.Any(measure => !ValidMeasureAvailability(measure)))
            return false;

        if (coverage.ExpectedRouteHours is { } expected)
        {
            if (observed > expected || coverage.UnavailableRouteHours != expected - observed) return false;
        }
        else if (!coverage.HistoricalMembershipUnknown || coverage.UnavailableRouteHours is not null)
            return false;
        return true;
    }

    static bool ValidSourceCoverage(StatsSourceCoverage? source, string? expectedWindow, DateTime? latestUtc)
    {
        if (source is null || !ValidCityKey(source.CitySlug) || !ValidSourceKey(source.SourceKey)
            || !StatsOverlayVocabulary.WindowReferences.Contains(source.WindowReference, StringComparer.Ordinal)
            || expectedWindow is not null && source.WindowReference != expectedWindow
            || !StatsOverlayVocabulary.CoverageStates.Contains(source.State, StringComparer.Ordinal)
            || source.ContributingHours < 0 || source.FullyCoveredHours < 0
            || source.FullyCoveredHours > source.ContributingHours
            || source.CoveredPeriods is null || source.UnavailablePeriods is null
            || source.CoveredPeriods.Count > 8_784 || source.UnavailablePeriods.Count > 8_784
            || source.Reasons is null || !ValidReasons(source.Reasons, 32)
            || !ValidHourPeriods(source.CoveredPeriods, latestUtc)
            || !ValidHourPeriods(source.UnavailablePeriods, latestUtc))
            return false;

        var periods = source.CoveredPeriods.Concat(source.UnavailablePeriods).OrderBy(period => period.StartUtc).ToArray();
        for (var index = 1; index < periods.Length; index++)
            if (periods[index - 1].EndUtc > periods[index].StartUtc) return false;
        return source.CoveredPeriods.Sum(period => (int)(period.EndUtc - period.StartUtc).TotalHours) == source.ContributingHours;
    }

    static bool ValidHourPeriods(IReadOnlyList<StatsUtcHourPeriod> periods, DateTime? latestUtc)
    {
        DateTime previousEnd = default;
        foreach (var period in periods)
        {
            if (period is null || !IsUtc(period.StartUtc) || !IsUtc(period.EndUtc)
                || period.StartUtc.Ticks % Hour.Ticks != 0 || period.EndUtc.Ticks % Hour.Ticks != 0
                || period.StartUtc >= period.EndUtc || (period.EndUtc - period.StartUtc).Ticks % Hour.Ticks != 0
                || period.EndUtc - period.StartUtc > TimeSpan.FromDays(366)
                || latestUtc is { } latest && period.EndUtc > latest
                || previousEnd != default && period.StartUtc < previousEnd)
                return false;
            previousEnd = period.EndUtc;
        }
        return true;
    }

    static bool ValidMeasureAvailability(StatsMeasureAvailability? measure) => measure is not null
        && measure.MeasureKey is "distance" or "activity" or "publication" or "routeVehicleHours" or "activeClockHours"
        && StatsOverlayVocabulary.CoverageStates.Contains(measure.State, StringComparer.Ordinal)
        && measure.Reasons is not null && ValidReasons(measure.Reasons, 32)
        && measure.DefinitionVersions is not null && ValidDefinitionVersions(measure.DefinitionVersions);

    static bool ValidDefinitionVersions(IReadOnlyList<string> versions) => versions.Count <= 16
        && versions.All(version => version is StatsOverlayVocabulary.MetricDefinitionVersion
            or StatsOverlayVocabulary.MetricDefinitionVersionV2)
        && versions.Distinct(StringComparer.Ordinal).Count() == versions.Count
        && versions.SequenceEqual(versions.OrderBy(version => version, StringComparer.Ordinal), StringComparer.Ordinal);

    static bool ValidReasons(IReadOnlyList<string> reasons, int maximum) => reasons.Count <= maximum
        && reasons.All(IsSafeReason)
        && reasons.Distinct(StringComparer.Ordinal).Count() == reasons.Count;

    static bool IsSafeReason(string? reason) => reason is not null
        && StatsOverlayVocabulary.Reasons.Contains(reason, StringComparer.Ordinal);

    static bool ValidBubbleMeasures(StatsBubbleMeasureAvailability? measures) => measures is not null
        && ValidReasons(measures.XReasons, 32) && ValidReasons(measures.YReasons, 32)
        && ValidReasons(measures.SizeReasons, 32) && ValidReasons(measures.PopulationReasons, 32);

    static bool ValidCityKey(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 64
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    static bool ValidSourceKey(string value) => !string.IsNullOrWhiteSpace(value)
        && Encoding.UTF8.GetByteCount(value) <= 64
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    static bool HasV2Fields(StatsOverlayResponse response)
    {
        static bool Has(StatsCoverage coverage) => coverage.ObservedRouteHours is not null
            || coverage.UnavailableRouteHours is not null || coverage.AssessmentMeasure is not null
            || coverage.SourceCoverage is not null || coverage.MeasureAvailability is not null
            || coverage.DefinitionVersions is not null || coverage.DefinitionTransitionWarning is not null;
        return response.Cities.Any(city => Has(city.TodayCoverage) || Has(city.Last24Coverage))
            || Has(response.TodayDistance.Coverage) || Has(response.Last24Distance.Coverage)
            || response.CategoryShares.Any(category => category.Coverage is not null || category.DenominatorKind is not null)
            || response.Bubbles.Any(bubble => bubble.MeasureAvailability is not null)
            || response.Timeline.Series.SelectMany(series => series.Buckets).Any(bucket => bucket.SourceCoverage is not null)
            || response.PreviousDay is { } previous && (Has(previous.City.TodayCoverage) || Has(previous.City.Last24Coverage)
                || Has(previous.Distance.Coverage) || previous.CategoryShares.Any(category => category.Coverage is not null
                    || category.DenominatorKind is not null)
                || previous.Bubbles.Any(bubble => bubble.MeasureAvailability is not null)
                || previous.Timeline.Series.SelectMany(series => series.Buckets).Any(bucket => bucket.SourceCoverage is not null));
    }

    static bool ValidReportedSummary(StatsReportedVehicleSummary? summary, string expectedWindow) =>
        summary is not null && summary.Window == expectedWindow
        && summary.Cities is not null && summary.Limitations is not null
        && summary.Cities.All(city => city is not null && !string.IsNullOrWhiteSpace(city.CitySlug)
            && !string.IsNullOrWhiteSpace(city.WindowReference)
            && StatsOverlayVocabulary.CoverageStates.Contains(city.State, StringComparer.Ordinal)
            && city.Exclusions is not null && city.Exclusions.All(value => value is not null)
            && city.Limitations is not null);

    static bool ValidTimeline(StatsTimeline? timeline, bool v2) => timeline is not null
        && timeline.Series is not null
        && timeline.Series.All(series => series is not null && !string.IsNullOrWhiteSpace(series.Key)
            && StatsOverlayVocabulary.WindowReferences.Contains(series.WindowReference, StringComparer.Ordinal)
            && series.Buckets is not null && series.Buckets.All(bucket => bucket is not null
                && IsUtc(bucket.StartUtc) && IsUtc(bucket.EndUtc) && bucket.StartUtc < bucket.EndUtc
                && bucket.EndUtc - bucket.StartUtc <= Hour
                && !string.IsNullOrWhiteSpace(bucket.LocalEndLabel)
                && !string.IsNullOrWhiteSpace(bucket.UtcOffset)
                && StatsOverlayVocabulary.CoverageStates.Contains(bucket.CoverageState, StringComparer.Ordinal)
                && (!v2 || bucket.Reason is null || IsSafeReason(bucket.Reason))
                && (!v2 || ValidBucketSources(bucket.SourceCoverage, bucket.EndUtc))));

    static bool ValidBucketSources(IReadOnlyList<StatsBucketSourceCoverage>? sources, DateTime endUtc) => sources is not null
        && sources.Count <= 32 && sources.All(source => source is not null
            && ValidCityKey(source.CitySlug) && ValidSourceKey(source.SourceKey)
            && StatsOverlayVocabulary.CoverageStates.Contains(source.State, StringComparer.Ordinal)
            && source.Reasons is not null && ValidReasons(source.Reasons, 32));

    static bool ValidRoutes(IReadOnlyList<StatsRouteOption> routes) => routes.All(route => route is not null
        && !string.IsNullOrWhiteSpace(route.CitySlug) && !string.IsNullOrWhiteSpace(route.RouteJoinKey)
        && !string.IsNullOrWhiteSpace(route.Label) && !string.IsNullOrWhiteSpace(route.CategoryKey)
        && IsUtc(route.MetadataTimestampUtc));

    static bool IsUtc(DateTime value) => value.Kind == DateTimeKind.Utc;

    static bool SameEvidence(StatsOverlayResponse left, StatsOverlayResponse right) =>
        JsonSerializer.SerializeToUtf8Bytes(left).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(right));

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
        CategoryShares = FreezeList(response.CategoryShares.Select(FreezeCategory).ToArray()),
        Bubbles = FreezeList(response.Bubbles.Select(FreezeBubble).ToArray()),
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
            CategoryShares = FreezeList(previous.CategoryShares.Select(FreezeCategory).ToArray()),
            Bubbles = FreezeList(previous.Bubbles.Select(FreezeBubble).ToArray()),
            Timeline = FreezeTimeline(previous.Timeline)
        } : null
    };

    static StatsDistanceSummary FreezeDistance(StatsDistanceSummary summary) => summary with { Coverage = FreezeCoverage(summary.Coverage) };

    static StatsCategoryShare FreezeCategory(StatsCategoryShare category) => category with
    {
        Coverage = category.Coverage is null ? null : FreezeCoverage(category.Coverage)
    };

    static StatsBubbleStatistic FreezeBubble(StatsBubbleStatistic bubble) => bubble with
    {
        Coverage = FreezeCoverage(bubble.Coverage),
        MeasureAvailability = bubble.MeasureAvailability is { } measures ? measures with
        {
            XReasons = FreezeList(measures.XReasons.ToArray()),
            YReasons = FreezeList(measures.YReasons.ToArray()),
            SizeReasons = FreezeList(measures.SizeReasons.ToArray()),
            PopulationReasons = FreezeList(measures.PopulationReasons.ToArray())
        } : null
    };

    static StatsCoverage FreezeCoverage(StatsCoverage coverage) => coverage with
    {
        Exclusions = FreezeList(coverage.Exclusions.ToArray()),
        SelectedRouteCoverage = coverage.SelectedRouteCoverage is null ? null : FreezeList(
            coverage.SelectedRouteCoverage.Select(route => route with { Reasons = FreezeList(route.Reasons.ToArray()) }).ToArray()),
        SourceCoverage = coverage.SourceCoverage is null ? null : FreezeList(coverage.SourceCoverage.Select(source => source with
        {
            CoveredPeriods = FreezeList(source.CoveredPeriods.ToArray()),
            UnavailablePeriods = FreezeList(source.UnavailablePeriods.ToArray()),
            Reasons = FreezeList(source.Reasons.ToArray())
        }).ToArray()),
        MeasureAvailability = coverage.MeasureAvailability is null ? null : FreezeList(coverage.MeasureAvailability.Select(measure => measure with
        {
            Reasons = FreezeList(measure.Reasons.ToArray()),
            DefinitionVersions = FreezeList(measure.DefinitionVersions.ToArray())
        }).ToArray()),
        DefinitionVersions = coverage.DefinitionVersions is null ? null : FreezeList(coverage.DefinitionVersions.ToArray())
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
        Series = FreezeList(timeline.Series.Select(series => series with
        {
            Buckets = FreezeList(series.Buckets.Select(bucket => bucket with
            {
                SourceCoverage = bucket.SourceCoverage is null ? null : FreezeList(bucket.SourceCoverage.Select(source => source with
                {
                    Reasons = FreezeList(source.Reasons.ToArray())
                }).ToArray())
            }).ToArray())
        }).ToArray())
    };

    static IReadOnlyList<T> FreezeList<T>(T[] values) => Array.AsReadOnly(values);
}
