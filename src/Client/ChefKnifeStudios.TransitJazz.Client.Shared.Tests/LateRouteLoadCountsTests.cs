using ChefKnifeStudios.TransitJazz.Client.Shared.Components;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

// ── Feature 045 addendum: counts-before-shapes startup race ───────────────────
//
// At startup the SignalR JoinCity replay (which seeds every vehicle count) races the
// route-shape fetch. When the replay lands FIRST, the counts recompute fires while
// CategoryOrder is still empty, so TransitRunningLabel paints nothing — and since the
// label's ONLY re-render trigger is an ActiveCountsByCategory PropertyChanged, it
// stayed blank until a much later batch happened to add a NEW vehicle (the replay had
// already seeded them all). The fix: BuildRouteItems recomputes the counts once the
// shapes arrive, so whichever half (shapes / counts) lands last completes the render.
//
// Asserts the OBSERVABLE contract (PropertyChanged fires after RoutesLoaded and the
// counts/categories are then consistent), not the implementation (util-testing/
// review-criteria.md: "assert behaviour, not implementation").
public class LateRouteLoadCountsTests
{
    static List<EventEnvelope> BatchWith(string vehicleId, string routeJoinKey, string category) =>
        new()
        {
            new EventEnvelope(
                nameof(RouteNearestPointBatchEvent),
                DateTimeOffset.UnixEpoch,
                new RouteNearestPointBatchEvent(new[]
                {
                    new RouteNearestPointBatchEvent.RouteNearestPointRecord(
                        vehicleId, routeJoinKey,
                        3_375_000, -8_439_000, 3_375_100, -8_438_900,
                        10000, null, null, IsStale: false, Category: category)
                }))
        };

    [Fact]
    public async Task ActiveCounts_RaisePropertyChanged_WhenRoutesLoadAfterCountsWereSeeded()
    {
        var app = new FakeApplicationViewModel(
            new[] { FakeApplicationViewModel.Shape("501", "streetcar", 0) },
            routesLoaded: false);
        var vm = new RouteFilterViewModel(
            NullLogger<RouteFilterViewModel>.Instance, new NoopToastService(), app);

        // Counts seeded by the replay while shapes are still loading — nothing renderable yet.
        await app.RaiseAsync(BatchWith("veh-1", "501", "streetcar"));
        Assert.Empty(vm.CategoryOrder);

        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        // Shapes arrive second. The label's re-render trigger must fire now, not on some
        // future batch that happens to contain a new vehicle.
        app.RaiseRoutesLoaded();

        Assert.Contains(nameof(IRouteFilterViewModel.ActiveCountsByCategory), fired);
        Assert.Contains("streetcar", vm.CategoryOrder);
        Assert.Equal(1, vm.ActiveCountsByCategory.GetValueOrDefault("streetcar"));
    }

    sealed class NoopToastService : IToastService
    {
        public void ShowSuccess(string message, string? title = null) { }
        public void ShowWarning(string message, string? title = null) { }
        public void ShowError(string message, string? title = null) { }
    }
}
