using ChefKnifeStudios.TransitJazz.Client.Shared.Components.Charts;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class StatsChartModelTests
{
    [Fact]
    public void SeriesModel_ValidatesAlignedFiniteValuesAndIncreasingNumericX()
    {
        var model = new SeriesChartModel(
            "Hours since local midnight",
            "Observed distance (km)",
            ["00:00", "01:00", "02:00"],
            [0, 1, 2],
            [new ChartSeries("bus", "Bus", [12, null, 0], [12, 15, 0])],
            isStacked: false);

        model.Validate();
        Assert.Equal(3, model.Labels.Count);
        Assert.Equal(12d, model.Series[0].Values[0]);
        Assert.Null(model.Series[0].Values[1]);
        Assert.Equal(0d, model.Series[0].Values[2]);
        Assert.Equal(model.Series[0].Values, model.Series[0].CanvasValues);
        Assert.Equal(model.Series[0].Values, model.Series[0].TableValues);
        Assert.Equal(new double?[] { 12, 15, 0 }, model.Series[0].KnownValues);
    }

    [Fact]
    public void SeriesModel_RejectsMisalignmentNonfiniteValuesAndNonincreasingX()
    {
        Assert.Throws<ArgumentException>(() => new SeriesChartModel(
            "Time", "Kilometers", ["00:00", "01:00"], [0, 1],
            [new ChartSeries("bus", "Bus", [1])], false).Validate());

        Assert.Throws<ArgumentException>(() => new SeriesChartModel(
            "Time", "Kilometers", ["00:00", "01:00"], [0, 1],
            [new ChartSeries("bus", "Bus", [1, double.NaN])], false).Validate());

        Assert.Throws<ArgumentException>(() => new SeriesChartModel(
            "Time", "Kilometers", ["00:00", "01:00"], [0, 0],
            [new ChartSeries("bus", "Bus", [1, 2])], false).Validate());

        Assert.Throws<ArgumentException>(() => new SeriesChartModel(
            "Time", "Kilometers", ["00:00", "01:00"], [0, 1],
            [new ChartSeries("bus", "Bus", [1, 2], [1, double.PositiveInfinity])], false).Validate());

        Assert.Throws<ArgumentException>(() => new SeriesChartModel(
            "Time", "Kilometers", ["00:00", "01:00"], [0, 1],
            [new ChartSeries("bus", "Bus", [1, 2])], false,
            [new ChartPointEvidence("midnight", "00Z", "01Z", "+00", "complete", 1, 1, 1, ["evidence"])]).Validate());
    }

    [Fact]
    public void PointEvidenceCarriesLocalAndUtcIdentityAndKnownVersusPlottedAmounts()
    {
        var evidence = new ChartPointEvidence(
            "01:00 -04:00", "2026-04-01T05:00:00Z", "2026-04-01T06:00:00Z", "-04:00", "partial",
            13, 13, 37, ["Bus: 8 km", "Combined plotted subtotal: 13 km", "Known cumulative: 37 km"]);
        var model = new SeriesChartModel(
            "Local time", "Cumulative kilometers", ["bucket-1"], [1],
            [new ChartSeries("bus", "Bus", [8], [13])], true, [evidence]);

        model.Validate();

        Assert.Equal("01:00 -04:00", model.PointEvidence![0]!.LocalLabel);
        Assert.Equal("2026-04-01T05:00:00Z", model.PointEvidence[0]!.IntervalStartUtc);
        Assert.Equal(13, model.PointEvidence[0]!.HourlyKnownKilometers);
        Assert.Equal(37, model.PointEvidence[0]!.KnownCumulativeKilometers);
        Assert.Equal(8, model.Series[0].Values[0]);
        Assert.Equal(13, model.Series[0].KnownValues![0]);
    }

    [Fact]
    public void AllCitySeriesKeepTheirOwnTimezoneDateCoverageAndKnownTimelineEvidence()
    {
        var toronto = new ChartSeries("city:toronto", "Toronto", [40, 55], [40, 55],
        [
            new ChartPointEvidence("Apr 2 · 01:00 -04:00", "2026-04-02T05:00:00Z", "2026-04-02T06:00:00Z", "-04:00", "complete", 40, 40, 40,
                ["Toronto · 2026-04-02 01:00 -04:00", "Hourly known: 40 km", "Known cumulative: 40 km"]),
            new ChartPointEvidence("Apr 2 · 02:00 -04:00", "2026-04-02T06:00:00Z", "2026-04-02T07:00:00Z", "-04:00", "partial", 15, 15, 55,
                ["Toronto · 2026-04-02 02:00 -04:00", "Hourly known: 15 km", "Known cumulative: 55 km"])
        ]);
        var denver = new ChartSeries("city:denver", "Denver", [30, null], [30, 42],
        [
            new ChartPointEvidence("Apr 1 · 23:00 -06:00", "2026-04-02T05:00:00Z", "2026-04-02T06:00:00Z", "-06:00", "complete", 30, 30, 30,
                ["Denver · 2026-04-01 23:00 -06:00", "Hourly known: 30 km", "Known cumulative: 30 km"]),
            new ChartPointEvidence("Apr 2 · 00:00 -06:00", "2026-04-02T06:00:00Z", "2026-04-02T07:00:00Z", "-06:00", "partial", null, 12, 42,
                ["Denver · 2026-04-02 00:00 -06:00", "Hourly known: 12 km", "Known cumulative: 42 km"])
        ]);
        var model = new SeriesChartModel("Elapsed hours", "Cumulative kilometers", ["Hour 1", "Hour 2"], [1, 2],
            [toronto, denver], isStacked: false, numericAxisLabelMode: NumericAxisLabelMode.ElapsedHours);

        model.Validate();

        Assert.Equal(NumericAxisLabelMode.ElapsedHours, model.NumericAxisLabelMode);
        Assert.Equal("Apr 2 · 01:00 -04:00", model.Series[0].PointEvidence![0]!.LocalLabel);
        Assert.Equal("Apr 1 · 23:00 -06:00", model.Series[1].PointEvidence![0]!.LocalLabel);
        Assert.Equal("2026-04-02T05:00:00Z", model.Series[0].PointEvidence[0]!.IntervalStartUtc);
        Assert.Equal("2026-04-02T05:00:00Z", model.Series[1].PointEvidence[0]!.IntervalStartUtc);
        Assert.Equal(55, model.Series[0].PointEvidence[1]!.KnownCumulativeKilometers);
        Assert.Equal(42, model.Series[1].PointEvidence[1]!.KnownCumulativeKilometers);
        Assert.Null(model.Series[1].Values[1]);
    }

    [Fact]
    public void CityNumericAxisRequiresPerSeriesEvidenceForClockAndOffsetTicks()
    {
        Assert.Throws<ArgumentException>(() => new SeriesChartModel(
            "Local time", "Kilometers", ["01:00"], [1], [new ChartSeries("city:toronto", "Toronto", [1])], false,
            numericAxisLabelMode: NumericAxisLabelMode.LocalClockOffset).Validate());
    }

    [Fact]
    public void StackedSeriesRetainRawContributionsAndNullGapForCanvasAndTable()
    {
        var model = new SeriesChartModel(
            "Hours since local midnight",
            "Observed distance (km)",
            ["00:00", "01:00", "02:00"],
            [0, 1, 2],
            [
                new ChartSeries("bus", "Bus", [10, null, 4]),
                new ChartSeries("rail", "Rail", [2, null, 6])
            ],
            isStacked: true);

        model.Validate();

        Assert.True(model.IsStacked);
        Assert.Equal(new double?[] { 10, null, 4 }, model.Series[0].CanvasValues);
        Assert.Equal(new double?[] { 10, null, 4 }, model.Series[0].TableValues);
        Assert.Equal(new double?[] { 2, null, 6 }, model.Series[1].CanvasValues);
        Assert.Equal(new double?[] { 2, null, 6 }, model.Series[1].TableValues);
        Assert.Equal(12d, model.Series.Sum(series => series.Values[0]));
        Assert.Equal(10d, model.Series.Sum(series => series.Values[2]));
        Assert.All(model.Series, series => Assert.Null(series.Values[1]));
    }

    [Fact]
    public void SeriesStylesAreStableByKeyAndUseThemePalette()
    {
        var lightBus = ChartPresentation.GetSeriesStyle("bus", isDark: false);
        var lightRail = ChartPresentation.GetSeriesStyle("rail", isDark: false);
        var reorderedLightBus = ChartPresentation.GetSeriesStyle("bus", isDark: false);
        var darkBus = ChartPresentation.GetSeriesStyle("bus", isDark: true);

        Assert.Equal(lightBus, reorderedLightBus);
        Assert.Equal(lightBus, ChartPresentation.GetSeriesStyle("category:bus", isDark: false));
        Assert.Equal(lightRail, ChartPresentation.GetSeriesStyle("category:rail", isDark: false));
        Assert.NotEqual(lightBus.Color, lightRail.Color);
        Assert.NotEqual(lightBus.Color, darkBus.Color);
        Assert.NotEqual(lightBus.TextColor, darkBus.TextColor);
        Assert.Equal(ChartPresentation.GetSeriesStyle("future-category", false),
            ChartPresentation.GetSeriesStyle("future-category", false));
        var configuredCities = new[] { "atlanta", "boston", "new-york-city", "toronto", "philadelphia", "washington-dc", "denver" };
        Assert.Equal(configuredCities.Length, configuredCities.Select(city => ChartPresentation.GetSeriesStyle(city, false).Color).Distinct().Count());
    }

    [Fact]
    public void PieKeepsZeroAndUnavailableRowsButOnlyPositiveAmountsBecomeSlices()
    {
        var model = new PieChartModel(
            [
                new PieChartRow("bus", "Bus", 0, null, "complete"),
                new PieChartRow("rail", "Rail", 25, 100, "partial"),
                new PieChartRow("other", "Other", null, null, "unavailable")
            ]);

        model.Validate();

        Assert.Equal(3, model.Rows.Count);
        var slices = model.GetPositiveSlices();
        Assert.Single(slices);
        Assert.Equal("rail", slices[0].Key);
        Assert.Equal(25d, slices[0].Kilometers);
        Assert.Equal(model.Rows.Select(row => row.Kilometers), model.GetTableKilometers());
    }

    [Fact]
    public void BubbleModelOmitsUndefinedAndZeroAreaRowsAndKeepsCoincidentZeroCoordinates()
    {
        var model = new BubbleChartModel(
            [
                new BubbleChartRow("route-a", "Route A", 0, 0, 4, "complete", null),
                new BubbleChartRow("route-b", "Route B", 0, 0, 1, "partial", null),
                new BubbleChartRow("route-c", "Route C", null, 2, 3, "unavailable", "zero_denominator"),
                new BubbleChartRow("route-d", "Route D", 4, 2, 0, "complete", "no_active_vehicles")
            ]);

        model.Validate();
        var plotted = model.GetPlottedPoints();

        Assert.Equal(2, plotted.Count);
        Assert.Equal(new[] { "route-a", "route-b" }, plotted.Select(point => point.Key));
        Assert.All(plotted, point => Assert.Equal((0d, 0d), (point.X, point.Y)));
        Assert.Equal(24d, plotted[0].RadiusPixels);
        Assert.Equal(12d, plotted[1].RadiusPixels);
        Assert.Equal(4, model.Rows.Count);
        Assert.Equal(4, model.GetTableRows().Count);
        Assert.Equal("zero_denominator", model.GetTableRows()[2].OmissionReason);
        Assert.Equal("no_active_vehicles", model.GetTableRows()[3].OmissionReason);
    }

    [Fact]
    public void BubblePointsRetainEntityColorTooltipAndExactMetrics()
    {
        var model = new BubbleChartModel(
        [
            new BubbleChartRow("route:10", "Route 10", 2, 5, 4, "partial", null,
                "bus", ["Route 10", "5 active vehicles; 6 of 8 route hours"], 120, 8, 3.5, 7, 10, 8, 6)
        ]);

        var point = Assert.Single(model.GetPlottedPoints());

        Assert.Equal("bus", point.ColorKey);
        Assert.Equal("Route 10", point.Label);
        Assert.Equal(new[] { "Route 10", "5 active vehicles; 6 of 8 route hours" }, point.TooltipLines);
        Assert.Equal(120, point.DistanceKilometers);
        Assert.Equal(7, point.ActivitySampleNumerator);
        Assert.Equal(10, point.ActivitySampleDenominator);
        Assert.Equal(8, point.ExpectedCoverageHours);
        Assert.Equal(6, point.AvailableCoverageHours);
    }

    [Fact]
    public void BubbleSampleRatioMayExceedOneAndRetainsExactNumeratorAndDenominator()
    {
        var model = new BubbleChartModel(
        [
            new BubbleChartRow("route:10", "Route 10", 5, 15, 1.5, "partial", null,
                "bus", ["180 / 120 activity samples"], 30, 6, 2, 180, 120, 8, 6)
        ]);

        model.Validate();
        var point = Assert.Single(model.GetPlottedPoints());
        Assert.Equal(1.5, point.MeanActiveVehicles);
        Assert.Equal(180, point.ActivitySampleNumerator);
        Assert.Equal(120, point.ActivitySampleDenominator);
    }

    [Fact]
    public void BubbleRadiusUsesSquareRootScaleAndDisclosedThreePixelMinimum()
    {
        var model = new BubbleChartModel(
            [
                new BubbleChartRow("large", "Large", 1, 2, 16, "complete", null),
                new BubbleChartRow("quarter", "Quarter", 1, 2, 4, "complete", null),
                new BubbleChartRow("tiny", "Tiny", 1, 2, 0.01, "complete", null)
            ]);

        var plotted = model.GetPlottedPoints();

        Assert.Equal(24d, plotted[0].RadiusPixels);
        Assert.Equal(12d, plotted[1].RadiusPixels);
        Assert.Equal(3d, plotted[2].RadiusPixels);
        Assert.Equal(3d, BubbleChartModel.MinimumMarkerRadiusPixels);
    }

    [Fact]
    public void ChartModelsRejectDuplicateKeysAndInvalidMeasurements()
    {
        Assert.Throws<ArgumentException>(() => new SeriesChartModel(
            "Time", "Kilometers", ["00:00"], null,
            [new ChartSeries("bus", "Bus", [1]), new ChartSeries("bus", "Bus again", [2])], false).Validate());

        Assert.Throws<ArgumentException>(() => new PieChartModel(
            [new PieChartRow("bus", "Bus", -1, 100, "complete")]).Validate());

        Assert.Throws<ArgumentException>(() => new BubbleChartModel(
            [new BubbleChartRow("route", "Route", -1, 0, 1, "complete", null)]).Validate());

        Assert.Throws<ArgumentException>(() => new BubbleChartModel(
            [new BubbleChartRow("route", "Route", 1, 0, double.NaN, "complete", null)]).Validate());

        Assert.Throws<ArgumentException>(() => new BubbleChartModel(
            [new BubbleChartRow("route", "Route", 1, 0, 1, "complete", null, ActivitySampleNumerator: -1, ActivitySampleDenominator: 4)]).Validate());
    }
}
