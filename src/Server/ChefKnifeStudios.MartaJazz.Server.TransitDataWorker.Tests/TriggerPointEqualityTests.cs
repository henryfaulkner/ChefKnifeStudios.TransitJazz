using ChefKnifeStudios.MartaJazz.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Linq;
using Xunit;

namespace ChefKnifeStudios.MartaJazz.Server.TransitDataWorker.Tests;

public class TriggerPointEqualityTests
{
    static readonly TriggerPointGenerator Generator =
        new(NullLogger<TriggerPointGenerator>.Instance);

    // Representative route: 5 points spaced ~500m apart along a straight line.
    // cumDist[i] computed from Haversine approximation; kept as constants for test stability.
    static readonly double[][] SampleCoords =
    [
        [-84.388, 33.749],   // lon, lat — GeoJSON order
        [-84.388, 33.7535],  // ~500m north
        [-84.388, 33.758],   // ~1000m
        [-84.388, 33.7625],  // ~1500m
        [-84.388, 33.767],   // ~2000m
    ];

    static double[] BuildCumDist(double[][] coords)
    {
        var dist = new double[coords.Length];
        dist[0] = 0;
        for (var i = 1; i < coords.Length; i++)
        {
            double lat1 = coords[i - 1][1], lon1 = coords[i - 1][0];
            double lat2 = coords[i][1],     lon2 = coords[i][0];
            double dLat = (lat2 - lat1) * Math.PI / 180;
            double dLon = (lon2 - lon1) * Math.PI / 180;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
                     * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            dist[i] = dist[i - 1] + 6371000 * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }
        return dist;
    }

    [Fact]
    public void Generate_SameInputsTwice_ProducesIdenticalCount()
    {
        var cumDist = BuildCumDist(SampleCoords);
        var first  = Generator.Generate(SampleCoords, cumDist);
        var second = Generator.Generate(SampleCoords, cumDist);
        Assert.Equal(first.Count, second.Count);
    }

    [Fact]
    public void Generate_SameInputsTwice_ProducesIdenticalIndexSequence()
    {
        var cumDist = BuildCumDist(SampleCoords);
        var first  = Generator.Generate(SampleCoords, cumDist).ToList();
        var second = Generator.Generate(SampleCoords, cumDist).ToList();
        for (var i = 0; i < first.Count; i++)
            Assert.Equal(first[i].Index, second[i].Index);
    }

    [Fact]
    public void Generate_SameInputsTwice_ProducesIdenticalAlongDistances()
    {
        var cumDist = BuildCumDist(SampleCoords);
        var first  = Generator.Generate(SampleCoords, cumDist).ToList();
        var second = Generator.Generate(SampleCoords, cumDist).ToList();
        for (var i = 0; i < first.Count; i++)
            Assert.Equal(first[i].AlongDistanceM, second[i].AlongDistanceM);
    }

    [Fact]
    public void Generate_AtLeastOnePoint_ForRouteOver400m()
    {
        var cumDist = BuildCumDist(SampleCoords);
        var points = Generator.Generate(SampleCoords, cumDist);
        Assert.True(points.Count >= 1, "Expected at least one trigger point for a ~2 km route");
    }

    [Fact]
    public void Generate_EmptyCoords_ReturnsEmpty()
    {
        var result = Generator.Generate([], []);
        Assert.Empty(result);
    }

    [Fact]
    public void Generate_ShortRoute_ReturnsEmpty()
    {
        double[][] coords = [[-84.388, 33.749], [-84.388, 33.750]]; // ~111m
        double[] cumDist = [0, 111];
        var result = Generator.Generate(coords, cumDist);
        Assert.Empty(result);
    }
}
