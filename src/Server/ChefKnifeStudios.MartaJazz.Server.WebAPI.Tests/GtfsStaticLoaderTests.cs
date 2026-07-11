using ChefKnifeStudios.MartaJazz.Server.WebAPI.GtfsStatic;
using ChefKnifeStudios.MartaJazz.Shared.Events;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace ChefKnifeStudios.MartaJazz.Server.WebAPI.Tests;

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
        var (_, color, textColor, _) = meta["47"];

        Assert.Equal("#FFC72C", color);
        Assert.Equal("#000000", textColor);
    }

    [Fact]
    public void ParseRouteMetadata_Mbta_AllColorsAreValidHexOrNull()
    {
        using var archive = MakeZip("routes.txt", MbtaRoutesTxt);
        var meta = GtfsStaticLoader.ParseRouteMetadata(archive);

        foreach (var (routeId, (_, color, textColor, _)) in meta)
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

        var (shuttleName, shuttleColor, _, shuttleMode) = meta["Shuttle-ForestHillsJackson"];
        Assert.Equal("Orange Line Shuttle", shuttleName);
        Assert.Equal("#FFC72C", shuttleColor);
        Assert.Equal(TransitMode.Bus, shuttleMode);

        var (_, kingstonColor, kingstonText, kingstonMode) = meta["CR-Kingston"];
        Assert.Equal("#80276C", kingstonColor);
        Assert.Equal("#FFFFFF", kingstonText);
        Assert.Equal(TransitMode.Rail, kingstonMode);
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

        var (name, color, textColor, mode) = meta["110"];
        Assert.Equal("110", name);
        Assert.Equal("#0E6B4A", color);
        Assert.Equal("#FFFFFF", textColor);
        Assert.Equal(TransitMode.Bus, mode);
    }

    [Fact]
    public void ParseRouteMetadata_Marta_EmptyColorFields_ReturnNull()
    {
        using var archive = MakeZip("routes.txt", MartaRoutesTxt);
        var meta = GtfsStaticLoader.ParseRouteMetadata(archive);

        var (_, color, textColor, _) = meta["104"];
        Assert.Null(color);
        Assert.Null(textColor);
    }

    [Fact]
    public void ParseRouteMetadata_Marta_AllColorsAreValidHexOrNull()
    {
        using var archive = MakeZip("routes.txt", MartaRoutesTxt);
        var meta = GtfsStaticLoader.ParseRouteMetadata(archive);

        foreach (var (_, (_, color, textColor, _)) in meta)
        {
            if (color is not null)
                Assert.Matches(@"^#[0-9A-F]{3}([0-9A-F]{3})?$", color);
            if (textColor is not null)
                Assert.Matches(@"^#[0-9A-F]{3}([0-9A-F]{3})?$", textColor);
        }
    }
}
