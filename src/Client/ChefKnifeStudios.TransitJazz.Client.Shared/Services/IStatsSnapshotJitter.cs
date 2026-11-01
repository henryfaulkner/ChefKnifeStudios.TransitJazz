using System;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services;

/// <summary>Supplies one independent positive refresh delay sample in the range [0, 5 seconds].</summary>
public interface IStatsSnapshotJitter
{
    TimeSpan NextDelay();
}
