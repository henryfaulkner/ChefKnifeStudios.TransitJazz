using System.Collections.Immutable;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using ChefKnifeStudios.TransitJazz.Shared;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

/// <summary>Builds the fixed source set used by a host from endpoint-aligned configuration.</summary>
public static class ReportedVehicleCatalogBuilder
{
    public static ReportedVehicleSourceCatalog Build(IEnumerable<CityConfig> configuredCities)
    {
        ArgumentNullException.ThrowIfNull(configuredCities);
        var cities = configuredCities.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var entries = new List<ReportedVehicleSourceCatalogEntry>();
        foreach (var (slug, city) in cities)
        {
            // An absent policy makes the entire city unsupported. Once any policy
            // is supplied, require a complete valid catalog; never infer sources.
            if (city.ReportedVehicleSources.Length == 0 && city.BusReportedVehicleSources.Length == 0
                && city.RailRealtime?.ReportedVehicleSource is null)
                continue;

            var sources = new List<ReportedVehicleSourceDefinition>();
            if (string.Equals(slug, CityNames.Marta, StringComparison.OrdinalIgnoreCase)
                && city.GtfsRtUrls.Length != 1)
                throw new InvalidOperationException("MARTA reported-vehicle capture requires exactly one configured bus source.");
            AddAligned(sources, city.ReportedVehicleSources, city.GtfsRtUrls, slug, "GtfsRtUrls");
            AddAligned(sources, city.BusReportedVehicleSources, city.BusGtfsRtUrls, slug, "BusGtfsRtUrls");

            if (string.Equals(slug, CityNames.Marta, StringComparison.OrdinalIgnoreCase)
                && city.RailRealtime?.Enabled == true)
            {
                var railSource = city.RailRealtime.ReportedVehicleSource
                    ?? throw new InvalidOperationException($"Reported-vehicle capture requires an explicit MARTA rail source policy for {slug}.");
                sources.Add(railSource.ToDefinition());
            }

            if (sources.Count is < 1 or > 32)
                throw new InvalidOperationException($"Reported-vehicle capture requires 1-32 explicit sources for {slug}.");
            entries.Add(new ReportedVehicleSourceCatalogEntry(slug, sources.ToImmutableArray()));
        }
        return new ReportedVehicleSourceCatalog(entries);
    }

    static void AddAligned(List<ReportedVehicleSourceDefinition> target, ReportedVehicleSourceConfig[] policies,
        string[] urls, string city, string endpointProperty)
    {
        if (urls.Length == 0)
        {
            if (policies.Length != 0)
                throw new InvalidOperationException($"Reported-vehicle source policies for {city}.{endpointProperty} have no matching endpoints.");
            return;
        }
        if (policies.Length != urls.Length)
            throw new InvalidOperationException($"Reported-vehicle source policies must match {city}.{endpointProperty} endpoint count.");
        target.AddRange(policies.Select(x => x.ToDefinition()));
    }
}
