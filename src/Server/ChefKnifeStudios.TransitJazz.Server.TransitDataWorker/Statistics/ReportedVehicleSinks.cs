using Microsoft.Extensions.Logging;

namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Statistics;

public interface IReportedVehicleStatisticsSink
{
    bool TryEnqueue(FinalizedReportedVehicleHourBatch batch, IDisposable reservation);
}

public sealed class NullReportedVehicleStatisticsSink : IReportedVehicleStatisticsSink
{
    public static NullReportedVehicleStatisticsSink Instance { get; } = new();
    private NullReportedVehicleStatisticsSink() { }
    public bool TryEnqueue(FinalizedReportedVehicleHourBatch batch, IDisposable reservation)
    {
        reservation.Dispose();
        return true;
    }
}

/// <summary>Validates and logs bounded counts; it does not persist raw identities.</summary>
public sealed class DryRunReportedVehicleStatisticsSink(
    ILogger<DryRunReportedVehicleStatisticsSink> logger,
    ReportedVehicleHistoryOptions options) : IReportedVehicleStatisticsSink
{
    public bool TryEnqueue(FinalizedReportedVehicleHourBatch batch, IDisposable reservation)
    {
        try
        {
            batch.Validate(options);
            logger.LogInformation("Reported-vehicle dry-run accepted {City} hour={HourStartUtc}; status={Status}, memberships={MembershipCount}, sources={SourceCount}, missingRecords={MissingRecords}.",
                batch.Header.CitySlug, batch.Header.HourStartUtc, batch.Header.CollectionStatus,
                batch.Header.MembershipCount, batch.Header.ExpectedSourceCount, batch.Header.UnidentifiableRecordCount);
            return true;
        }
        finally { reservation.Dispose(); }
    }
}
