using ChefKnifeStudios.MartaJazz.Client.Shared.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace ChefKnifeStudios.MartaJazz.Client.Shared.Services;

public class TriggerPointGenerator : ITriggerPointGenerator
{
    // SC-005 requires cadence in [5s, 30s]. At typical MARTA speeds (5–15 m/s):
    //   200m @ 10 m/s → 20s per trigger (comfortably inside band)
    //   200m @ 15 m/s → 13s per trigger (inside band)
    //   200m @  5 m/s → 40s per trigger (slightly above upper bound — truthfully sparse for slow traffic)
    // Adjust via manual verification (quickstart.md Test 5); try 150m if too sparse, 250m if too frequent.
    const double TriggerSpacingMeters = 200.0;

    readonly ILogger<TriggerPointGenerator> _logger;

    public TriggerPointGenerator(ILogger<TriggerPointGenerator> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<TriggerPoint> Generate(double[][] coords, double[] cumDist)
    {
        if (coords is null || cumDist is null || coords.Length == 0 || cumDist.Length == 0)
            return Array.Empty<TriggerPoint>();

        var totalDist = cumDist[^1];
        if (totalDist < TriggerSpacingMeters)
        {
            _logger.LogWarning(
                "TriggerPointGenerator: route is shorter than spacing ({TotalDist:F0}m < {Spacing}m) — no trigger points generated",
                totalDist, TriggerSpacingMeters);
            return Array.Empty<TriggerPoint>();
        }

        var result = new List<TriggerPoint>();
        var d = TriggerSpacingMeters;

        while (d < totalDist)
        {
            var index = BinarySearchFirstIndexAtOrBeyond(cumDist, d);
            result.Add(new TriggerPoint(index, d));
            d += TriggerSpacingMeters;
        }

        return result;
    }

    // Returns the smallest index i such that cumDist[i] >= targetDist.
    static int BinarySearchFirstIndexAtOrBeyond(double[] cumDist, double targetDist)
    {
        var lo = 0;
        var hi = cumDist.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (cumDist[mid] < targetDist)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }
}
