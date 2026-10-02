namespace ChefKnifeStudios.TransitJazz.Server.Data.Models;

public sealed class CityCategoryHourStatistic
{
    public string CitySlug { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTime HourStartUtc { get; set; }
    public string DefinitionVersion { get; set; } = CityCategoryMinuteStatistic.CurrentDefinitionVersion;
    public CategoryCollectionStatus CollectionStatus { get; set; }
    public bool HasConflict { get; set; }
    public int HealthyCadenceLimitSeconds { get; set; }
    public long ObservedCycleCount { get; set; }
    public long ValidActiveSampleCount { get; set; }
    public long ValidPublishCycleCount { get; set; }
    public long FailedCycleCount { get; set; }
    public DateTime? FirstCycleUtc { get; set; }
    public DateTime? LastCycleUtc { get; set; }
    public decimal? MaxObservationGapSeconds { get; set; }
    public long ActiveVehicleCountSum { get; set; }
    public decimal DistanceMetersSum { get; set; }
    public long DistanceIntervalCount { get; set; }
    public long DistanceRejectedCount { get; set; }
    public long CrossingsPublishedCount { get; set; }
    public long? DistinctActiveVehicleCount { get; set; }
    public int CoveredMinutes { get; set; }

    public void Validate()
    {
        if (HourStartUtc.Kind != DateTimeKind.Utc || HourStartUtc.Minute != 0 || HourStartUtc.Second != 0 || HourStartUtc.Ticks % TimeSpan.TicksPerMinute != 0)
            throw new ArgumentException("HourStartUtc must be aligned to a UTC hour.", nameof(HourStartUtc));
        var shared = new CityCategoryMinuteStatistic
        {
            CitySlug = CitySlug, Category = Category, StatMinuteUtc = HourStartUtc,
            DefinitionVersion = DefinitionVersion, CollectionStatus = CollectionStatus, HasConflict = HasConflict,
            HealthyCadenceLimitSeconds = HealthyCadenceLimitSeconds, ObservedCycleCount = ObservedCycleCount,
            ValidActiveSampleCount = ValidActiveSampleCount, ValidPublishCycleCount = ValidPublishCycleCount,
            FailedCycleCount = FailedCycleCount, FirstCycleUtc = FirstCycleUtc, LastCycleUtc = LastCycleUtc,
            MaxObservationGapSeconds = MaxObservationGapSeconds, ActiveVehicleCountSum = ActiveVehicleCountSum,
            DistanceMetersSum = DistanceMetersSum, DistanceIntervalCount = DistanceIntervalCount,
            DistanceRejectedCount = DistanceRejectedCount, CrossingsPublishedCount = CrossingsPublishedCount,
        };
        shared.ValidateWindow(HourStartUtc, TimeSpan.FromHours(1), requireMinuteAlignment: false);
        if (DistinctActiveVehicleCount < 0) throw new ArgumentOutOfRangeException(nameof(DistinctActiveVehicleCount));
        if (CoveredMinutes is < 0 or > 60) throw new ArgumentOutOfRangeException(nameof(CoveredMinutes));
        if (CollectionStatus == CategoryCollectionStatus.NoData
            && (ValidActiveSampleCount != 0 || ValidPublishCycleCount != 0 || ActiveVehicleCountSum != 0 || DistanceMetersSum != 0
                || DistanceIntervalCount != 0 || CrossingsPublishedCount != 0))
            throw new ArgumentException("NoData rows cannot contain eligible measures.");
        if (CollectionStatus == CategoryCollectionStatus.Complete && (CoveredMinutes != 60 || DistinctActiveVehicleCount is null))
            throw new ArgumentException("Complete hours require 60 covered minutes, an intact population, and no conflict.");
    }
}
