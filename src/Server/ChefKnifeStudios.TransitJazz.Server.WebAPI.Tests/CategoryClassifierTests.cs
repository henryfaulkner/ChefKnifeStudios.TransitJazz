using ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests;

// ── Feature 044: Dynamic Per-City Vehicle Categories ──────────────────────────
//
// TDD-RED SCAFFOLD. These tests target the category classifier contract defined in
// specs/044-dynamic-vehicle-categories/data-model.md + contracts/wire-contract.md
// and DO NOT COMPILE against the pre-044 code — `GtfsStaticLoader.ClassifyCategory`
// does not exist yet (the classification is currently an inline 3-line switch in
// ParseRouteMetadata that returns a `TransitMode` enum).
//
// They become green once tasks T007 (extract `ClassifyCategory`) lands. Each test
// names the tasks.md task and requirement it locks in. Until then this file is the
// executable spec for the classifier.
//
// Contract signature (data-model.md):
//   static string ClassifyCategory(string routeType,
//       IReadOnlyDictionary<string,string>? cityMap, string cityName, ILogger logger)
//
// Layer: unit (pure function, no I/O) — the lowest layer that proves the behaviour
// (util-testing/classification.md: "push coverage to the lowest layer").
public class CategoryClassifierTests
{
    // ── D5a: no config → today's exact rail/bus rule (FR-003, SC-002) ─────────
    // Locks the byte-for-byte "existing cities unchanged" guarantee. T024 depends
    // on this staying true. route_type 0/1/2 → rail; everything else → bus.

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    public void ClassifyCategory_NoConfig_RailFamilyRouteType_ReturnsRail(string routeType)
    {
        var category = GtfsStaticLoader.ClassifyCategory(routeType, cityMap: null, "marta", NullLogger.Instance);
        Assert.Equal("rail", category);
    }

    [Theory]
    [InlineData("3")]   // bus
    [InlineData("4")]   // ferry route_type, but no config → default bucket
    [InlineData("")]    // missing route_type column value
    [InlineData("99")]  // unknown
    public void ClassifyCategory_NoConfig_NonRailRouteType_ReturnsBus(string routeType)
    {
        var category = GtfsStaticLoader.ClassifyCategory(routeType, cityMap: null, "marta", NullLogger.Instance);
        Assert.Equal("bus", category);
    }

    // ── D5 config hit: TTC mapping (FR-001, FR-002, SC-001) ───────────────────
    // The concrete streetcar problem that triggered the feature (T013/T014).

    public static IReadOnlyDictionary<string, string> TtcMap => new Dictionary<string, string>
    {
        ["0"] = "streetcar",
        ["1"] = "rail",
        ["3"] = "bus",
    };

    [Theory]
    [InlineData("0", "streetcar")]
    [InlineData("1", "rail")]
    [InlineData("3", "bus")]
    public void ClassifyCategory_TtcConfig_MapsRouteTypeToConfiguredCategory(string routeType, string expected)
    {
        var category = GtfsStaticLoader.ClassifyCategory(routeType, TtcMap, "ttc", NullLogger.Instance);
        Assert.Equal(expected, category);
    }

    // ── D5b: configured city, unmapped route_type → bus + WARNING (FR-004) ────
    // Must NOT fail the city load; must default to bus AND log a warning (T015).
    // Uses a spy logger to assert the warning fired — util-testing/test-doubles.md
    // (Spy: "verify a dependency was invoked").

    [Fact]
    public void ClassifyCategory_ConfiguredCity_UnmappedRouteType_ReturnsBus()
    {
        var category = GtfsStaticLoader.ClassifyCategory("4", TtcMap, "ttc", NullLogger.Instance);
        Assert.Equal("bus", category);
    }

    [Fact]
    public void ClassifyCategory_ConfiguredCity_UnmappedRouteType_LogsWarning()
    {
        var spy = new WarningSpyLogger();

        GtfsStaticLoader.ClassifyCategory("4", TtcMap, "ttc", spy);

        Assert.True(spy.WarningCount >= 1,
            "an unmapped route_type in a configured city must emit a warning (D5b), not fail silently");
    }

    // ── D1/FR-015/SC-004: generality — an arbitrary category, no shared-code edit
    // Proves the classifier hardcodes no fixed {bus,rail,streetcar} set (T029).

    [Fact]
    public void ClassifyCategory_ArbitraryConfiguredCategory_IsReturnedVerbatim()
    {
        var ferryCity = new Dictionary<string, string> { ["4"] = "ferry" };

        var category = GtfsStaticLoader.ClassifyCategory("4", ferryCity, "sample", NullLogger.Instance);

        Assert.Equal("ferry", category);
    }

    // ── Empty map {} is "configured with nothing", NOT the default rule ───────
    // category-config.md: an empty map means every route_type is unmapped → bus.
    // A city wanting the rail/bus default must OMIT the key (cityMap: null), not
    // supply {}. This boundary is easy to get wrong; pin it.

    [Fact]
    public void ClassifyCategory_EmptyMap_TreatsRailRouteTypeAsUnmappedBus_NotRail()
    {
        var emptyMap = new Dictionary<string, string>();

        var category = GtfsStaticLoader.ClassifyCategory("1", emptyMap, "oddcity", NullLogger.Instance);

        // With config present-but-empty, route_type 1 is unmapped → "bus" (+warning),
        // distinctly NOT the "rail" the null-config default would give.
        Assert.Equal("bus", category);
    }

    // ── RouteType carry-through (D8, SC-005 ordering source; T008/T014) ───────
    // The raw GTFS route_type int must ride RouteShapeProperties so the client can
    // sort categories by min(route_type). ParseRouteMetadata's tuple is retyped
    // from (…, TransitMode Mode) to (…, string Category, int RouteType). These
    // tests read that new tuple shape and therefore only compile post-T008/T011.

    // A TTC-shaped routes.txt: a streetcar (route_type 0), a subway (1), a bus (3).
    const string TtcRoutesTxt = """
        route_id,route_short_name,route_long_name,route_type,route_color,route_text_color
        501,501,Queen,0,DA251D,FFFFFF
        1,1,Line 1 Yonge-University,1,F8C300,000000
        35,35,Jane,3,,
        """;

    static ZipArchive MakeZip(string fileName, string content)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry(fileName);
            using var w = new StreamWriter(entry.Open(), Encoding.UTF8);
            w.Write(content);
        }
        ms.Position = 0;
        return new ZipArchive(ms, ZipArchiveMode.Read);
    }

    [Fact]
    public void ParseRouteMetadata_CarriesRawRouteTypeInt()
    {
        using var archive = MakeZip("routes.txt", TtcRoutesTxt);
        var meta = GtfsStaticLoader.ParseRouteMetadata(archive);

        // New tuple shape (data-model.md): (RouteShortName, Color, TextColor, Category, RouteType)
        Assert.Equal(0, meta["501"].RouteType);   // streetcar route → route_type 0
        Assert.Equal(1, meta["1"].RouteType);      // subway → 1
        Assert.Equal(3, meta["35"].RouteType);     // bus → 3
    }

    [Fact]
    public void ParseRouteMetadata_MissingOrUnparsableRouteType_DefaultsTo3()
    {
        // route_type column absent entirely → default 3 (bus-ordered) per data-model.md.
        const string noRouteType = """
            route_id,route_short_name,route_color
            9,9,00FF00
            """;
        using var archive = MakeZip("routes.txt", noRouteType);
        var meta = GtfsStaticLoader.ParseRouteMetadata(archive);

        Assert.Equal(3, meta["9"].RouteType);
    }
}
