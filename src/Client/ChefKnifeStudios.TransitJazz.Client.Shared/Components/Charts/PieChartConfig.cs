#nullable enable

using System.Collections.Generic;
using System.Linq;
using pax.BlazorChartJs;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Components.Charts;

internal static class PieChartConfig
{
    public static ChartJsConfig Build(PieChartModel model, bool isDark)
    {
        model.Validate();
        var theme = ChartPresentation.GetThemeColors(isDark);
        return new ChartJsConfig
        {
            Type = ChartType.pie,
            Data = new ChartJsData
            {
                Labels = model.GetPositiveSlices().Select(slice => slice.Label).ToList(),
                Datasets = BuildDatasets(model, isDark)
            },
            Options = BuildOptions(isDark)
        };
    }

    public static IList<ChartJsDataset> BuildDatasets(PieChartModel model, bool isDark)
    {
        var slices = model.GetPositiveSlices();
        var theme = ChartPresentation.GetThemeColors(isDark);
        return
        [
            new PieDataset
            {
                Id = "category-distance",
                Label = "Observed distance (km)",
                Data = slices.Select(slice => (object)slice.Kilometers).ToList(),
                BackgroundColor = new IndexableOption<string>(slices.Select(slice =>
                    ChartPresentation.GetSeriesStyle("category:" + slice.Key, isDark).Color).ToList()),
                BorderColor = new IndexableOption<string>(theme.SurfaceColor),
                BorderWidth = 2
            }
        ];
    }

    public static ChartJsOptions BuildOptions(bool isDark)
    {
        var theme = ChartPresentation.GetThemeColors(isDark);
        return new ChartJsOptions
        {
            Responsive = true,
            MaintainAspectRatio = false,
            Color = new IndexableOption<string>(theme.TextColor),
            // Category slices are evidence, not independent filters; hiding one would mislead the total.
            Plugins = new Plugins
            {
                Legend = new Legend { Display = false },
                Tooltip = new Tooltip
                {
                    Callbacks = new TooltipCallbacks { Label = ChartJsFunction.FromName("formatStatsPiePercentage") }
                }
            }
        };
    }
}
