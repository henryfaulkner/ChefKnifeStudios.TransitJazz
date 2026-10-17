using System.Globalization;
using ChefKnifeStudios.TransitJazz.Client.Shared.Components;
using ChefKnifeStudios.TransitJazz.Client.Shared.Components.Stats;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class StatsOverlayProjectionTests
{
    [Fact]
    public void ReportedVehicleProjectionKeepsNullSeparateFromZeroAndFormatsIntegerCounts()
    {
        string Localize(string key) => key switch
        {
            "StatsCoverageUnavailable" => "Unavailable",
            "StatsCoveragePending" => "Pending",
            "StatsReportedVehiclePartial" => "Observed; partial coverage ({0} of {1} city-hours)",
            "StatsReportedVehicleRouteScope" => "All city feeds; independent of route selection",
            "StatsReportedVehicleMissingIdentity" => "Records without a usable vehicle ID: {0}",
            _ => key,
        };
        var legacyResponse = Response("city", [], [], [], []) with
        {
            Timeline = new StatsTimeline { SeriesKind = "city", Series = [] }
        };
        var legacy = StatsOverlayProjection.Build(legacyResponse, Localize);
        Assert.Equal("Unavailable", legacy.TodayReportedVehicles.CountText);
        Assert.DoesNotContain("0", legacy.TodayReportedVehicles.CountText);

        var response = Response("city", [], [], [], []) with
        {
            TodayReportedVehicles = new StatsReportedVehicleSummary
            {
                Window = "today", Count = 0, State = "partial", ExpectedCityHours = 8,
                ContributingCityHours = 5, FullyCoveredCityHours = 2, ExpectedCityCount = 1,
                ContributingCityCount = 1,
                Cities = [new StatsReportedVehicleCitySummary
                {
                    CitySlug = "toronto", WindowReference = "toronto:today", Count = 0,
                    State = "partial", ExpectedHours = 8, ContributingHours = 5, FullyCoveredHours = 2,
                    UnidentifiableRecordCount = 3
                }]
            }
        };

        response = response with { Timeline = new StatsTimeline { SeriesKind = "city", Series = [] } };
        var projected = StatsOverlayProjection.Build(response, Localize).TodayReportedVehicles;
        Assert.Equal("0", projected.CountText);
        Assert.Equal("Observed; partial coverage (5 of 8 city-hours)", projected.CoverageText);
        Assert.Contains("All city feeds; independent of route selection", projected.ScopeText);
        Assert.Contains("Records without a usable vehicle ID: 3", Assert.Single(projected.CityDetails));
    }

    [Fact]
    public void AllReportedVehicleProjectionShowsSumScopeAndEachLocalWindow()
    {
        string Localize(string key) => key == "StatsReportedVehicleAllScope"
            ? "Sum of city feed populations; independent of route selection. Each city uses its own local reporting window."
            : key;
        var response = Response("all", [], [], [], [],
            [City("denver", "StatsCityDenver", new DateOnly(2026, 4, 1), "America/Denver"),
             City("toronto", "StatsCityToronto", new DateOnly(2026, 11, 1), "America/Toronto")]) with
        {
            Last24ReportedVehicles = new StatsReportedVehicleSummary
            {
                Window = "last24", Count = 17, State = "partial", PopulationScope = "sumOfCityFeeds",
                ExpectedCityHours = 48, ContributingCityHours = 47, FullyCoveredCityHours = 40,
                Cities =
                [
                    new StatsReportedVehicleCitySummary { CitySlug = "denver", WindowReference = "denver:last24", Count = 8, State = "complete", ExpectedHours = 24, ContributingHours = 24, FullyCoveredHours = 24 },
                    new StatsReportedVehicleCitySummary { CitySlug = "toronto", WindowReference = "toronto:last24", Count = 9, State = "partial", ExpectedHours = 24, ContributingHours = 23, FullyCoveredHours = 16 },
                ]
            }
        };

        response = response with { Timeline = new StatsTimeline { SeriesKind = "city", Series = [] } };
        var projected = StatsOverlayProjection.Build(response, Localize).Last24ReportedVehicles;
        Assert.Equal("17", projected.CountText);
        Assert.Contains("Sum of city feed populations", projected.ScopeText);
        Assert.Contains(projected.CityDetails, detail => detail.Contains("StatsCityDenver", StringComparison.Ordinal)
            && detail.Contains("America/Denver", StringComparison.Ordinal));
        Assert.Contains(projected.CityDetails, detail => detail.Contains("StatsCityToronto", StringComparison.Ordinal)
            && detail.Contains("America/Toronto", StringComparison.Ordinal));
        Assert.Contains(projected.CityDetails, detail => detail.Contains("UTC", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportedVehicleProjectionLocalizesBoundedCoverageReasonsWithoutExposingWireCodes()
    {
        string Localize(string key) => key switch
        {
            "StatsCoverageUnavailable" => "Unavailable",
            "StatsCoveragePending" => "Pending",
            "StatsReportedVehiclePartial" => "Observed; partial coverage ({0} of {1} city-hours)",
            "StatsReportedVehicleRouteScope" => "All city feeds; independent of route selection",
            "StatsReportedVehicleDefinition" => "Distinct vehicles reported by transit feeds",
            "StatsReportedVehicleMissingHour" => "Missing hours: {0}",
            "StatsReportedVehicleSourcePolicyChanged" => "Source policy changed: {0} hours",
            "StatsReportedVehicleCadenceGap" => "Feed cadence gap detected",
            _ => key,
        };
        var response = Response("city", [], [], [], []) with
        {
            Timeline = new StatsTimeline { SeriesKind = "city", Series = [] },
            TodayReportedVehicles = new StatsReportedVehicleSummary
            {
                Window = "today", Count = 4, State = "partial", ExpectedCityHours = 2,
                ContributingCityHours = 1, PopulationScope = "cityFeeds",
                Cities = [new StatsReportedVehicleCitySummary
                {
                    CitySlug = "toronto", WindowReference = "toronto:today", Count = 4,
                    State = "partial", ExpectedHours = 2, ContributingHours = 1,
                    Exclusions = [new("missing_hour", 1), new("source_policy_mismatch", 1)],
                    Limitations = ["cadence_gap"],
                }]
            }
        };

        var detail = Assert.Single(StatsOverlayProjection.Build(response, Localize)
            .TodayReportedVehicles.CityDetails);
        Assert.Contains("Missing hours: 1", detail);
        Assert.Contains("Source policy changed: 1 hours", detail);
        Assert.Contains("Feed cadence gap detected", detail);
        Assert.DoesNotContain("source_policy_mismatch", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("cadence_gap", detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RouteBubbleColorsMatchFilterPillsAfterSizeOrderingAndMetadataArrival(bool isDark)
    {
        string[] keys = ["A", "a", "white", "historical", "other-city"];
        var options = keys.Select(key => new StatsRouteOption
        {
            CitySlug = "toronto", RouteJoinKey = key, Label = key,
            CategoryKey = key == "historical" ? "rail" : "bus"
        }).ToArray();
        var shapes = new Dictionary<string, RouteShapeFeature>(StringComparer.Ordinal);
        foreach (var (key, color, city) in new[]
        {
            ("A", "#F4C300", "toronto"), ("a", "#00AABB", "toronto"),
            ("white", "#FFFFFF", "toronto"), ("other-city", "#00FF00", "atlanta")
        })
            shapes[key] = new("Feature", new("LineString", []), new(key, key, color, "#FFFFFF", "bus", 3, city));
        var response = Response("city", [], [], [], keys.Select((key, index) =>
            Bubble(key, "route", "toronto", key, 5m, 10m, index + 1, 20m, 4, 2, 60, 60, "bus")).ToArray())
            with { RouteOptions = options };

        var beforeMetadata = StatsOverlayProjection.Build(response, key => key, isDark: isDark);
        var view = StatsOverlayProjection.Build(response, key => key, shapes, isDark: isDark);
        var pills = RouteFilterListProjection.FromStats(options, [], key => key, _ => null, _ => null,
            (_, label) => label, (_, label) => label, routeShapes: shapes, isDark: isDark)
            .Groups.SelectMany(group => group.Items).ToDictionary(item => item.RouteKey, StringComparer.Ordinal);
        var plotted = view.Bubbles.GetPlottedPoints();

        Assert.Equal(keys.Reverse(), plotted.Select(point => point.Key));
        Assert.All(plotted, point => Assert.Equal(pills[point.Key].Color, point.Color));
        Assert.Equal("#F4C300", plotted.Single(point => point.Key == "A").Color);
        Assert.Equal("#00AABB", plotted.Single(point => point.Key == "a").Color);
        Assert.Equal("#CC0000", plotted.Single(point => point.Key == "white").Color);
        Assert.Equal(isDark ? "#7986CB" : "#1A237E", plotted.Single(point => point.Key == "historical").Color);
        Assert.NotEqual(beforeMetadata.Bubbles.Rows[0].Color, view.Bubbles.Rows[0].Color);
        Assert.Equal(beforeMetadata.Bubbles.GetPlottedPoints().Select(point => (point.Key, point.X, point.Y, point.RadiusPixels)),
            plotted.Select(point => (point.Key, point.X, point.Y, point.RadiusPixels)));
        Assert.Same(response, view.Snapshot);
    }

    [Fact]
    public void CityProjectionPreservesServerSharesRatiosZeroAndKnownAmountsAcrossPlotGap()
    {
        var response = Response("city", ["toronto"],
            [
                new StatsCategoryShare("bus", 30m, 60m, "today"),
                new StatsCategoryShare("rail", 20m, 40m, "today"),
                new StatsCategoryShare("tram", 0m, 0m, "today"),
                new StatsCategoryShare("future-mode", null, null, "today")
            ],
            [
                new StatsTimelineSeries
                {
                    Key = "category:bus", CategoryKey = "bus", WindowReference = "today",
                    Buckets = [Bucket(1, 10, 10, 10), Bucket(2, null, 25, null, "unavailable"), Bucket(3, 40, 40, 40)]
                },
                new StatsTimelineSeries
                {
                    Key = "category:rail", CategoryKey = "rail", WindowReference = "today",
                    Buckets = [Bucket(1, 2, 2, 2), Bucket(2, null, 10, null, "unavailable"), Bucket(3, 5, 5, 5)]
                }
            ],
            [Bubble("route:stationary", "route", "toronto", "Stationary route", 0m, 0m, 1.5m, 30m, 6, 2, 180, 120, "bus"),
             Bubble("route:omitted", "route", "toronto", "Unavailable route", null, null, null, null, null, null, null, null, null, "unavailable", "zero_denominator")]);

        var view = StatsOverlayProjection.Build(response, key => key);

        Assert.Equal("partial", view.CategoryPie.Rows[0].CoverageReference);
        Assert.Equal(30d, view.CategoryPie.Rows[0].Kilometers);
        Assert.Equal(60d, view.CategoryPie.Rows[0].Percent);
        Assert.Equal(0d, view.CategoryPie.Rows[2].Kilometers);
        Assert.Null(view.CategoryPie.Rows[3].Kilometers);
        Assert.Equal("StatsCategoryUnknown", view.CategoryPie.Rows[3].Label);

        var plottedBubble = Assert.Single(view.Bubbles.GetPlottedPoints());
        Assert.Equal((0d, 0d), (plottedBubble.X, plottedBubble.Y));
        Assert.Equal(1.5d, plottedBubble.MeanActiveVehicles);
        Assert.Equal(180, plottedBubble.ActivitySampleNumerator);
        Assert.Equal(120, plottedBubble.ActivitySampleDenominator);
        Assert.Equal(2d, plottedBubble.AvailableCoverageHours);
        Assert.Equal(2d, plottedBubble.ExpectedCoverageHours);
        Assert.Equal("zero_denominator", view.Bubbles.Rows[1].OmissionReason);

        var bus = view.Timeline.Series.Single(series => series.Key == "category:bus");
        var rail = view.Timeline.Series.Single(series => series.Key == "category:rail");
        Assert.True(view.Timeline.IsStacked);
        Assert.Equal(new double?[] { 10, null, 40 }, bus.Values);
        Assert.Equal(new double?[] { 10, 25, 40 }, bus.KnownValues);
        Assert.Equal(new double?[] { 2, null, 5 }, rail.Values);
        Assert.Equal(new double?[] { 2, 10, 5 }, rail.KnownValues);
        Assert.Equal(12d, bus.PointEvidence![0]!.CombinedPlottedSubtotal);
        Assert.Null(bus.PointEvidence[1]!.CombinedPlottedSubtotal);
        Assert.Equal(45d, bus.PointEvidence[2]!.CombinedPlottedSubtotal);
        Assert.Equal("2026-04-02T05:00:00.0000000Z", bus.PointEvidence[0]!.IntervalStartUtc);
        Assert.Contains("01:00 -04:00", bus.PointEvidence[0]!.LocalLabel);
    }

    [Fact]
    public void AllProjectionUsesEachCityDateAndTimezoneAndPadsTwentyThreeHourCityToTwentyFiveHourAxis()
    {
        var denver = Enumerable.Range(1, 23).Select(hour => Bucket(hour, 1,
            hour, hour, localDate: new DateOnly(2026, 4, 1), offset: "-06:00")).ToArray();
        var toronto = Enumerable.Range(1, 25).Select(hour => Bucket(hour,
            hour == 25 ? null : 1,
            hour == 25 ? null : hour,
            hour == 25 ? null : hour,
            state: hour == 25 ? "pending" : "complete",
            reason: hour == 25 ? "future" : null,
            nullFuture: hour == 25,
            localDate: new DateOnly(2026, 11, 1),
            offset: hour == 2 ? "-04:00" : "-05:00")).ToArray();
        var response = Response("all", [], [],
            [
                new StatsTimelineSeries { Key = "city:denver", CitySlug = "denver", WindowReference = "today", Buckets = denver },
                new StatsTimelineSeries { Key = "city:toronto", CitySlug = "toronto", WindowReference = "today", Buckets = toronto }
            ],
            [Bubble("city:denver", "city", "denver", "StatsCityDenver", 7m, 12m, 1m, 7m, 2, 2, 14, 14, null),
             Bubble("city:toronto", "city", "toronto", "StatsCityToronto", 4m, 9m, 1m, 4m, 1, 1, 6, 6, null)],
            cities:
            [
                City("denver", "StatsCityDenver", new DateOnly(2026, 4, 1), "America/Denver"),
                City("toronto", "StatsCityToronto", new DateOnly(2026, 11, 1), "America/Toronto")
            ], extent: 25);

        var view = StatsOverlayProjection.Build(response, key => $"localized:{key}");

        Assert.False(view.Timeline.IsStacked);
        Assert.True(view.Timeline.EnableSeriesVisibility);
        Assert.Equal(25, view.Timeline.NumericXValues!.Count);
        Assert.Equal(25d, view.Timeline.NumericXValues[^1]);
        var denverSeries = view.Timeline.Series.Single(series => series.Key == "city:denver");
        var torontoSeries = view.Timeline.Series.Single(series => series.Key == "city:toronto");
        Assert.Equal(25, denverSeries.Values.Count);
        Assert.Null(denverSeries.Values[23]);
        Assert.Equal(23d, denverSeries.PointEvidence![22]!.KnownCumulativeKilometers);
        Assert.Contains("01:00 -06:00", denverSeries.PointEvidence[0]!.LocalLabel);
        Assert.Contains("01:00 -05:00", torontoSeries.PointEvidence![0]!.LocalLabel);
        Assert.Equal("-04:00", torontoSeries.PointEvidence[1]!.UtcOffset);
        Assert.Null(torontoSeries.Values[24]);
        Assert.Null(torontoSeries.PointEvidence[24]!.HourlyKnownKilometers);
        Assert.Null(torontoSeries.PointEvidence[24]!.KnownCumulativeKilometers);
        Assert.Equal("localized:StatsCityDenver", denverSeries.Label);
        var denverBubble = view.Bubbles.Rows.Single(row => row.Key == "city:denver");
        Assert.Null(denverBubble.Color);
        Assert.Equal("city:denver", denverBubble.ColorKey);
        Assert.Equal("localized:StatsCityDenver", denverBubble.Label);
        Assert.Equal("localized:StatsCityDenver", denverBubble.TooltipLines![0]);
        Assert.Equal(2, view.Bubbles.GetTableRows().Count);
    }

    [Fact]
    public void TimelinePointEvidenceLocalizesMetricLabelsAndCoverageInsteadOfShowingWireCodes()
    {
        var response = Response("city", [], [],
            [new StatsTimelineSeries
            {
                Key = "category:bus", CategoryKey = "bus", WindowReference = "today",
                Buckets = [Bucket(1, 10, 10, 10, state: "partial")]
            }],
            []);

        var view = StatsOverlayProjection.Build(response, key => $"localized:{key}");
        var evidence = Assert.Single(view.Timeline.Series).PointEvidence![0]!;

        Assert.Contains(evidence.TooltipLines, line => line.StartsWith("localized:StatsCoveragePartial:", StringComparison.Ordinal));
        Assert.Contains(evidence.TooltipLines, line => line.StartsWith("localized:StatsHourlyKnown: 10.00 localized:StatsDistanceUnit", StringComparison.Ordinal));
        Assert.Contains(evidence.TooltipLines, line => line.StartsWith("localized:StatsKnownCumulative: 10.00 localized:StatsDistanceUnit", StringComparison.Ordinal));
        Assert.DoesNotContain(evidence.TooltipLines, line => line.StartsWith("partial:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("partial")]
    [InlineData("pending")]
    [InlineData("unavailable")]
    public void PieProjectionKeepsFiniteCoverageStatesAndNullAmountsDistinct(string coverageState)
    {
        var response = Response("city", [],
            [new StatsCategoryShare("bus", null, null, "today")], [], [],
            extent: 0) with
        {
            TodayDistance = new StatsDistanceSummary
            {
                Window = "today", Kilometers = null, Coverage = Coverage(coverageState, 4, 0)
            }
        };

        var view = StatsOverlayProjection.Build(response, key => key);
        var row = Assert.Single(view.CategoryPie.Rows);

        Assert.Equal(coverageState, row.CoverageReference);
        Assert.Null(row.Kilometers);
        Assert.Null(row.Percent);
    }

    [Fact]
    public void TimelineRetainsKnownAmountsAtFinalAndAllPointPlotGaps()
    {
        var response = Response("city", [], [],
            [
                new StatsTimelineSeries
                {
                    Key = "category:bus", CategoryKey = "bus", WindowReference = "today",
                    Buckets = [Bucket(1, 7, 7, 7), Bucket(2, null, 10, null, "partial")]
                },
                new StatsTimelineSeries
                {
                    Key = "category:rail", CategoryKey = "rail", WindowReference = "today",
                    Buckets = [Bucket(1, 3, 3, 3), Bucket(2, null, 4, null, "partial")]
                },
                new StatsTimelineSeries
                {
                    Key = "category:ferry", CategoryKey = "ferry", WindowReference = "today",
                    Buckets = [Bucket(1, null, 0, null, "partial"), Bucket(2, null, 0, null, "partial")]
                }
            ], [], extent: 2);

        var view = StatsOverlayProjection.Build(response, key => key);
        var bus = view.Timeline.Series.Single(series => series.Key == "category:bus");
        var rail = view.Timeline.Series.Single(series => series.Key == "category:rail");
        var ferry = view.Timeline.Series.Single(series => series.Key == "category:ferry");

        Assert.Equal(new double?[] { 7, null }, bus.Values);
        Assert.Equal(new double?[] { 7, 10 }, bus.KnownValues);
        Assert.Equal(new double?[] { 3, null }, rail.Values);
        Assert.Equal(new double?[] { 3, 4 }, rail.KnownValues);
        Assert.Equal(new double?[] { null, null }, ferry.Values);
        Assert.Equal(new double?[] { 0, 0 }, ferry.KnownValues);
    }

    static StatsOverlayResponse Response(
        string scope,
        string[] routeKeys,
        IReadOnlyList<StatsCategoryShare> shares,
        IReadOnlyList<StatsTimelineSeries> timeline,
        IReadOnlyList<StatsBubbleStatistic> bubbles,
        IReadOnlyList<StatsCityWindow>? cities = null,
        decimal extent = 3) => new()
    {
        GeneratedAtUtc = new DateTime(2026, 4, 2, 12, 0, 0, DateTimeKind.Utc),
        CutoffUtc = new DateTime(2026, 4, 2, 11, 0, 0, DateTimeKind.Utc),
        Selection = new StatsSelection(scope, scope == "city" ? "toronto" : null, routeKeys),
        Cities = cities ?? [City("toronto", "StatsCityToronto", new DateOnly(2026, 4, 2), "America/Toronto")],
        RouteOptions = [],
        TodayDistance = new StatsDistanceSummary
        {
            Window = "today", Kilometers = 50,
            Coverage = Coverage("partial", 3, 2)
        },
        Last24Distance = new StatsDistanceSummary
        {
            Window = "last24CompletedHours", Kilometers = 160,
            Coverage = Coverage("complete", 24, 24)
        },
        CategoryShares = shares,
        Bubbles = bubbles,
        Timeline = new StatsTimeline { SeriesKind = scope == "city" ? "category" : "city", Stacked = scope == "city", ElapsedHourExtent = extent, Series = timeline },
        Warnings = []
    };

    static StatsCityWindow City(string slug, string label, DateOnly localDate, string zone) => new()
    {
        CitySlug = slug, DisplayNameKey = label, LocalDate = localDate, TimeZone = zone,
        LocalDayStartUtc = new DateTime(2026, 4, 2, 4, 0, 0, DateTimeKind.Utc),
        LocalDayEndUtc = new DateTime(2026, 4, 3, 4, 0, 0, DateTimeKind.Utc),
        RequestedTodayStartUtc = new DateTime(2026, 4, 2, 4, 0, 0, DateTimeKind.Utc),
        RequestedTodayEndUtc = new DateTime(2026, 4, 2, 12, 0, 0, DateTimeKind.Utc),
        Last24StartUtc = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc),
        Last24EndUtc = new DateTime(2026, 4, 2, 12, 0, 0, DateTimeKind.Utc),
        TodayCoverage = Coverage("partial", 8, 6), Last24Coverage = Coverage("complete", 24, 24)
    };

    static StatsCoverage Coverage(string state, int expectedHours, int availableHours) => new()
    {
        State = state, ExpectedClosedHours = expectedHours, ContributingHours = availableHours,
        FullyCoveredHours = availableHours, AvailableRouteHours = availableHours,
        PendingThroughUtc = new DateTime(2026, 4, 2, 12, 0, 0, DateTimeKind.Utc),
        Exclusions = []
    };

    static StatsBubbleStatistic Bubble(
        string key, string kind, string city, string label,
        decimal? x, decimal? y, decimal? mean, decimal? distance,
        long? routeHours, long? activeHours, long? numerator, long? denominator,
        string? category, string state = "partial", string? omission = null) => new()
    {
        EntityKey = key, EntityKind = kind, CitySlug = city, Label = label,
        RouteJoinKey = kind == "route" ? key : null, CategoryKey = category,
        X = x, Y = y, MeanActiveVehicles = mean, DistanceKilometers = distance,
        RouteVehicleHours = routeHours, ActiveClockHours = activeHours,
        ActivitySampleNumerator = numerator, SampleDenominator = denominator,
        Coverage = Coverage(state, 2, 2), OmissionReason = omission
    };

    static StatsTimelineBucket Bucket(
        int elapsed,
        decimal? hourly,
        decimal? known,
        decimal? plotted,
        string state = "complete",
        string? reason = null,
        bool nullFuture = false,
        DateOnly? localDate = null,
        string offset = "-04:00") => new()
    {
        StartUtc = new DateTime(2026, 4, 2, 5, 0, 0, DateTimeKind.Utc).AddHours(elapsed - 1),
        EndUtc = new DateTime(2026, 4, 2, 6, 0, 0, DateTimeKind.Utc).AddHours(elapsed - 1),
        LocalDate = localDate ?? new DateOnly(2026, 4, 2), LocalEndLabel = $"{elapsed % 24:00}:00", UtcOffset = offset,
        ElapsedHours = elapsed, HourlyKnownKilometers = nullFuture ? null : hourly,
        CumulativeObservedKilometers = nullFuture ? null : known,
        PlotCumulativeKilometers = nullFuture ? null : plotted,
        CoverageState = state, Reason = reason
    };
}
