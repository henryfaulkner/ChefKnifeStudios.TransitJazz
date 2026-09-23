using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace ChefKnifeStudios.TransitJazz.Server.WebAPI.Statistics;

public sealed class HistoricalStatisticsOptions
{
    public const string SectionName = "HistoricalStatistics";
    public const int FixedCollectionIntervalMinutes = 1;
    public const int InitialChunkHours = 6;

    public bool Enabled { get; set; }
    public bool DryRun { get; set; } = true;
    public bool InitialBackfill { get; set; }
    public string SourceEndpoint { get; set; } = string.Empty;
    public string ReaderAuthorization { get; set; } = string.Empty;
    public string SourceDefinitionVersion { get; set; } = WorkerDashboardStatisticsCatalog.Version;
    public int CollectionIntervalMinutes { get; set; } = FixedCollectionIntervalMinutes;
    public int IngestionGraceMinutes { get; set; } = 2;
    public int OverlapMinutes { get; set; } = 5;
    public DateTime? BackfillStartUtc { get; set; }
    public DateTime? BackfillEndUtc { get; set; }
    public List<string> Cities { get; set; } = [];

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
        if (BackfillStartUtc is not null && BackfillStartUtc.Value.Kind != DateTimeKind.Utc)
            failures.Add("BackfillStartUtc must be UTC.");
        if (BackfillEndUtc is not null && BackfillEndUtc.Value.Kind != DateTimeKind.Utc)
            failures.Add("BackfillEndUtc must be UTC.");
        if (BackfillStartUtc is not null && BackfillEndUtc is not null && BackfillStartUtc >= BackfillEndUtc)
            failures.Add("BackfillStartUtc must precede BackfillEndUtc.");

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
            if (InitialBackfill && (BackfillStartUtc is null || BackfillEndUtc is null))
                failures.Add("Initial backfill requires an explicit bounded UTC range.");
        }

        if (failures.Count > 0)
            throw new OptionsValidationException(SectionName, typeof(HistoricalStatisticsOptions), failures);
    }
}
