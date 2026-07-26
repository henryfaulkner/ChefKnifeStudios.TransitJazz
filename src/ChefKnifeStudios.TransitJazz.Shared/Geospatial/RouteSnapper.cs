using System;

namespace ChefKnifeStudios.TransitJazz.Shared.Geospatial;

public readonly record struct Snap(int Index, RoutePoint Point, double DistanceKm);

public static class RouteSnapper
{
    public static Snap? FindNearest(double lat, double lon, ReadOnlySpan<RoutePoint> points)
    {
        if (points.IsEmpty) return null;

        int bestIndex = 0;
        double bestDistance = double.MaxValue;

        for (int i = 0; i < points.Length; i++)
        {
            double distance = HaversineCalculator.DistanceKm(lat, lon, points[i].Lat, points[i].Lon);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return new Snap(bestIndex, points[bestIndex], bestDistance);
    }

    // Constrains the search to a window around a known prior index to prevent
    // the snapper from teleporting across a route that doubles back on itself.
    public static Snap? FindNearestInWindow(double lat, double lon, ReadOnlySpan<RoutePoint> points, int priorIndex, int windowSize)
    {
        if (points.IsEmpty) return null;

        int lo = Math.Max(0, priorIndex - windowSize);
        int hi = Math.Min(points.Length - 1, priorIndex + windowSize);

        int bestIndex = lo;
        double bestDistance = double.MaxValue;

        for (int i = lo; i <= hi; i++)
        {
            double distance = HaversineCalculator.DistanceKm(lat, lon, points[i].Lat, points[i].Lon);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return new Snap(bestIndex, points[bestIndex], bestDistance);
    }
}
