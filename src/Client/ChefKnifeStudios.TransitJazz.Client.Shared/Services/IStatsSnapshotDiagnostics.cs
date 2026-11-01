namespace ChefKnifeStudios.TransitJazz.Client.Shared.Services;

/// <summary>Finite Stats retrieval events. Values intentionally carry no selection or identity labels.</summary>
public enum StatsSnapshotDiagnosticEvent
{
    PreparationCompleted,
    WarmOpen,
    Loader,
    SnapshotAgeOnOpen,
    AdmissionSkipped,
    Failure
}

public enum StatsSnapshotAgeBucket
{
    UnderOneMinute,
    OneToFiveMinutes,
    FiveToFifteenMinutes,
    FifteenToSixtyMinutes,
    OverSixtyMinutes
}

public interface IStatsSnapshotDiagnostics
{
    void Record(StatsSnapshotDiagnosticEvent diagnosticEvent, StatsSnapshotAgeBucket? ageBucket = null);
}
