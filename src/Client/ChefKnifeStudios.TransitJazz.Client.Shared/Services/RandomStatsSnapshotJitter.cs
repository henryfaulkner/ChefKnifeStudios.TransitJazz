using System;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services;

public sealed class RandomStatsSnapshotJitter : IStatsSnapshotJitter
{
    public TimeSpan NextDelay() => TimeSpan.FromMilliseconds(Random.Shared.Next(0, 5001));
}
