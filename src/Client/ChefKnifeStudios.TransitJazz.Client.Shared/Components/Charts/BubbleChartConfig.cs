#nullable enable

using System.Collections.Generic;
using System.Linq;
using pax.BlazorChartJs;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components.Charts;

internal static class BubbleChartConfig
{
    public static ChartJsConfig Build(BubbleChartModel model, bool isDark, string xTitle, string yTitle)
    {
        model.Validate();
        return new ChartJsConfig
        {
            Type = ChartType.bubble,
            Data = new ChartJsData { Labels = [], Datasets = BuildDatasets(model, isDark) },
            Options = BuildOptions(isDark, xTitle, yTitle)
        };
    }

    public static IList<ChartJsDataset> BuildDatasets(BubbleChartModel model, bool isDark)
    {
        var points = model.GetPlottedPoints();
        var theme = ChartPresentation.GetThemeColors(isDark);
        return
        [
            new BubbleDataset
            {
                Id = "bubble-measures",
                Label = "Measured transit activity",
                Data = points.Select(point => (object)new
                {
                    x = point.X,
                    y = point.Y,
                    r = point.RadiusPixels,
                    entityLabel = point.Label,
                    tooltipLines = point.TooltipLines.Count > 0 ? point.TooltipLines.ToArray() : new[] { point.Label }
                }).ToList(),
                BackgroundColor = new IndexableOption<string>(points
                    .Select(point => point.Color ?? ChartPresentation.GetSeriesStyle(point.ColorKey, isDark).Color).ToList()),
                BorderColor = new IndexableOption<string>(points.Select(_ => theme.SurfaceColor).ToList()),
                BorderWidth = 1
            }
        ];
    }

    public static ChartJsOptions BuildOptions(bool isDark, string xTitle, string yTitle)
    {
        var theme = ChartPresentation.GetThemeColors(isDark);
        var tooltip = new Tooltip
        {
            Callbacks = new TooltipCallbacks { Label = ChartJsFunction.FromName("formatStatsTooltip") }
        };
        return new ChartJsOptions
        {
            Responsive = true,
            MaintainAspectRatio = false,
            Color = new IndexableOption<string>(theme.TextColor),
            BorderColor = new IndexableOption<string>(theme.GridColor),
            Scales = new ChartJsOptionsScales
            {
                X = new LinearAxis
                {
                    BeginAtZero = true,
                    Title = new Title { Display = true, Text = new IndexableOption<string>(xTitle), Color = new IndexableOption<string>(theme.TextColor) },
                    Ticks = new LinearAxisTick { Color = new IndexableOption<string>(theme.TextColor) },
                    Grid = AxisGrid(theme),
                    Border = new ChartJsAxisBorder { Color = new IndexableOption<string>(theme.GridColor) }
                },
                Y = new LinearAxis
                {
                    BeginAtZero = true,
                    Title = new Title { Display = true, Text = new IndexableOption<string>(yTitle), Color = new IndexableOption<string>(theme.TextColor) },
                    Ticks = new LinearAxisTick { Color = new IndexableOption<string>(theme.TextColor) },
                    Grid = AxisGrid(theme),
                    Border = new ChartJsAxisBorder { Color = new IndexableOption<string>(theme.GridColor) }
                }
            },
            Plugins = new Plugins { Legend = new Legend { Display = false }, Tooltip = tooltip }
        };
    }

    static ChartJsGrid AxisGrid(ChartThemeColors theme) => new()
    {
        Color = new IndexableOption<string>(theme.GridColor),
        BorderColor = new IndexableOption<string>(theme.GridColor),
        TickColor = new IndexableOption<string>(theme.GridColor)
    };
}
