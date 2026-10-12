using ChefKnifeStudios.TransitJazz.Client.Shared.Components.Stats;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class StatsRouteSelectionTests
{
    [Fact]
    public void SelectionIsOrdinalSortedAndDoesNotNormalizeRouteKeys()
    {
        var selection = new StatsRouteSelectionState();

        Assert.True(selection.Toggle("Route:A"));
        Assert.True(selection.Toggle("route:a"));
        Assert.False(selection.Toggle("Route:A"));

        Assert.Equal(new[] { "route:a" }, selection.SelectedKeys);
    }

    [Fact]
    public void SelectAllAndCategoryClearUseOnlyProvidedOptions()
    {
        var selection = new StatsRouteSelectionState();
        var options = new[]
        {
            Option("bus-2", "bus"),
            Option("rail-1", "rail"),
            Option("bus-1", "bus")
        };

        selection.SelectAll(options);
        Assert.Equal(new[] { "bus-1", "bus-2", "rail-1" }, selection.SelectedKeys);

        foreach (var option in options.Where(option => option.CategoryKey == "bus"))
            selection.Toggle(option.RouteJoinKey);
        Assert.Equal(new[] { "rail-1" }, selection.SelectedKeys);
    }

    [Fact]
    public void SelectedRouteMissingFromRefreshedMetadataRemainsSelectedAndExplainable()
    {
        var selection = new StatsRouteSelectionState();
        selection.Toggle("retained:Route-7");
        var rows = selection.MergeOptions([Option("other-route", "bus")], "toronto");

        Assert.Equal(new[] { "other-route", "retained:Route-7" }, rows.Select(row => row.RouteJoinKey));
        var absent = rows.Single(row => row.RouteJoinKey == "retained:Route-7");
        Assert.Equal("retained:Route-7", absent.Label);
        Assert.Equal("route_not_retained", absent.AvailabilityReason);
        Assert.Equal(new[] { "retained:Route-7" }, selection.SelectedKeys);
    }

    [Fact]
    public void SelectedAbsentRowsKeepServerUnknownVersusPreviouslyRetainedReason()
    {
        var selection = new StatsRouteSelectionState();
        selection.Toggle("historical-route");
        selection.Toggle("never-known-route");
        var evidence = new[]
        {
            new StatsSelectedRouteCoverage("toronto", "historical-route", 4, 2, ["route_not_retained"]),
            new StatsSelectedRouteCoverage("toronto", "never-known-route", 4, 0, ["unknown_route"])
        };

        var rows = selection.MergeOptions([], "toronto", evidence).ToDictionary(row => row.RouteJoinKey, StringComparer.Ordinal);

        Assert.Equal("route_not_retained", rows["historical-route"].AvailabilityReason);
        Assert.Equal("unknown_route", rows["never-known-route"].AvailabilityReason);
    }

    static StatsRouteOption Option(string key, string category) => new()
    {
        CitySlug = "toronto",
        RouteJoinKey = key,
        Label = key,
        CategoryKey = category,
        MetadataTimestampUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
    };
}
