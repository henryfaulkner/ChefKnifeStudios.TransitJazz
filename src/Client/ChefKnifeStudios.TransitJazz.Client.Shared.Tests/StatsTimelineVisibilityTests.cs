using ChefKnifeStudios.TransitJazz.Client.Shared.Components.Charts;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

public sealed class StatsTimelineVisibilityTests
{
    [Fact]
    public void ReconcilePreservesVisibilityAcrossRefreshReorderAndThemeInputs()
    {
        var state = new StatsTimelineVisibilityState();
        state.Reconcile(["city:toronto", "city:denver"]);
        state.SetVisibility("city:toronto", false);

        state.Reconcile(["city:denver", "city:toronto"]);

        Assert.False(state.IsVisible("city:toronto"));
        Assert.True(state.IsVisible("city:denver"));
        Assert.Equal(new[] { "city:denver", "city:toronto" }, state.Values.Keys.OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void ReconcileAddsNewCitiesVisibleAndDropsRemovedCities()
    {
        var state = new StatsTimelineVisibilityState();
        state.Reconcile(["city:toronto", "city:denver"]);
        state.SetVisibility("city:toronto", false);

        state.Reconcile(["city:denver", "city:boston"]);

        Assert.True(state.IsVisible("city:boston"));
        Assert.True(state.IsVisible("city:denver"));
        Assert.False(state.Values.ContainsKey("city:toronto"));
    }

    [Fact]
    public void HideAllAndShowAllChangeOnlyPresentationVisibilityAndUnavailableExitResetsIt()
    {
        var state = new StatsTimelineVisibilityState();
        string[] keys = ["city:toronto", "city:denver"];
        state.Reconcile(keys);
        state.SetAll(keys, false);
        Assert.All(keys, key => Assert.False(state.IsVisible(key)));

        state.SetAll(keys, true);
        Assert.All(keys, key => Assert.True(state.IsVisible(key)));
        state.SetVisibility("city:toronto", false);

        state.Reconcile(keys, enabled: false); // stacked city categories, unavailable data, or mode exit

        Assert.Empty(state.Values);
        Assert.True(state.IsVisible("city:toronto"));
    }

    [Fact]
    public void ResetOnCloseRestoresAllSeriesToVisibleOnNextSession()
    {
        var state = new StatsTimelineVisibilityState();
        state.Reconcile(["city:toronto"]);
        state.SetVisibility("city:toronto", false);

        state.Reset();
        state.Reconcile(["city:toronto"]);

        Assert.True(state.IsVisible("city:toronto"));
    }
}
