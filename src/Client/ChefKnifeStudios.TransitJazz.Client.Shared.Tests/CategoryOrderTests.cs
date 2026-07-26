using ChefKnifeStudios.TransitJazz.Client.Shared.Components;
using ChefKnifeStudios.TransitJazz.Client.Shared.Services;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Tests;

// ── Feature 044 · D8 / SC-005: category display order ─────────────────────────
//
// TDD-RED SCAFFOLD (satisfies tasks.md T025). CategoryOrder groups the route
// catalog by Category and sorts by min(RouteType) ascending, ordinal tie-break
// (contracts/client-ui-contract.md §1). It is derived once in BuildRouteItems and
// exposed on IRouteFilterViewModel.
//
// This file is RED until:
//   • T004 adds RouteShapeProperties.Category/RouteType (via FakeApplicationViewModel.Shape)
//   • T016 adds RouteItem.Category/RouteType + IRouteFilterViewModel.CategoryOrder
//
// Layer: component (the ViewModel with a stubbed IApplicationViewModel; no external
// deps) — util-testing/classification.md. Asserts observable output (the ordered
// list), not internal derivation steps.
public class CategoryOrderTests
{
    static RouteFilterViewModel BuildViewModel(params RouteShapeFeature[] shapes)
    {
        var app = new FakeApplicationViewModel(shapes);
        return new RouteFilterViewModel(
            NullLogger<RouteFilterViewModel>.Instance,
            new NoopToastService(),
            app);
    }

    // TTC: streetcar(0), rail(1), bus(3) → [streetcar, rail, bus] (SC-001 ordering).
    [Fact]
    public void CategoryOrder_Ttc_StreetcarRailBus_ByAscendingRouteType()
    {
        var vm = BuildViewModel(
            FakeApplicationViewModel.Shape("35", "bus", 3),
            FakeApplicationViewModel.Shape("501", "streetcar", 0),
            FakeApplicationViewModel.Shape("1", "rail", 1));

        Assert.Equal(new[] { "streetcar", "rail", "bus" }, vm.CategoryOrder);
    }

    // MARTA-shaped: rail(0/1/2), bus(3) → [rail, bus]. Today's Rail-first order is
    // preserved with NO regression (SC-002/SC-005) — the load-bearing guarantee for
    // existing cities.
    [Fact]
    public void CategoryOrder_Marta_RailThenBus_UnchangedFromToday()
    {
        var vm = BuildViewModel(
            FakeApplicationViewModel.Shape("110", "bus", 3),
            FakeApplicationViewModel.Shape("BLUE", "rail", 1),
            FakeApplicationViewModel.Shape("GREEN", "rail", 2),
            FakeApplicationViewModel.Shape("RED", "rail", 0));

        Assert.Equal(new[] { "rail", "bus" }, vm.CategoryOrder);
    }

    // A category's rank is the MINIMUM route_type across its routes — a rail category
    // containing route_types {0,1,2} still ranks at 0, ahead of bus at 3.
    [Fact]
    public void CategoryOrder_RanksByMinRouteTypePerCategory()
    {
        var vm = BuildViewModel(
            FakeApplicationViewModel.Shape("busA", "bus", 3),
            FakeApplicationViewModel.Shape("railHi", "rail", 2),
            FakeApplicationViewModel.Shape("railLo", "rail", 0));

        Assert.Equal(new[] { "rail", "bus" }, vm.CategoryOrder);
    }

    // Generality (FR-015/SC-004): an arbitrary category (ferry, route_type 4) slots
    // in by its route_type with no fixed-list code — proves the client hardcodes no
    // category set.
    [Fact]
    public void CategoryOrder_ArbitraryCategory_SlotsByRouteType()
    {
        var vm = BuildViewModel(
            FakeApplicationViewModel.Shape("busA", "bus", 3),
            FakeApplicationViewModel.Shape("ferryA", "ferry", 4),
            FakeApplicationViewModel.Shape("railA", "rail", 1));

        Assert.Equal(new[] { "rail", "bus", "ferry" }, vm.CategoryOrder);
    }

    sealed class NoopToastService : IToastService
    {
        public void ShowSuccess(string message, string? title = null) { }
        public void ShowWarning(string message, string? title = null) { }
        public void ShowError(string message, string? title = null) { }
    }
}
