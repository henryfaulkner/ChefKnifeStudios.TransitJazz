using ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;
using ChefKnifeStudios.TransitJazz.Server.WebAPI.Interfaces;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;
using ChefKnifeStudios.TransitJazz.Shared;
using ChefKnifeStudios.TransitJazz.Shared.GtfsData;
using Ardalis.Result;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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

    [Fact]
    public void SeattleDirectStaticUnionResolvesKnownAliasesAndKeepsGeometry()
    {
        var kcm = SeattleZip(
            ("100451", "556"),
            ("100479", "1 Line"));
        var soundTransit = SeattleZip(
            ("102767", "547"),
            ("2LINE", "2 Line"));

        var merged = GtfsStaticLoader.MergeSeattleShapeSets([kcm, soundTransit]);

        Assert.Equal(new[] { "seattle:1 Line", "seattle:2 Line", "seattle:547", "seattle:556" },
            merged.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("100451", JsonSerializer.Deserialize<RouteShapeFeature>(merged["seattle:556"], Shared.JsonOptions.Get())!.Properties.RouteId);
        Assert.Equal("102767", JsonSerializer.Deserialize<RouteShapeFeature>(merged["seattle:547"], Shared.JsonOptions.Get())!.Properties.RouteId);
    }

    [Fact]
    public async Task SeattleDirectStaticRequestsAreKeylessAndJoinTheirOwnZipGeometry()
    {
        const string kcmUrl = "https://www.soundtransit.org/GTFS-KCM/google_transit.zip";
        const string stUrl = "https://www.soundtransit.org/GTFS-rail/40_gtfs.zip";
        var handler = new CapturingStaticHandler(request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(request.RequestUri!.AbsoluteUri == kcmUrl
                ? SeattleStaticZip("100451", "556")
                : SeattleStaticZip("102767", "547")),
        }));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cities:0:Name"] = CityNames.Seattle,
            ["Cities:0:StaticZipUrls:0"] = kcmUrl,
            ["Cities:0:StaticZipUrls:1"] = stUrl,
            ["Cities:0:SeattleSoundTransitApiKeyEnvVar"] = "SOUND_TRANSIT_OBA_API_KEY",
        }).Build();
        var repository = new StaticRouteRepository();
        var shapes = new InMemoryRouteShapeSource();
        var loader = new GtfsStaticLoader(new CapturingStaticClientFactory(handler), repository, configuration,
            new RouteShapeResponseCache(), shapes, NullWorkerStructuredEventLogger.Instance,
            NullLogger<GtfsStaticLoader>.Instance);

        await loader.StartAsync(CancellationToken.None);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var loaded = await shapes.GetAllShapesAsync(timeout.Token);

            Assert.Equal(new[] { kcmUrl, stUrl }, handler.Requests.Select(request => request.RequestUri!.AbsoluteUri));
            Assert.All(handler.Requests, request => Assert.Equal(string.Empty, request.RequestUri!.Query));
            Assert.Equal(new[] { "100451", "102767" }, loaded.Select(shape => shape.Properties.RouteId).Order(StringComparer.Ordinal));
            Assert.Equal(new[] { "seattle:547", "seattle:556" }, repository.Keys
                .Where(key => key.StartsWith("seattle:", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        }
        finally
        {
            await loader.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void SeattleUnionWithholdsDuplicateDisplayKeysAndRawAliasesRegardlessOfZipOrder()
    {
        var kcm = SeattleZip(
            ("kcm-duplicate-display", "1"),
            ("same-raw-id", "KCM route"),
            ("kcm-unrelated", "KCM unrelated"),
            ("route-512", "512"));
        var soundTransit = SeattleZip(
            ("st-duplicate-display", "1"),
            ("same-raw-id", "ST route"),
            ("st-unrelated", "ST unrelated"));

        var forward = GtfsStaticLoader.MergeSeattleShapeSets([kcm, soundTransit]);
        var reverse = GtfsStaticLoader.MergeSeattleShapeSets([soundTransit, kcm]);

        Assert.Equal(forward.Keys.Order(StringComparer.Ordinal), reverse.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "seattle:KCM unrelated", "seattle:ST unrelated" }, forward.Keys.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("seattle:512", forward.Keys);
        Assert.DoesNotContain("seattle:1", forward.Keys);
        Assert.DoesNotContain("seattle:KCM route", forward.Keys);
        Assert.DoesNotContain("seattle:ST route", forward.Keys);
    }

    [Fact]
    public void SeattleUnionWithholdsSameZipDisplayDuplicatesAndAliasesWithoutDroppingUnrelatedRoutes()
    {
        var oneZip = SeattleZip(
            ("first-id", "duplicate-display"),
            ("second-id", "duplicate-display"),
            ("duplicate-display", "different-display"),
            ("unrelated", "Unrelated"));

        var merged = GtfsStaticLoader.MergeSeattleShapeSets([oneZip]);

        Assert.Equal(new[] { "seattle:Unrelated" }, merged.Keys);
        Assert.DoesNotContain("seattle:duplicate-display", merged.Keys);
        Assert.DoesNotContain("seattle:different-display", merged.Keys);
    }

    [Fact]
    public void SeattleStaticBuilderOmitsUnmatchedRoutesWithoutShapes()
    {
        var routes = new Dictionary<string, string> { ["100451"] = "known", ["unmatched"] = "missing" };
        var shapes = new Dictionary<string, List<(double Lat, double Lon, int Seq)>>
        {
            ["known"] = [(47.60, -122.33, 1), (47.61, -122.32, 2)],
        };
        var metadata = new Dictionary<string, (string?, string?, string?, string, int)>
        {
            ["100451"] = ("556", null, null, "bus", 3),
            ["unmatched"] = ("unmatched", null, null, "bus", 3),
        };

        var result = GtfsStaticLoader.BuildZipRouteFeatures(CityNames.Seattle, routes, shapes, metadata);

        Assert.Equal("seattle:556", Assert.Single(result).Key);
    }

    static Dictionary<string, string> SeattleZip(params (string RouteId, string ShortName)[] routes)
    {
        var routeToShape = new Dictionary<string, string>(StringComparer.Ordinal);
        var shapes = new Dictionary<string, List<(double Lat, double Lon, int Seq)>>(StringComparer.Ordinal);
        var metadata = new Dictionary<string, (string? RouteShortName, string? RouteColor, string? TextColor, string Category, int RouteType)>(StringComparer.Ordinal);
        for (var index = 0; index < routes.Length; index++)
        {
            var (routeId, shortName) = routes[index];
            var shapeId = $"shape-{index}";
            routeToShape[routeId] = shapeId;
            shapes[shapeId] = [(47.60 + index * 0.001, -122.33, 1), (47.61 + index * 0.001, -122.32, 2)];
            metadata[routeId] = (shortName, null, null, "bus", 3);
        }
        return GtfsStaticLoader.BuildZipRouteFeatures(CityNames.Seattle, routeToShape, shapes, metadata);
    }

    static byte[] SeattleStaticZip(string routeId, string shortName)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "routes.txt", $"route_id,route_short_name,route_type\n{routeId},{shortName},3\n");
            Write(archive, "trips.txt", $"route_id,service_id,trip_id,shape_id\n{routeId},weekday,trip-{routeId},shape-{routeId}\n");
            Write(archive, "shapes.txt", $"shape_id,shape_pt_lat,shape_pt_lon,shape_pt_sequence\nshape-{routeId},47.60,-122.33,1\nshape-{routeId},47.61,-122.32,2\n");
        }
        return stream.ToArray();
    }

    static void Write(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), Encoding.UTF8);
        writer.Write(content);
    }

    sealed class CapturingStaticHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return respond(request);
        }
    }

    sealed class CapturingStaticClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    sealed class StaticRouteRepository : IKeyValueRepository<string>
    {
        readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        public IEnumerable<string> Keys => _values.Keys;

        public Task<Result<string>> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? Result<string>.Success(value) : Result<string>.NotFound());

        public Task<Result<Dictionary<string, string>>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult(Result<Dictionary<string, string>>.Success(new Dictionary<string, string>(_values, StringComparer.Ordinal)));

        public Task<Result> SetAsync(string key, string value, CancellationToken ct = default)
        {
            _values[key] = value;
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteAsync(string key, CancellationToken ct = default)
        {
            _values.Remove(key);
            return Task.FromResult(Result.Success());
        }
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
