#nullable enable

using System.Collections.Generic;
using System.Linq;
using pax.BlazorChartJs;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components.Charts;

internal static class SeriesChartConfig
{
    public static ChartJsConfig Build(SeriesChartModel model, bool isDark)
    {
        model.Validate();
        return new ChartJsConfig
        {
            Type = ChartType.line,
            Data = new ChartJsData
            {
                Labels = model.Labels.ToList(),
                Datasets = BuildDatasets(model, isDark)
            },
            Options = BuildOptions(model, isDark)
        };
    }

    public static IList<ChartJsDataset> BuildDatasets(SeriesChartModel model, bool isDark) => model.Series
        .Select(series =>
        {
            var style = ChartPresentation.GetSeriesStyle(series.Key, isDark);
            var values = Enumerable.Range(0, model.Labels.Count)
                .Select(index =>
                {
                    var evidence = series.PointEvidence?[index] ?? model.PointEvidence?[index];
                    return (object)new
                    {
                    x = model.NumericXValues is null ? (object)model.Labels[index] : model.NumericXValues[index],
                    y = series.CanvasValues[index],
                    tooltipLines = evidence is not null
                        ? new[] { $"{series.Label}: {Format(series.CanvasValues[index])}" }.Concat(evidence.TooltipLines).ToArray()
                        : new[] { $"{series.Label}: {Format(series.CanvasValues[index])}" },
                    tickLabel = model.NumericAxisLabelMode == NumericAxisLabelMode.LocalClockOffset ? evidence?.LocalLabel : null
                    };
                }).ToList();
            return (ChartJsDataset)new LineDataset
            {
                Id = series.Key,
                Label = series.Label,
                Data = values,
                BackgroundColor = style.Color,
                BorderColor = style.Color,
                PointBackgroundColor = style.Color,
                PointBorderColor = style.Color,
                PointRadius = model.NumericXValues is null ? 2 : 3,
                Tension = 0,
                SpanGaps = false,
                Fill = false,
                Stack = model.IsStacked ? "category-contributions" : null
            };
        }).ToList();

    static ChartJsGrid AxisGrid(ChartThemeColors theme) => new()
    {
        Color = new IndexableOption<string>(theme.GridColor),
        BorderColor = new IndexableOption<string>(theme.GridColor),
        TickColor = new IndexableOption<string>(theme.GridColor)
    };

    public static ChartJsOptions BuildOptions(SeriesChartModel model, bool isDark, ChartThemeColors? theme = null)
    {
        theme ??= ChartPresentation.GetThemeColors(isDark);
        var xAxis = model.NumericXValues is null
            ? (ChartJsAxis)new ChartJsAxis { Type = "category" }
            : new LinearAxis
            {
                Type = "linear",
                BeginAtZero = true,
                Ticks = new LinearAxisTick { Callback = ChartJsFunction.FromName("formatStatsElapsedTick") }
            };
        xAxis.Ticks ??= new ChartJsAxisTick();
        xAxis.Ticks.Color = new IndexableOption<string>(theme.TextColor);
        xAxis.Grid = AxisGrid(theme);
        xAxis.Border = new ChartJsAxisBorder { Color = new IndexableOption<string>(theme.GridColor) };
        xAxis.Title = new Title
        {
            Display = true,
            Text = new IndexableOption<string>(model.XAxisTitle),
            Color = new IndexableOption<string>(theme.TextColor)
        };
        var yAxis = new LinearAxis
        {
            BeginAtZero = true,
            Ticks = new LinearAxisTick { Color = new IndexableOption<string>(theme.TextColor) },
            Grid = AxisGrid(theme),
            Border = new ChartJsAxisBorder { Color = new IndexableOption<string>(theme.GridColor) },
            Title = new Title
            {
                Display = true,
                Text = new IndexableOption<string>(model.YAxisTitle),
                Color = new IndexableOption<string>(theme.TextColor)
            }
        };
        xAxis.Stacked = model.IsStacked;
        yAxis.Stacked = model.IsStacked;
        return new ChartJsOptions
        {
            Responsive = true,
            MaintainAspectRatio = false,
            Color = new IndexableOption<string>(theme.TextColor),
            BorderColor = new IndexableOption<string>(theme.GridColor),
            Stacked = model.IsStacked,
            Scales = new ChartJsOptionsScales { X = xAxis, Y = yAxis },
            Plugins = new Plugins
            {
                Legend = new Legend
                {
                    Display = model.IsStacked || model.EnableSeriesVisibility,
                    OnClickEvent = model.EnableSeriesVisibility,
                    OnClick = model.IsStacked ? ChartJsFunction.FromName("disableStatsLegendToggle")
                        : model.EnableSeriesVisibility ? ChartJsFunction.FromName("toggleStatsCityLegend")
                        : null
                },
                Tooltip = new Tooltip
                {
                    Callbacks = new TooltipCallbacks { Label = ChartJsFunction.FromName("formatStatsTooltip") }
                }
            }
        };
    }

    static string Format(double? value) => value?.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) ?? "—";
}
