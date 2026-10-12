using pax.BlazorChartJs;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public class PaxTypeProbeTests
{
    [Fact]
    public void PinnedChartPackageExposesAxisTickGridAndBorderColors()
    {
        Assert.Equal(typeof(ChartJsAxisTick), typeof(ChartJsAxis).GetProperty(nameof(ChartJsAxis.Ticks))!.PropertyType);
        Assert.Equal(typeof(ChartJsGrid), typeof(ChartJsAxis).GetProperty(nameof(ChartJsAxis.Grid))!.PropertyType);
        Assert.Equal(typeof(ChartJsAxisBorder), typeof(ChartJsAxis).GetProperty(nameof(ChartJsAxis.Border))!.PropertyType);
        Assert.Equal(typeof(IndexableOption<string>), typeof(ChartJsAxisTick).GetProperty(nameof(ChartJsAxisTick.Color))!.PropertyType);
        Assert.Equal(typeof(IndexableOption<string>), typeof(ChartJsGrid).GetProperty(nameof(ChartJsGrid.Color))!.PropertyType);
        Assert.Equal(typeof(IndexableOption<string>), typeof(ChartJsAxisBorder).GetProperty(nameof(ChartJsAxisBorder.Color))!.PropertyType);
    }
}
