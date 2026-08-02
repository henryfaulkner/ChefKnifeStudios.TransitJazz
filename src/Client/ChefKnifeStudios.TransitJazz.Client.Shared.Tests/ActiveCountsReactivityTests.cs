using System.ComponentModel;
using ChefKnifeStudios.TransitJazz.Client.Shared.Components;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using ChefKnifeStudios.TransitJazz.Shared.Events;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

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

    static List<EventEnvelope> BatchWith(string vehicleId, string routeJoinKey, string? category) =>
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

    // ── Feature 051 · US4 / FR-013: v2 omits the category for catalog-resolvable routes ──
    //
    // Regression guard. Every test above passes an EXPLICIT category, which is precisely why
    // they stayed green while the counts silently stopped populating: under v2 the wire value
    // is null in the normal case, and storing it raw left every vehicle uncounted.

    [Fact]
    public async Task ActiveCountsByCategory_CountsVehicle_WhenWireCategoryIsOmitted()
    {
        var app = new FakeApplicationViewModel(new[]
        {
            FakeApplicationViewModel.Shape("501", "streetcar", 0),
        });
        var vm = new RouteFilterViewModel(
            NullLogger<RouteFilterViewModel>.Instance, new NoopToastService(), app);

        // v2 steady-state record: category omitted, to be resolved from the route catalog.
        await app.RaiseAsync(BatchWith("veh-1", "501", null));

        Assert.Equal(1, vm.ActiveCountsByCategory.GetValueOrDefault("streetcar"));
    }

    [Fact]
    public async Task ActiveCountsByCategory_KeepsUnknown_WhenRouteIsNotInTheCatalog()
    {
        // Catalog has 501; the vehicle rides an unlisted route. Resolution must NOT guess a
        // real category — "unknown" is the data-quality signal FR-013 preserves.
        var app = new FakeApplicationViewModel(new[]
        {
            FakeApplicationViewModel.Shape("501", "streetcar", 0),
        });
        var vm = new RouteFilterViewModel(
            NullLogger<RouteFilterViewModel>.Instance, new NoopToastService(), app);

        await app.RaiseAsync(BatchWith("veh-1", "GHOST-999", null));

        Assert.Equal(1, vm.ActiveCountsByCategory.GetValueOrDefault("unknown"));
        Assert.Equal(0, vm.ActiveCountsByCategory.GetValueOrDefault("streetcar"));
    }

    [Fact]
    public async Task ActiveCountsByCategory_ServerSentUnknown_IsCountedNotDropped()
    {
        var app = new FakeApplicationViewModel(new[]
        {
            FakeApplicationViewModel.Shape("501", "streetcar", 0),
        });
        var vm = new RouteFilterViewModel(
            NullLogger<RouteFilterViewModel>.Instance, new NoopToastService(), app);

        await app.RaiseAsync(BatchWith("veh-1", "GHOST-999", "unknown"));

        Assert.Equal(1, vm.ActiveCountsByCategory.GetValueOrDefault("unknown"));
    }

    sealed class NoopToastService : IToastService
    {
        public void ShowSuccess(string message, string? title = null) { }
        public void ShowWarning(string message, string? title = null) { }
        public void ShowError(string message, string? title = null) { }
    }
}
