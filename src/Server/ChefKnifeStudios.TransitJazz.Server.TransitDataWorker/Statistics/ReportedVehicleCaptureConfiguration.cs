using System.Collections.Immutable;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Cities;
using Microsoft.Extensions.Configuration;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

public sealed record ReportedVehicleCaptureConfiguration(
    ReportedVehicleHistoryOptions Options,
    ReportedVehicleSourceCatalog Catalog,
    ReportedVehicleHistoryRuntimeOptions Runtime)
{
    public static ReportedVehicleCaptureConfiguration FromConfiguration(IConfiguration configuration,
        IReadOnlyCollection<CityConfig> cityConfigs, ReportedVehicleCaptureHost host)
    {
        var options = configuration.GetSection(ReportedVehicleHistoryOptions.SectionName)
            .Get<ReportedVehicleHistoryOptions>() ?? new ReportedVehicleHistoryOptions();
        options.Validate();
        options.ValidateHost(host);

        var catalog = options.Mode == ReportedVehicleCaptureMode.Disabled
            ? new ReportedVehicleSourceCatalog([])
            : ReportedVehicleCatalogBuilder.Build(cityConfigs);
        if (options.Mode != ReportedVehicleCaptureMode.Disabled)
            options.Validate(catalog.Cities);

        var runtime = new ReportedVehicleHistoryRuntimeOptions(options.Mode,
            catalog.Cities.Select(city => city.CitySlug).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase), options);
        return new ReportedVehicleCaptureConfiguration(options, catalog, runtime);
    }
}
