using ChefKnifeStudios.TransitJazz.Client.Shared.Components;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using ChefKnifeStudios.TransitJazz.Shared.Stats;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class RouteFilterListTests
{
    [Fact]
    public void StatsRouteColorsMatchMapMetadataWithoutLosingHistoricalOrCaseDistinctRoutes()
    {
        RouteShapeFeature Shape(string key, string? color, string city = "atlanta") =>
            FakeApplicationViewModel.Shape(key, "bus", 3) with
            {
                Properties = new(key, key, color, "#FFFFFF", "bus", 3, city)
            };

        var app = new FakeApplicationViewModel(new[]
        {
            Shape("A", "#F4C300"), Shape("a", "#00AABB"),
            Shape("white", "#FFFFFF"), Shape("black", "#000000"), Shape("no-color", null),
            Shape("other-city", "#008000", "boston")
        });
        using var map = new RouteFilterViewModel(NullLogger<RouteFilterViewModel>.Instance, new NoopToastService(), app);
        StatsRouteOption[] options = new[] { "A", "a", "white", "black", "no-color", "other-city", "historical" }
            .Select(key => new StatsRouteOption { CitySlug = "atlanta", RouteJoinKey = key, Label = key, CategoryKey = "bus" })
            .ToArray();

        RouteFilterListModel Project(IReadOnlyDictionary<string, RouteShapeFeature>? shapes = null) =>
            RouteFilterListProjection.FromStats(options, ["A"], key => key, _ => null, _ => null,
                (_, label) => label, (_, label) => label, routeShapes: shapes);

        var items = Project(app.RouteShapes).Groups.Single().Items.ToDictionary(item => item.RouteKey, StringComparer.Ordinal);
        var fallbackColor = Project().Groups.Single().Items.First().Color;
        Assert.Equal(options.Length, items.Count);
        Assert.Equal("#F4C300", items["A"].Color);
        Assert.Equal("#00AABB", items["a"].Color);
        foreach (var key in new[] { "A", "a", "white", "black", "no-color" })
            Assert.Equal(map.RouteItems.Single(item => item.RouteJoinKey == key).Color, items[key].Color);
        Assert.Equal(fallbackColor, items["other-city"].Color);
        Assert.Equal(fallbackColor, items["historical"].Color);
        Assert.True(items["A"].IsSelected);
        Assert.False(items["a"].IsSelected);
        Assert.Empty(map.SelectedRouteJoinKeys);
        Assert.Equal("#FFFFFF", app.RouteShapes["white"].Properties.Color);
    }

    [Fact]
    public void StatsProjectionKeepsHistoricalNotesAndOrdinalSelectionWithMapStyleEmphasis()
    {
        StatsRouteOption[] options =
        [
            new() { CitySlug = "atlanta", RouteJoinKey = "A", Label = "Historical route A", CategoryKey = "bus",
                CategoryChanged = true, AvailabilityReason = "no_retained_history" },
            new() { CitySlug = "atlanta", RouteJoinKey = "a", Label = "a", CategoryKey = "bus" },
            new() { CitySlug = "atlanta", RouteJoinKey = "rail", Label = "rail", CategoryKey = "rail" }
        ];
        string[] selectedKeys = ["A"];

        RouteFilterListModel Project(IEnumerable<string> selected) => RouteFilterListProjection.FromStats(
            options, selected, key => key,
            option => option.AvailabilityReason is null ? null : "No retained history",
            option => option.CategoryChanged ? "Category changed" : null,
            (_, label) => $"Select {label}", (_, label) => $"Clear {label}");

        var model = Project(selectedKeys);
        var items = model.Groups.SelectMany(group => group.Items).ToArray();
        var selectedItem = Assert.Single(items.Where(item => item.IsSelected));
        Assert.Equal("A", selectedItem.RouteKey);
        Assert.False(selectedItem.IsMuted);
        Assert.Equal("Category changed", selectedItem.BadgeText);
        Assert.Equal("No retained history", selectedItem.AvailabilityText);
        Assert.Equal("Historical route A. Category changed. No retained history", selectedItem.AccessibleLabel);
        Assert.All(items.Where(item => item.RouteKey != "A"), item => Assert.True(item.IsMuted));
        Assert.All(items, item => Assert.False(string.IsNullOrWhiteSpace(item.Color)));
        Assert.Equal(items[0].Color, items[1].Color);
        Assert.NotEqual(items[0].Color, items[2].Color);
        Assert.All(Project([]).Groups.SelectMany(group => group.Items), item => Assert.False(item.IsMuted));
        Assert.Equal(new[] { "A" }, selectedKeys);
        Assert.Equal("no_retained_history", options[0].AvailabilityReason);
    }

    static RouteFilterViewModel CreateViewModel(params (string Key, string Category, int Type)[] routes) =>
        new(NullLogger<RouteFilterViewModel>.Instance, new NoopToastService(),
            new FakeApplicationViewModel(routes.Select(route => FakeApplicationViewModel.Shape(route.Key, route.Category, route.Type))));

    [Fact]
    public void MapProjectionKeepsLiveCategoryAndRouteOrderAndOrdinalSelection()
    {
        using var viewModel = CreateViewModel(
            ("bus-z", "bus", 3),
            ("rail", "rail", 1),
            ("bus-a", "bus", 3));
        var selected = viewModel.RouteItems.Single(item => item.RouteJoinKey == "bus-z");
        var hovered = viewModel.RouteItems.Single(item => item.RouteJoinKey == "bus-a");
        viewModel.SelectRoute(selected);
        viewModel.SetHoveredRoute(hovered);

        var liveOrder = viewModel.RouteItems.Select(item => item.RouteJoinKey).ToArray();
        var beforeProjection = viewModel.RouteItems.Select(item => (item.RouteJoinKey, item.IsSelected, item.Color)).ToArray();
        var model = RouteFilterListProjection.FromMap(
            viewModel.RouteItems,
            viewModel.CategoryOrder,
            viewModel.SelectedRouteJoinKeys,
            viewModel.HoveredRouteJoinKeys,
            viewModel.HoveredCategory,
            key => $"category:{key}",
            key => $"select:{key}",
            key => $"clear:{key}");

        Assert.Equal(new[] { "rail", "bus" }, model.Groups.Select(group => group.Key));
        Assert.Equal("category:bus", model.Groups[1].Label);
        Assert.Equal("select:category:bus", model.Groups[1].SelectActionLabel);
        Assert.Equal("clear:category:bus", model.Groups[1].ClearActionLabel);
        Assert.Equal(liveOrder.Where(key => key.StartsWith("bus-", StringComparison.Ordinal)),
            model.Groups[1].Items.Select(item => item.RouteKey));
        Assert.Equal(1, model.SelectedCount);
        Assert.Equal("bus-z", model.Groups[1].Items.Single(item => item.IsSelected).RouteKey);
        Assert.False(model.Groups[1].Items.Single(item => item.RouteKey == "bus-a").IsMuted);
        Assert.True(model.Groups[0].Items.Single().IsMuted);
        Assert.Equal(beforeProjection,
            viewModel.RouteItems.Select(item => (item.RouteJoinKey, item.IsSelected, item.Color)).ToArray());
    }

    [Fact]
    public void MapBulkAndCategoryActionsKeepCaseDistinctKeysAndClearOnlyTheirCategory()
    {
        using var viewModel = CreateViewModel(("A", "bus", 3), ("a", "bus", 3), ("rail", "rail", 1));
        viewModel.SelectRoute(viewModel.RouteItems.Single(item => item.RouteJoinKey == "A"));
        viewModel.SelectRoute(viewModel.RouteItems.Single(item => item.RouteJoinKey == "a"));

        Assert.Equal(new[] { "A", "a" }, viewModel.SelectedRouteJoinKeys.OrderBy(key => key, StringComparer.Ordinal));
        var selectedModel = RouteFilterListProjection.FromMap(viewModel.RouteItems, viewModel.CategoryOrder,
            viewModel.SelectedRouteJoinKeys, viewModel.HoveredRouteJoinKeys, viewModel.HoveredCategory,
            key => key, key => key, key => key);
        Assert.Equal(2, selectedModel.SelectedCount);
        Assert.Equal(2, selectedModel.Groups.Single(group => group.Key == "bus").Items.Count(item => item.IsSelected));

        viewModel.SelectAll("rail");
        viewModel.ClearSelection("bus");
        Assert.Equal(new[] { "rail" }, viewModel.SelectedRouteJoinKeys);
    }

    [Fact]
    public async Task ProjectedSelectionAndHoverContinueToScopeMapVehicleCounts()
    {
        var app = new FakeApplicationViewModel(new[]
        {
            FakeApplicationViewModel.Shape("bus", "bus", 3),
            FakeApplicationViewModel.Shape("rail", "rail", 1)
        });
        using var viewModel = new RouteFilterViewModel(NullLogger<RouteFilterViewModel>.Instance, new NoopToastService(), app);
        await app.RaiseAsync(BatchWith("bus-vehicle", "bus"));
        await app.RaiseAsync(BatchWith("rail-vehicle", "rail"));

        viewModel.SelectRoute(viewModel.RouteItems.Single(item => item.RouteJoinKey == "bus"));
        Assert.Equal(1, viewModel.ActiveCountsByCategory.GetValueOrDefault("bus"));
        Assert.Equal(0, viewModel.ActiveCountsByCategory.GetValueOrDefault("rail"));

        viewModel.SetHoveredRoute(viewModel.RouteItems.Single(item => item.RouteJoinKey == "rail"));
        Assert.Equal(1, viewModel.ActiveCountsByCategory.GetValueOrDefault("bus"));
        Assert.Equal(1, viewModel.ActiveCountsByCategory.GetValueOrDefault("rail"));
    }

    static List<EventEnvelope> BatchWith(string vehicleId, string routeKey) =>
    [
        new(nameof(RouteNearestPointBatchEvent), DateTimeOffset.UnixEpoch,
            new RouteNearestPointBatchEvent(new[]
            {
                new RouteNearestPointBatchEvent.RouteNearestPointRecord(
                    vehicleId, routeKey, 0, 0, 0, 0, 1000, null, null, false, null)
            }))
    ];

    sealed class NoopToastService : IToastService
    {
        public void ShowSuccess(string message, string? title = null) { }
        public void ShowWarning(string message, string? title = null) { }
        public void ShowError(string message, string? title = null) { }
    }
}
