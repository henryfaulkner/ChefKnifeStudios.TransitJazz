using System.ComponentModel;
using ChefKnifeStudios.MartaJazz.Client.Shared.Components;
using ChefKnifeStudios.MartaJazz.Client.Shared.Services;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using ChefKnifeStudios.MartaJazz.Shared.GtfsData;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Tests;

// ── Feature 044 · E1 / FR-018 / SC-008: ActiveCountsByCategory reactivity ─────
//
// TDD-RED SCAFFOLD (closes analyze-report gap E1; satisfies tasks.md T017a). This
// is the executable guard for the plan's SELF-DECLARED #1 silent bug (tasks.md:229,
// data-model.md:127): ActiveCountsByCategory must be an [ObservableProperty]-backed
// dict that is REASSIGNED (fresh reference) on each recompute — a dict mutated in
// place never raises PropertyChanged, so TransitRunningLabel would show stale counts.
//
// The test asserts the OBSERVABLE contract (a PropertyChanged event fires + the new
// value is correct), NOT the implementation — so it passes whether the fix is a
// reassign or any other mechanism that correctly notifies (util-testing/
// review-criteria.md: "assert behaviour, not implementation").
//
// RED until T003 (record.Category) + T017 (ActiveCountsByCategory) land.
public class ActiveCountsReactivityTests
{
    static RouteFilterViewModel BuildViewModel(params RouteShapeFeature[] shapes)
    {
        var app = new FakeApplicationViewModel(shapes);
        var vm = new RouteFilterViewModel(
            NullLogger<RouteFilterViewModel>.Instance,
            new NoopToastService(),
            app);
        return vm;
    }

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
                        33.75, -84.39, 33.751, -84.389,
                        10000, null, null, IsStale: false, Category: category)
                }))
        };

    [Fact]
    public async Task ActiveCountsByCategory_RaisesPropertyChanged_WhenCountChanges()
    {
        var app = new FakeApplicationViewModel(new[]
        {
            FakeApplicationViewModel.Shape("501", "streetcar", 0),
        });
        var vm = new RouteFilterViewModel(
            NullLogger<RouteFilterViewModel>.Instance, new NoopToastService(), app);

        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        await app.RaiseAsync(BatchWith("veh-1", "501", "streetcar"));

        Assert.Contains(nameof(IRouteFilterViewModel.ActiveCountsByCategory), fired);
    }

    [Fact]
    public async Task ActiveCountsByCategory_ReflectsTheNewCount_AfterBatch()
    {
        var app = new FakeApplicationViewModel(new[]
        {
            FakeApplicationViewModel.Shape("501", "streetcar", 0),
        });
        var vm = new RouteFilterViewModel(
            NullLogger<RouteFilterViewModel>.Instance, new NoopToastService(), app);

        await app.RaiseAsync(BatchWith("veh-1", "501", "streetcar"));

        Assert.Equal(1, vm.ActiveCountsByCategory.GetValueOrDefault("streetcar"));
    }

    sealed class NoopToastService : IToastService
    {
        public void ShowSuccess(string message, string? title = null) { }
        public void ShowWarning(string message, string? title = null) { }
        public void ShowError(string message, string? title = null) { }
    }
}
