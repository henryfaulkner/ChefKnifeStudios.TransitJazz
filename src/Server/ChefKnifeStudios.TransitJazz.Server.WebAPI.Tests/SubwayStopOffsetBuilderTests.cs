using ChefKnifeStudios.TransitJazz.Server.WebAPI.GtfsStatic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests;

// Tests for SubwayStopOffsetBuilder: stops.txt + stop_times.txt + trips.txt + shapes.txt
// -> per (route, direction) ordered stop offsets. Fixtures are a small synthetic 2-stop
// "7" line (both directions) with a 3-point shape.
public class SubwayStopOffsetBuilderTests
{
    // A simple straight shape: 3 points along one line (roughly 0m, ~1113m, ~2226m apart
    // at the equator-ish latitude used here — exact distance doesn't matter for the
    // invariants, only monotonicity).
    const string ShapesTxt = """
        shape_id,shape_pt_lat,shape_pt_lon,shape_pt_sequence
        7_shape,40.7500,-73.9900,0
        7_shape,40.7500,-73.9800,1
        7_shape,40.7500,-73.9700,2
        """;

    const string TripsTxt = """
        route_id,trip_id,shape_id
        7,7_trip_N,7_shape
        7,7_trip_S,7_shape
        """;

    const string StopsTxt = """
        stop_id,stop_lat,stop_lon
        701N,40.7500,-73.9900
        702N,40.7500,-73.9700
        701S,40.7500,-73.9900
        702S,40.7500,-73.9700
        """;

    const string StopTimesTxt = """
        trip_id,stop_id,stop_sequence
        7_trip_N,701N,1
        7_trip_N,702N,2
        7_trip_S,702S,1
        7_trip_S,701S,2
        """;

    static ZipArchive MakeMultiEntryZip(params (string FileName, string Content)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (fileName, content) in entries)
            {
                var entry = zip.CreateEntry(fileName);
                using var w = new StreamWriter(entry.Open(), Encoding.UTF8);
                w.Write(content);
            }
        }
        ms.Position = 0;
        return new ZipArchive(ms, ZipArchiveMode.Read);
    }

    static List<Shared.GtfsData.SubwayStopOffsetSet> BuildFromFixture()
    {
        using var archive = MakeMultiEntryZip(
            ("shapes.txt", ShapesTxt),
            ("trips.txt", TripsTxt),
            ("stops.txt", StopsTxt),
            ("stop_times.txt", StopTimesTxt));

        var shapes = GtfsStaticLoader.ParseShapes(archive);
        var routeToShape = GtfsStaticLoader.ParseRouteToShapeMap(archive);

        return SubwayStopOffsetBuilder.Build(archive, shapes, routeToShape);
    }

    [Fact]
    public void Build_TwoDirections_YieldsTwoSets()
    {
        // INV-E3: a route appearing in both directions yields two sets.
        var sets = BuildFromFixture();

        Assert.Equal(2, sets.Count);
        Assert.Contains(sets, s => s.RouteJoinKey == "7" && s.Direction == "N");
        Assert.Contains(sets, s => s.RouteJoinKey == "7" && s.Direction == "S");
    }

    [Fact]
    public void Build_CumulativeDistance_MonotonicAndStartsAtZero()
    {
        // INV-E1: cumulativeDistanceMeters.Length == coordinates.Length, [0] == 0, non-decreasing.
        var sets = BuildFromFixture();

        foreach (var set in sets)
        {
            Assert.Equal(set.Coordinates.Length, set.CumulativeDistanceMeters.Length);
            Assert.Equal(0, set.CumulativeDistanceMeters[0]);

            for (int i = 1; i < set.CumulativeDistanceMeters.Length; i++)
                Assert.True(set.CumulativeDistanceMeters[i] >= set.CumulativeDistanceMeters[i - 1]);
        }
    }

    [Fact]
    public void Build_Stops_OrderedByDistanceAndInRange()
    {
        // INV-E2: stops ordered by distanceAlongShapeMeters ascending; every value in
        // [0, last cumulative].
        var sets = BuildFromFixture();

        foreach (var set in sets)
        {
            var lastCum = set.CumulativeDistanceMeters[^1];
            for (int i = 0; i < set.Stops.Length; i++)
            {
                Assert.InRange(set.Stops[i].DistanceAlongShapeMeters, 0, lastCum);
                if (i > 0)
                    Assert.True(set.Stops[i].DistanceAlongShapeMeters >= set.Stops[i - 1].DistanceAlongShapeMeters);
            }
        }
    }

    [Fact]
    public void Build_NorthboundStops_StartsAtFirstStation()
    {
        var sets = BuildFromFixture();
        var north = sets.Single(s => s.Direction == "N");

        Assert.Equal(2, north.Stops.Length);
        Assert.Equal("701N", north.Stops[0].StopId);
        Assert.Equal("702N", north.Stops[1].StopId);
        Assert.Equal(0, north.Stops[0].DistanceAlongShapeMeters);
    }

    [Fact]
    public void Build_RouteWithNoStops_IsOmitted()
    {
        // INV-E6: a route with an empty shape or zero mapped stops is omitted, not a
        // null/empty set. Fixture: a route_id present in trips.txt with a shape but no
        // stop_times rows referencing its trip.
        const string tripsWithExtraRoute = """
            route_id,trip_id,shape_id
            7,7_trip_N,7_shape
            7,7_trip_S,7_shape
            X,X_trip_N,7_shape
            """;

        using var archive = MakeMultiEntryZip(
            ("shapes.txt", ShapesTxt),
            ("trips.txt", tripsWithExtraRoute),
            ("stops.txt", StopsTxt),
            ("stop_times.txt", StopTimesTxt));

        var shapes = GtfsStaticLoader.ParseShapes(archive);
        var routeToShape = GtfsStaticLoader.ParseRouteToShapeMap(archive);
        var sets = SubwayStopOffsetBuilder.Build(archive, shapes, routeToShape);

        Assert.DoesNotContain(sets, s => s.RouteJoinKey == "X");
    }
}
