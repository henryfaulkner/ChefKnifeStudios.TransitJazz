#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChefKnifeStudios.TransitJazz.Client.Shared.Components.Charts;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using ChefKnifeStudios.TransitJazz.Shared.Stats;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components.Stats;

/// <summary>
/// Aligns one server snapshot with the finite chart models. Statistical values and their
/// null/coverage meaning are carried through from the API; this type does not recalculate them.
/// </summary>
public static class StatsOverlayProjection
{
    public static StatsOverlayPresentation Build(
        StatsOverlayResponse response,
        Func<string, string> localize,
        IReadOnlyDictionary<string, RouteShapeFeature>? routeShapes = null,
        bool isDark = false,
        bool isHistorical = false)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(localize);
        var snapshot = response;
        if (!isHistorical && response.Selection.Scope == "city" && response.PreviousDay is { } previous)
        {
            response = response with
            {
                Cities = [previous.City],
                TodayDistance = previous.Distance,
                TodayReportedVehicles = previous.ReportedVehicles,
                CategoryShares = previous.CategoryShares,
                Bubbles = previous.Bubbles,
                Timeline = previous.Timeline
            };
        }
        var culture = CultureInfo.GetCultureInfo("en");

        var pie = new PieChartModel(response.CategoryShares.Select(item => new PieChartRow(
            item.CategoryKey,
            localize(CategoryResourceKey(item.CategoryKey)),
            ToDouble(item.Kilometers),
            ToDouble(item.Percent),
            CoverageForReference(response, item.CoverageReference))).ToArray());

        var routeCategories = response.RouteOptions.ToDictionary(option => (option.CitySlug, option.RouteJoinKey),
            option => option.CategoryKey);
        var bubbles = new BubbleChartModel(response.Bubbles.Select(item =>
        {
            var label = item.EntityKind == "city"
                ? CityLabel(item.CitySlug, response.Cities, localize)
                : item.Label;
            var selectedRouteCoverage = item.RouteJoinKey is null ? null
                : item.Coverage.SelectedRouteCoverage?.FirstOrDefault(route =>
                    string.Equals(route.CitySlug, item.CitySlug, StringComparison.Ordinal)
                    && string.Equals(route.RouteJoinKey, item.RouteJoinKey, StringComparison.Ordinal));
            var expectedHours = selectedRouteCoverage?.ExpectedHours ?? item.Coverage.ExpectedClosedHours;
            var availableHours = selectedRouteCoverage?.AvailableHours ?? item.Coverage.ContributingHours;
            var category = item.RouteJoinKey is { } routeKey && routeCategories.TryGetValue((item.CitySlug, routeKey), out var routeCategory)
                ? routeCategory : item.CategoryKey ?? "other";
            return new BubbleChartRow(
                item.EntityKey,
                label,
                ToDouble(item.X),
                ToDouble(item.Y),
                ToDouble(item.MeanActiveVehicles),
                item.Coverage.State,
                item.OmissionReason,
                item.EntityKind == "city" ? "city:" + item.CitySlug : item.CategoryKey ?? item.EntityKey,
                BubbleTooltip(item, label, localize, culture),
                ToDouble(item.DistanceKilometers),
                ToDouble(item.RouteVehicleHours),
                ToDouble(item.ActiveClockHours),
                item.ActivitySampleNumerator,
                item.SampleDenominator,
                expectedHours,
                availableHours,
                Color: item.EntityKind == "route"
                    ? ChartPresentation.GetRouteColor(item.CitySlug, item.RouteJoinKey ?? item.EntityKey, category, routeShapes, isDark)
                    : null);
        }).ToArray());

        var timeline = BuildTimeline(response, localize, culture);
        pie.Validate();
        bubbles.Validate();
        timeline.Validate();
        var dailyReported = ProjectReportedVehicles(response.TodayReportedVehicles, response, localize, culture);
        if (isHistorical && snapshot.Selection.Scope == "city")
            dailyReported = dailyReported with { HeadingKey = "StatsReportedVehiclesOnDate" };
        else if (snapshot.Selection.Scope == "city" && snapshot.PreviousDay is not null)
            dailyReported = dailyReported with { HeadingKey = "StatsReportedVehiclesYesterday" };
        return new StatsOverlayPresentation(snapshot, pie, bubbles, timeline,
            dailyReported,
            ProjectReportedVehicles(response.Last24ReportedVehicles, response, localize, culture),
            isHistorical);
    }

    static StatsReportedVehiclePresentation ProjectReportedVehicles(StatsReportedVehicleSummary summary,
        StatsOverlayResponse response, Func<string, string> localize, CultureInfo culture)
    {
        var heading = summary.Window == "today" ? "StatsReportedVehiclesToday" : "StatsReportedVehiclesLast24";
        var count = summary.Count?.ToString("N0", culture)
            ?? localize(summary.State switch
            {
                "pending" => "StatsCoveragePending",
                _ => "StatsCoverageUnavailable",
            });
        var coverage = summary.State switch
        {
            "complete" => localize("StatsReportedVehicleComplete"),
            "partial" => string.Format(culture, localize("StatsReportedVehiclePartial"),
                summary.ContributingCityHours, summary.ExpectedCityHours),
            "pending" => localize("StatsCoveragePending"),
            _ => localize("StatsCoverageUnavailable"),
        };
        var details = summary.Cities.Select(city =>
        {
            var window = response.Cities.FirstOrDefault(x => x.CitySlug == city.CitySlug);
            var cityName = window is null ? localize("StatsCityUnknown") : localize(window.DisplayNameKey);
            var period = window is null ? city.WindowReference : WindowPeriod(window, summary.Window == "today");
            var state = city.State switch
            {
                "complete" => localize("StatsReportedVehicleComplete"),
                "partial" => string.Format(culture, localize("StatsReportedVehiclePartial"), city.ContributingHours, city.ExpectedHours),
                "pending" => localize("StatsCoveragePending"),
                _ => localize("StatsCoverageUnavailable"),
            };
            var localCount = city.Count?.ToString("N0", culture) ?? state;
            var text = $"{cityName}: {localCount}; {state}; {period}";
            if (city.UnidentifiableRecordCount > 0)
                text += "; " + string.Format(culture, localize("StatsReportedVehicleMissingIdentity"), city.UnidentifiableRecordCount);
            if (city.FallbackRecordCount > 0)
                text += "; " + string.Format(culture, localize("StatsReportedVehicleFallbackIdentity"), city.FallbackRecordCount);
            foreach (var exclusion in city.Exclusions.Where(item => item.Hours > 0))
            {
                var key = ExclusionResourceKey(exclusion.Code);
                if (key is not null)
                    text += "; " + string.Format(culture, localize(key), exclusion.Hours);
            }
            foreach (var limitation in city.Limitations)
            {
                var key = LimitationResourceKey(limitation);
                if (key is not null) text += "; " + localize(key);
            }
            return text;
        }).ToArray();
        return new StatsReportedVehiclePresentation(heading, count, coverage, details);
    }

    static string WindowPeriod(StatsCityWindow city, bool today)
    {
        if (today) return $"{city.LocalDate.ToString("d", CultureInfo.GetCultureInfo("en"))} ({city.TimeZone})";
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(city.TimeZone);
            var start = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(city.Last24StartUtc, DateTimeKind.Utc), zone);
            var end = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(city.Last24EndUtc, DateTimeKind.Utc), zone);
            var utc = CultureInfo.GetCultureInfo("en");
            return $"{start.ToString("g", utc)}–{end.ToString("g", utc)} ({city.TimeZone}; UTC {city.Last24StartUtc:HH:mm}–{city.Last24EndUtc:HH:mm})";
        }
        catch (TimeZoneNotFoundException)
        {
            return $"{UtcText(city.Last24StartUtc)}–{UtcText(city.Last24EndUtc)}";
        }
        catch (InvalidTimeZoneException)
        {
            return $"{UtcText(city.Last24StartUtc)}–{UtcText(city.Last24EndUtc)}";
        }
    }

    static string? ExclusionResourceKey(string code) => code switch
    {
        "missing_hour" => "StatsReportedVehicleMissingHour",
        "no_usable_observation" => "StatsReportedVehicleNoObservation",
        "conflict" => "StatsReportedVehicleConflict",
        "identity_loss" => "StatsReportedVehicleIdentityLoss",
        "unsupported_definition" => "StatsReportedVehicleUnsupportedDefinition",
        "source_policy_mismatch" => "StatsReportedVehicleSourcePolicyChanged",
        "cadence_policy_mismatch" => "StatsReportedVehicleCadencePolicyChanged",
        "source_failure" => "StatsReportedVehicleSourceFailure",
        "source_missing" => "StatsReportedVehicleSourceMissing",
        "boundary_excluded" => "StatsReportedVehicleBoundaryExcluded",
        _ => null,
    };

    static string? LimitationResourceKey(string code) => code switch
    {
        "cadence_gap" => "StatsReportedVehicleCadenceGap",
        "clock_regression" => "StatsReportedVehicleClockRegression",
        "end_boundary_missing" => "StatsReportedVehicleEndBoundaryMissing",
        "identity_limit_exceeded" => "StatsReportedVehicleIdentityLimit",
        "missing_identity" => "StatsReportedVehicleMissingIdentityLimitation",
        "missing_source" => "StatsReportedVehicleSourceMissingLimitation",
        "source_failure" => "StatsReportedVehicleSourceFailureLimitation",
        "source_set_changed" => "StatsReportedVehicleSourceSetChanged",
        "start_boundary_missing" => "StatsReportedVehicleStartBoundaryMissing",
        "startup_fragment" => "StatsReportedVehicleStartupFragment",
        "shutdown_fragment" => "StatsReportedVehicleShutdownFragment",
        "agency_fallback_identity" => "StatsReportedVehicleAgencyFallbackLimitation",
        "descriptor_identity" => "StatsReportedVehicleDescriptorIdentityLimitation",
        _ => null,
    };

    public static string CategoryResourceKey(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        "bus" => "StatsCategoryBus",
        "rail" or "train" => "StatsCategoryRail",
        "streetcar" => "streetcar",
        "tram" => "StatsCategoryTram",
        "ferry" => "StatsCategoryFerry",
        _ => "StatsCategoryUnknown"
    };

    static SeriesChartModel BuildTimeline(
        StatsOverlayResponse response,
        Func<string, string> localize,
        CultureInfo culture)
    {
        if (response.Timeline.SeriesKind == "category")
        {
            var bucketsBySeries = response.Timeline.Series.ToDictionary(series => series.Key,
                series => series.Buckets.OrderBy(bucket => bucket.ElapsedHours).ToArray(), StringComparer.Ordinal);
            var orderedBuckets = response.Timeline.Series.SelectMany(series => series.Buckets)
                .GroupBy(bucket => bucket.ElapsedHours).OrderBy(group => group.Key).Select(group => group.First()).ToArray();
            var labels = orderedBuckets.Select(bucket =>
            {
                var time = TimeOnly.ParseExact(bucket.LocalEndLabel, "HH:mm", CultureInfo.InvariantCulture);
                return time.ToString(time.Minute == 0 ? "h tt" : "h:mm tt", CultureInfo.GetCultureInfo("en"));
            }).ToArray();
            var x = orderedBuckets.Select(bucket => (double)bucket.ElapsedHours).ToArray();
            var categories = response.Timeline.Series.OrderBy(series => series.CategoryKey, StringComparer.Ordinal).ToArray();
            var perCategoryValues = categories.ToDictionary(series => series.Key,
                series => bucketsBySeries[series.Key].ToDictionary(bucket => bucket.ElapsedHours), StringComparer.Ordinal);
            var series = new List<ChartSeries>(categories.Length);
            for (var categoryIndex = 0; categoryIndex < categories.Length; categoryIndex++)
            {
                var category = categories[categoryIndex];
                var rows = perCategoryValues[category.Key];
                var aligned = orderedBuckets.Select(bucket => rows.TryGetValue(bucket.ElapsedHours, out var row) ? row : null).ToArray();
                var categoryTotals = aligned.Select(bucket => ToDouble(bucket?.PlotCumulativeKilometers)).ToArray();
                series.Add(ProjectSeries(category.Key, localize(CategoryResourceKey(category.CategoryKey)), aligned, categoryTotals, culture, localize));
            }

            return new SeriesChartModel(
                localize("StatsLocalTime"),
                localize("StatsCumulativeDistance"),
                labels,
                x,
                series,
                isStacked: false,
                numericAxisLabelMode: NumericAxisLabelMode.LocalClockOffset,
                enableSeriesVisibility: true);
        }

        if (response.Timeline.SeriesKind == "city")
        {
            var maxHour = Math.Max(0, (int)Math.Ceiling((double)response.Timeline.ElapsedHourExtent));
            var coordinates = Enumerable.Range(1, maxHour).Select(value => (double)value).ToArray();
            var labels = coordinates.Select(value => TimeOnly.MinValue.AddHours(value)
                .ToString("h tt", CultureInfo.GetCultureInfo("en"))).ToArray();
            var series = response.Timeline.Series.Select(city =>
            {
                var byHour = city.Buckets.ToDictionary(bucket => (double)bucket.ElapsedHours);
                var aligned = coordinates.Select(hour => byHour.TryGetValue(hour, out var bucket) ? bucket : null).ToArray();
                return ProjectSeries(city.Key, CityLabel(city.CitySlug, response.Cities, localize), aligned,
                    aligned.Select(bucket => ToDouble(bucket?.PlotCumulativeKilometers)).ToArray(), culture,
                    localize, citySlug: city.CitySlug);
            }).ToArray();
            return new SeriesChartModel(
                localize("StatsLocalTime"),
                localize("StatsCumulativeDistance"),
                labels,
                coordinates,
                series,
                isStacked: false,
                numericAxisLabelMode: NumericAxisLabelMode.LocalClockOffset,
                enableSeriesVisibility: true);
        }

        throw new ArgumentException("The response contains an unsupported timeline shape.", nameof(response));
    }

    static ChartSeries ProjectSeries(
        string key,
        string label,
        IReadOnlyList<StatsTimelineBucket?> alignedBuckets,
        IReadOnlyList<double?> combinedValues,
        CultureInfo culture,
        Func<string, string> localize,
        string? citySlug = null)
    {
        var values = alignedBuckets.Select(bucket => ToDouble(bucket?.PlotCumulativeKilometers)).ToArray();
        var known = alignedBuckets.Select(bucket => ToDouble(bucket?.CumulativeObservedKilometers)).ToArray();
        var evidence = alignedBuckets.Select((bucket, index) => bucket is null ? null : BuildPointEvidence(bucket, label,
            combinedValues[index], culture, citySlug, localize)).ToArray();
        return new ChartSeries(key, label, values, known, evidence);
    }

    static ChartPointEvidence BuildPointEvidence(
        StatsTimelineBucket bucket,
        string seriesLabel,
        double? combined,
        CultureInfo culture,
        string? citySlug,
        Func<string, string> localize)
    {
        var local = FormatLocalBucket(bucket, culture);
        var intervalStart = UtcText(bucket.StartUtc);
        var intervalEnd = UtcText(bucket.EndUtc);
        var known = ToDouble(bucket.CumulativeObservedKilometers);
        var hourly = ToDouble(bucket.HourlyKnownKilometers);
        var lines = new List<string>
        {
            citySlug is null ? seriesLabel : $"{seriesLabel} · {local}",
            $"{intervalStart} – {intervalEnd} ({bucket.UtcOffset})",
            $"{localize(ChartLocalization.CoverageKey(bucket.CoverageState))}: {local}"
        };
        if (bucket.Reason is { } reason) lines.Add(localize(ChartLocalization.ReasonKey(reason)));
        if (hourly is { } amount) lines.Add(string.Format(culture, "{0}: {1:N2} {2}", localize("StatsHourlyKnown"), amount, localize("StatsDistanceUnit")));
        if (known is { } prefix) lines.Add(string.Format(culture, "{0}: {1:N2} {2}", localize("StatsKnownCumulative"), prefix, localize("StatsDistanceUnit")));
        if (combined is { } total) lines.Add(string.Format(culture, "{0}: {1:N2} {2}", localize("StatsCombinedPlottedSubtotal"), total, localize("StatsDistanceUnit")));

        return new ChartPointEvidence(local, intervalStart, intervalEnd, bucket.UtcOffset,
            bucket.CoverageState, combined, hourly, known, lines, bucket.Reason);
    }

    static IReadOnlyList<string> BubbleTooltip(
        StatsBubbleStatistic item,
        string label,
        Func<string, string> localize,
        CultureInfo culture)
    {
        string Format(decimal? value) => value?.ToString("#,##0.##", culture) ?? localize("StatsUnavailable");
        var lines = new List<string>
        {
            label,
            $"{localize("StatsAverageVehicles")}: {Format(item.MeanActiveVehicles)} vehicles",
            $"{localize("StatsAverageKilometersPerHour")}: {Format(item.Y)} km/hr",
            $"{localize("StatsAverageDistancePerVehicleHour")}: {Format(item.X)} km/hr"
        };
        if (item.OmissionReason is { } reason) lines.Add(localize(ChartLocalization.ReasonKey(reason)));
        return lines;
    }

    static string CityLabel(string? citySlug, IReadOnlyList<StatsCityWindow> cities, Func<string, string> localize)
    {
        var city = cities.FirstOrDefault(item => string.Equals(item.CitySlug, citySlug, StringComparison.Ordinal));
        return city is null ? localize("StatsCityUnknown") : localize(city.DisplayNameKey);
    }

    static string FormatLocalBucket(StatsTimelineBucket bucket, CultureInfo culture) =>
        $"{bucket.LocalDate.ToString("d", culture)} · {bucket.LocalEndLabel} {bucket.UtcOffset}";

    static string CoverageForReference(StatsOverlayResponse response, string reference) => reference switch
    {
        "today" => response.TodayDistance.Coverage.State,
        "last24CompletedHours" => response.Last24Distance.Coverage.State,
        _ => "unavailable"
    };

    static string UtcText(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
    static double? ToDouble(decimal? value) => value is { } number ? (double)number : null;

}

public sealed record StatsOverlayPresentation(
    StatsOverlayResponse Snapshot,
    PieChartModel CategoryPie,
    BubbleChartModel Bubbles,
    SeriesChartModel Timeline,
    StatsReportedVehiclePresentation TodayReportedVehicles,
    StatsReportedVehiclePresentation Last24ReportedVehicles,
    bool IsHistorical = false)
{
    public bool IsPreviousDay => !IsHistorical && Snapshot.Selection.Scope == "city" && Snapshot.PreviousDay is not null;
    public StatsDistanceSummary DailyDistance => IsPreviousDay ? Snapshot.PreviousDay!.Distance : Snapshot.TodayDistance;
    public DateOnly? DailyEvidenceDate => Snapshot.Selection.Scope != "city" ? null : IsPreviousDay
        ? Snapshot.PreviousDay!.City.LocalDate
        : Snapshot.Cities.FirstOrDefault()?.LocalDate;
    public string DailyDistanceHeadingKey => IsHistorical && Snapshot.Selection.Scope == "city" ? "StatsDistanceOnDate"
        : IsPreviousDay ? "StatsDistanceYesterday" : "StatsDistanceToday";
}

public sealed record StatsReportedVehiclePresentation(
    string HeadingKey,
    string CountText,
    string CoverageText,
    IReadOnlyList<string> CityDetails);
