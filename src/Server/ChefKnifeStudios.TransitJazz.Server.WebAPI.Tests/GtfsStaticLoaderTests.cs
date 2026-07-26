using ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests;

/// <summary>
/// Integration tests for GTFS static route metadata parsing.
/// Each city added to the app should have a representative fixture below
/// that covers its routes.txt column layout and any known tricky rows.
/// </summary>
public class GtfsStaticLoaderTests
{
    // ── SplitCsvLine ──────────────────────────────────────────────────────────

    [Fact]
    public void SplitCsvLine_SimpleRow_SplitsOnComma()
    {
        var cols = GtfsStaticLoader.SplitCsvLine("47,1,47,Local Bus,3");
        Assert.Equal(["47", "1", "47", "Local Bus", "3"], cols);
    }

    [Fact]
    public void SplitCsvLine_QuotedFieldWithComma_TreatedAsSingleField()
    {
        // This is the root cause of the MBTA route-47 color bug:
        // "Central Square, Cambridge - Broadway Station" must not split.
        var cols = GtfsStaticLoader.SplitCsvLine(
            "47,1,47,\"Central Square, Cambridge - Broadway Station\",Local Bus,3,https://www.mbta.com/schedules/47,FFC72C,000000");
        Assert.Equal(9, cols.Length);
        Assert.Equal("47", cols[0]);
        Assert.Equal("Central Square, Cambridge - Broadway Station", cols[3]);
        Assert.Equal("https://www.mbta.com/schedules/47", cols[6]);
        Assert.Equal("FFC72C", cols[7]);
        Assert.Equal("000000", cols[8]);
    }

    [Fact]
    public void SplitCsvLine_DoubledQuoteInsideField_UnescapedCorrectly()
    {
        var cols = GtfsStaticLoader.SplitCsvLine("\"He said \"\"hello\"\"\",next");
        Assert.Equal(2, cols.Length);
        Assert.Equal("He said \"hello\"", cols[0]);
    }

    [Fact]
    public void SplitCsvLine_EmptyFields_PreservedAsEmpty()
    {
        var cols = GtfsStaticLoader.SplitCsvLine("a,,c");
        Assert.Equal(3, cols.Length);
        Assert.Equal("", cols[1]);
    }

    [Fact]
    public void SplitCsvLine_CrLfStripped()
    {
        var cols = GtfsStaticLoader.SplitCsvLine("a,b\r");
        Assert.Equal(2, cols.Length);
        Assert.Equal("b", cols[1]);
    }

    // ── NormalizeColor ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("FFC72C", "#FFC72C")]   // MBTA orange, no prefix
    [InlineData("#FFC72C", "#FFC72C")]  // already prefixed
    [InlineData("ffc72c", "#FFC72C")]   // lowercase → uppercased
    [InlineData("ABC", "#ABC")]         // 3-char shorthand valid
    public void NormalizeColor_ValidHex_ReturnsPrefixedUppercase(string input, string expected)
    {
        Assert.Equal(expected, GtfsStaticLoader.NormalizeColor(input));
    }

    [Theory]
    [InlineData("https://www.mbta.com/schedules/47")]  // URL (the actual bug)
    [InlineData("ZZZZZZ")]                              // non-hex chars
    [InlineData("FFCC")]                                // 4 chars — not 3 or 6
    [InlineData("")]                                    // empty
    [InlineData("   ")]                                 // whitespace only
    public void NormalizeColor_Invalid_ReturnsNull(string input)
    {
        Assert.Null(GtfsStaticLoader.NormalizeColor(input));
    }

    // ── ParseRouteMetadata — MBTA fixture ────────────────────────────────────

    // Reproduces the exact MBTA routes.txt column layout and the tricky route-47 row.
    // Column order: route_id,agency_id,route_short_name,route_long_name,route_desc,
    //               route_type,route_url,route_color,route_text_color,route_sort_order,
    //               route_fare_class,line_id,listed_route,network_id
    const string MbtaRoutesTxt = """
        route_id,agency_id,route_short_name,route_long_name,route_desc,route_type,route_url,route_color,route_text_color,route_sort_order,route_fare_class,line_id,listed_route,network_id
        Shuttle-ForestHillsJackson,1,Orange Line Shuttle,Forest Hills - Jackson Square,Shuttle,3,https://www.mbta.com/schedules/Shuttle-ForestHillsJackson,FFC72C,000000,99900,Free,line-Orange,,local_bus
        CR-Kingston,1,,Kingston/Plymouth Line,,2,https://www.mbta.com/schedules/CR-Kingston,80276C,FFFFFF,10070,Commuter Rail,,,
        47,1,47,"Central Square, Cambridge - Broadway Station",Local Bus,3,https://www.mbta.com/schedules/47,FFC72C,000000,50470,Local Bus,line-47,,local_bus
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
    public void ParseRouteMetadata_Mbta_Route47_ColorNotUrl()
    {
        using var archive = MakeZip("routes.txt", MbtaRoutesTxt);
        var meta = GtfsStaticLoader.ParseRouteMetadata(archive);

        Assert.True(meta.ContainsKey("47"), "route 47 should be present");
        var (_, color, textColor, _, _) = meta["47"];

        Assert.Equal("#FFC72C", color);
        Assert.Equal("#000000", textColor);
    }

    [Fact]
    public void ParseRouteMetadata_Mbta_AllColorsAreValidHexOrNull()
    {
        using var archive = MakeZip("routes.txt", MbtaRoutesTxt);
        var meta = GtfsStaticLoader.ParseRouteMetadata(archive);

        foreach (var (routeId, (_, color, textColor, _, _)) in meta)
        {
            if (color is not null)
                Assert.Matches(@"^#[0-9A-F]{3}([0-9A-F]{3})?$", color);
            if (textColor is not null)
                Assert.Matches(@"^#[0-9A-F]{3}([0-9A-F]{3})?$", textColor);
        }
    }

    [Fact]
    public void ParseRouteMetadata_Mbta_ShuttleAndCommuter_ParseCorrectly()
    {
        using var archive = MakeZip("routes.txt", MbtaRoutesTxt);
        var meta = GtfsStaticLoader.ParseRouteMetadata(archive);

        var (shuttleName, shuttleColor, _, shuttleCategory, _) = meta["Shuttle-ForestHillsJackson"];
        Assert.Equal("Orange Line Shuttle", shuttleName);
        Assert.Equal("#FFC72C", shuttleColor);
        Assert.Equal("bus", shuttleCategory);

        var (_, kingstonColor, kingstonText, kingstonCategory, _) = meta["CR-Kingston"];
        Assert.Equal("#80276C", kingstonColor);
        Assert.Equal("#FFFFFF", kingstonText);
        Assert.Equal("rail", kingstonCategory);
    }

    // ── ParseRouteMetadata — MARTA fixture ───────────────────────────────────

    // MARTA routes.txt has no route_url column — simpler layout.
    // Verifies the parser handles a different column order without breaking.
    const string MartaRoutesTxt = """
        route_id,route_short_name,route_long_name,route_desc,route_type,route_color,route_text_color
        110,110,Marietta Blvd,Local,3,0E6B4A,FFFFFF
        104,104,Cascade,Local,3,,
        """;

    [Fact]
    public void ParseRouteMetadata_Marta_Route110_ParsesColor()
    {
        using var archive = MakeZip("routes.txt", MartaRoutesTxt);
        var meta = GtfsStaticLoader.ParseRouteMetadata(archive);

        var (name, color, textColor, category, _) = meta["110"];
        Assert.Equal("110", name);
        Assert.Equal("#0E6B4A", color);
        Assert.Equal("#FFFFFF", textColor);
        Assert.Equal("bus", category);
    }

    [Fact]
    public void ParseRouteMetadata_Marta_EmptyColorFields_ReturnNull()
    {
        using var archive = MakeZip("routes.txt", MartaRoutesTxt);
        var meta = GtfsStaticLoader.ParseRouteMetadata(archive);

        var (_, color, textColor, _, _) = meta["104"];
        Assert.Null(color);
        Assert.Null(textColor);
    }

    [Fact]
    public void ParseRouteMetadata_Marta_AllColorsAreValidHexOrNull()
    {
        using var archive = MakeZip("routes.txt", MartaRoutesTxt);
        var meta = GtfsStaticLoader.ParseRouteMetadata(archive);

        foreach (var (_, (_, color, textColor, _, _)) in meta)
        {
            if (color is not null)
                Assert.Matches(@"^#[0-9A-F]{3}([0-9A-F]{3})?$", color);
            if (textColor is not null)
                Assert.Matches(@"^#[0-9A-F]{3}([0-9A-F]{3})?$", textColor);
        }
    }

    // ── BuildZipRouteFeatures — cross-zip route_id collision (nymta: NYCT + Bus Co) ────

    // Reproduces the BX41 bug: two zips from different publishers can independently mint
    // the same numeric route_id for two entirely different routes. A flat cross-zip merge
    // keyed on bare route_id would let the first zip's route silently shadow the second's.
    // BuildZipRouteFeatures must key on route_short_name (falling back to route_id only
    // when no short name exists), so both zips' merge results (via caller-side TryAdd on
    // the per-zip dictionaries) keep their own distinctly-named route.
    [Fact]
    public void BuildZipRouteFeatures_KeysByShortName_NotRawRouteId()
    {
        // Zip A (e.g. an NYCT borough zip): route_id "41" happens to be some local route.
        var routeToShapeA = new Dictionary<string, string> { ["41"] = "shapeA" };
        var shapesA = new Dictionary<string, List<(double, double, int)>>
        {
            ["shapeA"] = [(40.80, -73.90, 0), (40.81, -73.91, 1)]
        };
        var metaA = new Dictionary<string, (string?, string?, string?, string, int)>
        {
            ["41"] = ("Bx41", null, null, "bus", 3)
        };

        // Zip B (MTA Bus Company): a DIFFERENT route also happens to use route_id "41".
        var routeToShapeB = new Dictionary<string, string> { ["41"] = "shapeB" };
        var shapesB = new Dictionary<string, List<(double, double, int)>>
        {
            ["shapeB"] = [(40.60, -73.95, 0), (40.61, -73.96, 1)]
        };
        var metaB = new Dictionary<string, (string?, string?, string?, string, int)>
        {
            ["41"] = ("BX41", null, null, "bus", 3)
        };

        var featuresA = GtfsStaticLoader.BuildZipRouteFeatures("nymta", routeToShapeA, shapesA, metaA);
        var featuresB = GtfsStaticLoader.BuildZipRouteFeatures("nymta", routeToShapeB, shapesB, metaB);

        // Simulate BuildCityShapeSetAsync's caller-side merge across zips.
        var fresh = new Dictionary<string, string>();
        foreach (var (k, v) in featuresA) fresh.TryAdd(k, v);
        foreach (var (k, v) in featuresB) fresh.TryAdd(k, v);

        Assert.True(fresh.ContainsKey("nymta:Bx41"), "zip A's route should be present under its own short name");
        Assert.True(fresh.ContainsKey("nymta:BX41"), "zip B's route (BX41) must not be shadowed by zip A's route_id=41");
    }

    [Fact]
    public void BuildZipRouteFeatures_NoShortName_FallsBackToRouteId()
    {
        var routeToShape = new Dictionary<string, string> { ["shuttle-1"] = "shapeX" };
        var shapes = new Dictionary<string, List<(double, double, int)>>
        {
            ["shapeX"] = [(40.0, -74.0, 0), (40.01, -74.01, 1)]
        };
        var meta = new Dictionary<string, (string?, string?, string?, string, int)>();

        var features = GtfsStaticLoader.BuildZipRouteFeatures("nymta", routeToShape, shapes, meta);

        Assert.True(features.ContainsKey("nymta:shuttle-1"));
    }

    [Fact]
    public void BuildZipRouteFeatures_DuplicateShortNameWithinZip_FirstWins()
    {
        // Same short name reachable via two different route_ids in the same zip
        // (e.g. direction-split route_ids sharing one display name) — dedup within
        // one zip is still expected, first occurrence wins.
        var routeToShape = new Dictionary<string, string>
        {
            ["41-north"] = "shapeA",
            ["41-south"] = "shapeB"
        };
        var shapes = new Dictionary<string, List<(double, double, int)>>
        {
            ["shapeA"] = [(40.0, -74.0, 0), (40.01, -74.01, 1)],
            ["shapeB"] = [(41.0, -75.0, 0), (41.01, -75.01, 1)]
        };
        var meta = new Dictionary<string, (string?, string?, string?, string, int)>
        {
            ["41-north"] = ("41", null, null, "bus", 3),
            ["41-south"] = ("41", null, null, "bus", 3)
        };

        var features = GtfsStaticLoader.BuildZipRouteFeatures("marta", routeToShape, shapes, meta);

        Assert.Single(features);
        Assert.True(features.ContainsKey("marta:41"));
    }

    // ── ResolveNestedGtfsArchive — SEPTA zip-of-zips ─────────────────────────

    const string MinimalTripsTxt = "route_id,service_id,trip_id,shape_id\n47,weekday,47-trip-1,47-shape\n";

    static ZipArchive MakeZipWithEntries(params (string Name, byte[] Bytes)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var s = entry.Open();
                s.Write(bytes, 0, bytes.Length);
            }
        }
        ms.Position = 0;
        return new ZipArchive(ms, ZipArchiveMode.Read);
    }

    static byte[] ZipBytesOf(string fileName, string content)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry(fileName);
            using var w = new StreamWriter(entry.Open(), Encoding.UTF8);
            w.Write(content);
        }
        return ms.ToArray();
    }

    [Fact]
    public void ResolveNestedGtfsArchive_FlatZipWithTripsAtRoot_ReturnsNull()
    {
        // Regression guard: every existing city (MARTA, WMATA, MBTA, NYMTA, TTC) ships a flat
        // zip. The resolver must be a no-op for them.
        using var archive = MakeZip("trips.txt", MinimalTripsTxt);

        var nested = GtfsStaticLoader.ResolveNestedGtfsArchive(archive);

        Assert.Null(nested);
    }

    [Fact]
    public void ResolveNestedGtfsArchive_SingleNestedZip_UnwrapsAndFindsTrips()
    {
        var busZipBytes = ZipBytesOf("trips.txt", MinimalTripsTxt);
        using var outer = MakeZipWithEntries(("google_bus.zip", busZipBytes));

        using var nested = GtfsStaticLoader.ResolveNestedGtfsArchive(outer);

        Assert.NotNull(nested);
        Assert.NotNull(nested!.GetEntry("trips.txt"));
    }

    [Fact]
    public void ResolveNestedGtfsArchive_BusAndRailNestedZips_SelectsNonRailEntry()
    {
        // SEPTA's actual structure: gtfs_public.zip contains both google_bus.zip and
        // google_rail.zip. The bus/trolley/streetcar/NHSL data (this feature's scope) lives in
        // the non-"rail"-named entry — Regional Rail (google_rail.zip) must never be selected.
        var busZipBytes = ZipBytesOf("trips.txt", MinimalTripsTxt);
        var railZipBytes = ZipBytesOf("trips.txt", "route_id,service_id,trip_id,shape_id\nR1,weekday,r1-trip-1,r1-shape\n");
        using var outer = MakeZipWithEntries(
            ("google_rail.zip", railZipBytes),
            ("google_bus.zip", busZipBytes));

        using var nested = GtfsStaticLoader.ResolveNestedGtfsArchive(outer);

        Assert.NotNull(nested);
        var routeToShape = GtfsStaticLoader.ParseRouteToShapeMap(nested!);
        Assert.True(routeToShape.ContainsKey("47"), "should have unwrapped google_bus.zip, not google_rail.zip");
        Assert.False(routeToShape.ContainsKey("R1"), "must not select the rail-named nested zip");
    }

    [Fact]
    public void ResolveNestedGtfsArchive_NoRootTripsAndNoNestedZip_ReturnsNull()
    {
        // Corrupted/unexpected upstream format: no trips.txt at root, no nested zip to fall
        // back to either. Caller treats this the same as any other unreadable zip (0 routes,
        // logged warning, last-known-good data kept) — must not throw.
        using var archive = MakeZip("routes.txt", "route_id,route_short_name\n47,47\n");

        var nested = GtfsStaticLoader.ResolveNestedGtfsArchive(archive);

        Assert.Null(nested);
    }
}
