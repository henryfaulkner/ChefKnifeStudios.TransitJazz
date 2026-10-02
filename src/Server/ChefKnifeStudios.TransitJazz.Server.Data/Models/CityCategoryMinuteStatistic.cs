namespace ChefKnifeStudios.TransitJazz.Server.Data.Models;

public sealed class CityCategoryMinuteStatistic
{
    public const string CurrentDefinitionVersion = "observed-city-category-statistics-v1";

    public string CitySlug { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTime StatMinuteUtc { get; set; }
    public string DefinitionVersion { get; set; } = CurrentDefinitionVersion;
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

    public void Validate()
    {
        ValidateWindow(StatMinuteUtc, TimeSpan.FromMinutes(1), requireMinuteAlignment: true);
    }

    internal void ValidateWindow(DateTime windowStartUtc, TimeSpan windowDuration, bool requireMinuteAlignment)
    {
        if (windowStartUtc.Kind != DateTimeKind.Utc || windowStartUtc.Second != 0 || windowStartUtc.Ticks % TimeSpan.TicksPerMinute != 0
            || (requireMinuteAlignment && windowStartUtc.Ticks % TimeSpan.TicksPerMinute != 0))
            throw new ArgumentException("Window start must be aligned to a UTC minute.", nameof(windowStartUtc));
        if (string.IsNullOrWhiteSpace(CitySlug) || CitySlug.Length > 64 || CitySlug != CitySlug.Trim() || CitySlug != CitySlug.ToLowerInvariant())
            throw new ArgumentException("CitySlug must be nonblank and at most 64 characters.", nameof(CitySlug));
        if (string.IsNullOrWhiteSpace(Category) || Category.Length > 64 || Category != Category.Trim() || Category != Category.ToLowerInvariant())
            throw new ArgumentException("Category must be a lowercase nonblank label of at most 64 characters.", nameof(Category));
        if (string.IsNullOrWhiteSpace(DefinitionVersion) || DefinitionVersion.Length > 64)
            throw new ArgumentException("DefinitionVersion must be nonblank and at most 64 characters.", nameof(DefinitionVersion));
        if (!Enum.IsDefined(CollectionStatus)) throw new ArgumentOutOfRangeException(nameof(CollectionStatus));
        if (HealthyCadenceLimitSeconds is <= 0 or > 60) throw new ArgumentOutOfRangeException(nameof(HealthyCadenceLimitSeconds));
        if (ObservedCycleCount < 0 || ValidActiveSampleCount < 0 || ValidPublishCycleCount < 0 || FailedCycleCount < 0
            || ActiveVehicleCountSum < 0 || DistanceMetersSum < 0 || DistanceIntervalCount < 0
            || DistanceRejectedCount < 0 || CrossingsPublishedCount < 0 || MaxObservationGapSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(ObservedCycleCount), "Aggregate counts and distances must be nonnegative.");
        if (ValidActiveSampleCount > ObservedCycleCount || ValidPublishCycleCount > ObservedCycleCount || FailedCycleCount > ObservedCycleCount)
            throw new ArgumentException("Sample and failure counts cannot exceed observed cycles.");
        ValidateOptionalUtc(FirstCycleUtc, nameof(FirstCycleUtc));
        ValidateOptionalUtc(LastCycleUtc, nameof(LastCycleUtc));
        if ((FirstCycleUtc is null) != (LastCycleUtc is null) || (ObservedCycleCount == 0) != (FirstCycleUtc is null))
            throw new ArgumentException("Cycle timestamps must be present exactly when a cycle was observed.");
        if (FirstCycleUtc is { } first && LastCycleUtc is { } last && first > last)
            throw new ArgumentException("FirstCycleUtc must not be after LastCycleUtc.");
        if (MaxObservationGapSeconds is { } gap && decimal.Round(gap, 6) != gap)
            throw new ArgumentException("Maximum observation gap must use six-decimal precision.");
        if (decimal.Round(DistanceMetersSum, 6) != DistanceMetersSum)
            throw new ArgumentException("DistanceMetersSum must use six-decimal precision.");
        if (FirstCycleUtc is { } firstCycle && (firstCycle < windowStartUtc || firstCycle >= windowStartUtc.Add(windowDuration)))
            throw new ArgumentException("First cycle timestamp must fall within the represented window.");
        if (LastCycleUtc is { } lastCycle && (lastCycle < windowStartUtc || lastCycle >= windowStartUtc.Add(windowDuration)))
            throw new ArgumentException("Last cycle timestamp must fall within the represented window.");
        if (CollectionStatus == CategoryCollectionStatus.Complete
            && (ObservedCycleCount == 0 || ValidActiveSampleCount != ObservedCycleCount || ValidPublishCycleCount != ObservedCycleCount
                || FailedCycleCount != 0 || MaxObservationGapSeconds is null || MaxObservationGapSeconds > HealthyCadenceLimitSeconds))
            throw new ArgumentException("Complete minutes require valid activity/publication samples, healthy timing, and no failed cycles.");
        if (CollectionStatus == CategoryCollectionStatus.NoData
            && (ValidActiveSampleCount != 0 || ValidPublishCycleCount != 0 || ActiveVehicleCountSum != 0 || DistanceMetersSum != 0
                || DistanceIntervalCount != 0 || CrossingsPublishedCount != 0))
            throw new ArgumentException("NoData rows cannot contain eligible measures.");
    }

    static void ValidateOptionalUtc(DateTime? value, string name)
    {
        if (value is { Kind: not DateTimeKind.Utc }) throw new ArgumentException("Cycle timestamps must be UTC.", name);
        if (value is { } date && date.Ticks % 10 != 0) throw new ArgumentException("Cycle timestamps must use microsecond precision.", name);
    }
}
