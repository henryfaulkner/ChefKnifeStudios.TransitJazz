using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;
using Microsoft.Extensions.Options;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;

public sealed class HistoricalStatisticsOptions
{
    public const string SectionName = "HistoricalStatistics";
    public const int FixedCollectionIntervalMinutes = 1;

    public bool Enabled { get; set; }
    public bool DryRun { get; set; } = true;
    public string SourceEndpoint { get; set; } = string.Empty;
    public string ReaderAuthorization { get; set; } = string.Empty;
    public string SourceDefinitionVersion { get; set; } = WorkerDashboardStatisticsCatalog.Version;
    public int CollectionIntervalMinutes { get; set; } = FixedCollectionIntervalMinutes;
    public int IngestionGraceMinutes { get; set; } = 2;
    public int OverlapMinutes { get; set; } = 5;
    public List<string> Cities { get; set; } = [];
    public RouteHourHistoryOptions RouteHours { get; set; } = new();

    public void ResolveCitySelection(IEnumerable<string> configuredCities, string fallbackCity)
    {
        if (Cities.Count > 0) return;
        var configured = configuredCities.ToList();
        Cities = configured.Count == 0 ? [fallbackCity] : configured;
    }

    public RouteHourCaptureRuntimeOptions CreateRouteHourRuntimeOptions()
    {
        var mode = !Enabled ? RouteHourCaptureMode.Disabled : DryRun ? RouteHourCaptureMode.DryRun : RouteHourCaptureMode.Persistence;
        return new(mode, Cities.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase), RouteHours);
    }

    public void Validate()
    {
        var failures = new List<string>();
        if (!string.Equals(SourceDefinitionVersion, WorkerDashboardStatisticsCatalog.Version, StringComparison.Ordinal))
            failures.Add("SourceDefinitionVersion must be worker-dashboard-statistics-v1.");
        if (CollectionIntervalMinutes != FixedCollectionIntervalMinutes)
            failures.Add("CollectionIntervalMinutes must be exactly one minute.");
        if (IngestionGraceMinutes < 1 || IngestionGraceMinutes > 10)
            failures.Add("IngestionGraceMinutes must be between 1 and 10.");
        if (OverlapMinutes < 1 || OverlapMinutes > 60)
            failures.Add("OverlapMinutes must be between 1 and 60.");

        try
        {
            WorkerDashboardStatisticsCatalog.Validate(Cities);
        }
        catch (ArgumentException exception)
        {
            failures.Add(exception.Message);
        }

        if (Enabled)
        {
            if (!Uri.TryCreate(SourceEndpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
                failures.Add("SourceEndpoint must be an HTTPS URI when collection is enabled.");
            if (string.IsNullOrWhiteSpace(ReaderAuthorization))
                failures.Add("ReaderAuthorization is required when collection is enabled.");
        }

        if (failures.Count > 0)
            throw new OptionsValidationException(SectionName, typeof(HistoricalStatisticsOptions), failures);
    }
}
