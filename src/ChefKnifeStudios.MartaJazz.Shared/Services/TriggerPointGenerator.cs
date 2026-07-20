using ChefKnifeStudios.MartaJazz.Shared.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace ChefKnifeStudios.MartaJazz.Shared.Services;

public class TriggerPointGenerator : ITriggerPointGenerator
{
    // SC-005 requires cadence in [5s, 30s]. At typical MARTA speeds (5–15 m/s):
    //   400m @ 10 m/s → 40s per trigger (above upper bound — sparse)
    //   400m @ 15 m/s → 27s per trigger (inside band)
    //   400m @  5 m/s → 80s per trigger (well above upper bound — sparse for slow traffic)
    // Adjust via manual verification (quickstart.md Test 5); try 150m if too sparse, 250m if too frequent.
    // Halving to 200m is a deliberately DEFERRED density lever (feature 045, D9) — re-measure
    // after the direction fix (US2) and join replay (US3) are shipped and measured before
    // touching this constant; halving also doubles NYMTA batch volume against the feature-040
    // 5 MB SignalR ceiling and doubles client timer volume. See specs/045-time-to-first-note/
    // research.md D9.
    const double TriggerSpacingMeters = 400.0;

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
