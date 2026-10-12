using ChefKnifeStudios.TransitJazz.Client.Shared.Components.Charts;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class StatsAccessibleEvidenceTests
{
    [Fact]
    public void PieTableRetainsZeroAndUnavailableRowsThatDoNotCreateCanvasSlices()
    {
        var model = new PieChartModel(
        [
            new PieChartRow("bus", "Bus", 30, 60, "partial"),
            new PieChartRow("rail", "Rail", 20, 40, "complete"),
            new PieChartRow("tram", "Tram", 0, 0, "complete"),
            new PieChartRow("ferry", "Ferry", null, null, "unavailable")
        ]);

        model.Validate();

        Assert.Equal(new[] { "bus", "rail" }, model.GetPositiveSlices().Select(slice => slice.Key));
        Assert.Equal(new double?[] { 30, 20, 0, null }, model.GetTableKilometers());
        Assert.Equal(new[] { "partial", "complete", "complete", "unavailable" }, model.Rows.Select(row => row.CoverageReference));
    }

    [Fact]
    public void BubbleTableRetainsUnavailableAndCoincidentEvidenceWhileCanvasUsesExactFiniteRows()
    {
        var model = new BubbleChartModel(
        [
            new BubbleChartRow("r1", "Route one", 0, 0, 1.5, "complete", null, "bus", ["180/120"], 30, 6, 2, 180, 120, 24, 24),
            new BubbleChartRow("r2", "Route two", 0, 0, 1, "partial", null, "rail", ["missing hour"], 10, 2, 1, 20, 20, 24, 20),
            new BubbleChartRow("r3", "Unavailable", null, null, null, "unavailable", "missing_cohort", "tram", ["missing cohort"])
        ]);

        model.Validate();

        Assert.Equal(new[] { "r1", "r2" }, model.GetPlottedPoints().Select(point => point.Key));
        Assert.Equal((0d, 0d, 30d, 6d, 2d, 180L, 120L),
            (model.GetPlottedPoints()[0].X, model.GetPlottedPoints()[0].Y,
                model.GetPlottedPoints()[0].DistanceKilometers,
                model.GetPlottedPoints()[0].RouteVehicleHours,
                model.GetPlottedPoints()[0].ActiveClockHours,
                model.GetPlottedPoints()[0].ActivitySampleNumerator,
                model.GetPlottedPoints()[0].ActivitySampleDenominator));
        Assert.Equal(3, model.GetTableRows().Count);
        Assert.Equal("missing_cohort", model.GetTableRows()[2].OmissionReason);
    }

    [Fact]
    public void TimelineModelKeepsPerCityUtcLocalCoverageAndKnownValuesWhenCanvasPlotsAGap()
    {
        var points = new ChartPointEvidence?[]
        {
            new("Oct 31 · 23:00 -06:00", "2026-11-01T04:00:00.0000000Z", "2026-11-01T05:00:00.0000000Z",
                "-06:00", "partial", 7, 7, 7, ["Toronto local 01:00 -04:00"]),
            new("Nov 1 · 00:00 -06:00", "2026-11-01T05:00:00.0000000Z", "2026-11-01T06:00:00.0000000Z",
                "-06:00", "partial", null, 5, 12, ["gap; known subtotal 12"])
        };
        var series = new ChartSeries("city:denver", "Denver", [7, null], [7, 12], points);
        var model = new SeriesChartModel("Elapsed hours", "Kilometers", ["1", "2"], [1, 2],
            [series], isStacked: false, numericAxisLabelMode: NumericAxisLabelMode.ElapsedHours,
            enableSeriesVisibility: true);

        model.Validate();

        Assert.Equal(new double?[] { 7, null }, series.CanvasValues);
        Assert.Equal(series.CanvasValues, series.TableValues);
        Assert.Equal(new double?[] { 7, 12 }, series.KnownValues);
        Assert.Equal("2026-11-01T05:00:00.0000000Z", series.PointEvidence![0]!.IntervalEndUtc);
        Assert.Equal("-06:00", series.PointEvidence[0]!.UtcOffset);
        Assert.Equal("partial", series.PointEvidence[1]!.CoverageState);
        Assert.Equal(12, series.PointEvidence[1]!.KnownCumulativeKilometers);
    }
}
