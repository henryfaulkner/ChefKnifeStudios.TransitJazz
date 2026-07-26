using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests;

// ── Feature 044 · D6 / FR-005 / SC-006: route-join-failure → "unknown" ────────
//
// TDD-RED SCAFFOLD. When a live vehicle's routeJoinKey is absent from the per-city
// category map, the Worker must assign category "unknown" — NOT silently "bus"
// (which would inflate the bus count and hide a data-quality signal).
//
// Today (pre-044) that fallback is `... ? m : TransitMode.Bus`, inlined at TWO
// identical sites (Worker.cs ~L415 and ~L448) inside the private async method
// ProcessSpatialReconciliationAsync — there is NO testable seam, and the value is
// the wrong one (Bus, not unknown).
//
// These tests target the seam the implementation SHOULD introduce (T010): extract
// the duplicated `map.TryGetValue(key, out var c) ? c : "unknown"` expression into
//
//     internal static string Worker.ResolveCategory(
//         IReadOnlyDictionary<string,string>? categoryMap, string routeJoinKey)
//
// and call it from both sites. That removes the L415/L448 duplication (DRY) AND
// makes the load-bearing "unknown, not bus" rule unit-testable at the lowest layer
// (util-testing/classification.md). Until that seam exists this file does not
// compile — the intended red state.
public class CategoryFallbackTests
{
    static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>
    {
        ["B41"] = "bus",
        ["501"] = "streetcar",
        ["1"] = "rail",
    };

    [Fact]
    public void ResolveCategory_KnownRoute_ReturnsMappedCategory()
    {
        Assert.Equal("streetcar", Worker.ResolveCategory(Map, "501"));
        Assert.Equal("rail", Worker.ResolveCategory(Map, "1"));
        Assert.Equal("bus", Worker.ResolveCategory(Map, "B41"));
    }

    [Fact]
    public void ResolveCategory_UnknownRoute_ReturnsUnknown_NotBus()
    {
        // The core D6 guarantee: an unmatched route is "unknown", never "bus".
        var category = Worker.ResolveCategory(Map, "GHOST-ROUTE-999");

        Assert.Equal("unknown", category);
        Assert.NotEqual("bus", category);
    }

    [Fact]
    public void ResolveCategory_NullMap_ReturnsUnknown()
    {
        // Before the shape catalog has loaded (map null), every vehicle is unmatched
        // → "unknown", not "bus".
        Assert.Equal("unknown", Worker.ResolveCategory(null, "anything"));
    }
}
