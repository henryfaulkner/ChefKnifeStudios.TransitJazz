using System;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services;

/// <summary>Small navigation signal seam owned separately from the map's browser visibility listener.</summary>
public interface IStatsSnapshotNavigation
{
    event Action<string>? CityChanged;
}
