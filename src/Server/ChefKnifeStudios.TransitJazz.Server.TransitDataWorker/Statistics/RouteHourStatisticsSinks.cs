using Microsoft.Extensions.Logging;
using System;
using System.Linq;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

public sealed class NullRouteHourStatisticsSink : IRouteHourStatisticsSink
{
    public static NullRouteHourStatisticsSink Instance { get; } = new();
    private NullRouteHourStatisticsSink() { }
    public bool TryEnqueue(FinalizedRouteHourStatisticsBatch batch) => true;
}

/// <summary>Validates and summarizes aggregate envelopes without providing durable storage.</summary>
public sealed class DryRunRouteHourStatisticsSink(ILogger<DryRunRouteHourStatisticsSink> logger, int maxRows) : IRouteHourStatisticsSink
{
    public bool TryEnqueue(FinalizedRouteHourStatisticsBatch batch)
    {
        batch.Validate(maxRows);
        logger.LogInformation("Route-hour dry-run accepted {City} envelope for {HourStartUtc}; rows={Rows}, completeRows={CompleteRows}, partialRows={PartialRows}, noDataRows={NoDataRows}.",
            batch.CitySlug, batch.HourStartUtc, batch.RowCount,
            batch.Rows.Count(row => row.CollectionStatus == RouteHourCoverageStatus.Complete),
            batch.Rows.Count(row => row.CollectionStatus == RouteHourCoverageStatus.Partial),
            batch.Rows.Count(row => row.CollectionStatus == RouteHourCoverageStatus.NoData));
        return true;
    }
}
